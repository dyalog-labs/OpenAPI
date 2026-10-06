# API Reference

Internal C# API reference for the generator. For the user-facing CLI reference, see the [CLI Reference](../docs/usage/cli.md) in the user docs.

## CLI

```
OpenAPIDyalog [options] <spec-file-path> [output-directory]
```

| Argument | Required | Default | Description |
|---|---|---|---|
| `<spec-file-path>` | Yes | | Path to the OpenAPI specification file (JSON or YAML) |
| `[output-directory]` | No | `./generated` | Directory for generated output |

| Option | Short | Description |
|---|---|---|
| `--no-validation` | `-nv` | Skip OpenAPI spec validation |

## `OpenApiService`

Parses an OpenAPI specification file.

```csharp
Task<OpenApiParseResult> LoadSpecificationAsync(string filePath, bool disableValidation)
```

Returns an `OpenApiParseResult` with:

| Property | Type | Description |
|---|---|---|
| `Document` | `OpenApiDocument` | The parsed document |
| `Diagnostic` | `OpenApiDiagnostic` | Parse warnings and errors |
| `IsSuccess` | `bool` | Whether parsing succeeded |
| `ErrorMessage` | `string?` | Human-readable error, if any |

## `CodeGeneratorService`

Top-level orchestrator. Delegates to the three sub-services in order.

```csharp
Task GenerateAsync(OpenApiDocument document, GeneratorOptions options)
```

## `ArtifactGeneratorService`

Generates static/shared output files.

```csharp
Task GenerateUtilsAsync(OpenApiDocument document, string outputDirectory)
Task GenerateVersionAsync(OpenApiDocument document, string outputDirectory)
Task CopyHttpCommandAsync(string outputDirectory)
Task CopySpecificationAsync(string specificationPath, string outputDirectory)
Task GenerateClientAsync(OpenApiDocument document, string outputDirectory)
Task GenerateReadmeAsync(OpenApiDocument document, string outputDirectory, IReadOnlyDictionary<string, IOpenApiSchema>? inlineSchemas = null)
Task GenerateTagDocsAsync(OpenApiDocument document, string outputDirectory, IReadOnlyDictionary<string, IOpenApiSchema>? inlineSchemas = null)
```

`GenerateTagDocsAsync` writes `docs/<tag>.md` for each tag, named by the tag's APL name.

## `EndpointGeneratorService`

Generates one `.aplf` file per operation.

```csharp
Task<IReadOnlyDictionary<string, IOpenApiSchema>> GenerateEndpointsAsync(
    OpenApiDocument document,
    string outputDirectory,
    string? @namespace = null)
```

Returns the inline request body schemas found during generation, keyed by the name of the model to generate for each.

## `ModelGeneratorService`

Generates one model class per schema, in `APLSource/models/`.

```csharp
Task GenerateComponentModelsAsync(OpenApiDocument document, string outputDirectory)
Task GenerateInlineSchemaModelsAsync(IReadOnlyDictionary<string, IOpenApiSchema> inlineSchemas, OpenApiDocument document, string outputDirectory)
```

## `TemplateService`

Loads Scriban templates from embedded assembly resources and renders them.

```csharp
Task<Template> LoadTemplateAsync(string templateName)
Task<string> RenderAsync(Template template, object context)
string Render(Template template, object context)
Task<string> LoadAndRenderAsync(string templateName, object context)
Task SaveOutputAsync(string output, string outputPath)
IEnumerable<string> GetAvailableTemplates()
Stream GetEmbeddedResourceStream(string relativePath)
```

Template names are relative paths within the `Templates/` directory, e.g. `APLSource/Client.aplc.scriban`.

## `GeneratorOptions`

CLI configuration passed through the pipeline.

| Property | Type | Default | Description |
|---|---|---|---|
| `SpecificationPath` | `string` | | Path to the input spec file |
| `OutputDirectory` | `string` | `./generated` | Output directory |
| `DisableValidation` | `bool` | `false` | Skip OpenAPI validation |

## `StringHelpers`

```csharp
static string ToValidAplName(string name)
```

Converts an arbitrary string to a valid APL identifier. Invalid characters are replaced with `⍙<UCS code>⍙` escaping, identical to Dyalog's JSON name mangling (`0(7162⌶)`).

```csharp
static string CommentLines(string? text)
```

Prefixes each line of `text` with `⍝ `.

## `PathConverter`

```csharp
static string ToDyalogPath(string path)
```

Converts an OpenAPI path template (e.g. `/pets/{petId}`) to a Dyalog APL expression (e.g. `'/pets/',(c.∆.HttpCommand.UrlEncode⍕argsNs.petId)`).

## `OperationNaming`

Naming rules shared by code and docs generation.

```csharp
static string TagOf(OpenApiOperation operation)
static string TagName(string tag)
static string FunctionName(string? operationId, string method, string path)
static List<IOpenApiParameter> MergeParameters(IOpenApiPathItem? pathItem, OpenApiOperation operation)
static JsonBody? DescribeJsonBody(IOpenApiSchema schema, string functionName, OpenApiDocument? document)
```

`TagName` gives the Client field, `_tags` directory and docs file name for a tag; it is always a valid APL name, so is safe as a file name. `MergeParameters` overrides path item parameters with operation parameters of the same name and location. `DescribeJsonBody` gives the argument name, model name and array-ness of a JSON request body. A `$ref` is resolved, so a reference to an array of models is an array; a reference to anything else has no model. For an unnamed body that is not a model or an array of models, it returns null (the body is then passed as `body`).

## `SchemaHelpers`

Schema rules shared by model, endpoint and docs generation.

```csharp
static IOpenApiSchema? Resolve(IOpenApiSchema? schema, OpenApiDocument? document)
static bool IsObjectModel(IOpenApiSchema schema)
static bool IsModelReference(IOpenApiSchema? schema, OpenApiDocument? document)
static string ClassNameOf(string schemaName)
static IEnumerable<KeyValuePair<string, IOpenApiSchema>> ModelComponents(OpenApiDocument document)
static List<(string Key, IOpenApiSchema Schema, bool Required)> Properties(IOpenApiSchema schema, OpenApiDocument? document)
static void CheckModelClassNames(OpenApiDocument document)
```

Only object schemas (`ModelComponents`) get a model class. `Properties` includes properties inherited through `allOf` at any depth. `CheckModelClassNames` throws if two schema names map to the same class.

## `DocsBuilder`

```csharp
DocsBuilder(OpenApiDocument document, IReadOnlyDictionary<string, IOpenApiSchema>? inlineSchemas = null)
List<TagDoc> BuildTags()
List<ModelDoc> BuildModels()
```

Builds the documentation model for `README.md` and `docs/<tag>.md`, including a usage example for each operation. `inlineSchemas` is what `EndpointGeneratorService.GenerateEndpointsAsync` returns, so the docs name each inline model as it was generated (an inline model is numbered if its name is taken).
