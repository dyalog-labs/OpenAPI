namespace OpenAPIDyalog.Constants;

/// <summary>
/// Centralised string constants for the generator. All magic strings live here.
/// </summary>
public static class GeneratorConstants
{
    // ── Output directory names ────────────────────────────────────────────────
    public const string AplSourceDir   = "APLSource";
    public const string TagsSubDir     = "_tags";
    public const string ModelsSubDir   = "models";

    // ── Embedded template resource paths ─────────────────────────────────────
    public const string EndpointTemplate    = "APLSource/_tags/endpoint.aplf.scriban";
    public const string ClientTemplate      = "APLSource/Client.aplc.scriban";
    public const string UtilsTemplate       = "APLSource/utils.apln.scriban";
    public const string VersionTemplate     = "APLSource/Version.aplf.scriban";
    public const string ReadmeTemplate      = "README.md.scriban";
    public const string TagDocTemplate      = "docs/tag.md.scriban";
    public const string ModelTemplate       = "APLSource/models/model.aplc.scriban";
    public const string HttpCommandResource        = "APLSource/HttpCommand.aplc";
    public const string ThirdPartyNoticesResource = "OpenAPIDyalog.THIRD_PARTY_NOTICES.txt";

    // ── Content types ─────────────────────────────────────────────────────────
    public const string ContentTypeJson          = "application/json";
    public const string ContentTypeOctetStream   = "application/octet-stream";
    public const string ContentTypeMultipartForm = "multipart/form-data";

    // ── Defaults ─────────────────────────────────────────────────────────────
    public const string DefaultOutputDirectory = "./generated";
    public const string DefaultTagName         = "default";
    public const string DefaultClientClass     = "Client";
}
