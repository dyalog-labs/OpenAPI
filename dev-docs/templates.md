# Templates

The generator uses [Scriban](https://github.com/scriban/scriban) templates to produce APL source files. Each template corresponds to one output file.

## Location in the repo

Templates live under `src/OpenAPIDyalog/Templates/`:

```
Templates/
├── APLSource/
│   ├── Client.aplc.scriban       → APLSource/Client.aplc
│   ├── utils.apln.scriban        → APLSource/utils.apln
│   ├── Version.aplf.scriban      → APLSource/Version.aplf
│   ├── HttpCommand.aplc          → APLSource/HttpCommand.aplc  (copied as-is, not a template)
│   ├── _tags/
│   │   └── endpoint.aplf.scriban → APLSource/_tags/<tag>/<OperationId>.aplf
│   └── models/
│       └── model.aplc.scriban    → APLSource/models/<Model>.aplc
├── docs/
│   └── tag.md.scriban            → docs/<tag>.md
└── README.md.scriban             → README.md
```

## Bundled in the executable

All files under `Templates/` are declared as `<EmbeddedResource>` in the `.csproj` and compiled into the assembly. The published single-file binary carries all templates inside it — no external template directory is needed at runtime. `TemplateService` loads them via `Assembly.GetManifestResourceStream()` using paths of the form:

```
OpenAPIDyalog.Templates.APLSource.Client.aplc.scriban
```

(Forward slashes and backslashes in the path are replaced with dots.)

If you add a new template file to the `Templates/` tree, it will be automatically picked up as an embedded resource on the next build — no `.csproj` changes are needed.

## Scriban syntax

Scriban uses `{{` and `}}` for expressions and `{{~`/`~}}` to strip surrounding whitespace. For example:

```scriban
⍝ {{ title }} - Version {{ version }}
```

Conditionals and loops:

```scriban
{{~ if description ~}}
{{ comment_lines description }}
{{~ end ~}}

{{~ for tag in tags ~}}
    :field public {{ tag }}
{{~ end ~}}
```

See the [Scriban documentation](https://github.com/scriban/scriban/blob/master/doc/language.md) for full syntax reference.

## Property naming

C# context properties are automatically renamed to `snake_case` before being passed to templates. So `OperationId` becomes `operation_id`, `GeneratedAt` becomes `generated_at`, and so on. `CustomProperties` dictionary keys are snake_cased the same way.

## Available properties

### Document-level templates

Used by `Client.aplc.scriban`, `utils.apln.scriban`, `Version.aplf.scriban`, `README.md.scriban`, `docs/tag.md.scriban`.

| Property | Description |
|---|---|
| `title` | API title from `info.title` |
| `version` | API version from `info.version` |
| `description` | API description from `info.description` |
| `base_url` | First server URL, or empty |
| `generated_at` | UTC timestamp of generation |
| `tags` | List of all tag names used in the spec |
| `security_schemes` | Map of security scheme name → scheme info (`type`, `in`, `parameter_name`, `scheme`, `bearer_format`) |
| `has_security_schemes` | Whether there are any security schemes (`.size` does not work on maps in Scriban) |
| `schemas` | Map of component schema name → schema |
| `tag_docs` | List of tags, each with `name`, `apl_name`, `display_name`, `description`, `doc_path` and `operations`. Each operation has `function_name`, `method`, `path`, `summary`, `description`, `deprecated`, `parameters`, `request_body`, `responses` and `usage_example` |
| `models` | List of generated models, each with `class_name` and `description` |

Custom properties (set per-template in `ArtifactGeneratorService`):

| Property | Set for | Description |
|---|---|---|
| `class_name` | `Client.aplc`, `README.md` | Generated class name (default: `Client`) |
| `tags` | `Client.aplc` | APL names of the tags, one per Client field |
| `tag` | `docs/tag.md` | The tag being documented (an item of `tag_docs`) |

### Endpoint template

Used by `_tags/endpoint.aplf.scriban`.

| Property | Description |
|---|---|
| `operation_id` | Sanitised, PascalCase function name |
| `method` | HTTP method (lowercase) |
| `path` | Original path string (e.g. `/pets/{petId}`) |
| `dyalog_path` | Path converted to Dyalog APL expression (e.g. `'/pets/',(c.∆.HttpCommand.UrlEncode⍕argsNs.petId)`) |
| `summary` | Operation summary |
| `description` | Operation description |
| `parameters` | List of parameters, each with `name`, `in`, `required`, `schema` |
| `request_body` | Request body object, or null |
| `request_content_type` | Content type of the request body |
| `request_json_body_type` | Model class for a JSON body (or its items), or null |
| `request_body_arg_name` | Field on `argsNs` that holds a JSON body |
| `request_body_is_array` | Whether a JSON body is an array |
| `form_fields` | List of form fields for `multipart/form-data` |
| `responses` | Map of status code → response |
| `has_security` | Boolean — whether the operation declares security requirements |
| `security_scheme_names` | List of required security scheme names |

## Custom template functions

These functions are available in all templates:

**`comment_lines <text>`** — prefixes every line of `text` with `⍝ `.

```scriban
{{ comment_lines description }}
```

**`apl_name <name>`** — converts `name` to a valid APL identifier, as used for parameters on `argsNs`.

**`one_line <text>`** — collapses `text` onto one line.

**`md_cell <text>`** — makes `text` safe for a Markdown table cell: one line, with `|` escaped.

## Modifying a template

1. Edit the `.scriban` file under `src/OpenAPIDyalog/Templates/`
2. Run `dotnet build` — the updated template is re-embedded automatically
3. Run the generator against a known spec: `dotnet run --project src/OpenAPIDyalog -- petstore.yaml ./out`
4. Inspect the output in `./out/APLSource/` and verify the rendered result

For larger changes, consider diffing the output against a reference: generate into two directories before and after your change and use `diff -r` to compare.

## Adding a new template

1. Create the `.scriban` file in the appropriate location under `Templates/`
2. Call `_templateService.LoadAndRenderAsync("<relative-path>", context)` from the relevant service
3. Call `_templateService.SaveOutputAsync(output, destinationPath)` to write it
4. No `.csproj` changes are needed — `<EmbeddedResource Include="Templates\**\*" />` already picks it up
