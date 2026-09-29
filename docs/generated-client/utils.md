# Utilities

`APLSource/utils.apln` contains internal helper functions used by the generated client. These functions are not part of the public API — they are called by the generated operation functions and the `Client` class, not by user code directly.

## `request`

The central HTTP dispatch function. Every generated operation function calls `utils.request` to send its HTTP request.

It is responsible for:

- Setting the base URL from `config.baseUrl`
- Appending the `User-Agent` header (identifying the client version)
- Attaching any extra headers from `config.headers`
- Forwarding cookies from previous responses
- Applying the `mock` setting (see [Mock mode](client.md#mock-mode))
- Running the request via HttpCommand and updating the cookie jar

## `Authenticate`

Applies authentication to an outgoing request based on the security schemes declared in the spec and the credentials in `config.security`.

The scheme used is determined by the operation's declared security requirements and what credentials are present in `config.security`. When multiple schemes are available, they are tried in this order:

1. **API key** — appended as a header or query parameter, depending on the spec; may also be set as a cookie by the server
2. **Bearer token** — added as an `Authorization: Bearer …` header
3. **Basic auth** — credentials are base64-encoded and added as an `Authorization: Basic …` header

OAuth is not currently supported.

## `joinArray`

Joins an array query parameter into one value, for parameters the spec declares with `explode: false`. The left argument is the delimiter for the parameter's style (`,` for `form`, a space for `spaceDelimited`, `|` for `pipeDelimited`). A single string or number is passed through as it is.

## `isValidPathParam`

Validates that a value is usable as a path parameter. A valid path parameter is either a character vector or a scalar number. Generated operation functions call this before substituting values into URL path templates.

## `base64`

Encodes and decodes Base64. Used internally by `Authenticate` to encode credentials for HTTP Basic auth.
