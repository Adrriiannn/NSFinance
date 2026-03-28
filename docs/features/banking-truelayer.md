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

## Disconnect lifecycle

Disconnect now follows an async-first lifecycle for reliability at higher data volume:

1. Mobile calls `POST /api/banking/connections/{connectionId}/disconnect`.
2. API persists `disconnect_pending` quickly and revokes local token material.
3. API enqueues background cleanup and returns immediately.
4. Background worker performs set-based cleanup (linked accounts + projected financial data).
5. Final status is written as:
   - `revoked` when cleanup succeeds
   - `disconnect_failed` when cleanup fails

Sync guards:

- sync is skipped for `disconnect_pending`, `disconnect_failed`, and `revoked` connections
- sync state updates do not overwrite active disconnect lifecycle states

## Callback return contract (mobile)

Preferred app return URI path:

- `nsfinance://accounts/connect-bank?intent=new`

Legacy compatibility path (still accepted):

- `nsfinance://modals/add-account?intent=new`

The callback endpoint normalizes and accepts both current and legacy route shapes, but always prefers the current route for default fallback behavior.

## Environment model

Development defaults (from `appsettings.Development.json`):

- `TrueLayer:Environment=sandbox`
- sandbox auth/API base URLs
- local callback URI

Production (Azure settings):

- `TrueLayer__Environment=live`
- live auth/API base URLs
- callback: `https://api.finance.nsireland.ie/api/banking/truelayer/callback`

## Provider market targeting

Auth-link generation is backend-driven and deterministic. The backend is the source of truth, not the TrueLayer Console auth-link builder UI.

Current targeting rules:

- sandbox:
  - `providers=uk-cs-mock`
  - no `country_id`
- live:
  - `providers=ie-ob-all`
  - `country_id=IE`

This ensures live bank chooser flows open with Ireland providers instead of UK defaults.

## Safety checks

- callback state validation
- environment mismatch protection (sandbox vs live URL mismatches rejected)
- secure callback HTML response with safe status messaging
- strict redirect URI validation (`/api/banking/truelayer/callback`; HTTPS + non-localhost required for live)
- structured lifecycle logging for link, callback, token exchange, queueing, and sync
- persistent ASP.NET DataProtection key-ring support via `DataProtection:KeysPath` / `NSFINANCE_DATA_PROTECTION_KEYS_PATH` (production path auto-detected if unset)

## Known limitations

- provider-side token revocation is limited in current implementation
- advanced enrichment/categorization pipeline is deferred
- initial sync queue is still in-memory (`Channel`) and not durable across host crashes/redeploys; automatic queue failures now mark the connection status truthfully and require manual sync/reconnect follow-up
- disconnect cleanup queue is also in-memory (`Channel`) and not fully durable; startup requeue recovers `disconnect_pending` connections, and manual retry is idempotent if a pending cleanup did not finish
