using System.Text;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class TurnstileRegisterPageEndpoint
{
    public static IResult HandleAsync()
    {
        return Results.Content(Html, "text/html; charset=utf-8", Encoding.UTF8);
    }

    private const string Html = """
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NSFinance Security Verification</title>
    <script src="https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit" async defer></script>
    <style>
      :root {
        color-scheme: dark;
      }

      html, body {
        margin: 0;
        padding: 0;
        width: 100%;
        height: 100%;
        background: #0b1a2d;
        color: #f2f6fd;
        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
      }

      .wrap {
        min-height: 100%;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 20px;
        box-sizing: border-box;
      }

      .panel {
        width: 100%;
        max-width: 420px;
        border: 1px solid rgba(149, 168, 194, 0.4);
        border-radius: 14px;
        background: rgba(17, 36, 58, 0.9);
        padding: 16px;
        box-sizing: border-box;
        display: flex;
        flex-direction: column;
        gap: 12px;
      }

      .title {
        margin: 0;
        font-size: 17px;
        font-weight: 700;
      }

      .meta {
        margin: 0;
        color: rgba(226, 236, 249, 0.82);
        font-size: 13px;
        line-height: 1.4;
      }

      #turnstile-root {
        min-height: 70px;
        display: flex;
        align-items: center;
        justify-content: center;
      }

      .error {
        display: none;
        color: #ff9f9f;
        font-size: 12px;
      }
    </style>
  </head>
  <body>
    <div class="wrap">
      <div class="panel">
        <h1 class="title">Security verification</h1>
        <p class="meta">Complete the Turnstile challenge to continue registration.</p>
        <div id="turnstile-root"></div>
        <div id="error" class="error"></div>
      </div>
    </div>

    <script>
      (function () {
        var params = new URLSearchParams(window.location.search);
        var siteKey = (params.get('siteKey') || '').trim();
        var action = (params.get('action') || 'register').trim();
        var theme = (params.get('theme') || 'dark').trim();

        function postMessageToHost(payload) {
          var serialized = JSON.stringify(payload);

          if (window.ReactNativeWebView && typeof window.ReactNativeWebView.postMessage === 'function') {
            window.ReactNativeWebView.postMessage(serialized);
          }

          if (window.parent && window.parent !== window && typeof window.parent.postMessage === 'function') {
            window.parent.postMessage(serialized, '*');
          }
        }

        function setError(message) {
          var errorElement = document.getElementById('error');
          if (!errorElement) {
            return;
          }

          errorElement.textContent = message;
          errorElement.style.display = 'block';
        }

        if (!siteKey) {
          setError('Turnstile site key is missing.');
          postMessageToHost({
            type: 'turnstile.error',
            code: 'site_key_missing',
            message: 'Turnstile site key is missing in query string.'
          });
          return;
        }

        function renderWidget() {
          if (!window.turnstile || typeof window.turnstile.render !== 'function') {
            setTimeout(renderWidget, 60);
            return;
          }

          postMessageToHost({ type: 'turnstile.ready' });

          try {
            window.turnstile.render('#turnstile-root', {
              sitekey: siteKey,
              action: action || 'register',
              theme: theme === 'light' ? 'light' : 'dark',
              callback: function (token) {
                postMessageToHost({ type: 'turnstile.success', token: token });
              },
              'expired-callback': function () {
                postMessageToHost({ type: 'turnstile.expired' });
              },
              'timeout-callback': function () {
                postMessageToHost({ type: 'turnstile.expired' });
              },
              'error-callback': function (code) {
                postMessageToHost({
                  type: 'turnstile.error',
                  code: code || 'turnstile_error',
                  message: 'Turnstile challenge failed.'
                });
              }
            });
          } catch (error) {
            var message = error && error.message ? error.message : 'Failed to render Turnstile.';
            setError(message);
            postMessageToHost({
              type: 'turnstile.error',
              code: 'render_exception',
              message: message
            });
          }
        }

        renderWidget();
      })();
    </script>
  </body>
</html>
""";
}
