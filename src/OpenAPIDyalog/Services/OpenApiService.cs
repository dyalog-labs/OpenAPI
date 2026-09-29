using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using OpenAPIDyalog.Models;
using OpenAPIDyalog.Services.Interfaces;

namespace OpenAPIDyalog.Services;

/// <summary>
/// Service for parsing and processing OpenAPI specifications.
/// </summary>
public class OpenApiService : IOpenApiService
{
    private readonly ILogger<OpenApiService> _logger;

    public OpenApiService(ILogger<OpenApiService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads and parses an OpenAPI specification from a file.
    /// </summary>
    /// <param name="filePath">Path to the OpenAPI specification file.</param>
    /// <param name="disableValidation">If true, disables OpenAPI validation rules during parsing.</param>
    /// <returns>The parsed document and diagnostic information.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
    public async Task<OpenApiParseResult> LoadSpecificationAsync(string filePath, bool disableValidation = false)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Specification file not found: {filePath}", filePath);
        }

        try
        {
            var settings = new OpenApiReaderSettings();
            if (disableValidation)
            {
                settings.RuleSet = new ValidationRuleSet();
            }
            settings.AddYamlReader();

            using var stream = File.OpenRead(filePath);
            var (document, diagnostic) = await OpenApiDocument.LoadAsync(stream, settings: settings);

            return new OpenApiParseResult
            {
                Document   = document,
                Diagnostic = diagnostic,
                // With validation disabled, any document the reader produced is usable;
                // its diagnostics are reported as warnings instead of failing the run.
                IsSuccess  = document != null && (disableValidation || (diagnostic?.Errors.Count ?? 0) == 0)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing OpenAPI document: {Message}", ex.Message);
            return new OpenApiParseResult
            {
                Document     = null,
                Diagnostic   = null,
                IsSuccess    = false,
                ErrorMessage = $"Error parsing OpenAPI document: {ex.Message}"
            };
        }
    }
}
