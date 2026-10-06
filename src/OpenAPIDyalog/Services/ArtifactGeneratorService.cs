using CaseConverter;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using OpenAPIDyalog.Constants;
using OpenAPIDyalog.Models;
using OpenAPIDyalog.Services.Interfaces;
using OpenAPIDyalog.Utils;

namespace OpenAPIDyalog.Services;

/// <summary>
/// Generates non-endpoint artifacts: utils, version, HttpCommand, spec copy, client class, README.
/// </summary>
public class ArtifactGeneratorService
{
    private readonly ITemplateService _templateService;
    private readonly ILogger<ArtifactGeneratorService> _logger;

    public ArtifactGeneratorService(ITemplateService templateService, ILogger<ArtifactGeneratorService> logger)
    {
        _templateService = templateService;
        _logger          = logger;
    }

    /// <summary>
    /// Generates the APLSource/utils.apln namespace file.
    /// </summary>
    public async Task GenerateUtilsAsync(OpenApiDocument document, string outputDirectory)
    {
        var template = await _templateService.LoadTemplateAsync(GeneratorConstants.UtilsTemplate);
        var context  = new ApiTemplateContext { Document = document, GeneratedAt = DateTime.UtcNow };
        var output   = await _templateService.RenderAsync(template, context);
        var path     = Path.Combine(outputDirectory, GeneratorConstants.AplSourceDir, "utils.apln");

        await _templateService.SaveOutputAsync(output, path);
        _logger.LogInformation("Generated: {AplSourceDir}/utils.apln", GeneratorConstants.AplSourceDir);
    }

    /// <summary>
    /// Generates the APLSource/Version.aplf function file.
    /// </summary>
    public async Task GenerateVersionAsync(OpenApiDocument document, string outputDirectory)
    {
        var template = await _templateService.LoadTemplateAsync(GeneratorConstants.VersionTemplate);
        var context  = new ApiTemplateContext { Document = document, GeneratedAt = DateTime.UtcNow };
        var output   = await _templateService.RenderAsync(template, context);
        var path     = Path.Combine(outputDirectory, GeneratorConstants.AplSourceDir, "Version.aplf");

        await _templateService.SaveOutputAsync(output, path);
        _logger.LogInformation("Generated: {AplSourceDir}/Version.aplf", GeneratorConstants.AplSourceDir);
    }

    /// <summary>
    /// Copies the embedded HttpCommand.aplc binary resource to the output directory,
    /// skipping the write if the existing file is already identical.
    /// </summary>
    public async Task CopyHttpCommandAsync(string outputDirectory)
    {
        var destPath = Path.Combine(outputDirectory, GeneratorConstants.AplSourceDir, "HttpCommand.aplc");
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        using var srcStream = _templateService.GetEmbeddedResourceStream(GeneratorConstants.HttpCommandResource);
        using var memStream = new MemoryStream();
        await srcStream.CopyToAsync(memStream);
        var srcBytes = memStream.ToArray();

        if (File.Exists(destPath))
        {
            var destBytes = await File.ReadAllBytesAsync(destPath);
            if (srcBytes.SequenceEqual(destBytes))
            {
                _logger.LogInformation("Unchanged: {AplSourceDir}/HttpCommand.aplc", GeneratorConstants.AplSourceDir);
                return;
            }
        }

        await File.WriteAllBytesAsync(destPath, srcBytes);
        _logger.LogInformation("Copied: {AplSourceDir}/HttpCommand.aplc", GeneratorConstants.AplSourceDir);
    }

    /// <summary>
    /// Copies the OpenAPI specification file to the output directory,
    /// skipping the write if the existing file is already identical.
    /// </summary>
    /// <exception cref="IOException">Re-thrown after logging if the copy fails.</exception>
    public async Task CopySpecificationAsync(string sourcePath, string outputDirectory)
    {
        var fileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(outputDirectory, fileName);

        try
        {
            var srcBytes = await File.ReadAllBytesAsync(sourcePath);

            if (File.Exists(destPath))
            {
                var destBytes = await File.ReadAllBytesAsync(destPath);
                if (srcBytes.SequenceEqual(destBytes))
                {
                    _logger.LogInformation("Unchanged: {FileName}", fileName);
                    return;
                }
            }

            await File.WriteAllBytesAsync(destPath, srcBytes);
            _logger.LogInformation("Copied: {FileName}", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying {FileName}", fileName);
            throw;
        }
    }

    /// <summary>
    /// Generates the APLSource/Client.aplc class file.
    /// </summary>
    public async Task GenerateClientAsync(OpenApiDocument document, string outputDirectory, string? @namespace = null)
    {
        var template = await _templateService.LoadTemplateAsync(GeneratorConstants.ClientTemplate);
        var context  = new ApiTemplateContext
        {
            Document    = document,
            Namespace   = @namespace,
            GeneratedAt = DateTime.UtcNow
        };

        context.CustomProperties["class_name"] = GeneratorConstants.DefaultClientClass;
        context.CustomProperties["tags"] = context.GetAllTags()
            .Select(OperationNaming.TagName)
            .Distinct()
            .ToList();

        var output = await _templateService.RenderAsync(template, context);
        var path   = Path.Combine(outputDirectory, GeneratorConstants.AplSourceDir,
            $"{GeneratorConstants.DefaultClientClass}.aplc");

        await _templateService.SaveOutputAsync(output, path);
        _logger.LogInformation("Generated: {AplSourceDir}/{ClientClass}.aplc",
            GeneratorConstants.AplSourceDir, GeneratorConstants.DefaultClientClass);
    }

    /// <summary>
    /// Generates the README.md file.
    /// </summary>
    /// <exception cref="Exception">Re-thrown after logging if generation fails.</exception>
    public async Task GenerateReadmeAsync(
        OpenApiDocument document, string outputDirectory,
        IReadOnlyDictionary<string, IOpenApiSchema>? inlineSchemas = null)
    {
        try
        {
            var template = await _templateService.LoadTemplateAsync(GeneratorConstants.ReadmeTemplate);
            var context  = new ApiTemplateContext
            {
                Document         = document,
                GeneratedAt      = DateTime.UtcNow,
                InlineSchemas    = inlineSchemas ?? new Dictionary<string, IOpenApiSchema>()
            };
            context.CustomProperties["class_name"] = GeneratorConstants.DefaultClientClass;

            var output = await _templateService.RenderAsync(template, context);
            var path   = Path.Combine(outputDirectory, "README.md");

            await _templateService.SaveOutputAsync(output, path);
            _logger.LogInformation("Generated: README.md");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating README.md");
            throw;
        }
    }

    /// <summary>
    /// Generates docs/&lt;tag&gt;.md for each tag, named by the tag's APL name (as used for
    /// client.&lt;tag&gt;), which is always a safe file name.
    /// </summary>
    public async Task GenerateTagDocsAsync(
        OpenApiDocument document, string outputDirectory,
        IReadOnlyDictionary<string, IOpenApiSchema>? inlineSchemas = null)
    {
        var template = await _templateService.LoadTemplateAsync(GeneratorConstants.TagDocTemplate);
        var context  = new ApiTemplateContext
        {
            Document      = document,
            GeneratedAt   = DateTime.UtcNow,
            InlineSchemas = inlineSchemas ?? new Dictionary<string, IOpenApiSchema>()
        };

        foreach (var tag in context.TagDocs)
        {
            context.CustomProperties["tag"] = tag;

            var output = await _templateService.RenderAsync(template, context);
            var path   = Path.Combine(outputDirectory, tag.DocPath);

            await _templateService.SaveOutputAsync(output, path);
            _logger.LogInformation("Generated: {DocPath}", tag.DocPath);
        }
    }
}
