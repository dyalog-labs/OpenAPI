# Usage Guide

Learn how to use the OpenAPI Client Generator to create Dyalog APL clients from OpenAPI specifications.

## Basic Usage

### Generating a Client

The basic command to generate a client:

```bash
dotnet run -- path/to/openapi.json ./output
```

**Arguments:**
- `path/to/openapi.json` - Path to your OpenAPI specification file
- `./output` - Output directory for generated code (optional)

This will create a complete APL client in the `./output` directory.

## Using Generated Clients

### Loading the Client

```apl
⍝ Load the generated client
]LINK.Import # ./APLSource
```

### Making API Calls

```apl
⍝ Create a client instance
client ← ⎕NEW Client (baseUrl: 'https://api.example.com/v4')

⍝ Call an API method
result ← api.pet.findPetsByStatus.syncDetailed (client:client ⋄ status:'available')

⍝ Handle the response
:If result.statusCode≡200
    pets ← result.Data
    ⎕← 'Found ',⍕≢pets,' pets'
:Else
    ⎕← 'Error'
:EndIf
```

## Command-Line Options

### Input Options

- **OpenAPI Specification**: Path to OpenAPI 3.0 JSON or YAML file
- **Output Directory**: Where to generate the code (default: `./output`)

## Troubleshooting

TODO

## Next Steps

- [Generated Code](generated-code.md) - Understanding the generated structure
- [Examples](examples.md) - More detailed examples
- [API Reference](api-reference.md) - Complete API documentation
