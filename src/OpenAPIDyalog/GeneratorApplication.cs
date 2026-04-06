using System.Reflection;
using Microsoft.Extensions.Logging;
using OpenAPIDyalog.Constants;
using OpenAPIDyalog.Models;
using OpenAPIDyalog.Services;

namespace OpenAPIDyalog;

/// <summary>
/// CLI orchestrator: parses arguments, constructs services, and drives generation.
/// </summary>
public static class GeneratorApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            DisplayUsage();
            return 0;
        }

        if (args.Contains("--third-party-notices"))
        {
            DisplayThirdPartyNotices();
            return 0;
        }

        if (args.Length == 0)
        {
            DisplayUsage();
            return 1;
        }

        var options = ParseArguments(args);
        if (options == null)
        {
            DisplayUsage();
            return 1;
        }

        if (!options.IsValid())
        {
            Console.Error.WriteLine("Invalid options:");
            foreach (var error in options.GetValidationErrors())
                Console.Error.WriteLine($"  - {error}");
            return 1;
        }

        // Create logger factory — SimpleConsole with single-line output.
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddSimpleConsole(o => o.SingleLine = true));

        // Use a string category for the top-level logger since static classes
        // cannot be used as ILogger<T> type arguments.
        var appLogger = loggerFactory.CreateLogger("OpenAPIDyalog.GeneratorApplication");

        if (!File.Exists(options.SpecificationPath))
        {
            appLogger.LogError("File not found: {Path}", options.SpecificationPath);
            return 1;
        }

        appLogger.LogInformation("Reading OpenAPI spec from: {Path}", options.SpecificationPath);

        // Construct services with manual injection.
        var templateService = new TemplateService(loggerFactory.CreateLogger<TemplateService>());
        var openApiService  = new OpenApiService(loggerFactory.CreateLogger<OpenApiService>());
        var artifactService = new ArtifactGeneratorService(templateService, loggerFactory.CreateLogger<ArtifactGeneratorService>());
        var endpointService = new EndpointGeneratorService(templateService, loggerFactory.CreateLogger<EndpointGeneratorService>());
        var modelService    = new ModelGeneratorService(templateService, loggerFactory.CreateLogger<ModelGeneratorService>());
        var codeGen         = new CodeGeneratorService(artifactService, endpointService, modelService,
                                  loggerFactory.CreateLogger<CodeGeneratorService>());

        // Load and validate specification.
        var result = await openApiService.LoadSpecificationAsync(options.SpecificationPath, options.DisableValidation);

        if (!result.IsSuccess)
        {
            if (result.ErrorMessage != null)
                appLogger.LogError("{Message}", result.ErrorMessage);

            if (result.Diagnostic?.Errors?.Count > 0)
            {
                foreach (var error in result.Diagnostic.Errors)
                    appLogger.LogError("  {Pointer} — {Message}", error.Pointer, error.Message);
            }

            return 1;
        }

        var document = result.Document!;

        // Log document info (replaces DisplayDocumentInfo / DisplayPaths).
        appLogger.LogInformation("Title: {Title}  Version: {Version}  Paths: {PathCount}",
            document.Info?.Title ?? "N/A",
            document.Info?.Version ?? "N/A",
            document.Paths?.Count ?? 0);

        if (document.Paths != null)
        {
            foreach (var path in document.Paths)
            {
                if (path.Value?.Operations == null) continue;
                foreach (var op in path.Value.Operations)
                    appLogger.LogInformation("  {Method} {Path}: {Summary}",
                        op.Key, path.Key, op.Value.Summary ?? "N/A");
            }
        }

        // Check embedded templates are present.
        var availableTemplates = templateService.GetAvailableTemplates().ToList();
        if (availableTemplates.Count == 0)
        {
            appLogger.LogWarning("No templates found in assembly. This is a build defect. Skipping code generation.");
            return 1;
        }

        appLogger.LogInformation("Found {Count} template(s).", availableTemplates.Count);

        // Log operation summary.
        var summary = codeGen.GetOperationSummary(document);
        foreach (var tag in summary)
            appLogger.LogInformation("  {Tag}: {Count} operation(s)", tag.Key, tag.Value);

        appLogger.LogInformation("Generating client code to: {OutputDirectory}", options.OutputDirectory);

        try
        {
            await codeGen.GenerateAsync(document, options);
            appLogger.LogInformation("Code generation complete.");
            return 0;
        }
        catch (Exception ex)
        {
            appLogger.LogError(ex, "Error during code generation: {Message}", ex.Message);
            return 1;
        }
    }

    private static GeneratorOptions? ParseArguments(string[] args)
    {
        var positional        = new List<string>();
        var disableValidation = false;

        foreach (var arg in args)
        {
            if (arg == "--no-validation" || arg == "-nv")
                disableValidation = true;
            else
                positional.Add(arg);
        }

        if (positional.Count == 0) return null;

        return new GeneratorOptions
        {
            SpecificationPath = positional[0],
            OutputDirectory   = positional.Count > 1 ? positional[1] : GeneratorConstants.DefaultOutputDirectory,
            DisableValidation = disableValidation
        };
    }

    private static void DisplayThirdPartyNotices()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(GeneratorConstants.ThirdPartyNoticesResource)
            ?? throw new InvalidOperationException("Third-party notices resource not found. This is a build defect.");
        using var reader = new StreamReader(stream);
        Console.Write(reader.ReadToEnd());
    }

    // DisplayUsage is UI output, not a logging concern — Console.WriteLine is intentional.
    private static void DisplayUsage()
    {
        Console.WriteLine("OpenAPI Dyalog Generator");
        Console.WriteLine();
        Console.WriteLine("Usage: OpenAPIDyalog [options] <spec-file-path> [output-directory]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <spec-file-path>    Path to the OpenAPI specification file (JSON or YAML)");
        Console.WriteLine("  [output-directory]  Directory for generated files (default: ./generated)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --help, -h              Show this help message and exit");
        Console.WriteLine("  --no-validation, -nv    Disable OpenAPI specification validation rules");
        Console.WriteLine("  --third-party-notices   Print third-party software notices and exit");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  OpenAPIDyalog openapispec.json");
        Console.WriteLine("  OpenAPIDyalog ./specs/openapispec.yaml ./output");
        Console.WriteLine("  OpenAPIDyalog --no-validation openapispec.yaml");
    }
}
