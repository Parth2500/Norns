namespace CoreOps.Api.Authentication;

/// <summary>
/// Settings for validating Heimdall access tokens. Heimdall puts no tenant claim in its tokens: each Heimdall tenant
/// has its own issuer URL. <see cref="Tenants"/> maps each trusted issuer to a CoreOps tenant.
/// </summary>
public sealed class HeimdallOptions
{
    public const string SectionName = "Heimdall";

    /// <summary>The audience CoreOps accepts. It must be in the allowed audiences of the Heimdall client.</summary>
    public string Audience { get; set; } = default!;

    public List<HeimdallTenant> Tenants { get; set; } = [];

    /// <summary>Development only. See <see cref="HeimdallDevelopmentTls"/>.</summary>
    public HeimdallDevelopmentTls? DevelopmentTls { get; set; }
}

public sealed class HeimdallTenant
{
    /// <summary>The Heimdall issuer, for example <c>https://acme.localhost:7200</c>. Compared exactly.</summary>
    public string Issuer { get; set; } = default!;

    /// <summary>The CoreOps tenant that tokens from this issuer act in.</summary>
    public Guid TenantId { get; set; }
}

/// <summary>
/// In development Heimdall serves <c>https://&lt;tenant&gt;.localhost</c> with the ASP.NET development certificate,
/// which is issued for <c>localhost</c>. This pins that certificate and checks the name against
/// <see cref="TlsName"/>, so certificate checks stay on.
/// </summary>
public sealed class HeimdallDevelopmentTls
{
    /// <summary>The development certificate as PEM, from <c>dotnet dev-certs https --export-path ... --format PEM</c>.</summary>
    public string CaFile { get; set; } = default!;

    public string TlsName { get; set; } = "localhost";
}
