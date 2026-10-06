# Development Setup

## Prerequisites

- **.NET 10 SDK** — required to build and run the generator ([download](https://dotnet.microsoft.com/download))
- **Dyalog APL v20.0 or later** — needed to load and test the generated client output ([download](https://dyalog.com))
- **Python 3** with **MkDocs Material** — needed to serve the docs locally (optional)

## Clone the repository

```bash
git clone https://github.com/dyalog-labs/OpenAPI.git
cd OpenAPI
```

The `docs/documentation-assets` directory is a git submodule. Initialise it after cloning:

```bash
git submodule update --init
```

## Build

```bash
dotnet build
```

## Run from source

```bash
dotnet run --project src/OpenAPIDyalog -- <spec-file> [output-directory]
```

For example:

```bash
dotnet run --project src/OpenAPIDyalog -- petstore.yaml ./out
```

## Run tests

```bash
dotnet test
```

## Serve the docs locally

Install MkDocs Material if you haven't already:

```bash
pip install mkdocs-material
```

Then serve:

```bash
mkdocs serve
```

The docs will be available at `http://127.0.0.1:8000`.
