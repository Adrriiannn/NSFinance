using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Auth.Configuration;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class TurnstileRegisterPageEndpoint
{
    public static IResult HandleAsync(IOptions<TurnstileOptions> turnstileOptions)
    {
        var options = turnstileOptions.Value;
        var siteKey = options.SiteKey?.Trim() ?? string.Empty;
        var secretKey = options.SecretKey?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(siteKey))
        {
            throw new InvalidOperationException("Turnstile SiteKey is missing from configuration.");
        }

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("Turnstile SecretKey is missing from configuration.");
        }

        var html = BuildHtml(siteKey);
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8);
    }

    private static string BuildHtml(string siteKey)
    {
        var encodedSiteKey = WebUtility.HtmlEncode(siteKey);

        return $$"""
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NSFinance Turnstile</title>
    <script src="https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit" async defer></script>
    <style>
      html, body {
        margin: 0;
        padding: 0;
        width: 300px;
        height: 65px;
        background: #2f3136;
        overflow: hidden;
      }

      #turnstile-root {
        width: 300px;
        height: 65px;
        background: #2f3136;
        overflow: hidden;
        line-height: 0;
        font-size: 0;
      }

      #turnstile-root iframe {
        display: block !important;
        width: 300px !important;
        height: 65px !important;
        margin: 0 !important;
        padding: 0 !important;
        border: 0 !important;
        background: #2f3136 !important;
      }
    </style>
  </head>
  <body>
    <div id="turnstile-root" class="cf-turnstile" data-sitekey="{{encodedSiteKey}}"></div>

    <script>
      (function () {
        var params = new URLSearchParams(window.location.search);
        var action = (params.get('action') || 'register').trim();
        var theme = (params.get('theme') || 'dark').trim();
        var rootElement = document.getElementById('turnstile-root');
        var siteKey = (rootElement && rootElement.getAttribute('data-sitekey') || '').trim();

        function postMessageToHost(payload) {
          var serialized = JSON.stringify(payload);

          if (window.ReactNativeWebView && typeof window.ReactNativeWebView.postMessage === 'function') {
            window.ReactNativeWebView.postMessage(serialized);
          }

          if (window.parent && window.parent !== window && typeof window.parent.postMessage === 'function') {
            window.parent.postMessage(serialized, '*');
          }
        }

        if (!siteKey) {
          postMessageToHost({
            type: 'turnstile.error',
            code: 'site_key_missing',
            message: 'Turnstile site key is missing in server configuration.'
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
              size: 'normal',
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
}
