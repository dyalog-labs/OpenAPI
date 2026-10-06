using Microsoft.OpenApi;

namespace OpenAPIDyalog.Models;

/// <summary>
/// Represents an operation (endpoint) for code generation.
/// </summary>
public class OperationTemplateContext
{
    /// <summary>
    /// The operation ID.
    /// </summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method (get, post, put, delete, etc.).
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// The path/route for this operation.
    /// </summary>
    public string Path { get; set; } = string.Empty;
    
    /// <summary>
    /// The APL expression for the path for this operation.
    /// </summary>
    public string DyalogPath { get; set; } = string.Empty;

    /// <summary>
    /// Operation summary.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Operation description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Tags for grouping operations.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Parameters for this operation.
    /// </summary>
    public List<IOpenApiParameter> Parameters { get; set; } = new();

    /// <summary>
    /// Request body if present.
    /// </summary>
    public IOpenApiRequestBody? RequestBody { get; set; }

    /// <summary>
    /// The content type for the request body.
    /// </summary>
    public string? RequestContentType { get; set; }

    /// <summary>
    /// The model class for the JSON request body (or its items, for an array body), if any.
    /// </summary>
    public string? RequestJsonBodyType { get; set; }

    /// <summary>
    /// The field on argsNs that holds the JSON request body.
    /// </summary>
    public string? RequestBodyArgName { get; set; }

    /// <summary>
    /// Whether the JSON request body is an array.
    /// </summary>
    public bool RequestBodyIsArray { get; set; }

    /// <summary>
    /// Responses for this operation.
    /// </summary>
    public Dictionary<string, IOpenApiResponse> Responses { get; set; } = new();

    /// <summary>
    /// Whether this operation is deprecated.
    /// </summary>
    public bool Deprecated { get; set; }

    /// <summary>
    /// Form fields for multipart/form-data requests.
    /// </summary>
    public List<FormField> FormFields { get; set; } = new();

    /// <summary>
    /// Security requirements for this operation.
    /// Each item represents an alternative security requirement (OR relationship).
    /// Within each requirement, the schemes must all be satisfied (AND relationship).
    /// </summary>
    public List<OpenApiSecurityRequirement> Security { get; set; } = new();

    /// <summary>
    /// Gets the security scheme names required for this operation.
    /// Returns all unique security scheme names across all security requirements.
    /// </summary>
    /// <remarks>
    /// KNOWN INCONSISTENCY: This is derived data and could logically be a method, but it is kept as a
    /// property because the Scriban template references it as <c>security_scheme_names</c> (a property
    /// expression). Converting it to a method would require a template change, which is out of scope.
    /// </remarks>
    public List<string> SecuritySchemeNames
    {
        get
        {
            if (Security == null || Security.Count == 0)
                return new List<string>();

            return Security
                .SelectMany(req => req.Keys)
                .Select(schemeRef => schemeRef.Reference?.Id ?? string.Empty)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();
        }
    }

    /// <summary>
    /// Whether this operation requires any security/authentication.
    /// </summary>
    public bool HasSecurity => Security != null && Security.Count > 0;
}
