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

All parameters — path, query, header, and body — are passed as fields on the `argsNs` namespace. This includes parameters the spec declares for every operation on a path, as well as those declared for the operation itself:

```apl
response ← client.user.GetById (id: 42)
```

For operations with no parameters, pass an empty namespace:

```apl
response ← client.user.List ()
```

A parameter whose name is not a valid APL name is passed under its mangled name, formed in the same way as [Dyalog's JSON name mangling](https://docs.dyalog.com/20.0/language-reference-guide/primitive-operators/i-beam/json-translate-name/): the name is prefixed with `⍙`, and each invalid character is replaced with `⍙<UCS code>⍙`. The original name is used in the request. For example, an `X-Request-ID` header is passed as `⍙X⍙45⍙Request⍙45⍙ID`, and a `page[size]` query parameter as `⍙page⍙91⍙size⍙93⍙`:

```apl
response ← client.user.List (⍙page⍙91⍙size⍙93⍙: 50)
```

The pages generated in `docs/` give the name to use for each parameter.

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
| `application/json` | Named after the body's schema in camelCase — see below |
| `application/octet-stream` | `body` |
| `multipart/form-data` | One field per form field — see [Multipart form fields](#multipart-form-fields) below |
| Other | `data` |

A JSON body's field depends on its schema:

| Schema | Field name | Value |
|---|---|---|
| A named object schema, e.g. `User` | `user` | A namespace, or a `models.User` instance |
| An array of a named schema, e.g. of `User` | `user` | A vector of namespaces or `models.User` instances |
| A named schema that is an array, e.g. `UserList`, an array of `User` | `userList` | A vector of namespaces or `models.User` instances |
| A named schema that is anything else, e.g. `Note`, a string | `note` | Any value that `⎕JSON` can convert |
| An object defined in the operation itself | `<operationId>Request`, e.g. `createUserRequest` | A namespace, or an instance of the [model](models.md) generated for it |
| An array of objects defined in the operation itself | `<operationId>RequestItem` | A vector of namespaces or instances of the model generated for them |
| Anything else (a string, a free-form object, …) | `body` | Any value that `⎕JSON` can convert |

For example, for an operation that takes a `User`:

```apl
⍝ A namespace…
response ← client.user.CreateUser (user: (name: 'Ada' ⋄ email: 'ada@example.com'))

⍝ …or a model instance, which checks required fields and enum values as it is built
user ← ⎕NEW models.User (name: 'Ada' ⋄ email: 'ada@example.com')
response ← client.user.CreateUser (user: user)
```

The page generated in `docs/` for each tag shows the fields of each request body.

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
