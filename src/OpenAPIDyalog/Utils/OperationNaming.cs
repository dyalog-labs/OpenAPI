using System.Text.RegularExpressions;
using CaseConverter;
using Microsoft.OpenApi;
using OpenAPIDyalog.Constants;

namespace OpenAPIDyalog.Utils;

/// <summary>
/// Naming rules shared by code generation and documentation generation, so that the
/// generated docs always describe the names the generated code actually uses.
/// </summary>
public static class OperationNaming
{
    /// <summary>
    /// The tag an operation is grouped under: its first tag, or "default" if it has none.
    /// </summary>
    public static string TagOf(OpenApiOperation operation) =>
        operation.Tags?.FirstOrDefault()?.Name ?? GeneratorConstants.DefaultTagName;

    /// <summary>
    /// The APL name for a tag: the Client field, the _tags sub-directory and the docs file name.
    /// Always a valid APL identifier, so it is also safe to use as a file name.
    /// </summary>
    public static string TagName(string tag) =>
        StringHelpers.ToValidAplName(tag.ToCamelCase());

    /// <summary>
    /// The APL function name for an operation (also its .aplf file name).
    /// Falls back to the method and path when the operation has no operationId.
    /// </summary>
    public static string FunctionName(string? operationId, string method, string path)
    {
        var rawId = operationId
            ?? $"{method.ToLowerInvariant()}_{path.Replace("/", "_").Replace("{", "").Replace("}", "")}";
        return StringHelpers.ToValidAplName(rawId.Replace("/", "_").ToPascalCase());
    }

    /// <summary>
    /// Checks that no two operations map to the same function in the same Client namespace, which
    /// would make one overwrite the other (e.g. operationIds listPets and list_pets, or tags
    /// "store orders" and "store-orders"). Throws an exception naming the operations if they do.
    /// </summary>
    public static void CheckFunctionNames(OpenApiDocument document)
    {
        var clashes = (document.Paths ?? new OpenApiPaths())
            .SelectMany(path => (path.Value?.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>())
                .Select(op => (Path: path.Key, Method: op.Key.ToString().ToUpperInvariant(), Operation: op.Value)))
            .GroupBy(op => $"{TagName(TagOf(op.Operation))}.{FunctionName(op.Operation.OperationId, op.Method, op.Path)}")
            .Where(g => g.Count() > 1)
            .Select(g => $"{string.Join(", ", g.Select(op => $"{op.Method} {op.Path}"))} (all client.{g.Key})")
            .ToList();

        if (clashes.Count > 0)
            throw new InvalidOperationException(
                "Some operations would generate the same function: " + string.Join("; ", clashes)
                + ". Give each a distinct operationId in the specification.");
    }

    /// <summary>
    /// The parameters that apply to an operation: those declared on the path item, overridden
    /// by any operation-level parameter with the same name and location.
    /// The path parameters are made to match the {placeholders} in the path, as the generated
    /// function builds its URL from them: a placeholder that is not declared (a fault in the spec)
    /// becomes a required string parameter, and a declared path parameter that is not in the
    /// path is dropped.
    /// </summary>
    public static List<IOpenApiParameter> MergeParameters(string path, IOpenApiPathItem? pathItem, OpenApiOperation operation)
    {
        var opLevel   = operation.Parameters?.ToList() ?? new List<IOpenApiParameter>();
        var pathLevel = pathItem?.Parameters?.ToList() ?? new List<IOpenApiParameter>();

        var placeholders = Regex.Matches(path, @"\{([^}]+)\}").Select(m => m.Groups[1].Value).ToList();
        var merged = pathLevel
            .Where(pp => !opLevel.Any(op => op.Name == pp.Name && op.In == pp.In))
            .Concat(opLevel)
            .Where(p => p.In != ParameterLocation.Path || placeholders.Contains(p.Name ?? string.Empty))
            .ToList();

        foreach (var name in placeholders)
        {
            if (!merged.Any(p => p.In == ParameterLocation.Path && p.Name == name))
                merged.Add(new OpenApiParameter
                {
                    Name     = name,
                    In       = ParameterLocation.Path,
                    Required = true,
                    Schema   = new OpenApiSchema { Type = JsonSchemaType.String }
                });
        }

        return merged;
    }

    /// <summary>
    /// The argsNs field for a JSON body defined in the operation that has no model (a string, an
    /// array of strings, etc.): it is sent as given. An object, even a free-form one, has a model.
    /// </summary>
    public const string UntypedJsonBodyArgName = "body";

    /// <summary>
    /// How a JSON request body is passed to a generated function.
    /// </summary>
    /// <param name="ArgName">The field name on argsNs that holds the body.</param>
    /// <param name="ModelName">The model class generated for the body (or its items), if any.</param>
    /// <param name="IsArray">True when the body is a JSON array.</param>
    /// <param name="InlineSchema">The inline object schema a model is synthesised from, if any.</param>
    public record JsonBody(string ArgName, string? ModelName, bool IsArray, IOpenApiSchema? InlineSchema);

    /// <summary>
    /// Describes a JSON request body. A body that is a model (a named or inline object), or an
    /// array of models, is passed as namespaces or model instances. A $ref is named after the
    /// component it refers to, which may itself be an array of models, or something else that is
    /// sent as given. Returns null for any other body, which is passed in
    /// <see cref="UntypedJsonBodyArgName"/> and sent as given.
    /// </summary>
    public static JsonBody? DescribeJsonBody(IOpenApiSchema schema, string functionName, OpenApiDocument? document)
    {
        if (SchemaHelpers.ReferenceId(schema) is { } id)
        {
            var target = SchemaHelpers.Resolve(schema, document) ?? schema;
            if (SchemaHelpers.IsObjectModel(target))
                return FromReference(id, id, isArray: false);

            var isArray = IsType(target, JsonSchemaType.Array);
            if (isArray && SchemaHelpers.IsModelReference(target.Items, document))
                return FromReference(id, SchemaHelpers.ReferenceId(target.Items)!, isArray: true);

            return new(StringHelpers.ToValidAplName(id.ToCamelCase()), null, isArray, null);
        }

        if (IsType(schema, JsonSchemaType.Array))
        {
            if (SchemaHelpers.IsModelReference(schema.Items, document))
            {
                var itemsId = SchemaHelpers.ReferenceId(schema.Items)!;
                return FromReference(itemsId, itemsId, isArray: true);
            }

            if (schema.Items != null && SchemaHelpers.IsObjectModel(schema.Items))
                return FromInline($"{functionName}RequestItem", schema.Items, isArray: true);

            return null;
        }

        if (SchemaHelpers.IsObjectModel(schema))
            return FromInline($"{functionName}Request", schema, isArray: false);

        return null;
    }

    /// <summary>
    /// True when the schema's type is <paramref name="type"/>, ignoring any Null flag
    /// (so a nullable array is still an array).
    /// </summary>
    public static bool IsType(IOpenApiSchema schema, JsonSchemaType type) =>
        schema.Type is { } t && (t & ~JsonSchemaType.Null) == type;

    private static JsonBody FromReference(string argId, string modelId, bool isArray) => new(
        ArgName:      StringHelpers.ToValidAplName(argId.ToCamelCase()),
        ModelName:    SchemaHelpers.ClassNameOf(modelId),
        IsArray:      isArray,
        InlineSchema: null);

    private static JsonBody FromInline(string syntheticName, IOpenApiSchema schema, bool isArray)
    {
        var modelName = SchemaHelpers.ClassNameOf(syntheticName);
        return new(LowerFirst(modelName), modelName, isArray, schema);
    }

    private static string LowerFirst(string s) =>
        s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
