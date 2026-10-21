using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using CoreOps.Application.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CoreOps.Api.Authentication;

public static class HeimdallAuthentication
{
    public const string Scheme = "Heimdall";

    /// <summary>Added by CoreOps after validation. A token that already carries it is refused.</summary>
    public const string TenantClaim = "coreops:tenant_id";

    public const string SubjectClaim = "sub";

    public static IServiceCollection AddHeimdallAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration.GetSection(HeimdallOptions.SectionName).Get<HeimdallOptions>()
            ?? throw new InvalidOperationException($"The '{HeimdallOptions.SectionName}' section is missing.");
        Validate(options, environment);

        // One JWT bearer scheme per issuer, so each one reads the discovery document and JWKS of its own issuer.
        // The policy scheme picks the scheme from the token's (not yet validated) iss. The chosen scheme then
        // validates everything, the issuer included, so a wrong iss only picks a scheme that refuses the token.
        var schemesByIssuer = new Dictionary<string, string>(StringComparer.Ordinal);
        var builder = services.AddAuthentication(Scheme);
        for (var i = 0; i < options.Tenants.Count; i++)
        {
            var tenant = options.Tenants[i];
            var scheme = $"{Scheme}.{i}";
            schemesByIssuer[tenant.Issuer] = scheme;
            builder.AddJwtBearer(scheme, jwt => ConfigureJwtBearer(jwt, tenant, options, environment));
        }

        var fallbackScheme = schemesByIssuer[options.Tenants[0].Issuer];
        builder.AddPolicyScheme(Scheme, Scheme, policy =>
        {
            policy.ForwardDefaultSelector = context =>
                ReadUnvalidatedIssuer(context) is { } issuer && schemesByIssuer.TryGetValue(issuer, out var scheme)
                    ? scheme
                    : fallbackScheme;
        });

        // Every endpoint needs a signed-in user unless it says otherwise.
        services.AddAuthorization(authorization =>
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }

    private static void ConfigureJwtBearer(
        JwtBearerOptions jwt,
        HeimdallTenant tenant,
        HeimdallOptions options,
        IHostEnvironment environment)
    {
        jwt.Authority = tenant.Issuer;
        jwt.Audience = options.Audience;
        jwt.RequireHttpsMetadata = true;
        jwt.MapInboundClaims = false;

        // Heimdall signs access tokens with RS256 and typ at+jwt (RFC 9068). An ID token has typ JWT and fails here.
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = tenant.Issuer,
            ValidAudience = options.Audience,
            ValidTypes = ["at+jwt"],
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            NameClaimType = SubjectClaim,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        jwt.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var identity = context.Principal?.Identity as ClaimsIdentity;

                // A client_credentials token has the client id as sub. CoreOps acts only for users.
                if (identity is null || !Guid.TryParse(identity.FindFirst(SubjectClaim)?.Value, out _))
                {
                    context.Fail("The token does not belong to a user.");
                    return Task.CompletedTask;
                }

                if (identity.HasClaim(c => c.Type == TenantClaim))
                {
                    context.Fail("The token carries a reserved claim.");
                    return Task.CompletedTask;
                }

                identity.AddClaim(new Claim(TenantClaim, tenant.TenantId.ToString()));
                return Task.CompletedTask;
            },
        };

        if (options.DevelopmentTls is { } tls && environment.IsDevelopment())
        {
            jwt.BackchannelHttpHandler = CreateDevelopmentBackchannel(tls, environment);
        }
    }

    private static string? ReadUnvalidatedIssuer(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // CanReadToken checks only the shape, so a malformed token can still throw here. The chosen scheme refuses it.
        try
        {
            return new JsonWebTokenHandler().ReadJsonWebToken(header["Bearer ".Length..].Trim()).Issuer;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static HttpMessageHandler CreateDevelopmentBackchannel(HeimdallDevelopmentTls tls, IHostEnvironment environment)
    {
        var ca = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(environment.ContentRootPath, tls.CaFile)));
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(ca);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(certificate) && certificate.MatchesHostname(tls.TlsName);
            },
        };
    }

    private static void Validate(HeimdallOptions options, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException("Heimdall:Audience is required.");
        }

        if (options.Tenants.Count == 0)
        {
            throw new InvalidOperationException("Heimdall:Tenants needs at least one issuer.");
        }

        foreach (var tenant in options.Tenants)
        {
            if (!Uri.TryCreate(tenant.Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException($"Heimdall issuer '{tenant.Issuer}' must be an absolute https URL.");
            }

            if (tenant.TenantId == Guid.Empty)
            {
                throw new InvalidOperationException($"Heimdall issuer '{tenant.Issuer}' has no TenantId.");
            }
        }

        if (options.Tenants.Select(t => t.Issuer).Distinct(StringComparer.Ordinal).Count() != options.Tenants.Count)
        {
            throw new InvalidOperationException("Heimdall:Tenants lists an issuer twice.");
        }

        if (options.DevelopmentTls is { } tls)
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("Heimdall:DevelopmentTls is allowed only in Development.");
            }

            if (!File.Exists(Path.Combine(environment.ContentRootPath, tls.CaFile)))
            {
                throw new InvalidOperationException(
                    $"Heimdall:DevelopmentTls:CaFile '{tls.CaFile}' does not exist. Export it with "
                    + $"'dotnet dev-certs https --export-path {tls.CaFile} --format PEM --no-password' "
                    + "in CoreOps.Api, then delete the .key file it also writes.");
            }
        }
    }
}
