using System.Text;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenAPIDyalog.Utils;

namespace OpenAPIDyalog.Tests.Utils;

public class OperationNamingTests
{
    private static async Task<OpenApiDocument> LoadDocumentAsync(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var (document, _) = await OpenApiDocument.LoadAsync(stream, settings: new OpenApiReaderSettings());
        return document!;
    }

    private static OpenApiOperation Operation(OpenApiDocument document, string path) =>
        document.Paths[path].Operations!.Values.Single();

    // ── Names ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("listPets", "get", "/pets", "ListPets")]
    [InlineData("list_pets", "get", "/pets", "ListPets")]
    [InlineData(null, "GET", "/pets/{petId}", "GetPetsPetId")]
    public void FunctionName_UsesOperationIdOrMethodAndPath(string? operationId, string method, string path, string expected)
    {
        Assert.Equal(expected, OperationNaming.FunctionName(operationId, method, path));
    }

    [Theory]
    [InlineData("pet", "pet")]
    [InlineData("store orders", "storeOrders")]
    [InlineData("store-orders", "storeOrders")]
    public void TagName_IsCamelCase(string tag, string expected)
    {
        Assert.Equal(expected, OperationNaming.TagName(tag));
    }

    [Theory]
    [InlineData("../../outside")]
    [InlineData("a/b")]
    [InlineData("..")]
    public void TagName_IsSafeAsAFileName(string tag)
    {
        var name = OperationNaming.TagName(tag);

        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain('.', name);
    }

    // ── Parameters ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MergeParameters_IncludesPathItemParameters_OverriddenByNameAndLocation()
    {
        var document = await LoadDocumentAsync("""
            {
              "openapi": "3.0.0",
              "info": { "title": "Test", "version": "1.0.0" },
              "paths": {
                "/pets/{id}": {
                  "parameters": [
                    { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } },
                    { "name": "trace", "in": "header", "schema": { "type": "string" } }
                  ],
                  "get": {
                    "parameters": [
                      { "name": "id", "in": "path", "required": true, "description": "op", "schema": { "type": "integer" } },
                      { "name": "trace", "in": "query", "schema": { "type": "string" } }
                    ],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);

        var parameters = OperationNaming.MergeParameters(document.Paths["/pets/{id}"], Operation(document, "/pets/{id}"));

        Assert.Equal(3, parameters.Count);
        Assert.Equal("op", parameters.Single(p => p.Name == "id").Description);
        Assert.Contains(parameters, p => p.Name == "trace" && p.In == ParameterLocation.Header);
        Assert.Contains(parameters, p => p.Name == "trace" && p.In == ParameterLocation.Query);
    }

    // ── JSON request bodies ────────────────────────────────────────────────

    private static async Task<(IOpenApiSchema Schema, OpenApiDocument Document)> BodySchemaAsync(string schemaJson)
    {
        var document = await LoadDocumentAsync($$"""
            {
              "openapi": "3.1.0",
              "info": { "title": "Test", "version": "1.0.0" },
              "components": { "schemas": {
                "Pet": { "type": "object", "properties": { "name": { "type": "string" } } },
                "PetList": { "type": "array", "items": { "$ref": "#/components/schemas/Pet" } },
                "Note": { "type": "string" } } },
              "paths": {
                "/x": {
                  "post": {
                    "requestBody": { "content": { "application/json": { "schema": {{schemaJson}} } } },
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);
        return (Operation(document, "/x").RequestBody!.Content!["application/json"].Schema!, document);
    }

    private static async Task<OperationNaming.JsonBody?> DescribeAsync(string schemaJson, string functionName)
    {
        var (schema, document) = await BodySchemaAsync(schemaJson);
        return OperationNaming.DescribeJsonBody(schema, functionName, document);
    }

    [Fact]
    public async Task DescribeJsonBody_Reference_NamedAfterSchema()
    {
        var body = await DescribeAsync("""{ "$ref": "#/components/schemas/Pet" }""", "AddPet");

        Assert.NotNull(body);
        Assert.Equal("pet", body.ArgName);
        Assert.Equal("Pet", body.ModelName);
        Assert.False(body.IsArray);
        Assert.Null(body.InlineSchema);
    }

    [Fact]
    public async Task DescribeJsonBody_NullableArrayOfReferences_IsAnArray()
    {
        var body = await DescribeAsync("""{ "type": ["array", "null"], "items": { "$ref": "#/components/schemas/Pet" } }""", "AddPets");

        Assert.NotNull(body);
        Assert.Equal("pet", body.ArgName);
        Assert.True(body.IsArray);
    }

    [Fact]
    public async Task DescribeJsonBody_InlineObject_NamedAfterOperation()
    {
        var body = await DescribeAsync("""{ "type": "object", "properties": { "name": { "type": "string" } } }""", "RenamePet");

        Assert.NotNull(body);
        Assert.Equal("renamePetRequest", body.ArgName);
        Assert.Equal("RenamePetRequest", body.ModelName);
        Assert.NotNull(body.InlineSchema);
    }

    [Fact]
    public async Task DescribeJsonBody_Primitive_HasNoModel()
    {
        Assert.Null(await DescribeAsync("""{ "type": "string" }""", "Note"));
    }

    [Fact]
    public async Task DescribeJsonBody_ReferenceToAnArrayOfModels_IsAnArrayOfTheItemModel()
    {
        var body = await DescribeAsync("""{ "$ref": "#/components/schemas/PetList" }""", "AddPets");

        Assert.NotNull(body);
        Assert.Equal("petList", body.ArgName);
        Assert.Equal("Pet", body.ModelName);
        Assert.True(body.IsArray);
    }

    [Fact]
    public async Task DescribeJsonBody_ReferenceToAPrimitive_HasNoModel()
    {
        var body = await DescribeAsync("""{ "$ref": "#/components/schemas/Note" }""", "AddNote");

        Assert.NotNull(body);
        Assert.Equal("note", body.ArgName);
        Assert.Null(body.ModelName);
        Assert.False(body.IsArray);
    }
}
