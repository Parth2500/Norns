using CoreOps.Application.Common.Security;

namespace CoreOps.Api.Authentication;

internal sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid TenantId => ReadGuid(HeimdallAuthentication.TenantClaim);

    public Guid UserId => ReadGuid(HeimdallAuthentication.SubjectClaim);

    private Guid ReadGuid(string claimType)
    {
        var value = _httpContextAccessor.HttpContext?.User.FindFirst(claimType)?.Value;

        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("There is no signed-in user. The endpoint must require authorization.");
    }
}
