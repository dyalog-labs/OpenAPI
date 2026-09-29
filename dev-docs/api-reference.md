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
Task GenerateReadmeAsync(OpenApiDocument document, string outputDirectory)
```

## `EndpointGeneratorService`

Generates one `.aplf` file per operation.

```csharp
Task<List<(string Name, OpenApiSchema Schema)>> GenerateEndpointsAsync(
    OpenApiDocument document,
    string outputDirectory,
    string? @namespace)
```

Returns a list of inline schemas discovered during generation (reserved for future model generation).

## `ModelGeneratorService`

Placeholder — not yet implemented. Both methods are intentional no-ops.

```csharp
Task GenerateComponentModelsAsync(OpenApiDocument document, string outputDirectory)
Task GenerateInlineSchemaModelsAsync(List<(string Name, OpenApiSchema Schema)> schemas, string outputDirectory)
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

Converts an arbitrary string to a valid APL identifier. Invalid characters are replaced with `⍙<UCS code>⍙` escaping, identical to Dyalog's JSON name mangling (`7159⌶`).

```csharp
static string CommentLines(string? text)
```

Prefixes each line of `text` with `⍝ `.

## `PathConverter`

```csharp
static string ToDyalogPath(string path)
```

Converts an OpenAPI path template (e.g. `/pets/{petId}`) to a Dyalog APL expression (e.g. `'/pets/',(c.∆.HttpCommand.UrlEncode⍕argsNs.petId)`).
