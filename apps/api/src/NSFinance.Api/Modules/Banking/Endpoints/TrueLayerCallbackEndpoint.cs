using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class TrueLayerCallbackEndpoint
{
    public static async Task<IResult> HandleAsync(
        string? code,
        string? state,
        string? error,
        string? error_description,
        TrueLayerAuthService authService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("TrueLayerCallbackEndpoint");
        var outcome = await authService.HandleCallbackAsync(
            new TrueLayerCallbackQuery(code, state, error, error_description),
            cancellationToken);

        logger.LogInformation(
            "Returning TrueLayer callback HTML outcome={OutcomeCode} succeeded={Succeeded} connectionId={ConnectionId}",
            outcome.Code,
            outcome.Succeeded,
            outcome.ConnectionId);

        var safeHtml = BuildSafeHtml(outcome);
        return Results.Content(safeHtml, "text/html", statusCode: outcome.HttpStatusCode);
    }

    private static string BuildSafeHtml(TrueLayerCallbackOutcome outcome)
    {
        const int autoReturnDelayMs = 3000;

        var title = outcome.Succeeded ? "Bank Connected" : "Bank Connection Failed";
        var message = WebUtility.HtmlEncode(outcome.Message);
        var statusCode = WebUtility.HtmlEncode(outcome.Code);
        var appResult = outcome.Succeeded ? "success" : "error";
        var appReturnUrl = BuildAppReturnUrl(outcome.AppReturnUri, appResult, outcome.Code, outcome.ConnectionId);
        var appReturnUrlForHref = WebUtility.HtmlEncode(appReturnUrl);
        var appReturnUrlForScript = appReturnUrl.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var headingColor = outcome.Succeeded ? "#7ef0b8" : "#ff9f8d";
        var buttonLabel = outcome.Succeeded ? "Return to NSFinance" : "Return to NSFinance and retry";
        var helperText = outcome.Succeeded
            ? "If NSFinance does not open automatically, use the button below and the app will continue from your saved connection state."
            : "If NSFinance does not open automatically, return to the app and retry the bank connection there.";
        var nextStepText = outcome.Succeeded
            ? "Return to the app. Your bank connection is saved and the first sync will continue in the background."
            : "Return to the app, start the bank connection again, and complete the consent flow without refreshing this page.";
        var autoReturnMessage = outcome.Succeeded
            ? "We will try to reopen the app in 3 seconds."
            : "We will keep this page open. When you are ready, use the button above to return to the app.";
        var autoReturnFlag = outcome.Succeeded ? "true" : "false";

        return $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="utf-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1" />
                  <title>{{title}}</title>
                </head>
                <body style="margin:0; font-family: Arial, sans-serif; background:#081423; color:#e9f1ff; min-height:100vh; display:flex; align-items:center; justify-content:center; padding:24px; box-sizing:border-box;">
                  <main style="width:min(100%, 520px); border:1px solid rgba(150,190,255,0.25); background:rgba(16,34,57,0.96); border-radius:20px; padding:28px; box-shadow:0 18px 48px rgba(0,0,0,0.28);">
                    <p style="margin:0 0 8px; color:{{headingColor}}; font-size:12px; letter-spacing:0.12em; text-transform:uppercase;">NSFinance bank connection</p>
                    <h1 style="margin:0 0 12px; font-size:28px; line-height:1.15;">{{title}}</h1>
                    <p style="margin:0 0 12px; font-size:16px; line-height:1.5;">{{message}}</p>
                    <p style="margin:0 0 20px; color:#b6c9e8; font-size:14px; line-height:1.5;">{{helperText}}</p>
                    <div style="margin:0 0 20px; padding:14px 16px; border-radius:14px; background:rgba(255,255,255,0.05); color:#b6c9e8; font-size:14px;">
                      <div style="font-weight:600; color:#e9f1ff; margin-bottom:6px;">What to do next</div>
                      <div>{{nextStepText}}</div>
                    </div>
                    <a id="return-link" href="{{appReturnUrlForHref}}" style="display:inline-block; margin-top:4px; padding:14px 18px; border-radius:14px; background:linear-gradient(180deg, #3e86ff 0%, #2b6cff 100%); color:#ffffff; text-decoration:none; font-weight:600;">
                      {{buttonLabel}}
                    </a>
                    <p style="margin:18px 0 0; opacity:0.75; font-size:13px;">Code: {{statusCode}}</p>
                    <p style="margin:10px 0 0; opacity:0.75; font-size:13px;" id="return-status">{{autoReturnMessage}}</p>
                  </main>
                  <script>
                    (function () {
                      var target = "{{appReturnUrlForScript}}";
                      var isMobile = /Android|iPhone|iPad|iPod/i.test(navigator.userAgent || "");
                      var shouldAutoReturn = {{autoReturnFlag}};
                      var returnStatus = document.getElementById("return-status");
                      var countdownSeconds = 3;
                      if (!isMobile) {
                        return;
                      }

                      if (!shouldAutoReturn) {
                        return;
                      }

                      var countdownTimer = setInterval(function () {
                        countdownSeconds -= 1;
                        if (!returnStatus) {
                          return;
                        }

                        if (countdownSeconds <= 0) {
                          clearInterval(countdownTimer);
                          returnStatus.textContent = "Reopening NSFinance now...";
                          return;
                        }

                        returnStatus.textContent = "We will try to reopen the app in " + countdownSeconds + " seconds.";
                      }, 1000);

                      setTimeout(function () {
                        window.location.href = target;
                      }, {{autoReturnDelayMs}});
                    })();
                  </script>
                </body>
                </html>
                """;
    }

    private static string BuildAppReturnUrl(string? appReturnUri, string result, string code, Guid? connectionId)
    {
        var baseReturnUri = string.IsNullOrWhiteSpace(appReturnUri)
            ? "nsfinance://modals/add-account"
            : appReturnUri;

        var parameters = new Dictionary<string, string?>
        {
            ["bankingResult"] = result,
            ["code"] = code
        };

        if (connectionId.HasValue)
        {
            parameters["connectionId"] = connectionId.Value.ToString();
        }

        return QueryHelpers.AddQueryString(baseReturnUri, parameters);
    }
}
