using System.Text.Json;
using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class IdentityEmailRendererTests
{
    private readonly IdentityEmailRenderer _renderer = new();

    [Fact]
    public void Render_VersionTwoCodeEmail_ExposesCodeToEmailPreviewWithoutUsingSubject()
    {
        var rendered = _renderer.Render(
            IdentityEmailRenderer.EmailVerificationTemplate,
            IdentityEmailRenderer.CurrentTemplateVersion,
            CreatePayloadJson());

        Assert.StartsWith("123456 is your NSFinance security code.", rendered.PlainText);
        Assert.Contains("display:none;max-height:0", rendered.Html, StringComparison.Ordinal);
        Assert.Contains("123456 is your NSFinance security code.", rendered.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", rendered.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_VersionOneCodeEmail_RemainsCompatibleWithQueuedMessages()
    {
        var rendered = _renderer.Render(
            IdentityEmailRenderer.EmailVerificationTemplate,
            1,
            CreatePayloadJson());

        Assert.StartsWith("Hi Adrian,", rendered.PlainText);
        Assert.DoesNotContain("display:none;max-height:0", rendered.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_EncodesDisplayNameInHtml()
    {
        var rendered = _renderer.Render(
            IdentityEmailRenderer.EmailVerificationTemplate,
            IdentityEmailRenderer.CurrentTemplateVersion,
            CreatePayloadJson("<script>alert('x')</script>"));

        Assert.DoesNotContain("<script>", rendered.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", rendered.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RejectsUnsupportedTemplateVersion()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => _renderer.Render(
            IdentityEmailRenderer.EmailVerificationTemplate,
            99,
            CreatePayloadJson()));

        Assert.Contains("Unsupported identity email template version", exception.Message);
    }

    private static string CreatePayloadJson(string displayName = "Adrian") =>
        JsonSerializer.Serialize(new IdentityEmailPayload(
            displayName,
            "123456",
            10,
            DateTime.UtcNow));
}
