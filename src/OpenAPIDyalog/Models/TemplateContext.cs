using Microsoft.OpenApi;
using OpenAPIDyalog.Constants;
using OpenAPIDyalog.Utils;

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
    /// Whether the API defines any security schemes.
    /// </summary>
    public bool HasSecuritySchemes => Document?.Components?.SecuritySchemes?.Count > 0;

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
    /// Names of the models synthesised for inline request bodies, listed in the README
    /// after the component models.
    /// </summary>
    public IEnumerable<string> InlineModelNames { get; set; } = Enumerable.Empty<string>();

    private List<TagDoc>? _tagDocs;

    /// <summary>
    /// Operations grouped by tag, with usage examples, for README.md and docs/&lt;tag&gt;.md.
    /// </summary>
    public List<TagDoc> TagDocs => _tagDocs ??= new DocsBuilder(Document).BuildTags();

    /// <summary>
    /// The generated model classes, for README.md.
    /// </summary>
    public List<ModelDoc> Models => new DocsBuilder(Document).BuildModels(InlineModelNames);

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
