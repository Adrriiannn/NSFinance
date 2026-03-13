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
- `TRUELAYER_REDIRECT_URI=http://localhost:5080/api/banking/truelayer/callback`

## Recommended Production Values
- `TRUELAYER_ENVIRONMENT=live`
- `TRUELAYER_AUTH_BASE_URL=https://auth.truelayer.com`
- `TRUELAYER_API_BASE_URL=https://api.truelayer.com`
- `TRUELAYER_REDIRECT_URI=https://api.finance.nsireland.ie/api/banking/truelayer/callback`

## Notes
- Do not commit client secrets into source control.
- Redirect URI in environment config must match the URI registered in TrueLayer Console exactly.
- Use only these official callback targets:
  - local/development: `http://localhost:5080/api/banking/truelayer/callback`
  - production: `https://api.finance.nsireland.ie/api/banking/truelayer/callback`
- Auth/API base URLs must match selected environment; mismatches are rejected at runtime.
