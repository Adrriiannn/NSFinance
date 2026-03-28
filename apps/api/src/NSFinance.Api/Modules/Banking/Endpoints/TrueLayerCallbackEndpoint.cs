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
        var appResult = outcome.Succeeded ? "success" : "error";
        var appReturnUrl = BuildAppReturnUrl(outcome.AppReturnUri, appResult, outcome.Code, outcome.ConnectionId);

        logger.LogInformation(
            "Returning TrueLayer callback HTML outcome={OutcomeCode} succeeded={Succeeded} connectionId={ConnectionId} chosenReturnUri={ChosenReturnUri}",
            outcome.Code,
            outcome.Succeeded,
            outcome.ConnectionId,
            appReturnUrl);

        var safeHtml = BuildSafeHtml(outcome, appReturnUrl);
        return Results.Content(safeHtml, "text/html", statusCode: outcome.HttpStatusCode);
    }

    private static string BuildSafeHtml(TrueLayerCallbackOutcome outcome, string appReturnUrl)
    {
        const int autoReturnDelayMs = 3000;

        var title = outcome.Succeeded ? "Bank Connected" : "Bank Connection Failed";
        var message = WebUtility.HtmlEncode(outcome.Message);
        var statusCode = WebUtility.HtmlEncode(outcome.Code);
        var appReturnUrlForHref = WebUtility.HtmlEncode(appReturnUrl);
        var appReturnUrlForScript = appReturnUrl.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var headingColor = outcome.Succeeded ? "#1DBA72" : "#E25A5A";
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
                  <meta name="theme-color" content="#050505" />
                  <style>
                    :root {
                      color-scheme: dark;
                    }

                    * {
                      box-sizing: border-box;
                    }

                    body {
                      margin: 0;
                      min-height: 100vh;
                      display: flex;
                      align-items: center;
                      justify-content: center;
                      padding: 24px;
                      background: radial-gradient(circle at 14% 8%, rgba(242, 140, 40, 0.08), rgba(5, 5, 5, 0) 44%), #050505;
                      color: #F2F2F2;
                      font-family: Inter, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
                    }

                    .panel {
                      width: min(100%, 540px);
                      border-radius: 6px;
                      border: 1px solid rgba(242, 140, 40, 0.32);
                      background: #111111;
                      padding: 26px 24px;
                    }

                    .eyebrow {
                      margin: 0 0 10px;
                      color: #D8D8D8;
                      font-size: 12px;
                      letter-spacing: 0.11em;
                      text-transform: uppercase;
                    }

                    h1 {
                      margin: 0 0 12px;
                      color: #F2F2F2;
                      font-size: 38px;
                      line-height: 1.12;
                      font-weight: 600;
                    }

                    .lead {
                      margin: 0 0 12px;
                      color: #F2F2F2;
                      font-size: 17px;
                      line-height: 1.5;
                    }

                    .helper {
                      margin: 0 0 20px;
                      color: #B5B5B5;
                      font-size: 15px;
                      line-height: 1.55;
                    }

                    .status {
                      color: {{headingColor}};
                    }

                    .next-step {
                      margin: 0 0 20px;
                      border-radius: 6px;
                      border: 1px solid rgba(242, 140, 40, 0.18);
                      background: #151515;
                      padding: 14px 14px 12px;
                    }

                    .next-step-title {
                      margin: 0 0 6px;
                      color: #F2F2F2;
                      font-size: 15px;
                      line-height: 1.3;
                      font-weight: 500;
                    }

                    .next-step-copy {
                      margin: 0;
                      color: #B5B5B5;
                      font-size: 15px;
                      line-height: 1.5;
                    }

                    .return-link {
                      display: inline-flex;
                      align-items: center;
                      justify-content: center;
                      margin-top: 2px;
                      min-height: 44px;
                      border-radius: 6px;
                      border: 1px solid #F28C28;
                      background: #F28C28;
                      padding: 0 18px;
                      color: #050505;
                      text-decoration: none;
                      font-size: 18px;
                      line-height: 1.2;
                      font-weight: 600;
                    }

                    .return-link:active {
                      opacity: 0.92;
                    }

                    .meta {
                      margin: 16px 0 0;
                      color: #7C7C7C;
                      font-size: 13px;
                      line-height: 1.4;
                    }

                    .meta + .meta {
                      margin-top: 8px;
                    }
                  </style>
                </head>
                <body>
                  <main class="panel">
                    <p class="eyebrow">NSFinance bank connection</p>
                    <h1 class="status">{{title}}</h1>
                    <p class="lead">{{message}}</p>
                    <p class="helper">{{helperText}}</p>
                    <div class="next-step">
                      <p class="next-step-title">What to do next</p>
                      <p class="next-step-copy">{{nextStepText}}</p>
                    </div>
                    <a id="return-link" class="return-link" href="{{appReturnUrlForHref}}">
                      {{buttonLabel}}
                    </a>
                    <p class="meta">Code: {{statusCode}}</p>
                    <p class="meta" id="return-status">{{autoReturnMessage}}</p>
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
        var baseReturnUri = TrueLayerReturnUriContract.Normalize(appReturnUri)
            ?? TrueLayerReturnUriContract.BuildDefaultAppReturnUri();

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
