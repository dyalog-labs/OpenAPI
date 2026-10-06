using System.Text;
using System.Text.Json.Nodes;
using CaseConverter;
using Microsoft.OpenApi;
using OpenAPIDyalog.Constants;
using OpenAPIDyalog.Models;

namespace OpenAPIDyalog.Utils;

/// <summary>
/// Builds the documentation model for README.md and docs/&lt;tag&gt;.md, including a usage
/// example for each operation. Names come from <see cref="OperationNaming"/>, so the docs
/// describe exactly what the generated functions read.
/// </summary>
public sealed class DocsBuilder
{
    // Nested models in usage examples are expanded this many levels deep, then shown as ().
    private const int MaxNestingDepth = 3;
    // Comments in usage examples are aligned to this column at most.
    private const int MaxCommentColumn = 40;
    private const int MaxCommentDescription = 60;
    private const int MaxEnumValuesShown = 6;

    private readonly OpenApiDocument _document;
    private readonly IReadOnlyDictionary<string, IOpenApiSchema> _inlineSchemas;

    /// <param name="document">The API.</param>
    /// <param name="inlineSchemas">The inline request body schemas found by endpoint generation, keyed by
    /// the name of the model generated for each (which may be numbered to avoid a clash).</param>
    public DocsBuilder(OpenApiDocument document, IReadOnlyDictionary<string, IOpenApiSchema>? inlineSchemas = null)
    {
        _document      = document;
        _inlineSchemas = inlineSchemas ?? new Dictionary<string, IOpenApiSchema>();
    }

    /// <summary>
    /// Groups the document's operations by tag, in the order they appear in the spec.
    /// Tags that map to the same APL name share one group, as they share one Client field.
    /// </summary>
    public List<TagDoc> BuildTags()
    {
        var groups = new Dictionary<string, TagDoc>();

        foreach (var (path, pathItem) in _document.Paths ?? new OpenApiPaths())
        {
            if (pathItem?.Operations == null) continue;

            foreach (var (method, operation) in pathItem.Operations)
            {
                var tag     = OperationNaming.TagOf(operation);
                var aplName = OperationNaming.TagName(tag);

                if (!groups.TryGetValue(aplName, out var group))
                {
                    group = new TagDoc
                    {
                        Name        = tag,
                        AplName     = aplName,
                        DisplayName = ToDisplayName(tag),
                        Description = _document.Tags?.FirstOrDefault(t => t.Name == tag)?.Description
                    };
                    groups[aplName] = group;
                }

                group.Operations.Add(BuildOperation(aplName, path, method.ToString(), operation, pathItem));
            }
        }

        return groups.Values.ToList();
    }

    /// <summary>
    /// Lists the model classes generated from component schemas, then any synthesised for
    /// inline request bodies.
    /// </summary>
    public List<ModelDoc> BuildModels()
    {
        var models = SchemaHelpers.ModelComponents(_document)
            .Select(kvp => new ModelDoc
            {
                ClassName   = SchemaHelpers.ClassNameOf(kvp.Key),
                Description = kvp.Value.Description
            })
            .ToList();

        foreach (var name in _inlineSchemas.Keys)
        {
            models.Add(new ModelDoc
            {
                ClassName   = SchemaHelpers.ClassNameOf(name),
                Description = "Inline request body"
            });
        }

        return models;
    }

    private OperationDoc BuildOperation(
        string tagAplName, string path, string method, OpenApiOperation operation, IOpenApiPathItem pathItem)
    {
        var functionName = OperationNaming.FunctionName(operation.OperationId, method, path);

        // Cookie parameters are not read by the generated functions, so are not documented.
        var parameters = OperationNaming.MergeParameters(pathItem, operation)
            .Where(p => p.In is ParameterLocation.Path or ParameterLocation.Query or ParameterLocation.Header)
            .ToList();

        var doc = new OperationDoc
        {
            FunctionName = functionName,
            OperationId  = operation.OperationId,
            Method       = method.ToUpperInvariant(),
            Path         = path,
            Summary      = operation.Summary,
            Description  = operation.Description,
            Deprecated   = operation.Deprecated,
            Parameters   = parameters.Select(p => new ParameterDoc
            {
                Name        = p.Name ?? string.Empty,
                AplName     = StringHelpers.ToValidAplName(p.Name ?? string.Empty),
                Location    = p.In.ToString()!.ToLowerInvariant(),
                Required    = p.Required || p.In == ParameterLocation.Path,
                Type        = p.Schema != null ? TypeLabel(p.Schema) : "str",
                Description = p.Description
            }).ToList(),
            Responses = (operation.Responses ?? new OpenApiResponses())
                .Select(r => new ResponseDoc { Status = r.Key, Description = r.Value.Description })
                .ToList()
        };

        var example = new List<ExampleLine>();
        foreach (var (param, info) in parameters.Zip(doc.Parameters))
        {
            example.Add(new ExampleLine(
                Depth:     1,
                Commented: !info.Required,
                Code:      $"{info.AplName}: {ExampleValue(param.Schema, forParameter: true)}",
                Comment:   Annotate(info.Type, info.Required, param.Schema, param.Description)));
        }

        doc.RequestBody = DescribeRequestBody(operation, functionName, example);
        doc.UsageExample = RenderExample(example, $"client.{tagAplName}.{functionName}");
        return doc;
    }

    // ── Request bodies ───────────────────────────────────────────────────────

    private RequestBodyDoc? DescribeRequestBody(OpenApiOperation operation, string functionName, List<ExampleLine> example)
    {
        var requestBody = operation.RequestBody;
        if (requestBody?.Content == null || requestBody.Content.Count == 0) return null;

        // The generated function uses the first content type listed.
        var (contentType, mediaType) = requestBody.Content.First();
        var schema = mediaType?.Schema;
        var doc = new RequestBodyDoc { ContentType = contentType, Required = requestBody.Required };

        switch (contentType)
        {
            case GeneratorConstants.ContentTypeJson:
            {
                var body = schema != null ? OperationNaming.DescribeJsonBody(schema, functionName, _document) : null;
                doc.ArgName   = body?.ArgName ?? OperationNaming.UntypedJsonBodyArgName;
                doc.ModelName = body?.InlineSchema != null
                    ? InlineModelName(body.InlineSchema) ?? body.ModelName
                    : body?.ModelName;
                doc.IsArray   = body?.IsArray ?? false;

                var label = doc.ModelName == null
                    ? (schema != null ? TypeLabel(schema) : "any")
                    : doc.IsArray ? $"array[{doc.ModelName}]" : doc.ModelName;
                // The model's fields, for a body that is a model or an array of them.
                IOpenApiSchema? objectSchema = null;
                if (doc.ModelName != null)
                {
                    var target = body!.InlineSchema ?? Resolve(schema);
                    objectSchema = body.InlineSchema == null && doc.IsArray ? Resolve(target?.Items) : target;
                }

                if (objectSchema != null)
                    AddObject(example, 1, false, doc.ArgName, objectSchema, doc.IsArray,
                        Annotate(label, doc.Required, null, null), new HashSet<IOpenApiSchema>());
                else
                    example.Add(new ExampleLine(1, false, $"{doc.ArgName}: {ExampleValue(schema, forParameter: false)}",
                        Annotate(label, doc.Required, null, null)));
                break;
            }

            case GeneratorConstants.ContentTypeMultipartForm:
                foreach (var (key, prop) in schema?.Properties ?? new Dictionary<string, IOpenApiSchema>())
                {
                    var required = schema!.Required?.Contains(key) ?? false;
                    var isBinary = prop.Format == "binary";
                    example.Add(new ExampleLine(1, !required,
                        $"{StringHelpers.ToValidAplName(key.ToCamelCase())}: {(isBinary ? "'@/path/to/file'" : ExampleValue(prop, forParameter: true))}",
                        Annotate(isBinary ? "file" : TypeLabel(prop), required, prop, prop.Description)));
                }
                break;

            case GeneratorConstants.ContentTypeOctetStream:
                doc.ArgName = "body";
                example.Add(new ExampleLine(1, false, "body: 'raw bytes'", "binary data, as a character vector"));
                break;

            default:
                doc.ArgName = "data";
                example.Add(new ExampleLine(1, false, "data: '...'", contentType));
                break;
        }

        return doc;
    }

    /// <summary>
    /// Adds "name: (" … ")" for an object, with a line per writable property. An array of
    /// objects is shown as a one-item vector: "name: ,⊂(" … ")".
    /// </summary>
    private void AddObject(
        List<ExampleLine> lines, int depth, bool commented, string name, IOpenApiSchema schema,
        bool isArray, string comment, HashSet<IOpenApiSchema> visiting)
    {
        var open = isArray ? ",⊂(" : "(";
        var properties = SchemaHelpers.Properties(schema, _document).Where(p => !p.Schema.ReadOnly).ToList();

        if (properties.Count == 0 || depth > MaxNestingDepth || !visiting.Add(schema))
        {
            lines.Add(new ExampleLine(depth, commented, $"{name}: {open})", comment));
            return;
        }

        lines.Add(new ExampleLine(depth, commented, $"{name}: {open}", comment));
        foreach (var (key, propSchema, required) in properties)
        {
            var propName    = StringHelpers.ToValidAplName(key);
            var propComment = Annotate(TypeLabel(propSchema), required, propSchema, propSchema.Description);
            var resolved    = Resolve(propSchema) ?? propSchema;
            var isModelRef  = SchemaHelpers.IsModelReference(propSchema, _document);
            var isPropArray = !isModelRef && OperationNaming.IsType(resolved, JsonSchemaType.Array);
            var target      = isModelRef ? resolved
                            : isPropArray && Resolve(resolved.Items) is { } items && SchemaHelpers.IsObjectModel(items) ? items
                            : null;

            if (target != null)
                AddObject(lines, depth + 1, commented || !required, propName, target, isPropArray, propComment, visiting);
            else
                lines.Add(new ExampleLine(depth + 1, commented || !required,
                    $"{propName}: {ExampleValue(propSchema, forParameter: false)}", propComment));
        }
        lines.Add(new ExampleLine(depth, commented, ")", null));

        visiting.Remove(schema);
    }

    /// <summary>
    /// The name endpoint generation gave the model for an inline schema.
    /// </summary>
    private string? InlineModelName(IOpenApiSchema schema) =>
        _inlineSchemas.FirstOrDefault(kvp => ReferenceEquals(kvp.Value, schema)).Key is { } name
            ? StringHelpers.ToValidAplName(name.ToPascalCase())
            : null;

    // ── Example rendering ────────────────────────────────────────────────────

    private sealed record ExampleLine(int Depth, bool Commented, string Code, string? Comment);

    private static string RenderExample(List<ExampleLine> lines, string call)
    {
        var sb = new StringBuilder();
        if (lines.Count == 0)
        {
            sb.Append($"response ← {call} ()");
            return sb.ToString();
        }

        var texts  = lines.Select(l => new string(' ', 4 * l.Depth) + (l.Commented ? "⍝ " : "") + l.Code).ToList();
        var column = Math.Min(texts.Zip(lines).Where(t => t.Second.Comment != null).Select(t => t.First.Length).DefaultIfEmpty(0).Max(),
                              MaxCommentColumn) + 2;

        sb.AppendLine("args ← (");
        foreach (var (text, line) in texts.Zip(lines))
        {
            sb.Append(text);
            if (line.Comment != null)
                sb.Append(new string(' ', Math.Max(2, column - text.Length))).Append("⍝ ").Append(line.Comment);
            sb.AppendLine();
        }
        sb.AppendLine(")");
        sb.Append($"response ← {call} args");
        return sb.ToString();
    }

    private static string Annotate(string type, bool required, IOpenApiSchema? schema, string? description)
    {
        var parts = new List<string> { type };
        if (!required) parts.Add("optional");

        var enumValues = schema?.Enum?
            .Select(e => SchemaHelpers.IsJsonNull(e) ? "null"
                : e is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : e?.ToJsonString())
            .Where(v => v != null)
            .ToList();
        if (enumValues is { Count: > 0 })
            parts.Add("one of: " + string.Join(", ", enumValues.Take(MaxEnumValuesShown))
                      + (enumValues.Count > MaxEnumValuesShown ? ", …" : ""));

        var text = string.Join(", ", parts);
        var desc = StringHelpers.ToSingleLine(description);
        if (desc.Length > MaxCommentDescription)
            desc = desc[..(MaxCommentDescription - 1)].TrimEnd() + "…";
        return desc.Length > 0 ? $"{text} — {desc}" : text;
    }

    /// <summary>
    /// A placeholder APL value for a schema: its default or first enum value if it has one.
    /// Parameters are sent as text, so a boolean parameter is 'true'; in a JSON body it is ⊂'true'.
    /// </summary>
    private string ExampleValue(IOpenApiSchema? schema, bool forParameter)
    {
        schema = Resolve(schema);
        if (schema == null) return "'value'";

        var sample = (SchemaHelpers.IsJsonNull(schema.Default) ? null : schema.Default as JsonValue)
            ?? schema.Enum?.Where(e => !SchemaHelpers.IsJsonNull(e)).OfType<JsonValue>().FirstOrDefault();

        if (OperationNaming.IsType(schema, JsonSchemaType.String))
            return sample != null && sample.TryGetValue<string>(out var s) ? StringHelpers.ToAplString(s) : "'value'";

        if (OperationNaming.IsType(schema, JsonSchemaType.Integer) || OperationNaming.IsType(schema, JsonSchemaType.Number))
            return sample != null && !sample.TryGetValue<string>(out _) ? sample.ToJsonString().Replace('-', '¯') : "0";

        if (OperationNaming.IsType(schema, JsonSchemaType.Boolean))
        {
            var value = sample != null && sample.TryGetValue<bool>(out var b) && !b ? "false" : "true";
            return forParameter ? $"'{value}'" : $"⊂'{value}'";
        }

        if (OperationNaming.IsType(schema, JsonSchemaType.Array))
        {
            var items = Resolve(schema.Items);
            if (items != null && OperationNaming.IsType(items, JsonSchemaType.String))
                return forParameter ? "'value1' 'value2'" : ",⊂'value'";
            if (items != null && (OperationNaming.IsType(items, JsonSchemaType.Integer) || OperationNaming.IsType(items, JsonSchemaType.Number)))
                return forParameter ? "1 2" : ",0";
            return "⍬";
        }

        return SchemaHelpers.IsObjectModel(schema) ? "()" : "⍬";
    }

    // ── Schema helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// A short type description: a model name for a model, array[T] for an array, else str, int, etc.
    /// A $ref to a component that is not a model is described by that component's schema.
    /// </summary>
    private string TypeLabel(IOpenApiSchema schema)
    {
        if (SchemaHelpers.ReferenceId(schema) is { } id)
            return SchemaHelpers.IsModelReference(schema, _document)
                ? SchemaHelpers.ClassNameOf(id)
                : TypeLabel(Resolve(schema) is { } target && target != schema ? target : new OpenApiSchema());

        if (OperationNaming.IsType(schema, JsonSchemaType.Array))
            return schema.Items != null ? $"array[{TypeLabel(schema.Items)}]" : "array";

        return SchemaTypeMapper.MapSchemaTypeToAplType(schema);
    }

    private IOpenApiSchema? Resolve(IOpenApiSchema? schema) => SchemaHelpers.Resolve(schema, _document);

    private static string ToDisplayName(string tag) =>
        string.Join(" ", tag.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpper(w[0]) + w[1..]));
}
