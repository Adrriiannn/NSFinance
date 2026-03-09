using System.Net;
using NSFinTech.Api.Modules.Banking.DTOs;
using NSFinTech.Api.Modules.Banking.Services;

namespace NSFinTech.Api.Modules.Banking.Endpoints;

public static class TrueLayerCallbackEndpoint
{
    public static async Task<IResult> HandleAsync(
        string? code,
        string? state,
        string? error,
        string? error_description,
        TrueLayerAuthService authService,
        CancellationToken cancellationToken)
    {
        var outcome = await authService.HandleCallbackAsync(
            new TrueLayerCallbackQuery(code, state, error, error_description),
            cancellationToken);

        var safeHtml = BuildSafeHtml(outcome);
        return Results.Content(safeHtml, "text/html", statusCode: outcome.HttpStatusCode);
    }

    private static string BuildSafeHtml(TrueLayerCallbackOutcome outcome)
    {
        var title = outcome.Succeeded ? "Bank Connected" : "Bank Connection Failed";
        var message = WebUtility.HtmlEncode(outcome.Message);
        var statusCode = WebUtility.HtmlEncode(outcome.Code);
        var appResult = outcome.Succeeded ? "success" : "error";
        var appReturnUrl = BuildAppReturnUrl(appResult, outcome.Code);
        var appReturnUrlForHref = WebUtility.HtmlEncode(appReturnUrl);
        var appReturnUrlForScript = appReturnUrl.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        return $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="utf-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1" />
                  <title>{{title}}</title>
                </head>
                <body style="font-family: Arial, sans-serif; background:#081423; color:#e9f1ff; padding:24px;">
                  <h2>{{title}}</h2>
                  <p>{{message}}</p>
                  <p style="opacity:0.75;">Code: {{statusCode}}</p>
                  <p style="opacity:0.75;">Returning to NSFinTech...</p>
                  <a id="return-link" href="{{appReturnUrlForHref}}" style="display:inline-block; margin-top:12px; color:#9dccff;">
                    Return to app
                  </a>
                  <script>
                    (function () {
                      var target = "{{appReturnUrlForScript}}";
                      var isMobile = /Android|iPhone|iPad|iPod/i.test(navigator.userAgent || "");
                      if (!isMobile) {
                        return;
                      }

                      setTimeout(function () {
                        window.location.href = target;
                      }, 100);
                    })();
                  </script>
                </body>
                </html>
                """;
    }

    private static string BuildAppReturnUrl(string result, string code)
    {
        var encodedResult = Uri.EscapeDataString(result);
        var encodedCode = Uri.EscapeDataString(code);
        return $"nsfintech://modals/add-account?bankingResult={encodedResult}&code={encodedCode}";
    }
}
