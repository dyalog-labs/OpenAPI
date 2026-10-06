# Architecture

## Overview

The generator is a .NET CLI application. Given an OpenAPI spec file, it produces a self-contained Dyalog APL client. The pipeline has three stages: **parse**, **model**, **render**.

```
OpenAPI spec (JSON/YAML)
        │
        ▼
  OpenApiService          — parses the spec into a Microsoft.OpenApi document object
        │
        ▼
  CodeGeneratorService    — orchestrates generation; delegates to three sub-services:
  ├── ArtifactGeneratorService   — utils.apln, Version.aplf, HttpCommand.aplc, Client.aplc, README.md, docs/<tag>.md
  ├── EndpointGeneratorService   — one .aplf per operation, grouped under _tags/<tag>/
  └── ModelGeneratorService      — one .aplc model class per schema, under models/
        │
        ▼
  TemplateService         — renders each Scriban template with its context object
        │
        ▼
  Output directory        — APLSource/ tree + README.md + docs/
```

## Services

### `GeneratorApplication`

The CLI entry point. Parses arguments into a `GeneratorOptions` object, constructs and wires up all services manually (no DI framework is used), calls `OpenApiService` to load the spec, then calls `CodeGeneratorService.GenerateAsync()`. Returns exit code 0 on success, 1 on any failure.

### `OpenApiService`

Wraps `Microsoft.OpenApi.Reader` to parse and optionally validate the spec. Returns an `OpenApiParseResult` containing the parsed document and any diagnostics. Both JSON and YAML input are supported.

### `CodeGeneratorService`

Thin orchestrator. Calls the three sub-services in order:

1. `ArtifactGeneratorService` — shared/static artifacts
2. `EndpointGeneratorService` — per-operation function files, returning the inline request body schemas it found
3. `ArtifactGeneratorService` — `Client.aplc`, `README.md` and `docs/<tag>.md`
4. `ModelGeneratorService` — model classes for the inline schemas and the component schemas

### `ArtifactGeneratorService`

Generates files that are the same regardless of the number of operations:

| Output file | Template |
|---|---|
| `APLSource/utils.apln` | `utils.apln.scriban` |
| `APLSource/Version.aplf` | `Version.aplf.scriban` |
| `APLSource/HttpCommand.aplc` | Embedded binary (copied directly) |
| `APLSource/Client.aplc` | `Client.aplc.scriban` |
| `README.md` | `README.md.scriban` |
| `docs/<tag>.md` (one per tag) | `docs/tag.md.scriban` |

The original spec file is also copied into the output directory.

The README and tag pages are built from `DocsBuilder`, which produces a usage example for each operation. Their names come from `OperationNaming`, as the endpoint functions' names do, so the docs always match the generated code.

### `EndpointGeneratorService`

Groups all operations by tag, then for each operation:

1. Constructs an `OperationTemplateContext` from the OpenAPI operation object, with the path item's parameters merged in
2. Resolves the request body's argument name, model and form fields
3. Converts path parameter placeholders into Dyalog APL concatenation expressions via `PathConverter`
4. Renders `endpoint.aplf.scriban` and writes to `APLSource/_tags/<tag>/<OperationId>.aplf`

Operations with no tag are placed under the `default` tag.

### `ModelGeneratorService`

Renders `models/model.aplc.scriban` once per component schema, and once per inline request body schema found by `EndpointGeneratorService` (named `<OperationId>Request`). `allOf` sub-schemas are flattened into one class; a schema with only `additionalProperties` becomes a map type.

### `TemplateService`

Loads Scriban templates from embedded assembly resources and renders them with a context object. When building the Scriban script object, all C# property names are converted to `snake_case` so templates use `operation_id` rather than `OperationId`. `CustomProperties` dictionaries on context objects are merged in the same way.

These custom template functions are registered:

- `comment_lines` — prefixes each line of a string with `⍝`
- `apl_name` — converts a name to a valid APL identifier (`StringHelpers.ToValidAplName`)
- `one_line` — collapses a string onto one line
- `md_cell` — makes a string safe for a Markdown table cell

## Context objects

Each template receives a typed context object. The two main ones are:

**`ApiTemplateContext`** — used for document-level templates (Client, Utils, README, etc.). Exposes the full OpenAPI document, API title/version/description, base URL, all tags, and security scheme metadata, plus `TagDocs` and `Models` (built by `DocsBuilder`) for the README and tag pages.

**`ModelTemplateContext`** — used for model templates. Exposes a model's class name, properties and enum values.

**`OperationTemplateContext`** — used for endpoint templates. Exposes a single operation's method, path, parameters, request body (including content type and resolved field names), response codes, and security requirements.

## Key utilities

**`PathConverter`** — converts an OpenAPI path template such as `/pets/{petId}` into a Dyalog APL expression: `'/pets/',(c.∆.HttpCommand.UrlEncode⍕argsNs.petId)`. Segments and parameter references are concatenated with `,`. Each parameter value is percent-encoded with `HttpCommand.UrlEncode`, so a value containing `/` or a space stays in its own path segment.

**`StringHelpers.ToValidAplName`** — converts an arbitrary string into a valid APL identifier. If the name contains characters outside the APL identifier character set, or begins with a digit, it is prefixed with `⍙` and each invalid character is replaced with `⍙<UCS code>⍙`. This is the same escaping scheme used by Dyalog's JSON name mangling (`0(7162⌶)`), so `⎕JSON` restores the original name. Parameters and model properties whose names are not valid APL are held under these names.

**`OperationNaming`** — the naming rules shared by code and docs generation: a tag's APL name (its Client field, `_tags` directory and docs file name), an operation's function name, the merged parameter list, and the argument name and model for a JSON request body.

**`DocsBuilder`** — builds the documentation model for the README and tag pages, including a usage example for each operation.

## Design notes

**Why Scriban?** Scriban is a lightweight, embeddable .NET templating language with good whitespace control. It keeps the template logic close to the generated APL source rather than buried in C# string manipulation.

**Why no DI framework?** The service graph is shallow and fixed. Manual construction in `GeneratorApplication` keeps the project dependency-light and startup fast.

**How do errors propagate?** `OpenApiService` returns a result object rather than throwing; the caller checks `IsSuccess` and logs diagnostics. Template rendering errors surface as exceptions and are caught at the top-level `GeneratorApplication` handler, which logs them and returns exit code 1.
