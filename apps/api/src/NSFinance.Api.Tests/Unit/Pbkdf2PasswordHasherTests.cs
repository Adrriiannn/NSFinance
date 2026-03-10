using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Tests.Unit;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void HashAndVerify_WorksForValidPassword()
    {
        var hash = _hasher.HashPassword("ValidPassword123");

        Assert.True(_hasher.VerifyPassword("ValidPassword123", hash));
        Assert.False(_hasher.VerifyPassword("WrongPassword123", hash));
    }

    [Fact]
    public void NeedsRehash_ReturnsTrue_ForInvalidHashFormat()
    {
        Assert.True(_hasher.NeedsRehash("bad-format"));
    }
}
