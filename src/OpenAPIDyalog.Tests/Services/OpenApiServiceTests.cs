using Microsoft.Extensions.Logging.Abstractions;
using OpenAPIDyalog.Services;

namespace OpenAPIDyalog.Tests.Services;

public class OpenApiServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"oad-test-{Guid.NewGuid():N}");

    public OpenApiServiceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helper ─────────────────────────────────────────────────────────────

    private string WriteSpec(string json)
    {
        var path = Path.Combine(_tempDir, "spec.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static readonly OpenApiService Service = new(NullLogger<OpenApiService>.Instance);

    // "example" is not a valid property of a Response Object, so the reader reports an error
    private const string SpecWithReaderError = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Test", "version": "1.0.0" },
          "paths": {
            "/items": {
              "get": {
                "operationId": "ListItems",
                "responses": {
                  "401": { "description": "Unauthorized", "example": { "error": 401 } }
                }
              }
            }
          }
        }
        """;

    // ── LoadSpecificationAsync ─────────────────────────────────────────────

    [Fact]
    public async Task LoadSpecification_ValidSpec_Succeeds()
    {
        var path = WriteSpec("""
            {
              "openapi": "3.0.3",
              "info": { "title": "Test", "version": "1.0.0" },
              "paths": { "/items": { "get": { "operationId": "ListItems", "responses": { "200": { "description": "OK" } } } } }
            }
            """);

        var result = await Service.LoadSpecificationAsync(path);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Document);
    }

    [Fact]
    public async Task LoadSpecification_ReaderError_FailsByDefault()
    {
        var result = await Service.LoadSpecificationAsync(WriteSpec(SpecWithReaderError));

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Diagnostic!.Errors);
    }

    [Fact]
    public async Task LoadSpecification_ReaderError_SucceedsWithValidationDisabled()
    {
        var result = await Service.LoadSpecificationAsync(WriteSpec(SpecWithReaderError), disableValidation: true);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Document);
        Assert.NotEmpty(result.Diagnostic!.Errors);   // still reported, for the caller to log as warnings
        Assert.Contains("/items", result.Document!.Paths.Keys);
    }
}
