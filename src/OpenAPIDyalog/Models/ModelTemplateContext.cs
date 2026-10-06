using CaseConverter;
using Microsoft.OpenApi;
using OpenAPIDyalog.Utils;

namespace OpenAPIDyalog.Models;

/// <summary>
/// Represents a model/schema for code generation.
/// </summary>
public class ModelTemplateContext
{
    /// <summary>
    /// The name of the model/class.
    /// </summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>
    /// The description of the model.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// List of properties in this model.
    /// </summary>
    public List<ModelProperty> Properties { get; set; } = new();

    /// <summary>
    /// Whether this is a map/dictionary type (has additionalProperties but no named properties).
    /// </summary>
    public bool IsMapType { get; set; }

    /// <summary>
    /// The APL type of the additionalProperties values, if this is a map type.
    /// </summary>
    public string? MapValueType { get; set; }

    /// <summary>
    /// Whether any property has enum values (used to conditionally emit shared fields).
    /// </summary>
    public bool HasEnums => Properties.Any(p => p.HasEnumValues);

    /// <summary>
    /// Properties that have enum values.
    /// </summary>
    public IEnumerable<ModelProperty> EnumProperties => Properties.Where(p => p.HasEnumValues);

    /// <summary>
    /// Whether any non-readOnly property is required (used in make1 validation).
    /// ReadOnly fields are server-generated and cannot be required from the caller.
    /// </summary>
    public bool HasWritableRequiredFields => Properties.Any(p => p.IsRequired && !p.IsReadOnly);

    /// <summary>
    /// DyalogName values for required non-readOnly properties — what make1 callers must provide.
    /// </summary>
    public IEnumerable<string> WritableRequiredFields => Properties
        .Where(p => p.IsRequired && !p.IsReadOnly)
        .Select(p => p.DyalogName);
}

/// <summary>
/// Represents a property in a model.
/// </summary>
public class ModelProperty
{
    /// <summary>
    /// The property name as it appears in the API (raw JSON key).
    /// Used in comments only; DyalogName is used in generated APL code.
    /// </summary>
    public string ApiName { get; set; } = string.Empty;

    /// <summary>
    /// The APL-safe property name (ToValidAplName applied to the raw JSON key).
    /// Used for :Property declarations, backing variables (_DyalogName), and namespace members.
    /// ⎕JSON will correctly round-trip mangled names back to the original JSON key.
    /// </summary>
    public string DyalogName { get; set; } = string.Empty;

    /// <summary>
    /// The APL type string (str, int, bool, array[T], namespace, or a model class name).
    /// </summary>
    public string Type { get; set; } = "any";

    /// <summary>
    /// Whether this property is required.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Whether this property permits null values (⊂'null' in APL / null in JSON).
    /// </summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// Whether this is a server-generated read-only field (cannot be set, and skipped in FormatNS).
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Whether this is a write-only field (e.g. passwords).
    /// </summary>
    public bool IsWriteOnly { get; set; }

    /// <summary>
    /// OpenAPI format annotation (uuid, date-time, email, etc.).
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Default value rendered as a string, if declared in the schema.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Description of the property.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is a reference to another model class.
    /// </summary>
    public bool IsReference { get; set; }

    /// <summary>
    /// The referenced model class name, if this is a reference.
    /// </summary>
    public string? ReferenceType { get; set; }

    /// <summary>
    /// Whether this property is an array.
    /// </summary>
    public bool IsArray { get; set; }

    /// <summary>
    /// Whether this property is an array of strings (so a single string must be enclosed to stay one item).
    /// </summary>
    public bool IsStringArray { get; set; }

    /// <summary>
    /// Allowed enum values, or null if the property is not an enum.
    /// </summary>
    public List<EnumValue>? EnumValues { get; set; }

    /// <summary>
    /// Whether this property has enum values.
    /// </summary>
    public bool HasEnumValues => EnumValues?.Count > 0;

    /// <summary>
    /// Whether an enum property may also be null (its schema is nullable, or lists null as a value).
    /// </summary>
    public bool EnumAllowsNull { get; set; }

    /// <summary>
    /// The name of the enum constants namespace, after "Enum" (e.g. "role" → "Role").
    /// Derived from ApiName (raw JSON key) to stay clean even when DyalogName is mangled;
    /// set to DyalogName where that would make two properties' names the same.
    /// </summary>
    public string EnumFieldName
    {
        get => _enumFieldName ?? StringHelpers.ToValidAplName(ApiName.ToPascalCase());
        set => _enumFieldName = value;
    }

    private string? _enumFieldName;
}

/// <summary>
/// A single allowed value for an enum property.
/// </summary>
public class EnumValue
{
    /// <summary>
    /// The raw API value (e.g. "admin").
    /// </summary>
    public string ApiValue { get; set; } = string.Empty;

    /// <summary>
    /// The APL identifier for this value (e.g. "Admin").
    /// </summary>
    public string AplName { get; set; } = string.Empty;

    /// <summary>
    /// The value as an APL literal of the right type (e.g. 'admin', 42 or (⊂'true')).
    /// </summary>
    public string AplLiteral { get; set; } = string.Empty;
}
