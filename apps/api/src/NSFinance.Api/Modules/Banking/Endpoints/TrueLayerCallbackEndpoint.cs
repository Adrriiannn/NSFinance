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
        var appReturnUrl = BuildAppReturnUrl(
            outcome.AppReturnUri,
            appResult,
            outcome.Code,
            outcome.ConnectionId,
            outcome.AttemptId);

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
        const int autoReturnDelayMs = 650;
        const int autoCloseDelayMs = 2600;
        const int pollIntervalMs = 1250;

        var statusCode = WebUtility.HtmlEncode(outcome.Code);
        var lifecycleStage = WebUtility.HtmlEncode(outcome.CallbackLifecycleStage ?? string.Empty);
        var lifecycleReason = WebUtility.HtmlEncode(outcome.CallbackLifecycleReason ?? string.Empty);
        var attemptStatus = WebUtility.HtmlEncode(outcome.AttemptStatus ?? string.Empty);
        var appReturnUrlForHref = WebUtility.HtmlEncode(appReturnUrl);
        var appReturnUrlForScript = appReturnUrl.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var headingColor = outcome.Succeeded ? "#1DBA72" : "#F28C28";
        var buttonLabel = outcome.Succeeded ? "Return to NSFinance" : "Open NSFinance";

        var title = outcome.Code switch
        {
            "callback_attempt_completed" => "This connection is already complete",
            "callback_attempt_superseded" => "This attempt was replaced",
            "callback_attempt_expired" => "This attempt expired",
            "callback_attempt_failed" => "Connection needs attention",
            _ when outcome.ShouldAutoReturn => "Returning to NSFinance...",
            _ when outcome.Succeeded => "Your bank is connected",
            _ => "Connection needs attention"
        };

        var helperText = outcome.Code switch
        {
            "callback_attempt_completed" => "NSFinance has already completed this connection attempt.",
            "callback_attempt_superseded" => "A newer connection attempt is now active.",
            "callback_attempt_expired" => "Open NSFinance to start a fresh connection attempt.",
            "callback_attempt_failed" => "Open NSFinance to review the next step.",
            "callback_state_invalid" => "This callback does not map to a valid active attempt.",
            _ => "This browser tab is a helper surface. NSFinance remains the source of truth."
        };

        var nextStepText = outcome.SafeToClose
            ? "You can close this tab now."
            : outcome.ShouldAutoReturn
                ? "We are handing this back to NSFinance."
                : "Open NSFinance when you are ready to continue.";
        var autoReturnMessage = outcome.ShouldAutoReturn
            ? "Returning to NSFinance..."
            : "Open NSFinance to continue.";
        var closeHintMessage = outcome.SafeToClose
            ? "Safe to close."
            : "Please wait while NSFinance resumes.";

        var autoReturnFlag = outcome.ShouldAutoReturn ? "true" : "false";
        var safeToCloseFlag = outcome.SafeToClose ? "true" : "false";
        var attemptIdValue = outcome.AttemptId?.ToString() ?? string.Empty;
        var attemptTokenValue = outcome.AttemptPublicToken ?? string.Empty;
        var hasAttemptPolling = !string.IsNullOrWhiteSpace(attemptIdValue) && !string.IsNullOrWhiteSpace(attemptTokenValue);
        var pollEndpointPath = hasAttemptPolling
            ? $"/api/banking/truelayer/attempts/{attemptIdValue}/status?token={WebUtility.UrlEncode(attemptTokenValue)}"
            : string.Empty;
        var pollEndpointForScript = pollEndpointPath.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var hasAttemptPollingFlag = hasAttemptPolling ? "true" : "false";

        return $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="utf-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1" />
                  <title>{{title}}</title>
                  <meta name="theme-color" content="#050505" />
                  <style>
                    :root { color-scheme: dark; }
                    * { box-sizing: border-box; }
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
                      color: {{headingColor}};
                      font-size: 36px;
                      line-height: 1.12;
                      font-weight: 600;
                    }
                    .lead { margin: 0 0 12px; color: #F2F2F2; font-size: 17px; line-height: 1.5; }
                    .helper { margin: 0 0 20px; color: #B5B5B5; font-size: 15px; line-height: 1.55; }
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
                    .next-step-copy { margin: 0; color: #B5B5B5; font-size: 15px; line-height: 1.5; }
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
                    .meta { margin: 16px 0 0; color: #7C7C7C; font-size: 13px; line-height: 1.4; }
                    .meta + .meta { margin-top: 8px; }
                    .close-hint { margin: 8px 0 0; color: #B5B5B5; font-size: 13px; line-height: 1.4; }
                  </style>
                </head>
                <body>
                  <main class="panel">
                    <p class="eyebrow">NSFinance bank connection</p>
                    <h1>{{title}}</h1>
                    <p class="lead">{{WebUtility.HtmlEncode(outcome.Message)}}</p>
                    <p class="helper">{{helperText}}</p>
                    <div class="next-step">
                      <p class="next-step-title">What to do next</p>
                      <p class="next-step-copy" id="next-step">{{nextStepText}}</p>
                    </div>
                    <a id="return-link" class="return-link" href="{{appReturnUrlForHref}}">{{buttonLabel}}</a>
                    <p class="meta">Code: {{statusCode}}</p>
                    <p class="meta" id="return-status">{{autoReturnMessage}}</p>
                    <p class="close-hint" id="close-hint">{{closeHintMessage}}</p>
                    <p class="meta">Attempt status: {{attemptStatus}}</p>
                    <p class="meta">Lifecycle: {{lifecycleStage}}</p>
                    <p class="meta">Reason: {{lifecycleReason}}</p>
                  </main>
                  <script>
                    (function () {
                      var target = "{{appReturnUrlForScript}}";
                      var pollEndpoint = "{{pollEndpointForScript}}";
                      var shouldPollAttempt = {{hasAttemptPollingFlag}};
                      var shouldAutoReturn = {{autoReturnFlag}};
                      var safeToClose = {{safeToCloseFlag}};
                      var isMobile = /Android|iPhone|iPad|iPod/i.test(navigator.userAgent || "");
                      var returnStatus = document.getElementById("return-status");
                      var closeHint = document.getElementById("close-hint");
                      var nextStep = document.getElementById("next-step");
                      var pollTimer = null;
                      var autoCloseScheduled = false;

                      var maybeCloseTab = function () {
                        if (autoCloseScheduled || !safeToClose) {
                          return;
                        }
                        autoCloseScheduled = true;
                        setTimeout(function () {
                          var closed = false;
                          try {
                            window.close();
                            closed = window.closed;
                          } catch (_) {
                            closed = false;
                          }
                          if (closeHint) {
                            closeHint.textContent = closed ? "This tab can be closed." : "You can close this tab now.";
                          }
                        }, {{autoCloseDelayMs}});
                      };

                      var applyAttemptStatus = function (status) {
                        if (!status || typeof status !== "object") {
                          return;
                        }

                        if (typeof status.message === "string" && nextStep) {
                          nextStep.textContent = status.message;
                        }

                        if (typeof status.safeToClose === "boolean") {
                          safeToClose = status.safeToClose;
                          if (closeHint) {
                            closeHint.textContent = safeToClose
                              ? "You can close this tab now."
                              : "NSFinance is still finishing setup.";
                          }
                        }

                        if (typeof status.shouldAutoReturn === "boolean" && status.shouldAutoReturn && isMobile) {
                          if (returnStatus) {
                            returnStatus.textContent = "Returning to NSFinance...";
                          }
                          window.location.href = target;
                        }

                        if (safeToClose) {
                          maybeCloseTab();
                        }
                      };

                      if (isMobile && shouldAutoReturn) {
                        if (returnStatus) {
                          returnStatus.textContent = "Returning to NSFinance...";
                        }
                        window.location.href = target;
                        setTimeout(function () {
                          if (document.visibilityState !== "hidden") {
                            window.location.href = target;
                          }
                        }, {{autoReturnDelayMs}});
                      }

                      if (shouldPollAttempt && pollEndpoint) {
                        var pollCount = 0;
                        var maxPolls = 24;
                        var poll = function () {
                          pollCount += 1;
                          fetch(pollEndpoint, { method: "GET", cache: "no-store" })
                            .then(function (response) {
                              if (!response.ok) {
                                return null;
                              }
                              return response.json();
                            })
                            .then(function (payload) {
                              if (!payload) {
                                return;
                              }
                              applyAttemptStatus(payload);
                            })
                            .catch(function () {
                              // Best-effort polling; the page still offers manual return.
                            })
                            .finally(function () {
                              if (pollCount >= maxPolls || safeToClose) {
                                maybeCloseTab();
                                return;
                              }

                              pollTimer = setTimeout(poll, {{pollIntervalMs}});
                            });
                        };

                        pollTimer = setTimeout(poll, 500);
                      } else {
                        maybeCloseTab();
                      }
                    })();
                  </script>
                </body>
                </html>
                """;
    }

    private static string BuildAppReturnUrl(
        string? appReturnUri,
        string result,
        string code,
        Guid? connectionId,
        Guid? attemptId)
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

        if (attemptId.HasValue)
        {
            parameters["attemptId"] = attemptId.Value.ToString();
        }

        return QueryHelpers.AddQueryString(baseReturnUri, parameters);
    }
}
