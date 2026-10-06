# Dyalog APL OpenAPI Client Generator

Generates Dyalog APL client code from OpenAPI 3.0 specifications.

[![Release](https://github.com/dyalog-labs/OpenAPI/actions/workflows/release.yml/badge.svg)](https://github.com/dyalog-labs/OpenAPI/releases/latest)

> **This project is in active development and may not yet be fully functional with the entirety of the OpenAPI specification. Please raise a GitHub issue for any discovered bugs or feature requests.**

## Documentation

Full documentation is available at **[dyalog.github.io/OpenAPI](https://dyalog-labs.github.io/OpenAPI)**, including installation, CLI reference, and a guide to the generated client.

## Quick Start

1. Download the binary for your platform from the [GitHub Releases page](https://github.com/dyalog-labs/OpenAPI/releases/latest).
2. Run the generator against your spec:

```
openapidyalog path/to/openapi.yaml ./output
```

See [Installation](https://dyalog-labs.github.io/OpenAPI/installation/) for platform-specific setup details.

## Requirements

- Dyalog APL v20.0 or later (to use the generated client)
- An OpenAPI 3.0 specification file (JSON or YAML)

## Contributing

Developer documentation — architecture, build setup, templates, and contribution workflow — is in [dev-docs/](dev-docs/).
