using System.Text.Json.Nodes;
using CaseConverter;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using OpenAPIDyalog.Constants;
using OpenAPIDyalog.Models;
using OpenAPIDyalog.Services.Interfaces;
using OpenAPIDyalog.Utils;

namespace OpenAPIDyalog.Services;

/// <summary>
/// Generates APL model class files from OpenAPI component and inline schemas.
/// </summary>
public class ModelGeneratorService
{
    private readonly ITemplateService _templateService;
    private readonly ILogger<ModelGeneratorService> _logger;

    public ModelGeneratorService(ITemplateService templateService, ILogger<ModelGeneratorService> logger)
    {
        _templateService = templateService;
        _logger          = logger;
    }

    /// <summary>
    /// Generates model files for all schemas defined in document.Components.Schemas.
    /// </summary>
    public async Task GenerateComponentModelsAsync(OpenApiDocument document, string outputDirectory)
    {
        var schemas = document.Components?.Schemas;
        if (schemas == null || schemas.Count == 0)
        {
            _logger.LogDebug("No component schemas found.");
            return;
        }

        _logger.LogInformation("Generating {Count} component model(s)...", schemas.Count);

        // Schema names that differ only in case or punctuation (pet, Pet, pet_) map to one class.
        foreach (var clash in schemas.Keys.GroupBy(ClassNameOf).Where(g => g.Count() > 1))
            _logger.LogWarning("Schemas {Names} all map to model class {ClassName}; only the last is generated.",
                string.Join(", ", clash), clash.Key);

        foreach (var (name, schema) in schemas)
        {
            var context = CreateModelContext(name, schema, document);
            await GenerateModelAsync(context, outputDirectory);
        }
    }

    /// <summary>
    /// Generates model files for inline schemas discovered during endpoint generation.
    /// </summary>
    public async Task GenerateInlineSchemaModelsAsync(
        IReadOnlyDictionary<string, IOpenApiSchema> inlineSchemas, OpenApiDocument document, string outputDirectory)
    {
        if (inlineSchemas.Count == 0) return;

        _logger.LogInformation("Generating {Count} inline schema model(s)...", inlineSchemas.Count);

        foreach (var (name, schema) in inlineSchemas)
        {
            // The document resolves any allOf references to component schemas.
            var context = CreateModelContext(name, schema, document);
            await GenerateModelAsync(context, outputDirectory);
        }
    }

    /// <summary>
    /// The model class generated for a schema name.
    /// </summary>
    internal static string ClassNameOf(string schemaName) =>
        StringHelpers.ToValidAplName(schemaName.ToPascalCase());

    // ── Core helpers ────────────────────────────────────────────────────────────

    private async Task GenerateModelAsync(ModelTemplateContext context, string outputDirectory)
    {
        var template = await _templateService.LoadTemplateAsync(GeneratorConstants.ModelTemplate);
        var rendered = await _templateService.RenderAsync(template, context);

        var outputPath = Path.Combine(
            outputDirectory,
            GeneratorConstants.AplSourceDir,
            GeneratorConstants.ModelsSubDir,
            $"{context.ClassName}.aplc");

        await _templateService.SaveOutputAsync(rendered, outputPath);
        _logger.LogDebug("Generated model: {ClassName}", context.ClassName);
    }

    private static ModelTemplateContext CreateModelContext(
        string schemaName,
        IOpenApiSchema schema,
        OpenApiDocument? document,
        string? sourceInfo = null)
    {
        var context = new ModelTemplateContext
        {
            ClassName   = ClassNameOf(schemaName),
            Description = schema.Description ?? sourceInfo
        };

        // Map type: no named properties, but has additionalProperties.
        if ((schema.Properties == null || schema.Properties.Count == 0)
            && schema.AdditionalProperties != null)
        {
            context.IsMapType    = true;
            context.MapValueType = SchemaTypeMapper.MapSchemaTypeToAplType(schema.AdditionalProperties);
            return context;
        }

        // The schema's own properties, then those of its allOf sub-schemas (inheritance /
        // extension pattern). A property is required if the schema or any sub-schema says so.
        var sources = new List<IOpenApiSchema> { schema };
        if (schema.AllOf != null)
            sources.AddRange(schema.AllOf.Select(s => ResolveSchema(s, document)).OfType<IOpenApiSchema>());

        var required = sources.SelectMany(s => s.Required ?? new HashSet<string>()).ToHashSet();

        foreach (var source in sources)
        {
            foreach (var kvp in source.Properties ?? new Dictionary<string, IOpenApiSchema>())
            {
                // Dedup: an earlier source's property wins.
                if (context.Properties.Any(p => p.ApiName == kvp.Key)) continue;
                AddProperty(context, kvp.Key, kvp.Value, required.Contains(kvp.Key));
            }
        }

        return context;
    }

    private static void AddProperty(
        ModelTemplateContext context,
        string rawKey,
        IOpenApiSchema propSchema,
        bool isRequired)
    {
        var dyalogName = StringHelpers.ToValidAplName(rawKey);

        var prop = new ModelProperty
        {
            ApiName    = rawKey,
            DyalogName = dyalogName,
            Type       = SchemaTypeMapper.MapSchemaTypeToAplType(propSchema),
            IsRequired = isRequired,
            IsNullable = SchemaTypeMapper.IsNullable(propSchema),
            IsReadOnly = propSchema.ReadOnly,
            IsWriteOnly = propSchema.WriteOnly,
            Format     = propSchema.Format,
            Description = propSchema.Description,
            IsArray    = OperationNaming.IsType(propSchema, JsonSchemaType.Array),
        };
        prop.IsStringArray = prop.IsArray
            && propSchema.Items != null && OperationNaming.IsType(propSchema.Items, JsonSchemaType.String);

        // Default value — render as string for the comment.
        if (propSchema.Default is JsonNode defaultNode)
            prop.DefaultValue = RenderDefaultValue(defaultNode);

        // Enum values.
        if (propSchema.Enum?.Count > 0)
        {
            prop.EnumValues = propSchema.Enum
                .Select(ToEnumValue)
                .Where(v => v != null)
                .Cast<EnumValue>()
                .ToList();

            // Values whose readable names collide (e.g. "in-stock" and "in_stock") fall back to
            // the mangled value itself, which is unique.
            foreach (var group in prop.EnumValues.GroupBy(v => v.AplName).Where(g => g.Count() > 1))
                foreach (var v in group)
                    v.AplName = StringHelpers.ToValidAplName(v.ApiValue);
        }

        // Reference detection — direct $ref property.
        if (propSchema is OpenApiSchemaReference schemaRef)
        {
            var id = schemaRef.Reference.Id;
            if (!string.IsNullOrEmpty(id))
            {
                prop.IsReference  = true;
                prop.ReferenceType = StringHelpers.ToValidAplName(id.ToPascalCase());
                // Override the type to show the class name instead of "namespace".
                prop.Type = StringHelpers.ToValidAplName(id.ToCamelCase());
            }
        }
        // Reference detection — array whose items are a $ref.
        else if (prop.IsArray && propSchema.Items is OpenApiSchemaReference itemsRef)
        {
            var id = itemsRef.Reference.Id;
            if (!string.IsNullOrEmpty(id))
            {
                prop.IsReference  = true;
                prop.ReferenceType = StringHelpers.ToValidAplName(id.ToPascalCase());
                prop.Type = $"array[{StringHelpers.ToValidAplName(id.ToCamelCase())}]";
            }
        }

        context.Properties.Add(prop);
    }

    /// <summary>
    /// Resolves an allOf entry to a concrete schema, following $ref if present.
    /// </summary>
    private static IOpenApiSchema? ResolveSchema(IOpenApiSchema schema, OpenApiDocument? document)
    {
        if (schema is OpenApiSchemaReference r
            && r.Reference.Id != null
            && document?.Components?.Schemas != null)
        {
            document.Components.Schemas.TryGetValue(r.Reference.Id, out var resolved);
            return resolved;
        }
        return schema;
    }

    /// <summary>
    /// Renders a JsonNode default value to a human-readable string for comments.
    /// </summary>
    private static string? RenderDefaultValue(JsonNode node)
    {
        // JsonNode.ToString() on a string value includes surrounding quotes — strip them.
        if (node is JsonValue jv)
        {
            if (jv.TryGetValue<string>(out var s)) return s;
            return jv.ToString();
        }
        return node.ToString();
    }

    /// <summary>
    /// Converts an enum value to its API text, a name for its constant, and an APL literal of the
    /// right type: a quoted string, a number, or ⊂'true'/⊂'false' (as ⎕JSON represents booleans).
    /// Returns null for a null enum value, which has no constant.
    /// </summary>
    internal static EnumValue? ToEnumValue(JsonNode? node)
    {
        if (node is not JsonValue jv) return null;

        string apiValue;
        string literal;
        if (jv.TryGetValue<string>(out var s))
        {
            apiValue = s;
            literal  = StringHelpers.ToAplString(s);
        }
        else if (jv.TryGetValue<bool>(out var b))
        {
            apiValue = b ? "true" : "false";
            literal  = $"(⊂'{apiValue}')";
        }
        else
        {
            apiValue = jv.ToJsonString();
            literal  = apiValue.Replace('-', '¯');
        }

        // Strings get a readable PascalCase name; numbers and booleans are named by their
        // value, so 3 and -3 stay distinct.
        var name = s != null ? apiValue.ToPascalCase() : apiValue;
        return new EnumValue
        {
            ApiValue   = apiValue,
            AplName    = StringHelpers.ToValidAplName(name.Length > 0 ? name : apiValue),
            AplLiteral = literal
        };
    }
}
