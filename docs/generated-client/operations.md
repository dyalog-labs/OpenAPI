# Operation Functions

Each API operation in the spec produces a single APL function file at:

```
APLSource/_tags/<tag>/<OperationId>.aplf
```

These functions are the primary interface for making API calls. They are accessed through the `Client` class as `client.<tag>.<OperationId>`. Operations with no tag in the spec are placed under `_tags/default/` and accessed as `client.default.<OperationId>`.

## Function signature

```apl
response ← OperationId argsNs
```

The right argument `argsNs` is a namespace whose fields correspond to the operation's parameters. The return value is the [HttpCommand](https://github.com/Dyalog/HttpCommand) response namespace, unless `mock` is set — see [Mock mode](client.md#mock-mode).

## Passing parameters

All parameters — path, query, header, and body — are passed as fields on the `argsNs` namespace:

```apl
response ← client.user.GetById (id: 42)
```

For operations with no parameters, pass an empty namespace:

```apl
response ← client.user.List ()
```

### Path parameters

Path parameters are always required. The value must be a character vector or a scalar number — anything else signals an error. Numeric values are converted to strings automatically, and the value is percent-encoded, so a value containing `/`, `?` or a space stays within its own path segment.

### Query and header parameters

These are read from `argsNs` by name. Required parameters signal an error if absent; optional parameters are simply omitted from the request if not set on the namespace.

An array query parameter that the spec declares with `explode: false` is sent as one value, its items joined by the delimiter for its style: `,` for `form` (the default), a space for `spaceDelimited`, `|` for `pipeDelimited`. Pass a vector of strings or numbers, or a single string:

```apl
response ← client.weather.GetForecast (latitude:'51.5' ⋄ longitude:'-0.12' ⋄ hourly:'temperature_2m' 'rain')
⍝ GET /v1/forecast?hourly=temperature_2m%2Crain&latitude=51.5&longitude=-0.12
```

### Request body

How the body parameter is named on `argsNs` depends on the content type declared in the spec:

| Content type | Field name on `argsNs` |
|---|---|
| `application/json` | Named after the schema type in camelCase (e.g. `user` for a `User` schema) |
| `application/octet-stream` | `body` |
| `multipart/form-data` | One field per form field — see [Multipart form fields](#multipart-form-fields) below |
| Other | `data` |

### Multipart form fields

For `multipart/form-data` operations, each form field in the spec becomes a field on `argsNs`. The value of each field follows the [HttpCommand multipart convention](https://dyalog.github.io/HttpCommand/latest/content-types/#special-treatment-of-content-type-multipartform-data): it is either a simple value (e.g. a character vector) or a 1–3 element vector of:

```
(content) (mime-type) (filename)
```

`mime-type` and `filename` are optional. For file uploads, `content` may be a file path prefixed with:

- `@` — upload the file's content and include its original filename in the request
- `<` — upload only the file's content, omitting the filename

When no MIME type is given, HttpCommand defaults to `'text/plain'` for `.txt` files and `'application/octet-stream'` for all others.

## Function name derivation

The function name is derived from the `operationId` in the spec:

1. The `operationId` is converted to PascalCase (e.g. `list_users` → `ListUsers`)
2. Any characters not valid in an APL identifier are replaced using delta-underbar escaping: the name is prefixed with `⍙`, and each invalid character is replaced with `⍙<UCS code>⍙`. This functions in a similar manner to [Dyalog's JSON name mangling](https://docs.dyalog.com/20.0/language-reference-guide/primitive-operators/i-beam/json-translate-name/).

Most well-formed `operationId` values produce a plain PascalCase name with no escaping.
