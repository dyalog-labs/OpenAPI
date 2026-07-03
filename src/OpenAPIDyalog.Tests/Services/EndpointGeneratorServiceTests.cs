using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Moq;
using OpenAPIDyalog.Models;
using OpenAPIDyalog.Services;
using OpenAPIDyalog.Services.Interfaces;
using Scriban;

namespace OpenAPIDyalog.Tests.Services;

public class EndpointGeneratorServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"oad-test-{Guid.NewGuid():N}");

    public EndpointGeneratorServiceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helper ─────────────────────────────────────────────────────────────

    private static async Task<OpenApiDocument> LoadDocumentAsync(string json)
    {
        var settings = new OpenApiReaderSettings();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var (document, _) = await OpenApiDocument.LoadAsync(stream, settings: settings);
        return document!;
    }

    // ── GroupOperationsByTag ───────────────────────────────────────────────

    [Fact]
    public async Task GroupOperationsByTag_ReturnsCorrectGrouping()
    {
        var document = await LoadDocumentAsync("""
            {
              "openapi": "3.0.0",
              "info": { "title": "Test", "version": "1.0.0" },
              "paths": {
                "/pets": {
                  "get": {
                    "operationId": "listPets",
                    "tags": ["pet"],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);

        var result = EndpointGeneratorService.GroupOperationsByTag(document);

        Assert.Single(result);
        Assert.True(result.ContainsKey("pet"));
        Assert.Single(result["pet"]);
        Assert.Equal("/pets", result["pet"][0].path);
    }

    [Fact]
    public async Task GroupOperationsByTag_OperationWithoutTag_FallsIntoDefault()
    {
        var document = await LoadDocumentAsync("""
            {
              "openapi": "3.0.0",
              "info": { "title": "Test", "version": "1.0.0" },
              "paths": {
                "/health": {
                  "get": {
                    "operationId": "healthCheck",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);

        var result = EndpointGeneratorService.GroupOperationsByTag(document);

        Assert.True(result.ContainsKey("default"));
        Assert.Single(result["default"]);
    }

    [Fact]
    public async Task GroupOperationsByTag_MultipleOperationsSameTag_GroupedTogether()
    {
        var document = await LoadDocumentAsync("""
            {
              "openapi": "3.0.0",
              "info": { "title": "Test", "version": "1.0.0" },
              "paths": {
                "/pets": {
                  "get": {
                    "operationId": "listPets",
                    "tags": ["pet"],
                    "responses": { "200": { "description": "OK" } }
                  },
                  "post": {
                    "operationId": "createPet",
                    "tags": ["pet"],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);

        var result = EndpointGeneratorService.GroupOperationsByTag(document);

        Assert.Single(result);
        Assert.Equal(2, result["pet"].Count);
    }

    // ── GenerateEndpointsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GenerateEndpointsAsync_CallsSaveOutputAsync_OncePerOperation()
    {
        var document = await LoadDocumentAsync("""
            {
              "openapi": "3.0.0",
              "info": { "title": "Test", "version": "1.0.0" },
              "paths": {
                "/pets": {
                  "get": {
                    "operationId": "listPets",
                    "tags": ["pet"],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);

        var emptyTemplate = Template.Parse("");
        var mock = new Mock<ITemplateService>();
        mock.Setup(t => t.LoadTemplateAsync(It.IsAny<string>())).ReturnsAsync(emptyTemplate);
        mock.Setup(t => t.RenderAsync(It.IsAny<Template>(), It.IsAny<object>())).ReturnsAsync("");
        mock.Setup(t => t.SaveOutputAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var service = new EndpointGeneratorService(mock.Object, NullLogger<EndpointGeneratorService>.Instance);
        await service.GenerateEndpointsAsync(document, _tempDir);

        mock.Verify(t => t.SaveOutputAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GenerateEndpointsAsync_NoInlineRequestBody_ReturnsEmptyInlineSchemas()
    {
        var document = await LoadDocumentAsync("""
            {
              "openapi": "3.0.0",
              "info": { "title": "Test", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "Pet": {
                    "type": "object",
                    "properties": { "name": { "type": "string" } }
                  }
                }
              },
              "paths": {
                "/pets": {
                  "post": {
                    "operationId": "createPet",
                    "tags": ["pet"],
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": { "$ref": "#/components/schemas/Pet" }
                        }
                      }
                    },
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);

        var emptyTemplate = Template.Parse("");
        var mock = new Mock<ITemplateService>();
        mock.Setup(t => t.LoadTemplateAsync(It.IsAny<string>())).ReturnsAsync(emptyTemplate);
        mock.Setup(t => t.RenderAsync(It.IsAny<Template>(), It.IsAny<object>())).ReturnsAsync("");
        mock.Setup(t => t.SaveOutputAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var service = new EndpointGeneratorService(mock.Object, NullLogger<EndpointGeneratorService>.Instance);
        var inlineSchemas = await service.GenerateEndpointsAsync(document, _tempDir);

        Assert.Empty(inlineSchemas);
    }

    [Fact]
    public async Task GenerateEndpointsAsync_AnonymousSecurityAlternative_DoesNotRequireAuth()
    {
        var document = await LoadDocumentAsync("""
            {
              "openapi": "3.0.0",
              "info": { "title": "Test", "version": "1.0.0" },
              "components": {
                "securitySchemes": {
                  "apiKeyAuth": {
                    "type": "apiKey",
                    "in": "header",
                    "name": "X-API-Key"
                  }
                }
              },
              "paths": {
                "/status": {
                  "get": {
                    "operationId": "getStatus",
                    "tags": ["system"],
                    "security": [{}, { "apiKeyAuth": [] }],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);

        OperationTemplateContext? capturedContext = null;
        var emptyTemplate = Template.Parse("");
        var mock = new Mock<ITemplateService>();
        mock.Setup(t => t.LoadTemplateAsync(It.IsAny<string>())).ReturnsAsync(emptyTemplate);
        mock.Setup(t => t.RenderAsync(It.IsAny<Template>(), It.IsAny<object>()))
            .Callback<Template, object>((_, context) =>
            {
                capturedContext = Assert.IsType<OperationTemplateContext>(context);
            })
            .ReturnsAsync("");
        mock.Setup(t => t.SaveOutputAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var service = new EndpointGeneratorService(mock.Object, NullLogger<EndpointGeneratorService>.Instance);
        await service.GenerateEndpointsAsync(document, _tempDir);

        Assert.NotNull(capturedContext);
        Assert.False(capturedContext!.HasSecurity);
        Assert.Equal(new[] { "apiKeyAuth" }, capturedContext.SecuritySchemeNames);
    }
}
