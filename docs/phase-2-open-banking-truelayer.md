# Phase 2: TrueLayer Open Banking (Sandbox First)

## Summary
- Added backend-owned TrueLayer Data API integration flow:
  - auth link generation on backend
  - provider callback handling on backend
  - authorization-code exchange on backend
  - encrypted refresh-token persistence
  - initial/manual sync for accounts, balances, and transactions
- Added provider-aware banking persistence for:
  - open banking connections
  - connection tokens
  - linked bank accounts
  - balance snapshots
  - raw bank transactions
- Added secure user-owned banking endpoints under `/api/banking/*`.
- Updated mobile connect-bank modal to call backend link endpoint, open browser auth, refresh status, and trigger sync.
- Payments and non-scope data domains (identity, direct debits, standing orders) are not included in this phase.

## TrueLayer Callback Flow
1. Mobile calls `POST /api/banking/truelayer/link` (authenticated).
2. Backend creates `connection_started` record with a short-lived state nonce.
3. Backend returns auth URL built with configured redirect URI and scopes.
4. User consents in TrueLayer hosted flow.
5. TrueLayer redirects to `GET /api/banking/truelayer/callback` with `code` and `state`.
6. Backend validates callback query and state nonce ownership.
7. Backend exchanges `code` via `grant_type=authorization_code`.
8. Backend stores encrypted refresh token (never returned to client).
9. Backend fetches accounts, balances, transactions from Data API.
10. Backend persists raw snapshots and updates connection status (`synced` on success).

## Sandbox Setup Notes
- `TRUELAYER_ENVIRONMENT` defaults to `sandbox`.
- Sandbox defaults:
  - `TRUELAYER_AUTH_BASE_URL=https://auth.truelayer-sandbox.com`
  - `TRUELAYER_API_BASE_URL=https://api.truelayer-sandbox.com`
  - `TRUELAYER_REDIRECT_URI=http://localhost:5080/api/banking/truelayer/callback`
- Production callback:
  - `TRUELAYER_REDIRECT_URI=https://api.finance.nsireland.ie/api/banking/truelayer/callback`
- Environment mismatch protection is enforced:
  - sandbox environment + live URLs is rejected
  - live environment + sandbox URLs is rejected
- Live support is scaffolded but only active when `TRUELAYER_ENVIRONMENT=live` and matching live base URLs are configured.
- The URI registered in TrueLayer Console must exactly match the callback URI configured for the current environment.

## Scope for Phase 2
- Implemented scopes:
  - `accounts`
  - `balance`
  - `transactions`
  - `offline_access`
- Excluded from phase:
  - identity
  - direct debits
  - standing orders
  - payments

## Known Limitations
- Callback currently returns a safe HTML result page; deep-link return UX is not yet polished.
- Raw transactions are ingested and stored; advanced categorization/BI enrichment is deferred.
- Token revocation against provider API is not executed in this phase; local revocation/cleanup is enforced.
- Card data endpoints are not implemented yet; service design keeps extension points for later.
