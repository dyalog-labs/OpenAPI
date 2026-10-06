# Client Class

`Client.aplc` is the main entry point of the generated client. It is a Dyalog APL class that wires together all the generated operation namespaces and handles shared concerns — base URL, authentication, cookies, and HTTP dispatch.

## Creating an instance

The constructor takes a single config namespace. `baseUrl` is the only required field.

```apl
config ← (
    baseUrl: 'https://api.example.com'
)

client ← ⎕NEW Client config
```

## Config fields

| Field&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | Required | Default | Description |
|-------------|---|---|---|
| `baseUrl`   | Yes | | Root URL prepended to every request path. |
| `mock`      | No | `¯1` | Controls whether requests are actually sent. See [Mock mode](#mock-mode). |
| `headers`   | No | `⍬` | Extra headers sent with every request: a vector of name–value pairs, such as `('X-One' 'a')('X-Two' 'b')`, or a single pair, or a 2-column matrix. |
| `security` | No[^1] | | Authentication credentials. See [Authentication](#authentication). |

[^1]: Required if the API uses authentication.

## Authentication

API key, HTTP bearer, and HTTP basic authentication are supported. OAuth is not currently implemented, but planned to be implemented. An operation that accepts only schemes the client does not support, such as OAuth, is sent without credentials, so you can supply them yourself in `config.headers`:

```apl
config ← (
    baseUrl: 'https://api.example.com'
    headers: ,⊂'Authorization' ('Bearer ',token)
)
```

Set a `security` namespace on the config with the fields required by the API's authentication scheme. Only provide the fields relevant to the scheme used.

| Field | Description |
|---|---|
| `apiKey` | API key value, for specs that use `apiKey` security. |
| `bearerToken` | Bearer token, for specs that use HTTP bearer security. |
| `username` | Username for HTTP basic auth. |
| `password` | Password for HTTP basic auth. |

## Tag fields

After construction, the client exposes one public field per API tag. Calling an operation looks like:

```apl
client.<tag>.<OperationId> <args>
```

For example, if the spec defines a `user` tag with a `ListUsers` operation:

```apl
client.user.ListUsers ()
```

Operations with no tag in the spec are grouped under a `default` field.

## Mock mode

The `mock` config field controls whether requests are actually sent:

| Value | Behaviour |
|---|---|
| `¯1` | Send the request and return the response normally. |
| `1` | Build the request but do not send it; return the raw HttpCommand request object. |
| `2` | Return the HttpCommand argument namespace directly, without building or sending the request. |

## Debug mode

Setting `client.∆.Debug←1` prints the raw HTTP request to the session before sending it.

```apl
client.∆.Debug ← 1
client.user.ListUsers ()   ⍝ prints request details to the session
```

This is not compatible with `mock←2`.
