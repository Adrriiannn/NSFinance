# Phase 2 Environment and Config

## Required TrueLayer Environment Variables
- `TRUELAYER_CLIENT_ID`
- `TRUELAYER_CLIENT_SECRET`
- `TRUELAYER_REDIRECT_URI`
- `TRUELAYER_ENVIRONMENT` (`sandbox` or `live`)
- `TRUELAYER_AUTH_BASE_URL`
- `TRUELAYER_API_BASE_URL`

## Recommended Sandbox Values
- `TRUELAYER_ENVIRONMENT=sandbox`
- `TRUELAYER_AUTH_BASE_URL=https://auth.truelayer-sandbox.com`
- `TRUELAYER_API_BASE_URL=https://api.truelayer-sandbox.com`
- `TRUELAYER_REDIRECT_URI=https://<your-api-host>/api/banking/truelayer/callback`

## Notes
- Do not commit client secrets into source control.
- Redirect URI in environment config must match the URI registered in TrueLayer Console exactly.
- Auth/API base URLs must match selected environment; mismatches are rejected at runtime.
