using System.Reflection;
using CaseConverter;
using Microsoft.Extensions.Logging;
using OpenAPIDyalog.Models;
using OpenAPIDyalog.Services.Interfaces;
using OpenAPIDyalog.Utils;
using Scriban;
using Scriban.Runtime;

namespace OpenAPIDyalog.Services;

/// <summary>
/// Service for loading and rendering Scriban templates.
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly ILogger<TemplateService> _logger;

    public TemplateService(ILogger<TemplateService> logger)
    {
        _logger = logger;
    }

    // Convert a template relative path to its embedded resource name.
    // "APLSource/Client.aplc.scriban" → "OpenAPIDyalog.Templates.APLSource.Client.aplc.scriban"
    private static string ToResourceName(string relativePath)
    {
        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name!;
        return assemblyName + ".Templates." + relativePath.Replace('/', '.').Replace('\\', '.');
    }

    /// <summary>
    /// Returns a stream for a non-template embedded resource. The caller owns disposal.
    /// </summary>
    public Stream GetEmbeddedResourceStream(string relativePath)
    {
        return Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ToResourceName(relativePath))
            ?? throw new InvalidOperationException(
                $"Embedded resource not found: '{relativePath}'. This is a build defect.");
    }

    /// <summary>
    /// Loads a template from embedded resources.
    /// </summary>
    public async Task<Template> LoadTemplateAsync(string templateName)
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ToResourceName(templateName))
            ?? throw new FileNotFoundException(
                $"Embedded template not found: '{templateName}'. This is a build defect.", templateName);

        using var reader = new StreamReader(stream);
        var templateContent = await reader.ReadToEndAsync();
        var template = Template.Parse(templateContent);

        if (template.HasErrors)
        {
            var errors = string.Join("\n", template.Messages.Select(m => m.Message));
            throw new InvalidOperationException($"Template parsing errors:\n{errors}");
        }

        return template;
    }

    /// <summary>
    /// Renders a template with the provided data context.
    /// </summary>
    public string Render(Template template, object context)
    {
        try
        {
            var scriptObject = BuildScriptObject(context);
            var templateContext = new TemplateContext { LoopLimit = int.MaxValue };
            templateContext.PushGlobal(scriptObject);
            return template.Render(templateContext);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Template rendering failed: {ex.GetBaseException().Message}", ex);
        }
    }

    /// <summary>
    /// Renders a template asynchronously with the provided data context.
    /// </summary>
    public async Task<string> RenderAsync(Template template, object context)
    {
        try
        {
            var scriptObject = BuildScriptObject(context);
            var templateContext = new TemplateContext { LoopLimit = int.MaxValue };
            templateContext.PushGlobal(scriptObject);
            return await template.RenderAsync(templateContext);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Template rendering failed: {ex.GetBaseException().Message}", ex);
        }
    }

    /// <summary>
    /// Loads and renders a template in one operation.
    /// </summary>
    public async Task<string> LoadAndRenderAsync(string templateName, object context)
    {
        var template = await LoadTemplateAsync(templateName);
        return await RenderAsync(template, context);
    }

    /// <summary>
    /// Saves rendered output to a file, skipping the write if the existing content is identical.
    /// </summary>
    public async Task SaveOutputAsync(string output, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(outputPath) && await File.ReadAllTextAsync(outputPath) == output)
            return;

        await File.WriteAllTextAsync(outputPath, output);
    }

    /// <summary>
    /// Lists all available templates embedded in the assembly.
    /// </summary>
    public IEnumerable<string> GetAvailableTemplates()
    {
        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name!;
        var prefix = assemblyName + ".Templates.";
        return Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix) && n.EndsWith(".scriban"));
    }

    /// <summary>
    /// Builds the Scriban ScriptObject used by both Render and RenderAsync.
    /// Converts PascalCase property names to snake_case for templates.
    /// </summary>
    private static ScriptObject BuildScriptObject(object context)
    {
        var scriptObject = new ScriptObject();
        scriptObject.Import(context, renamer: member => member.Name.ToSnakeCase());

        // Use ITemplateContext interface to merge CustomProperties — no concrete type check needed.
        if (context is ITemplateContext tc && tc.CustomProperties.Any())
        {
            foreach (var prop in tc.CustomProperties)
            {
                scriptObject[prop.Key.ToSnakeCase()] = prop.Value;
            }
        }

        scriptObject.Import("comment_lines", new Func<string?, string>(StringHelpers.CommentLines));
        scriptObject.Import("apl_name",      new Func<string?, string>(n => StringHelpers.ToValidAplName(n ?? string.Empty)));
        scriptObject.Import("one_line",      new Func<string?, string>(StringHelpers.ToSingleLine));
        scriptObject.Import("md_cell",       new Func<string?, string>(StringHelpers.ToTableCell));

        return scriptObject;
    }

    private static string ToSnakeCase(string name) => name.ToSnakeCase();
}
