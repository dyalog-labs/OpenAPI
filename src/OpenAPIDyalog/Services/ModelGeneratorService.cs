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
    /// Generates a model file for each object schema in document.Components.Schemas. Array and
    /// primitive schemas get no class: wherever they are used, their values are used directly.
    /// Call <see cref="SchemaHelpers.CheckModelClassNames"/> first, as clashing names overwrite each other.
    /// </summary>
    public async Task GenerateComponentModelsAsync(OpenApiDocument document, string outputDirectory)
    {
        var schemas = SchemaHelpers.ModelComponents(document).ToList();
        if (schemas.Count == 0)
        {
            _logger.LogDebug("No component object schemas found.");
            return;
        }

        _logger.LogInformation("Generating {Count} component model(s)...", schemas.Count);

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
            ClassName   = SchemaHelpers.ClassNameOf(schemaName),
            Description = schema.Description ?? sourceInfo
        };

        // Map type: no named properties, and any keys allowed (a free-form object).
        if (SchemaHelpers.IsMap(schema))
        {
            context.IsMapType    = true;
            context.MapValueType = schema.AdditionalProperties != null
                ? SchemaTypeMapper.MapSchemaTypeToAplType(schema.AdditionalProperties)
                : null;
            return context;
        }

        // The schema's own properties, then those inherited through allOf (inheritance / extension pattern).
        foreach (var (key, propSchema, required) in SchemaHelpers.Properties(schema, document))
            AddProperty(context, key, propSchema, required, document);

        // Property names that differ only in punctuation (foo-bar, foo_bar) give the same readable
        // enum constants name; fall back to the mangled property name, which is unique.
        foreach (var clash in context.EnumProperties.GroupBy(p => p.EnumFieldName).Where(g => g.Count() > 1))
            foreach (var prop in clash)
                prop.EnumFieldName = prop.DyalogName;

        return context;
    }

    private static void AddProperty(
        ModelTemplateContext context,
        string rawKey,
        IOpenApiSchema propSchema,
        bool isRequired,
        OpenApiDocument? document)
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
        if (propSchema.Default is JsonNode defaultNode && !SchemaHelpers.IsJsonNull(defaultNode))
            prop.DefaultValue = RenderDefaultValue(defaultNode);

        // Enum values.
        if (propSchema.Enum?.Count > 0)
        {
            prop.EnumValues = propSchema.Enum
                .Select(ToEnumValue)
                .Where(v => v != null)
                .Cast<EnumValue>()
                .ToList();

            // JSON null has no constant, but a nullable enum accepts it.
            prop.EnumAllowsNull = prop.IsNullable || propSchema.Enum.Any(SchemaHelpers.IsJsonNull);

            // Values whose readable names collide (e.g. "in-stock" and "in_stock") fall back to
            // the mangled value itself, which is unique.
            foreach (var group in prop.EnumValues.GroupBy(v => v.AplName).Where(g => g.Count() > 1))
                foreach (var v in group)
                    v.AplName = StringHelpers.ToValidAplName(v.ApiValue);
        }

        // A $ref to a component model, or an array of them, holds model instances. A $ref to any
        // other component (an array of models, a string, …) is treated as that component's schema.
        var schema = propSchema;
        if (SchemaHelpers.ReferenceId(propSchema) is { } id)
        {
            if (SchemaHelpers.IsModelReference(propSchema, document))
            {
                prop.IsReference   = true;
                prop.ReferenceType = SchemaHelpers.ClassNameOf(id);
                prop.Type          = prop.ReferenceType;
            }
            else
            {
                schema = SchemaHelpers.Resolve(propSchema, document) ?? propSchema;
                prop.IsArray       = OperationNaming.IsType(schema, JsonSchemaType.Array);
                prop.IsStringArray = prop.IsArray && schema.Items != null
                    && OperationNaming.IsType(schema.Items, JsonSchemaType.String);
                prop.Type          = SchemaTypeMapper.MapSchemaTypeToAplType(schema);
            }
        }

        if (prop.IsArray && SchemaHelpers.IsModelReference(schema.Items, document))
        {
            prop.IsReference   = true;
            prop.ReferenceType = SchemaHelpers.ClassNameOf(SchemaHelpers.ReferenceId(schema.Items)!);
            prop.Type          = $"array[{prop.ReferenceType}]";
        }

        context.Properties.Add(prop);
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
        if (node is not JsonValue jv || SchemaHelpers.IsJsonNull(node)) return null;

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
