using CaseConverter;
using Microsoft.OpenApi;
using OpenAPIDyalog.Constants;
using OpenAPIDyalog.Utils;
using System.Text.Json.Nodes;

namespace OpenAPIDyalog.Models;

/// <summary>
/// Represents the data context passed to Scriban templates for code generation.
/// </summary>
public class ApiTemplateContext : ITemplateContext
{
    /// <summary>
    /// The OpenAPI document being processed.
    /// </summary>
    public OpenApiDocument Document { get; set; } = null!;

    /// <summary>
    /// API title.
    /// </summary>
    public string Title => Document?.Info?.Title ?? "API";

    /// <summary>
    /// API version.
    /// </summary>
    public string Version => Document?.Info?.Version ?? "1.0.0";

    /// <summary>
    /// API description.
    /// </summary>
    public string? Description => Document.Info.Description;

    /// <summary>
    /// Generated code namespace.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Timestamp when the code was generated.
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// All paths in the API.
    /// </summary>
    public Dictionary<string, IOpenApiPathItem> Paths => 
        Document?.Paths?.ToDictionary(p => p.Key, p => p.Value) ?? new();

    /// <summary>
    /// All schemas/models in the API.
    /// </summary>
    public Dictionary<string, IOpenApiSchema> Schemas => 
        Document?.Components?.Schemas?.ToDictionary(s => s.Key, s => s.Value) 
        ?? new();

    /// <summary>
    /// All servers defined in the API.
    /// </summary>
    public List<OpenApiServer> Servers => Document?.Servers?.ToList() ?? new();

    /// <summary>
    /// Base server URL (first server if available).
    /// </summary>
    public string? BaseUrl => Servers.FirstOrDefault()?.Url;

    /// <summary>
    /// All security schemes defined in the API, converted to a template-friendly format.
    /// </summary>
    public Dictionary<string, SecuritySchemeInfo> SecuritySchemes
    {
        get
        {
            var schemes = new Dictionary<string, SecuritySchemeInfo>();

            if (Document?.Components?.SecuritySchemes == null)
                return schemes;

            foreach (var scheme in Document.Components.SecuritySchemes)
            {
                schemes[scheme.Key] = new SecuritySchemeInfo
                {
                    Name = scheme.Key,
                    Type = scheme.Value.Type?.ToString().ToLowerInvariant() ?? "unknown",
                    Description = scheme.Value.Description,
                    In = scheme.Value.In?.ToString().ToLowerInvariant(),
                    ParameterName = scheme.Value.Name,
                    Scheme = scheme.Value.Scheme,
                    BearerFormat = scheme.Value.BearerFormat
                };
            }

            return schemes;
        }
    }

    /// <summary>
    /// Additional custom properties for template use.
    /// </summary>
    public Dictionary<string, object> CustomProperties { get; set; } = new();

    /// <summary>
    /// Gets all operation IDs from the document.
    /// </summary>
    public IEnumerable<string> GetOperationIds()
    {
        if (Paths == null) return Enumerable.Empty<string>();
        
        return Paths.Values
            .Where(path => path.Operations != null)
            .SelectMany(path => path.Operations!.Values)
            .Where(op => !string.IsNullOrEmpty(op.OperationId))
            .Select(op => op.OperationId!)
            .Where(id => id != null);
    }

    /// <summary>
    /// Gets all tags used in the API.
    /// </summary>
    public IEnumerable<string> GetAllTags()
    {
        if (Paths == null) return Enumerable.Empty<string>();

        var operations = Paths.Values
            .Where(path => path.Operations != null)
            .SelectMany(path => path.Operations!.Values)
            .ToList();

        var explicitTags = operations
            .Where(op => op.Tags is { Count: > 0 })
            .SelectMany(op => op.Tags!)
            .Select(tag => tag.Name)
            .Where(name => name != null)
            .Cast<string>()
            .Distinct();

        var hasTaglessOperations = operations.Any(op => op.Tags == null || op.Tags.Count == 0);
        if (hasTaglessOperations)
            return explicitTags.Append(GeneratorConstants.DefaultTagName).Distinct();

        return explicitTags;
    }

    /// <summary>
    /// Gets all operations grouped by tag.
    /// </summary>
    public Dictionary<string, List<OperationInfo>> GetOperationsByTag()
    {
        var operationsByTag = new Dictionary<string, List<OperationInfo>>();

        if (Paths == null) return operationsByTag;

        foreach (var path in Paths)
        {
            if (path.Value?.Operations == null) continue;

            foreach (var operation in path.Value.Operations)
            {
                var op = operation.Value;
                var tag = op.Tags?.FirstOrDefault()?.Name ?? "default";

                if (!operationsByTag.ContainsKey(tag))
                {
                    operationsByTag[tag] = new List<OperationInfo>();
                }

                var rawId = op.OperationId ?? $"{operation.Key}_{path.Key}";
                var aplName = StringHelpers.ToValidAplName(rawId.Replace("/", "_").ToPascalCase());
                var aplTag = StringHelpers.ToValidAplName(tag.ToCamelCase());

                // Merge path-item-level parameters with operation-level parameters.
                // Operation-level params shadow path-level params of the same name.
                var pathLevelParams = path.Value.Parameters?.ToList() ?? new List<IOpenApiParameter>();
                var opLevelParams   = op.Parameters?.ToList() ?? new List<IOpenApiParameter>();
                var allParams = pathLevelParams
                    .Where(pp => !opLevelParams.Any(op2 => op2.Name == pp.Name))
                    .Concat(opLevelParams)
                    .Select(MapToParameterInfo)
                    .ToList();

                var info = new OperationInfo
                {
                    OperationId     = rawId,
                    AplName         = aplName,
                    AplTag          = aplTag,
                    Method          = operation.Key.ToString().ToUpperInvariant(),
                    Path            = path.Key,
                    Summary         = op.Summary,
                    Description     = op.Description,
                    Parameters      = allParams,
                    PathParameters  = allParams.Where(p => p.Location == "Path").ToList(),
                    QueryParameters = allParams.Where(p => p.Location == "Query").ToList(),
                    OtherParameters = allParams.Where(p => p.Location != "Path" && p.Location != "Query").ToList(),
                    HasRequestBody  = op.RequestBody != null
                };

                ResolveRequestBodyForDocs(op, info);
                operationsByTag[tag].Add(info);
            }
        }

        return operationsByTag;
    }

    /// <summary>
    /// Helper class for operation information in templates.
    /// </summary>
    public class OperationInfo
    {
        public string OperationId { get; set; } = string.Empty;
        public string AplName { get; set; } = string.Empty;
        public string AplTag { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string? Description { get; set; }
        /// <summary>All parameters combined (path + query + header), for the reference table.</summary>
        public List<ParameterInfo> Parameters { get; set; } = new();
        /// <summary>Path parameters (always required).</summary>
        public List<ParameterInfo> PathParameters { get; set; } = new();
        /// <summary>Query parameters.</summary>
        public List<ParameterInfo> QueryParameters { get; set; } = new();
        /// <summary>Header/Cookie parameters.</summary>
        public List<ParameterInfo> OtherParameters { get; set; } = new();
        public bool HasRequestBody { get; set; }

        /// <summary>Content type of the request body (e.g. "application/json").</summary>
        public string? RequestContentType { get; set; }

        /// <summary>APL class name for a named JSON body model (PascalCase, e.g. "Pet").</summary>
        public string? RequestBodyModelName { get; set; }

        /// <summary>APL arg name used to attach the body to the args namespace (camelCase, e.g. "pet").</summary>
        public string? RequestBodyParamName { get; set; }

        /// <summary>Whether the request body is required.</summary>
        public bool RequestBodyRequired { get; set; }

        /// <summary>Resolved properties for a JSON request body.</summary>
        public List<RequestBodyField>? RequestBodyFields { get; set; }

        /// <summary>Form fields for a multipart/form-data request body.</summary>
        public List<FormField>? FormFields { get; set; }
    }

    /// <summary>
    /// A single field in a JSON request body, for use in documentation templates.
    /// </summary>
    public class RequestBodyField
    {
        /// <summary>APL-safe property name (ToValidAplName of the JSON key).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Original JSON key.</summary>
        public string ApiName { get; set; } = string.Empty;

        /// <summary>APL type string (str, int, bool, array[T], or a model class name).</summary>
        public string Type { get; set; } = "any";

        public bool IsRequired { get; set; }
        public bool IsReadOnly { get; set; }

        /// <summary>Default value as a string, if declared in the schema.</summary>
        public string? DefaultValue { get; set; }

        public string? Description { get; set; }

        /// <summary>Allowed enum values, if the property is an enum.</summary>
        public List<string>? EnumValues { get; set; }

        /// <summary>True when this field is a direct $ref to a named model.</summary>
        public bool IsModelRef { get; set; }

        /// <summary>PascalCase APL class name for a model-ref field (e.g. "Pet").</summary>
        public string? ModelRefName { get; set; }

        /// <summary>True when this field is an array whose items are a named model $ref.</summary>
        public bool IsArrayOfModels { get; set; }

        /// <summary>PascalCase APL class name for the array-item model (e.g. "Pet").</summary>
        public string? ArrayItemModelName { get; set; }

        /// <summary>
        /// Recursively resolved fields of the referenced model (populated for IsModelRef and
        /// IsArrayOfModels fields). Null if the model could not be resolved or the cycle detector fired.
        /// </summary>
        public List<RequestBodyField>? NestedFields { get; set; }

        /// <summary>
        /// A sensible placeholder value for use in APL code examples.
        /// Prefers the default value when available.
        /// </summary>
        public string ExampleValue
        {
            get
            {
                if (IsModelRef && ModelRefName != null)
                    return $"⎕NEW models.{ModelRefName} (...)";

                if (IsArrayOfModels && ArrayItemModelName != null)
                    return $"(⎕NEW models.{ArrayItemModelName} (...))";

                if (DefaultValue != null)
                    return Type == "str" ? $"'{DefaultValue}'" : DefaultValue;

                return Type switch
                {
                    "str"    => EnumValues?.Count > 0 ? $"'{EnumValues[0]}'" : "'value'",
                    "int"    => "0",
                    "number" => "0.0",
                    "bool"   => "⊂'true'",
                    _        => "⍬"
                };
            }
        }
    }

    // ── Parameter mapping helpers ─────────────────────────────────────────────

    private static ParameterInfo MapToParameterInfo(IOpenApiParameter p)
    {
        var schema = p.Schema;
        var type = schema != null ? SchemaTypeMapper.MapSchemaTypeToAplType(schema) : "str";

        List<string>? enumValues = null;
        if (schema?.Enum?.Count > 0)
        {
            enumValues = schema.Enum
                .Select(e => e is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : e?.ToString())
                .Where(v => v != null)
                .Cast<string>()
                .ToList();
        }

        string? defaultValue = null;
        if (schema?.Default is JsonNode defaultNode)
        {
            if (defaultNode is JsonValue jv2 && jv2.TryGetValue<string>(out var s2))
                defaultValue = s2;
            else
                defaultValue = defaultNode.ToString();
        }

        string exampleValue;
        if (defaultValue != null)
            exampleValue = type == "str" ? $"'{defaultValue}'" : defaultValue;
        else
            exampleValue = type switch
            {
                "str"    => enumValues?.Count > 0 ? $"'{enumValues[0]}'" : "'value'",
                "int"    => "0",
                "number" => "0.0",
                "bool"   => "⊂'true'",
                _        => "⍬"
            };

        return new ParameterInfo
        {
            Name         = p.Name ?? string.Empty,
            AplName      = StringHelpers.ToValidAplName(p.Name ?? string.Empty),
            Location     = p.In?.ToString() ?? "Query",
            Required     = p.Required,
            Type         = type,
            Description  = p.Description,
            ExampleValue = exampleValue
        };
    }

    // ── Request body resolution helpers (for doc templates) ──────────────────

    private void ResolveRequestBodyForDocs(OpenApiOperation op, OperationInfo info)
    {
        if (op.RequestBody?.Content == null || op.RequestBody.Content.Count == 0) return;

        var firstContent = op.RequestBody.Content.First();
        info.RequestContentType  = firstContent.Key;
        info.RequestBodyRequired = op.RequestBody.Required;

        var schema = firstContent.Value?.Schema;
        if (schema == null) return;

        if (firstContent.Key == GeneratorConstants.ContentTypeJson)
        {
            if (schema is OpenApiSchemaReference refSchema && refSchema.Reference?.Id != null)
            {
                var id = refSchema.Reference.Id;
                info.RequestBodyModelName = StringHelpers.ToValidAplName(id.ToPascalCase());
                info.RequestBodyParamName = StringHelpers.ToValidAplName(id.ToCamelCase());
                if (Document.Components?.Schemas?.TryGetValue(id, out var resolved) == true)
                    info.RequestBodyFields = ExtractBodyFields(resolved, Document);
            }
            else if (schema.Properties != null && schema.Properties.Count > 0)
            {
                info.RequestBodyFields = ExtractBodyFields(schema, Document);
            }
        }
        else if (firstContent.Key == GeneratorConstants.ContentTypeMultipartForm)
        {
            info.FormFields = ExtractDocFormFields(schema);
        }
    }

    private static List<RequestBodyField> ExtractBodyFields(
        IOpenApiSchema schema, OpenApiDocument? document, HashSet<string>? visiting = null)
    {
        visiting ??= new(StringComparer.Ordinal);
        var fields = new List<RequestBodyField>();

        if (schema.Properties != null)
        {
            foreach (var kvp in schema.Properties)
                fields.Add(MakeBodyField(kvp.Key, kvp.Value, schema, document, visiting));
        }

        if (schema.AllOf != null)
        {
            foreach (var entry in schema.AllOf)
            {
                IOpenApiSchema? sub = null;
                if (entry is OpenApiSchemaReference r && r.Reference?.Id != null)
                    document?.Components?.Schemas?.TryGetValue(r.Reference.Id, out sub);
                else
                    sub = entry;

                if (sub?.Properties == null) continue;
                foreach (var kvp in sub.Properties)
                {
                    if (fields.Any(f => f.ApiName == kvp.Key)) continue;
                    fields.Add(MakeBodyField(kvp.Key, kvp.Value, sub, document, visiting));
                }
            }
        }

        return fields;
    }

    private static RequestBodyField MakeBodyField(
        string key, IOpenApiSchema propSchema, IOpenApiSchema parentSchema,
        OpenApiDocument? document, HashSet<string> visiting)
    {
        var type = SchemaTypeMapper.MapSchemaTypeToAplType(propSchema);

        bool isModelRef = false;
        string? modelRefName = null;
        bool isArrayOfModels = false;
        string? arrayItemModelName = null;
        List<RequestBodyField>? nestedFields = null;

        if (propSchema is OpenApiSchemaReference refSchema && refSchema.Reference?.Id != null)
        {
            var id = refSchema.Reference.Id;
            modelRefName = StringHelpers.ToValidAplName(id.ToPascalCase());
            type = modelRefName;
            isModelRef = true;

            // Recursively resolve the nested model's fields (cycle-safe)
            if (!visiting.Contains(id) && document?.Components?.Schemas?.TryGetValue(id, out var resolved) == true)
            {
                visiting.Add(id);
                nestedFields = ExtractBodyFields(resolved, document, visiting);
                visiting.Remove(id);
            }
        }
        else if ((propSchema.Type & ~JsonSchemaType.Null) == JsonSchemaType.Array
                 && propSchema.Items is OpenApiSchemaReference itemRef
                 && itemRef.Reference?.Id != null)
        {
            var id = itemRef.Reference.Id;
            arrayItemModelName = StringHelpers.ToValidAplName(id.ToPascalCase());
            type = $"array[{arrayItemModelName}]";
            isArrayOfModels = true;

            // Recursively resolve the array-item model's fields (cycle-safe)
            if (!visiting.Contains(id) && document?.Components?.Schemas?.TryGetValue(id, out var resolved) == true)
            {
                visiting.Add(id);
                nestedFields = ExtractBodyFields(resolved, document, visiting);
                visiting.Remove(id);
            }
        }

        string? defaultValue = null;
        if (propSchema.Default is JsonNode defaultNode)
        {
            if (defaultNode is JsonValue jv && jv.TryGetValue<string>(out var s))
                defaultValue = s;
            else
                defaultValue = defaultNode.ToString();
        }

        List<string>? enumValues = null;
        if (propSchema.Enum?.Count > 0)
        {
            enumValues = propSchema.Enum
                .Select(e => e is JsonValue jv2 && jv2.TryGetValue<string>(out var s2) ? s2 : e?.ToString())
                .Where(v => v != null)
                .Cast<string>()
                .ToList();
        }

        return new RequestBodyField
        {
            ApiName            = key,
            Name               = StringHelpers.ToValidAplName(key),
            Type               = type,
            IsRequired         = parentSchema.Required?.Contains(key) ?? false,
            IsReadOnly         = propSchema.ReadOnly,
            DefaultValue       = defaultValue,
            Description        = propSchema.Description,
            EnumValues         = enumValues,
            IsModelRef         = isModelRef,
            ModelRefName       = modelRefName,
            IsArrayOfModels    = isArrayOfModels,
            ArrayItemModelName = arrayItemModelName,
            NestedFields       = nestedFields
        };
    }

    private static List<FormField> ExtractDocFormFields(IOpenApiSchema schema)
    {
        var fields = new List<FormField>();
        if (schema.Properties == null) return fields;

        foreach (var kvp in schema.Properties)
        {
            var propSchema = kvp.Value;
            var field = new FormField
            {
                ApiName    = kvp.Key,
                DyalogName = StringHelpers.ToValidAplName(kvp.Key.ToCamelCase()),
                IsRequired = schema.Required?.Contains(kvp.Key) ?? false,
                Description = propSchema.Description,
                IsArray    = propSchema.Type == JsonSchemaType.Array,
                IsBinary   = propSchema.Format == "binary"
            };
            field.Type = field.IsBinary ? "binary" : SchemaTypeMapper.MapSchemaTypeToAplType(propSchema);
            fields.Add(field);
        }

        return fields;
    }

    /// <summary>
    /// A single request parameter (path, query, header, or cookie), for use in documentation templates.
    /// </summary>
    public class ParameterInfo
    {
        /// <summary>Original API parameter name (for table display).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>APL-safe name via ToValidAplName (for code examples).</summary>
        public string AplName { get; set; } = string.Empty;

        /// <summary>"Path", "Query", "Header", or "Cookie".</summary>
        public string Location { get; set; } = string.Empty;

        public bool Required { get; set; }

        /// <summary>APL type string (str, int, bool, array[str], etc.).</summary>
        public string Type { get; set; } = "str";

        public string? Description { get; set; }

        /// <summary>Sensible placeholder value for use in APL code examples.</summary>
        public string ExampleValue { get; set; } = "'value'";
    }

    /// <summary>
    /// Helper class for security scheme information in templates.
    /// </summary>
    public class SecuritySchemeInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? In { get; set; }
        public string? ParameterName { get; set; }
        public string? Scheme { get; set; }
        public string? BearerFormat { get; set; }
    }
}
