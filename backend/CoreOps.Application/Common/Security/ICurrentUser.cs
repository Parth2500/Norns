namespace CoreOps.Application.Common.Security;

/// <summary>
/// The signed-in user of the current request, taken from a validated Heimdall access token.
/// Reading it outside an authenticated request throws.
/// </summary>
public interface ICurrentUser
{
    Guid TenantId { get; }
    Guid UserId { get; }
}
