using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenAPIDyalog.Models;
using OpenAPIDyalog.Services;

namespace OpenAPIDyalog.Tests.Services;

/// <summary>
/// Runs the whole generator, with the real templates, and checks the output: the README and
/// tag pages, and that what they document matches the generated functions and models.
/// </summary>
public class GeneratedDocsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"oad-test-{Guid.NewGuid():N}");

    public GeneratedDocsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private const string Spec = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Pet Shop", "version": "1.0.0" },
          "servers": [{ "url": "https://example.com/v1" }],
          "tags": [{ "name": "pet", "description": "Everything about pets" }],
          "paths": {
            "/pets": {
              "get": {
                "tags": ["pet"], "operationId": "listPets", "summary": "List | pets",
                "parameters": [
                  { "name": "limit", "in": "query", "required": true, "schema": { "type": "integer" } },
                  { "name": "X-Trace", "in": "header", "schema": { "type": "string" }, "description": "Line one\nline two" },
                  { "name": "session", "in": "cookie", "schema": { "type": "string" } }
                ],
                "responses": { "200": { "description": "OK" } }
              },
              "post": {
                "tags": ["pet"], "operationId": "addPet",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } } } },
                "responses": { "201": { "description": "Created" } }
              }
            },
            "/pets/{petId}": {
              "parameters": [{ "name": "petId", "in": "path", "required": true, "schema": { "type": "integer" } }],
              "patch": {
                "tags": ["pet"], "operationId": "renamePet",
                "requestBody": { "content": { "application/json": { "schema": {
                  "type": "object", "required": ["name"], "properties": { "name": { "type": "string" } } } } } },
                "responses": { "200": { "description": "OK" } }
              }
            },
            "/store/orders": {
              "post": { "tags": ["store orders"], "responses": { "200": { "description": "OK" } } }
            },
            "/store/refunds": {
              "post": { "tags": ["store-orders"], "responses": { "200": { "description": "OK" } } }
            },
            "/escape": {
              "get": { "tags": ["../../outside"], "operationId": "escape", "responses": { "200": { "description": "OK" } } }
            },
            "/health": { "get": { "responses": { "200": { "description": "OK" } } } }
          },
          "components": {
            "schemas": {
              "Pet": {
                "type": "object", "description": "A pet", "required": ["name"],
                "properties": {
                  "id": { "type": "integer", "readOnly": true },
                  "name": { "type": "string" },
                  "level": { "type": "integer", "enum": [1, -1] },
                  "mood": { "type": "string", "enum": ["it's fine"] },
                  "scores": { "type": "array", "items": { "type": "integer" } },
                  "friends": { "type": ["array", "null"], "items": { "$ref": "#/components/schemas/Pet" } }
                }
              }
            }
          }
        }
        """;

    private async Task<string> GenerateAsync(string spec = Spec)
    {
        var specPath = Path.Combine(_tempDir, "spec.json");
        await File.WriteAllTextAsync(specPath, spec);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(spec));
        var (document, _) = await OpenApiDocument.LoadAsync(stream, settings: new OpenApiReaderSettings());

        var templates = new TemplateService(NullLogger<TemplateService>.Instance);
        var generator = new CodeGeneratorService(
            new ArtifactGeneratorService(templates, NullLogger<ArtifactGeneratorService>.Instance),
            new EndpointGeneratorService(templates, NullLogger<EndpointGeneratorService>.Instance),
            new ModelGeneratorService(templates, NullLogger<ModelGeneratorService>.Instance),
            NullLogger<CodeGeneratorService>.Instance);

        var output = Path.Combine(_tempDir, "out");
        await generator.GenerateAsync(document!, new GeneratorOptions { SpecificationPath = specPath, OutputDirectory = output });
        return output;
    }

    private static string Read(string output, params string[] path) =>
        File.ReadAllText(Path.Combine([output, .. path]));

    // ── README ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Readme_IsFullyRendered_AndLinksEveryTagPage()
    {
        var output = await GenerateAsync();
        var readme = Read(output, "README.md");

        Assert.DoesNotContain("{{", readme);
        Assert.DoesNotContain("}}", readme);

        foreach (var page in Directory.GetFiles(Path.Combine(output, "docs")))
            Assert.Contains($"(docs/{Path.GetFileName(page)})", readme);

        Assert.Contains("| `Pet` | A pet |", readme);
        Assert.Contains("| `RenamePetRequest` |", readme);
    }

    // ── Tag pages ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TagPages_AreWrittenInsideDocs_OnePerClientField()
    {
        var output = await GenerateAsync();
        var docs = Path.Combine(output, "docs");

        // Every page is directly inside docs/, even for a tag like "../../outside"
        var pages = Directory.GetFiles(output, "*.md", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "README.md")
            .ToList();
        Assert.All(pages, p => Assert.Equal(docs, Path.GetDirectoryName(p)));

        // "store orders" and "store-orders" are both client.storeOrders, so share a page
        var tagDirs = Directory.GetDirectories(Path.Combine(output, "APLSource", "_tags")).Select(Path.GetFileName);
        Assert.Equal(tagDirs.Order(), pages.Select(Path.GetFileNameWithoutExtension).Order());
        Assert.Contains("PostStoreRefunds", Read(output, "docs", "storeOrders.md"));
    }

    [Fact]
    public async Task TagPage_OperationLinks_MatchAnchors()
    {
        var output = await GenerateAsync();
        var page = Read(output, "docs", "pet.md");

        var links   = Regex.Matches(page, @"\]\(#([^)]+)\)").Select(m => m.Groups[1].Value).ToList();
        var anchors = Regex.Matches(page, @"<a id=""([^""]+)""></a>").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(["ListPets", "AddPet", "RenamePet"], links);
        Assert.Equal(links, anchors);
    }

    [Fact]
    public async Task TagPage_DocumentsTheNamesTheFunctionsRead()
    {
        var output = await GenerateAsync();
        var page = Read(output, "docs", "pet.md");
        string Function(string name) => Read(output, "APLSource", "_tags", "pet", $"{name}.aplf");

        // A header whose name is not valid APL is passed under its mangled name
        Assert.Contains("⍝ ⍙X⍙45⍙Trace: 'value'", page);
        Assert.Contains("argsNs.⎕NC'⍙X⍙45⍙Trace'", Function("ListPets"));
        Assert.Contains("headerParams,←'X-Trace' argsNs.⍙X⍙45⍙Trace", Function("ListPets"));

        // Cookie parameters are not read by the function, so are not documented
        Assert.DoesNotContain("session", page);

        // A path-item parameter is documented, and read, for each operation on the path
        Assert.Contains("petId: 0", page);
        Assert.Contains("argsNs.⎕NC'petId'", Function("RenamePet"));

        // The body field is the one the function reads
        Assert.Contains("    pet: (", page);
        Assert.Contains("argsNs.⎕NC'pet'", Function("AddPet"));
        Assert.Contains("    renamePetRequest: (", page);
        Assert.Contains("argsNs.⎕NC'renamePetRequest'", Function("RenamePet"));
    }

    [Fact]
    public async Task TagPage_EscapesTableCells_AndOmitsReadOnlyFields()
    {
        var output = await GenerateAsync();
        var page = Read(output, "docs", "pet.md");

        Assert.Contains("| List \\| pets |", page);
        Assert.Contains("| Line one line two |", page);
        Assert.DoesNotContain("id: 0", page);
    }

    // ── Models ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Model_ChecksASingleRequiredFieldByName()
    {
        var model = Read(await GenerateAsync(), "APLSource", "models", "Pet.aplc");

        Assert.Contains("missing←((⊂,'name'))~args.⎕NL ¯2", model);
    }

    [Fact]
    public async Task Model_EnumConstants_AreTypedAndDistinct()
    {
        var model = Read(await GenerateAsync(), "APLSource", "models", "Pet.aplc");

        Assert.Contains("⍙1: 1", model);
        Assert.Contains("⍙⍙45⍙1: ¯1", model);
        Assert.Contains("ItSFine: 'it''s fine'", model);
    }

    [Fact]
    public async Task Model_FormatNS_SkipsReadOnly_AndTreatsNullableArraysAsArrays()
    {
        var model = Read(await GenerateAsync(), "APLSource", "models", "Pet.aplc");
        var formatNS = model[model.IndexOf("∇ build←FormatNS", StringComparison.Ordinal)..model.IndexOf("∇ inst←FromResponse", StringComparison.Ordinal)];

        Assert.DoesNotContain("_id", formatNS);
        Assert.Contains("build.scores←vec scores", formatNS);
        Assert.Contains("build.friends←asNS¨vec friends", formatNS);
    }

    // ── Inline models ──────────────────────────────────────────────────────

    private const string InlineSpec = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Inline", "version": "1.0.0" },
          "paths": {
            "/pets": {
              "post": {
                "tags": ["pet"], "operationId": "addPet",
                "requestBody": { "content": { "application/json": { "schema": {
                  "type": "object", "required": ["name"],
                  "allOf": [{ "$ref": "#/components/schemas/Base" }],
                  "properties": { "name": { "type": "string" } } } } } },
                "responses": { "200": { "description": "OK" } }
              }
            }
          },
          "components": {
            "schemas": {
              "Base": { "type": "object", "properties": { "id": { "type": "string" }, "owner": { "type": "string" } } },
              "AddPetRequest": { "type": "object", "properties": { "other": { "type": "string" } } },
              "Dog": {
                "type": "object", "required": ["owner"],
                "allOf": [{ "$ref": "#/components/schemas/Base" }],
                "properties": { "bark": { "type": "string" } }
              }
            }
          }
        }
        """;

    [Fact]
    public async Task InlineModel_DoesNotOverwriteAComponentModel_AndTheDocsUseItsName()
    {
        var output = await GenerateAsync(InlineSpec);

        Assert.Contains("other", Read(output, "APLSource", "models", "AddPetRequest.aplc"));
        Assert.Contains("name", Read(output, "APLSource", "models", "AddPetRequest2.aplc"));
        Assert.Contains("models.AddPetRequest2", Read(output, "docs", "pet.md"));
        Assert.Contains("| `AddPetRequest2` |", Read(output, "README.md"));
    }

    [Fact]
    public async Task InlineModel_IncludesPropertiesInheritedFromComponents()
    {
        var model = Read(await GenerateAsync(InlineSpec), "APLSource", "models", "AddPetRequest2.aplc");

        Assert.Contains(":Property owner", model);
    }

    [Fact]
    public async Task Model_InheritedPropertyIsRequired_WhenTheOuterSchemaSaysSo()
    {
        var model = Read(await GenerateAsync(InlineSpec), "APLSource", "models", "Dog.aplc");

        Assert.Contains("missing←((⊂,'owner'))~args.⎕NL ¯2", model);
    }
}
