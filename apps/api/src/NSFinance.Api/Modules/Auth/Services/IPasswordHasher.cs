namespace NSFinance.Api.Modules.Auth.Services;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string storedHash);
    void PerformDummyVerification(string password);
    bool NeedsRehash(string storedHash);
}
