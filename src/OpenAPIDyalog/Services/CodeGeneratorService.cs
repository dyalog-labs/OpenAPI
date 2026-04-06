using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using OpenAPIDyalog.Constants;
using OpenAPIDyalog.Models;
using OpenAPIDyalog.Services.Interfaces;

namespace OpenAPIDyalog.Services;

/// <summary>
/// Thin orchestrator: delegates generation work to specialised services.
/// </summary>
public class CodeGeneratorService : ICodeGeneratorService
{
    private readonly ArtifactGeneratorService _artifacts;
    private readonly EndpointGeneratorService _endpoints;
    private readonly ModelGeneratorService    _models;
    private readonly ILogger<CodeGeneratorService> _logger;

    public CodeGeneratorService(
        ArtifactGeneratorService    artifacts,
        EndpointGeneratorService    endpoints,
        ModelGeneratorService       models,
        ILogger<CodeGeneratorService> logger)
    {
        _artifacts = artifacts;
        _endpoints = endpoints;
        _models    = models;
        _logger    = logger;
    }

    /// <summary>
    /// Runs the full generation pipeline for the given document and options.
    /// </summary>
    public async Task GenerateAsync(OpenApiDocument document, GeneratorOptions options)
    {
        await _artifacts.GenerateUtilsAsync(document, options.OutputDirectory);
        await _artifacts.GenerateVersionAsync(document, options.OutputDirectory);
        await _artifacts.CopyHttpCommandAsync(options.OutputDirectory);
        await _artifacts.CopySpecificationAsync(options.SpecificationPath, options.OutputDirectory);

        var inlineSchemas = await _endpoints.GenerateEndpointsAsync(
            document, options.OutputDirectory, options.Namespace);

        await _artifacts.GenerateClientAsync(document, options.OutputDirectory, options.Namespace);
        await _artifacts.GenerateReadmeAsync(document, options.OutputDirectory);
        //await _artifacts.GenerateTagDocsAsync(document, options.OutputDirectory);

        // Model generation
        await _models.GenerateInlineSchemaModelsAsync(inlineSchemas, options.OutputDirectory);
        await _models.GenerateComponentModelsAsync(document, options.OutputDirectory);
    }

    /// <summary>
    /// Returns a count of operations per tag across the document.
    /// </summary>
    public Dictionary<string, int> GetOperationSummary(OpenApiDocument document)
    {
        var summary = new Dictionary<string, int>();

        foreach (var path in document.Paths)
        {
            if (path.Value?.Operations == null) continue;

            foreach (var operation in path.Value.Operations)
            {
                var tag = operation.Value.Tags?.FirstOrDefault()?.Name ?? GeneratorConstants.DefaultTagName;
                summary.TryAdd(tag, 0);
                summary[tag]++;
            }
        }

        return summary;
    }
}
