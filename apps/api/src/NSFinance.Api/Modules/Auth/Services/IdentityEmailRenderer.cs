using System.Net;
using System.Text.Json;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class IdentityEmailRenderer
{
    public const string EmailVerificationTemplate = "identity.email-verification";
    public const string PasswordResetTemplate = "identity.password-reset";
    public const string PasswordChangeTemplate = "identity.password-change";
    public const string AccountDeletionTemplate = "identity.account-deletion";
    public const string AccountCreatedTemplate = "identity.account-created";
    public const string PhoneChangedTemplate = "identity.phone-changed";
    public const int CurrentTemplateVersion = 1;

    public RenderedIdentityEmail Render(string templateKey, string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<IdentityEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("Transactional identity payload is invalid.");

        return templateKey switch
        {
            EmailVerificationTemplate => RenderCodeEmail(
                "Confirm your NSFinance email",
                "Use this code to finish creating your NSFinance account.",
                payload),
            PasswordResetTemplate => RenderCodeEmail(
                "Reset your NSFinance password",
                "Use this code in the NSFinance app to reset your password.",
                payload),
            PasswordChangeTemplate => RenderCodeEmail(
                "Confirm your NSFinance password change",
                "Use this code in the NSFinance app to confirm your password change.",
                payload),
            AccountDeletionTemplate => RenderCodeEmail(
                "Confirm your NSFinance account deletion request",
                "Use this code in the NSFinance app to confirm the account deletion request.",
                payload),
            AccountCreatedTemplate => RenderNoticeEmail(
                "Your NSFinance account is ready",
                "Your NSFinance account was created successfully.",
                "If this was not you, secure your email account and contact NSFinance support.",
                payload),
            PhoneChangedTemplate => RenderNoticeEmail(
                "Your NSFinance phone number changed",
                "The verified phone number on your NSFinance account was changed.",
                "If you did not make this change, use the security link below immediately.",
                payload),
            _ => throw new InvalidOperationException($"Unknown identity email template '{templateKey}'.")
        };
    }

    private static RenderedIdentityEmail RenderCodeEmail(
        string subject,
        string intro,
        IdentityEmailPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Code) || payload.ExpiresInMinutes is null)
        {
            throw new InvalidOperationException("The code email payload is incomplete.");
        }

        var safeName = WebUtility.HtmlEncode(NormalizeDisplayName(payload.DisplayName));
        var safeCode = WebUtility.HtmlEncode(payload.Code);
        var plainText =
            $"Hi {NormalizeDisplayName(payload.DisplayName)},\n\n{intro}\n\n" +
            $"{payload.Code}\n\nThis code expires in {payload.ExpiresInMinutes} minutes. " +
            "Never share it. NSFinance support will never ask for this code.\n\n" +
            "If you did not request this, you can ignore this message.";

        var body =
            $"<p style=\"margin:0 0 18px\">Hi {safeName},</p>" +
            $"<p style=\"margin:0 0 24px\">{WebUtility.HtmlEncode(intro)}</p>" +
            $"<div style=\"margin:0 0 24px;padding:18px 16px;background:#F4F8F6;border:1px solid #D9E6E0;border-radius:8px;text-align:center;font-size:32px;font-weight:700;letter-spacing:8px;color:#153F34\">{safeCode}</div>" +
            $"<p style=\"margin:0 0 12px;color:#52645E\">This code expires in {payload.ExpiresInMinutes} minutes.</p>" +
            "<p style=\"margin:0;color:#52645E\">Never share it. NSFinance support will never ask for this code.</p>";

        return new RenderedIdentityEmail(subject, plainText, WrapHtml(subject, body));
    }

    private static RenderedIdentityEmail RenderNoticeEmail(
        string subject,
        string intro,
        string securityCopy,
        IdentityEmailPayload payload)
    {
        var name = NormalizeDisplayName(payload.DisplayName);
        var safeName = WebUtility.HtmlEncode(name);
        var securityLine = string.IsNullOrWhiteSpace(payload.SecurityUrl)
            ? securityCopy
            : $"{securityCopy} {payload.SecurityUrl}";
        var plainText = $"Hi {name},\n\n{intro}\n\n{securityLine}";
        var body =
            $"<p style=\"margin:0 0 18px\">Hi {safeName},</p>" +
            $"<p style=\"margin:0 0 18px\">{WebUtility.HtmlEncode(intro)}</p>" +
            $"<p style=\"margin:0;color:#52645E\">{WebUtility.HtmlEncode(securityCopy)}</p>";

        if (!string.IsNullOrWhiteSpace(payload.SecurityUrl))
        {
            var safeUrl = WebUtility.HtmlEncode(payload.SecurityUrl);
            body += $"<p style=\"margin:24px 0 0\"><a href=\"{safeUrl}\" style=\"display:inline-block;padding:12px 18px;border-radius:6px;background:#153F34;color:#FFFFFF;text-decoration:none;font-weight:700\">Secure my account</a></p>";
        }

        return new RenderedIdentityEmail(subject, plainText, WrapHtml(subject, body));
    }

    private static string WrapHtml(string heading, string body)
    {
        return "<!doctype html><html><body style=\"margin:0;background:#F7FAF8;color:#19342C;font-family:Arial,sans-serif\">" +
            "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#F7FAF8;padding:28px 14px\"><tr><td align=\"center\">" +
            "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"max-width:560px;background:#FFFFFF;border:1px solid #E1E9E5;border-radius:8px\">" +
            "<tr><td style=\"padding:30px 30px 12px;font-size:18px;font-weight:700;color:#153F34\">NSFinance</td></tr>" +
            $"<tr><td style=\"padding:8px 30px 32px\"><h1 style=\"margin:0 0 22px;font-size:24px;line-height:1.25\">{WebUtility.HtmlEncode(heading)}</h1>{body}</td></tr>" +
            "<tr><td style=\"padding:20px 30px;border-top:1px solid #E8EFEB;color:#71807B;font-size:12px\">A private security message from NSFinance.</td></tr>" +
            "</table></td></tr></table></body></html>";
    }

    private static string NormalizeDisplayName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "there" : value.Trim();
    }
}
