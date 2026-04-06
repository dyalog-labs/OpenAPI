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
        IReadOnlyDictionary<string, IOpenApiSchema> inlineSchemas, string outputDirectory)
    {
        if (inlineSchemas.Count == 0) return;

        _logger.LogInformation("Generating {Count} inline schema model(s)...", inlineSchemas.Count);

        foreach (var (name, schema) in inlineSchemas)
        {
            // Inline schemas have no document context for $ref resolution, so pass null.
            var context = CreateModelContext(name, schema, document: null);
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
            ClassName   = StringHelpers.ToValidAplName(schemaName.ToPascalCase()),
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

        // Collect properties from the schema itself.
        if (schema.Properties != null)
        {
            foreach (var kvp in schema.Properties)
                AddProperty(context, kvp.Key, kvp.Value, schema);
        }

        // Flatten allOf sub-schemas (inheritance / extension pattern).
        if (schema.AllOf != null)
        {
            foreach (var allOfEntry in schema.AllOf)
            {
                var subSchema = ResolveSchema(allOfEntry, document);
                if (subSchema?.Properties == null) continue;

                foreach (var kvp in subSchema.Properties)
                {
                    // Dedup: skip if already added from the primary schema.
                    if (context.Properties.Any(p => p.ApiName == kvp.Key)) continue;
                    AddProperty(context, kvp.Key, kvp.Value, subSchema);
                }
            }
        }

        return context;
    }

    private static void AddProperty(
        ModelTemplateContext context,
        string rawKey,
        IOpenApiSchema propSchema,
        IOpenApiSchema parentSchema)
    {
        var dyalogName = StringHelpers.ToValidAplName(rawKey);

        var prop = new ModelProperty
        {
            ApiName    = rawKey,
            DyalogName = dyalogName,
            Type       = SchemaTypeMapper.MapSchemaTypeToAplType(propSchema),
            IsRequired = parentSchema.Required?.Contains(rawKey) ?? false,
            IsNullable = SchemaTypeMapper.IsNullable(propSchema),
            IsReadOnly = propSchema.ReadOnly,
            IsWriteOnly = propSchema.WriteOnly,
            Format     = propSchema.Format,
            Description = propSchema.Description,
            IsArray    = propSchema.Type == JsonSchemaType.Array
        };

        // Default value — render as string for the comment.
        if (propSchema.Default is JsonNode defaultNode)
            prop.DefaultValue = RenderDefaultValue(defaultNode);

        // Enum values.
        if (propSchema.Enum?.Count > 0)
        {
            prop.EnumValues = propSchema.Enum
                .Select(e => ExtractEnumString(e))
                .Where(v => v != null)
                .Select(v => new EnumValue
                {
                    ApiValue = v!,
                    AplName  = StringHelpers.ToValidAplName(v!.ToPascalCase())
                })
                .ToList();
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
        else if (propSchema.Type == JsonSchemaType.Array
                 && propSchema.Items is OpenApiSchemaReference itemsRef)
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
    /// Extracts the string representation of an enum value JsonNode.
    /// </summary>
    private static string? ExtractEnumString(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue jv && jv.TryGetValue<string>(out var s)) return s;
        // Non-string enums (integer, boolean) — render as-is.
        return node.ToString();
    }
}
