namespace NSFinTech.Api.Modules.Users.Services;

public interface ICurrentUserProvider
{
    Guid UserId { get; }
    bool TryGetUserId(out Guid userId);
}
