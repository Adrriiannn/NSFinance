using NSFinTech.Api.Modules.Banking.DTOs;
using NSFinTech.Api.Modules.Banking.Validators;

namespace NSFinTech.Api.Tests.Unit;

public class TrueLayerCallbackQueryValidatorTests
{
    [Fact]
    public void Validate_WithCodeAndState_ReturnsNoErrors()
    {
        var query = new TrueLayerCallbackQuery("auth-code", "state-123", null, null);

        var errors = TrueLayerCallbackQueryValidator.Validate(query);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MissingState_ReturnsStateError()
    {
        var query = new TrueLayerCallbackQuery("auth-code", null, null, null);

        var errors = TrueLayerCallbackQueryValidator.Validate(query);

        Assert.True(errors.ContainsKey("state"));
    }

    [Fact]
    public void Validate_BothCodeAndError_ReturnsValidationError()
    {
        var query = new TrueLayerCallbackQuery("auth-code", "state-123", "access_denied", "denied");

        var errors = TrueLayerCallbackQueryValidator.Validate(query);

        Assert.True(errors.ContainsKey("code"));
    }
}
