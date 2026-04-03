using NSFinance.Api.Modules.Auth.Validators;

namespace NSFinance.Api.Tests.Unit;

public class PasswordPolicyValidatorTests
{
    [Fact]
    public void Validate_ReturnsErrors_ForWeakPassword()
    {
        var errors = PasswordPolicyValidator.Validate("short");

        Assert.NotEmpty(errors);
        Assert.Contains(errors, x => x.Contains("at least 12", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ReturnsNoErrors_ForValidPassword()
    {
        var errors = PasswordPolicyValidator.Validate("ValidPassword123");

        Assert.Empty(errors);
    }
}
