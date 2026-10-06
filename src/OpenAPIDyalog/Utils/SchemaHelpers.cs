using CaseConverter;
using Microsoft.OpenApi;

namespace OpenAPIDyalog.Utils;

/// <summary>
/// Schema rules shared by model, endpoint and documentation generation.
/// </summary>
public static class SchemaHelpers
{
    /// <summary>
    /// Follows a $ref to the component schema it names; any other schema is returned as is.
    /// </summary>
    public static IOpenApiSchema? Resolve(IOpenApiSchema? schema, OpenApiDocument? document)
    {
        if (schema is OpenApiSchemaReference r && r.Reference.Id != null
            && document?.Components?.Schemas?.TryGetValue(r.Reference.Id, out var resolved) == true)
            return resolved;
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
        OperationNaming.IsType(schema, JsonSchemaType.Object)
        || schema.Properties is { Count: > 0 }
        || schema.AllOf is { Count: > 0 }
        || schema.AdditionalProperties != null;

    /// <summary>
    /// Whether a schema is a $ref to a component that gets a model class.
    /// </summary>
    public static bool IsModelReference(IOpenApiSchema? schema, OpenApiDocument? document) =>
        ReferenceId(schema) != null && Resolve(schema, document) is { } target && IsObjectModel(target);

    /// <summary>
    /// The model class generated for a component or inline schema name.
    /// </summary>
    public static string ClassNameOf(string schemaName) =>
        StringHelpers.ToValidAplName(schemaName.ToPascalCase());

    /// <summary>
    /// The component schemas that get a model class.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, IOpenApiSchema>> ModelComponents(OpenApiDocument document) =>
        (document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
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
