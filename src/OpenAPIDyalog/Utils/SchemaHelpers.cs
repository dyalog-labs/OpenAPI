using CaseConverter;
using Microsoft.OpenApi;

namespace OpenAPIDyalog.Utils;

/// <summary>
/// Schema rules shared by model, endpoint and documentation generation.
/// </summary>
public static class SchemaHelpers
{
    /// <summary>
    /// Follows a $ref to the component schema it names, and on through any components that are
    /// themselves references (A → B → Pet), stopping at a cycle. Any other schema is returned as is.
    /// </summary>
    public static IOpenApiSchema? Resolve(IOpenApiSchema? schema, OpenApiDocument? document)
    {
        var seen = new HashSet<string>();
        while (schema is OpenApiSchemaReference r && r.Reference.Id is { } id && seen.Add(id)
               && document?.Components?.Schemas?.TryGetValue(id, out var resolved) == true)
            schema = resolved;
        return schema;
    }

    /// <summary>
    /// The component schema id of a $ref, or null if the schema is not a reference.
    /// </summary>
    public static string? ReferenceId(IOpenApiSchema? schema) =>
        schema is OpenApiSchemaReference r && !string.IsNullOrEmpty(r.Reference.Id) ? r.Reference.Id : null;

    /// <summary>
    /// Whether a (resolved) schema describes a JSON object, and so gets a model class. Arrays and
    /// primitives do not: a component that is an array of models, say, is used as a vector of them.
    /// </summary>
    public static bool IsObjectModel(IOpenApiSchema schema) =>
        IsObjectModel(schema, new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance));

    // An allOf makes an object only if one of its members does: { "allOf": [{ "type": "string" }] } is a string.
    private static bool IsObjectModel(IOpenApiSchema schema, HashSet<IOpenApiSchema> seen) =>
        seen.Add(schema)
        && (OperationNaming.IsType(schema, JsonSchemaType.Object)
            || schema.Properties is { Count: > 0 }
            || schema.AdditionalProperties != null
            || (schema.AllOf?.Any(member => IsObjectModel(member, seen)) ?? false));

    /// <summary>
    /// Whether a schema is a $ref to a component that gets a model class.
    /// </summary>
    public static bool IsModelReference(IOpenApiSchema? schema, OpenApiDocument? document) =>
        ReferenceId(schema) != null && Resolve(schema, document) is { } target && IsObjectModel(target);

    /// <summary>
    /// Whether an enum value is JSON null. Microsoft.OpenApi reads null as a sentinel string.
    /// </summary>
    public static bool IsJsonNull(System.Text.Json.Nodes.JsonNode? node) =>
        node is null || node.IsJsonNullSentinel()
        || node.GetValueKind() == System.Text.Json.JsonValueKind.Null;

    /// <summary>
    /// The model class generated for a component or inline schema name.
    /// </summary>
    public static string ClassNameOf(string schemaName) =>
        StringHelpers.ToValidAplName(schemaName.ToPascalCase());

    /// <summary>
    /// Whether a (resolved) object schema has no properties of its own, so is a map of arbitrary
    /// keys: it has additionalProperties, or does not forbid them (as a bare {"type": "object"} does not).
    /// </summary>
    public static bool IsMap(IOpenApiSchema schema) =>
        schema.Properties is not { Count: > 0 }
        && schema.AllOf is not { Count: > 0 }
        && (schema.AdditionalProperties != null || schema.AdditionalPropertiesAllowed);

    /// <summary>
    /// The component schemas that get a model class, each resolved (a component that is a
    /// reference to another, an alias, gets a class of its own like the one it refers to).
    /// </summary>
    public static IEnumerable<KeyValuePair<string, IOpenApiSchema>> ModelComponents(OpenApiDocument document) =>
        (document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
            .Select(kvp => KeyValuePair.Create(kvp.Key, Resolve(kvp.Value, document) ?? kvp.Value))
            .Where(kvp => IsObjectModel(kvp.Value));

    /// <summary>
    /// An object schema's properties, including those inherited through allOf at any depth (cycle-safe).
    /// Each is required if the schema, or any schema it inherits from, lists it as required. Where two
    /// schemas declare the same property, the schema's own (or the nearer) declaration wins.
    /// </summary>
    public static List<(string Key, IOpenApiSchema Schema, bool Required)> Properties(
        IOpenApiSchema schema, OpenApiDocument? document)
    {
        var sources = new List<IOpenApiSchema>();
        Collect(schema, new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance));

        void Collect(IOpenApiSchema s, HashSet<IOpenApiSchema> seen)
        {
            if (!seen.Add(s)) return;
            sources.Add(s);
            foreach (var sub in s.AllOf ?? new List<IOpenApiSchema>())
                if (Resolve(sub, document) is { } resolved)
                    Collect(resolved, seen);
        }

        var required = sources.SelectMany(s => s.Required ?? new HashSet<string>()).ToHashSet();
        var result   = new List<(string, IOpenApiSchema, bool)>();
        foreach (var source in sources)
        {
            foreach (var (key, propSchema) in source.Properties ?? new Dictionary<string, IOpenApiSchema>())
            {
                if (result.Any(p => p.Item1 == key)) continue;
                result.Add((key, propSchema, required.Contains(key)));
            }
        }
        return result;
    }

    /// <summary>
    /// Checks that no two schemas map to the same model class, which would make one overwrite
    /// the other. Throws an exception naming the schemas if they do.
    /// </summary>
    public static void CheckModelClassNames(OpenApiDocument document)
    {
        var clashes = ModelComponents(document)
            .Select(kvp => kvp.Key)
            .GroupBy(ClassNameOf)
            .Where(g => g.Count() > 1)
            .Select(g => $"{string.Join(", ", g)} (all model class {g.Key})")
            .ToList();

        if (clashes.Count > 0)
            throw new InvalidOperationException(
                "Some schema names differ only in case or punctuation, so would generate the same model class: "
                + string.Join("; ", clashes) + ". Rename one of each in the specification.");
    }
}
