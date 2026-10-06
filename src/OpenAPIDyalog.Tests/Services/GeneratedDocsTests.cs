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

        Assert.Contains("missing←((⊂,'name'))~args.⎕NL ¯2 ¯9", model);
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

        Assert.DoesNotContain("build.id", formatNS);
        Assert.Contains("build.scores←vec ⍙v.scores", formatNS);
        Assert.Contains("build.friends←asNS¨vec ⍙v.friends", formatNS);
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

        Assert.Contains("missing←((⊂,'owner'))~args.⎕NL ¯2 ¯9", model);
    }

    // ── Schemas that are not objects, deep inheritance and name clashes ────

    private const string SchemasSpec = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Schemas", "version": "1.0.0" },
          "paths": {
            "/pets": {
              "post": {
                "tags": ["pet"], "operationId": "addPets",
                "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/PetList" } } } },
                "responses": { "200": { "description": "OK" } }
              }
            },
            "/dogs": {
              "post": {
                "tags": ["pet"], "operationId": "addDog",
                "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Dog" } } } },
                "responses": { "200": { "description": "OK" } }
              }
            }
          },
          "components": {
            "schemas": {
              "Animal": { "type": "object", "required": ["species"], "properties": { "species": { "type": "string" } } },
              "Pet": {
                "type": "object", "required": ["name"],
                "allOf": [{ "$ref": "#/components/schemas/Animal" }],
                "properties": { "name": { "type": "string" } }
              },
              "Dog": {
                "type": "object",
                "allOf": [{ "$ref": "#/components/schemas/Pet" }],
                "properties": {
                  "foo-bar": { "type": "string", "enum": ["a"] },
                  "foo_bar": { "type": "string", "enum": ["b"] },
                  "litter": { "$ref": "#/components/schemas/PetList" }
                }
              },
              "PetList": { "type": "array", "items": { "$ref": "#/components/schemas/Pet" } }
            }
          }
        }
        """;

    [Fact]
    public async Task ReferenceToAnArrayOfModels_IsUsedAsAVectorOfModels()
    {
        var output = await GenerateAsync(SchemasSpec);
        var page = Read(output, "docs", "pet.md");

        // No class for the array schema itself
        Assert.False(File.Exists(Path.Combine(output, "APLSource", "models", "PetList.aplc")));
        Assert.DoesNotContain("PetList", Read(output, "README.md"));

        // As a request body: a vector of Pets, sent as a JSON array
        Assert.Contains("    petList: ,⊂(", page);
        Assert.Contains("a vector of namespaces or `models.Pet` instances", page);
        Assert.Contains("body←,c.∆.utils.formatBody argsNs.petList", Read(output, "APLSource", "_tags", "pet", "AddPets.aplf"));

        // As a property: a vector of Pet instances when read from a response
        var dog = Read(output, "APLSource", "models", "Dog.aplc");
        Assert.Contains("build.litter←asNS¨vec ⍙v.litter", dog);
        Assert.Contains(".##.Pet).FromResponse ⍵}¨ns.litter", dog);
    }

    [Fact]
    public async Task InheritedProperties_AreIncludedAtAnyDepth()
    {
        var output = await GenerateAsync(SchemasSpec);

        Assert.Contains(":Property species", Read(output, "APLSource", "models", "Dog.aplc"));
        Assert.Contains("missing←((⊂,'name'),(⊂,'species'))~args.⎕NL ¯2 ¯9", Read(output, "APLSource", "models", "Dog.aplc"));
        Assert.Contains("        species: 'value'", Read(output, "docs", "pet.md"));
    }

    [Fact]
    public async Task EnumConstants_HaveDistinctNames_WhenPropertyNamesDifferOnlyInPunctuation()
    {
        var dog = Read(await GenerateAsync(SchemasSpec), "APLSource", "models", "Dog.aplc");

        Assert.Contains(":field public shared Enum⍙foo⍙45⍙bar", dog);
        Assert.Contains(":field public shared Enumfoo_bar", dog);
    }

    [Fact]
    public async Task SchemaNamesThatMakeTheSameClass_StopGeneration()
    {
        var spec = SchemasSpec.Replace("\"Animal\": {", "\"animal\": { \"type\": \"object\" }, \"Animal\": {");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GenerateAsync(spec));
        Assert.Contains("animal, Animal", ex.Message);
    }

    // ── Free-form objects, aliases, nullable arrays and enums ──────────────

    private const string NullableSpec = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Nullable", "version": "1.0.0" },
          "paths": {
            "/items": {
              "get": {
                "tags": ["item"], "operationId": "listItems",
                "parameters": [{ "name": "ids", "in": "query", "explode": false,
                  "schema": { "type": ["array", "null"], "items": { "type": "string" } } }],
                "responses": { "200": { "description": "OK" } }
              },
              "post": {
                "tags": ["item"], "operationId": "addItem",
                "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Alias" } } } },
                "responses": { "200": { "description": "OK" } }
              }
            }
          },
          "components": {
            "schemas": {
              "Item": {
                "type": "object",
                "properties": {
                  "colour": { "type": ["string", "null"], "enum": ["red", "blue", null] },
                  "extra": { "$ref": "#/components/schemas/Extra" }
                }
              },
              "Extra": { "type": "object" },
              "Alias": { "$ref": "#/components/schemas/Middle" },
              "Middle": { "$ref": "#/components/schemas/Item" }
            }
          }
        }
        """;

    [Fact]
    public async Task NullableArrayQueryParam_WithExplodeFalse_IsJoined()
    {
        var apl = Read(await GenerateAsync(NullableSpec), "APLSource", "_tags", "item", "ListItems.aplf");

        Assert.Contains("queryParams.ids←','c.∆.utils.joinArray argsNs.ids", apl);
    }

    [Fact]
    public async Task NullableEnum_AcceptsNull()
    {
        var item = Read(await GenerateAsync(NullableSpec), "APLSource", "models", "Item.aplc");

        Assert.Contains(":AndIf (⊂'null')≢args.NewValue", item);
        Assert.Contains("must be one of: red, blue, null", item);
    }

    [Fact]
    public async Task FreeFormObject_IsAMap()
    {
        var extra = Read(await GenerateAsync(NullableSpec), "APLSource", "models", "Extra.aplc");

        Assert.Contains(":field _data", extra);
    }

    [Fact]
    public async Task ReferenceChain_ResolvesToTheModel()
    {
        var output = await GenerateAsync(NullableSpec);

        Assert.Contains(":Property colour", Read(output, "APLSource", "models", "Alias.aplc"));
        Assert.Contains("a namespace or a `models.Alias` instance", Read(output, "docs", "item.md"));
        Assert.Contains("colour: 'red'", Read(output, "docs", "item.md"));
    }

    [Fact]
    public async Task Model_AcceptsAndSendsANestedModel()
    {
        var item = Read(await GenerateAsync(NullableSpec), "APLSource", "models", "Item.aplc");

        // A nested model is a namespace or an instance (name class 9), not only an array (2)
        Assert.Contains(":If (args.⎕NC 'extra')∊2 9 ⋄ extra←args.extra ⋄ :EndIf", item);
        Assert.Contains(":If (⍙v.⎕NC 'extra')∊2 9", item);
    }

    // ── Member names, line breaks and primitive allOf ──────────────────────

    private const string MembersSpec = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Members", "version": "1.0.0" },
          "paths": {
            "/things": {
              "post": {
                "tags": ["thing"], "operationId": "addThing",
                "requestBody": { "content": { "application/json": { "schema": { "type": "object" } } } },
                "responses": { "200": { "description": "OK" } }
              }
            }
          },
          "components": {
            "schemas": {
              "Thing": {
                "type": "object",
                "properties": {
                  "FormatNS": { "type": "string" },
                  "args": { "type": "string" },
                  "foo": { "type": "string" },
                  "_foo": { "type": "string" },
                  "mood": { "type": "string", "enum": ["calm", "two\nlines"] },
                  "EnumMood": { "type": "string" },
                  "code": { "$ref": "#/components/schemas/Code" }
                }
              },
              "Code": { "allOf": [{ "type": "string" }] }
            }
          }
        }
        """;

    [Fact]
    public async Task PropertyNamesThatClashWithClassMembers_AreRenamed()
    {
        var thing = Read(await GenerateAsync(MembersSpec), "APLSource", "models", "Thing.aplc");

        Assert.Contains(":Property FormatNS_", thing);
        Assert.Contains(":Property args_", thing);
        Assert.Contains(":Property EnumMood_", thing);
        Assert.Contains(":If (args.⎕NC 'FormatNS')∊2 9 ⋄ FormatNS_←args.FormatNS ⋄ :EndIf", thing);
        // foo and _foo are both plain properties, as values are kept in ⍙v
        Assert.Contains(":Property foo\n", thing);
        Assert.Contains(":Property _foo\n", thing);
    }

    [Fact]
    public async Task StringLiterals_WriteLineBreaksAsCodes()
    {
        var thing = Read(await GenerateAsync(MembersSpec), "APLSource", "models", "Thing.aplc");

        Assert.Contains("('two',(⎕UCS 10),'lines')", thing);
        Assert.Contains("must be one of: calm, two lines'", thing);
    }

    [Fact]
    public async Task AllOfOfAPrimitive_IsNotAModel()
    {
        var output = await GenerateAsync(MembersSpec);

        Assert.False(File.Exists(Path.Combine(output, "APLSource", "models", "Code.aplc")));
        Assert.DoesNotContain("⍙v.code←(⎕NEW", Read(output, "APLSource", "models", "Thing.aplc"));
    }

    [Fact]
    public async Task InlineFreeFormBody_HasAMapModel()
    {
        var output = await GenerateAsync(MembersSpec);

        Assert.Contains(":field _data", Read(output, "APLSource", "models", "AddThingRequest.aplc"));
        Assert.Contains("argsNs.⎕NC'addThingRequest'", Read(output, "APLSource", "_tags", "thing", "AddThing.aplf"));
    }
}
