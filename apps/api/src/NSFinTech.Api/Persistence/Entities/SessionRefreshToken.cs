namespace NSFinTech.Api.Persistence.Entities;

public class SessionRefreshToken
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid FamilyId { get; set; }
    public Guid? ParentTokenId { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? UsedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? RevocationReason { get; set; }

    public Session? Session { get; set; }
    public SessionRefreshToken? ParentToken { get; set; }
    public SessionRefreshToken? ReplacedByToken { get; set; }
}
