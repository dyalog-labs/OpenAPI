namespace OpenAPIDyalog.Models;

/// <summary>
/// The operations under one tag, as documented in README.md and docs/&lt;tag&gt;.md.
/// </summary>
public class TagDoc
{
    /// <summary>The tag as written in the spec (the first one, if several map to the same APL name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The APL name: the Client field (client.&lt;AplName&gt;) and the docs file name.</summary>
    public string AplName { get; set; } = string.Empty;

    /// <summary>A heading for the tag, e.g. "store-orders" → "Store Orders".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The tag's description from the spec's top-level tags list.</summary>
    public string? Description { get; set; }

    /// <summary>Path of the tag's page, relative to the output directory.</summary>
    public string DocPath => $"docs/{AplName}.md";

    public List<OperationDoc> Operations { get; set; } = new();
}

/// <summary>
/// One operation, as documented on its tag's page.
/// </summary>
public class OperationDoc
{
    /// <summary>The generated function's name, also the anchor of its section.</summary>
    public string FunctionName { get; set; } = string.Empty;

    public string? OperationId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public bool Deprecated { get; set; }

    /// <summary>Parameters the generated function reads from its argument namespace.</summary>
    public List<ParameterDoc> Parameters { get; set; } = new();

    /// <summary>The request body, or null if the operation takes none.</summary>
    public RequestBodyDoc? RequestBody { get; set; }

    public List<ResponseDoc> Responses { get; set; } = new();

    /// <summary>A complete, runnable APL example of calling the operation.</summary>
    public string UsageExample { get; set; } = string.Empty;
}

/// <summary>
/// A path, query or header parameter.
/// </summary>
public class ParameterDoc
{
    /// <summary>The name as written in the spec, and as sent to the server.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The field to set on the argument namespace; differs from Name only when that is not valid APL.</summary>
    public string AplName { get; set; } = string.Empty;

    /// <summary>"path", "query" or "header".</summary>
    public string Location { get; set; } = string.Empty;

    public bool Required { get; set; }

    /// <summary>A short type description, e.g. str, int, array[str] or a model name.</summary>
    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }
}

/// <summary>
/// An operation's request body.
/// </summary>
public class RequestBodyDoc
{
    public string ContentType { get; set; } = string.Empty;
    public bool Required { get; set; }

    /// <summary>The field on the argument namespace that holds the body (absent for multipart, where each form field is its own).</summary>
    public string? ArgName { get; set; }

    /// <summary>The model class for a JSON body (or its items), if one is generated.</summary>
    public string? ModelName { get; set; }

    /// <summary>Whether a JSON body is an array.</summary>
    public bool IsArray { get; set; }
}

/// <summary>
/// One documented response.
/// </summary>
public class ResponseDoc
{
    public string Status { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// A generated model class, as listed in README.md.
/// </summary>
public class ModelDoc
{
    public string ClassName { get; set; } = string.Empty;
    public string? Description { get; set; }
}
