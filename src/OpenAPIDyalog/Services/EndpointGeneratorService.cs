using CaseConverter;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using OpenAPIDyalog.Constants;
using OpenAPIDyalog.Models;
using OpenAPIDyalog.Services.Interfaces;
using OpenAPIDyalog.Utils;

namespace OpenAPIDyalog.Services;

/// <summary>
/// Generates APL endpoint function files from OpenAPI operations.
/// </summary>
public class EndpointGeneratorService
{
    private readonly ITemplateService _templateService;
    private readonly ILogger<EndpointGeneratorService> _logger;

    public EndpointGeneratorService(ITemplateService templateService, ILogger<EndpointGeneratorService> logger)
    {
        _templateService = templateService;
        _logger          = logger;
    }

    /// <summary>
    /// Generates all endpoint files and returns any inline schemas discovered.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IOpenApiSchema>> GenerateEndpointsAsync(
        OpenApiDocument document, string outputDirectory, string? @namespace = null)
    {
        var template = await _templateService.LoadTemplateAsync(GeneratorConstants.EndpointTemplate);

        var aplSourceDir = Path.Combine(outputDirectory, GeneratorConstants.AplSourceDir);
        var tagsDir      = Path.Combine(aplSourceDir, GeneratorConstants.TagsSubDir);
        var modelsDir    = Path.Combine(aplSourceDir, GeneratorConstants.ModelsSubDir);

        Directory.CreateDirectory(tagsDir);
        Directory.CreateDirectory(modelsDir);

        var inlineSchemas = new Dictionary<string, IOpenApiSchema>();
        var operationsByTag = GroupOperationsByTag(document);

        // Inline models share the models directory with component models, so must not take their names.
        var componentModels = SchemaHelpers.ModelComponents(document)
            .Select(kvp => SchemaHelpers.ClassNameOf(kvp.Key))
            .ToHashSet();

        foreach (var tagGroup in operationsByTag)
        {
            var tagDirName = OperationNaming.TagName(tagGroup.Key);
            var tagDir     = Path.Combine(tagsDir, tagDirName);
            Directory.CreateDirectory(tagDir);

            foreach (var (path, method, operation, pathItem) in tagGroup.Value)
            {
                var operationId = OperationNaming.FunctionName(operation.OperationId, method, path);

                var context = BuildOperationContext(path, method, operation, pathItem, document, operationId);
                ResolveRequestBody(operation, operationId, document, context, inlineSchemas, componentModels);

                var output     = await _templateService.RenderAsync(template, context);
                var outputPath = Path.Combine(tagDir, $"{operationId}.aplf");
                await _templateService.SaveOutputAsync(output, outputPath);

                _logger.LogInformation("Generated: {AplSourceDir}/{TagsSubDir}/{TagDir}/{OperationId}.aplf",
                    GeneratorConstants.AplSourceDir, GeneratorConstants.TagsSubDir, tagDirName, operationId);
            }
        }

        return inlineSchemas;
    }

    /// <summary>
    /// Groups all operations from the document by their first tag.
    /// Operations without a tag fall into "default".
    /// </summary>
    internal static Dictionary<string, List<(string path, string method, OpenApiOperation operation, IOpenApiPathItem pathItem)>>
        GroupOperationsByTag(OpenApiDocument document)
    {
        var result = new Dictionary<string, List<(string, string, OpenApiOperation, IOpenApiPathItem)>>();

        foreach (var path in document.Paths)
        {
            if (path.Value?.Operations == null) continue;

            foreach (var operation in path.Value.Operations)
            {
                var tag = OperationNaming.TagOf(operation.Value);

                if (!result.ContainsKey(tag))
                    result[tag] = new List<(string, string, OpenApiOperation, IOpenApiPathItem)>();

                result[tag].Add((path.Key, operation.Key.ToString().ToLowerInvariant(), operation.Value, path.Value));
            }
        }

        return result;
    }

    private static OperationTemplateContext BuildOperationContext(
        string path, string method, OpenApiOperation operation, IOpenApiPathItem pathItem,
        OpenApiDocument document, string operationId)
    {
        // Operation-level security overrides document-level.
        // Null → inherit from document; empty list → explicitly no security.
        var securityRequirements = operation.Security != null
            ? operation.Security.ToList()
            : document.Security?.ToList() ?? new List<OpenApiSecurityRequirement>();

        return new OperationTemplateContext
        {
            OperationId  = operationId,
            Method       = method,
            Path         = path,
            DyalogPath   = PathConverter.ToDyalogPath(path),
            Summary      = operation.Summary,
            Description  = operation.Description,
            Tags         = operation.Tags?.Select(t => t.Name).Where(n => n != null).Cast<string>().ToList() ?? new(),
            Parameters   = OperationNaming.MergeParameters(pathItem, operation),
            RequestBody  = operation.RequestBody,
            Responses    = operation.Responses?.ToDictionary(r => r.Key, r => r.Value) ?? new(),
            Deprecated   = operation.Deprecated,
            Security     = securityRequirements
        };
    }

    private static void ResolveRequestBody(
        OpenApiOperation operation,
        string operationId,
        OpenApiDocument document,
        OperationTemplateContext context,
        Dictionary<string, IOpenApiSchema> inlineSchemas,
        IReadOnlySet<string> componentModels)
    {
        if (operation.RequestBody?.Content == null) return;

        foreach (var content in operation.RequestBody.Content)
        {
            var contentType = content.Key;
            var mediaType   = content.Value;
            var schema      = mediaType.Schema;

            switch (contentType)
            {
                case GeneratorConstants.ContentTypeJson:
                    context.RequestContentType = contentType;
                    if (schema != null)
                        ResolveJsonBodyType(schema, operationId, document, context, inlineSchemas, componentModels);
                    else
                        context.RequestBodyArgName = OperationNaming.UntypedJsonBodyArgName;
                    break;

                case GeneratorConstants.ContentTypeOctetStream:
                    context.RequestContentType = contentType;
                    break;

                case GeneratorConstants.ContentTypeMultipartForm:
                    context.RequestContentType = contentType;
                    if (schema != null)
                        context.FormFields = ResolveFormFields(schema, mediaType);
                    break;

                default:
                    context.RequestContentType = contentType;
                    break;
            }

            // Use the first supported content type encountered.
            break;
        }
    }

    private static void ResolveJsonBodyType(
        IOpenApiSchema schema,
        string operationId,
        OpenApiDocument document,
        OperationTemplateContext context,
        Dictionary<string, IOpenApiSchema> inlineSchemas,
        IReadOnlySet<string> componentModels)
    {
        var body = OperationNaming.DescribeJsonBody(schema, operationId, document);
        if (body == null)
        {
            context.RequestBodyArgName = OperationNaming.UntypedJsonBodyArgName;
            return;
        }

        context.RequestBodyArgName  = body.ArgName;
        context.RequestBodyIsArray  = body.IsArray;
        context.RequestJsonBodyType = body.ModelName;

        // Inline object schemas get a synthesised model class. Its name may already be taken, by a
        // component model or by an operation with the same function name in another tag, so number
        // any repeats. The docs look the chosen name up from the returned inline schemas.
        if (body.InlineSchema != null)
        {
            var modelName = body.ModelName!;
            var counter   = 2;
            while (inlineSchemas.ContainsKey(modelName) || componentModels.Contains(modelName))
                modelName = $"{body.ModelName}{counter++}";
            inlineSchemas[modelName] = body.InlineSchema;
            context.RequestJsonBodyType = modelName;
        }
    }

    private static List<FormField> ResolveFormFields(IOpenApiSchema schema, IOpenApiMediaType mediaType)
    {
        var fields   = new List<FormField>();
        var encoding = mediaType.Encoding;

        if (schema.Properties == null) return fields;

        foreach (var property in schema.Properties)
        {
            var fieldName  = property.Key;
            var fieldSchema = property.Value;

            var formField = new FormField
            {
                ApiName    = fieldName,
                DyalogName = StringHelpers.ToValidAplName(fieldName.ToCamelCase()),
                IsRequired = schema.Required?.Contains(fieldName) ?? false,
                Description = fieldSchema.Description,
                IsArray    = fieldSchema.Type == JsonSchemaType.Array,
                IsBinary   = fieldSchema.Format == "binary"
            };

            formField.Type = formField.IsBinary
                ? "binary"
                : SchemaTypeMapper.MapSchemaTypeToAplType(fieldSchema);

            if (encoding != null && encoding.TryGetValue(fieldName, out var encodingValue))
                formField.ContentType = encodingValue.ContentType;

            fields.Add(formField);
        }

        return fields;
    }
}
