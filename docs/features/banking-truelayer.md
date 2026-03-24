# Open Banking: TrueLayer

## Scope

Implemented scope:

- accounts
- balances
- transactions
- offline access (refresh)

Excluded in current phase:

- payments
- direct debits
- standing orders
- identity enrichment

## Flow summary

1. Mobile calls `POST /api/banking/truelayer/link`.
2. API creates connection state and returns hosted auth URL.
3. Provider redirects to `/api/banking/truelayer/callback`.
4. API validates callback state ownership.
5. API exchanges auth code for tokens server-side.
6. API stores encrypted refresh token.
7. API performs initial sync and persists accounts/balances/transactions.

## Environment model

Development defaults (from `appsettings.Development.json`):

- `TrueLayer:Environment=sandbox`
- sandbox auth/API base URLs
- local callback URI

Production (Azure settings):

- `TrueLayer__Environment=live`
- live auth/API base URLs
- callback: `https://api.finance.nsireland.ie/api/banking/truelayer/callback`

## Safety checks

- callback state validation
- environment mismatch protection (sandbox vs live URL mismatches rejected)
- secure callback HTML response with safe status messaging

## Known limitations

- callback return UX is currently safe HTML + app return handling, not a fully polished deep-link completion journey
- provider-side token revocation is limited in current implementation
- advanced enrichment/categorization pipeline is deferred
