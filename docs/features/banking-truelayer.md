# Open Banking: TrueLayer

## Scope

Implemented scope:

- info
- accounts
- cards
- balance
- transactions
- offline_access (refresh continuity)
- direct_debits
- standing_orders

Not requested in current phase:

- payments

## Flow summary

1. Mobile calls `POST /api/banking/truelayer/link`.
2. API creates connection state and returns hosted auth URL.
3. Provider redirects to `/api/banking/truelayer/callback`.
4. API validates callback state ownership.
5. API exchanges auth code for tokens server-side.
6. API stores encrypted refresh token.
7. API performs initial sync and persists:
   - accounts
   - balances
   - transactions
   - cards (+ card balances/transactions when available)
   - direct debits
   - standing orders
   - connection identity info

## Product data model mapping

NSFinance now maps the expanded TrueLayer model into explicit product buckets:

- Account core:
  - linked account identity, display name, type/subtype, currency, account-number metadata
- Balance and activity:
  - account balances + transactions
  - card transactions are projected into the same activity ledger when they can be linked to a single financial account
- Card layer:
  - linked cards stored separately from bank accounts
  - card balances/transactions tracked separately
- Recurring commitments:
  - direct debits
  - standing orders
- Identity/comfort:
  - `/info` mapped into connection identity metadata
  - used for "Connected as ..." style trust/personalization surfaces

Important mapping rule:

- account and card labels come from account/card display metadata (`display_name` + safe fallbacks)
- `/info.full_name` is never used as the account title
- transaction titles use a merchant-first display strategy (merchant/display fields first, raw `description` fallback)

## Capability-aware sync behavior

Provider support varies by scope and endpoint. Sync now treats dataset availability explicitly:

- unsupported optional datasets (cards/direct debits/standing orders/info) are marked as unavailable without breaking the whole sync
- accounts/balances/transactions remain the primary ledger path
- connection metadata stores support flags:
  - `SupportsInfo`
  - `SupportsCards`
  - `SupportsDirectDebits`
  - `SupportsStandingOrders`

## Transaction history model

NSFinance now treats imported banking data as a long-term ledger:

- Initial sync uses an explicit backfill window (`from`/`to`) instead of relying on provider defaults.
- Backfill windows are provider-aware where we have known constraints/opportunities:
  - Revolut: target up to 6 years on initial sync
  - Ulster: target up to 6 years on initial sync
  - Bank of Ireland: target up to 1 year on initial sync
  - PTSB: target about 90 days on initial sync
  - AIB: target up to 1 year on initial sync
  - fallback for others: target up to 6 years on initial sync
- Ongoing syncs switch to incremental mode using the latest imported checkpoint with a guarded lookback window to catch late-posted items.
- Connection metadata tracks:
  - initial backfill started/completed
  - requested initial backfill window start
  - earliest/latest imported transaction timestamps

Provider limits still apply. NSFinance requests the widest practical range, but provider/API caps may return less.

## Internal transfer semantics (linked accounts)

NSFinance distinguishes true economic activity from money moved between a user’s own linked accounts:

- `Transfer` remains a visible taxonomy domain/category that users can assign manually.
- Sync applies linked-account internal transfer matching using same-user, opposite-sign, same amount/currency, timing tolerance, and transfer-hint signals.
- Matched pairs persist explicit linkage (`LinkedTransferTransactionId`) and transfer semantics (`TransferKind = linked_internal_transfer`) on both sides.
- Editing transfer taxonomy on one side of a verified linked pair propagates transfer taxonomy to the counterpart so both sides stay transfer-coherent.
- Manual transfer categorization sets `TransferKind = manual_transfer` without requiring a matched counterpart.
- A transfer policy engine applies category/subcategory-specific rules; global neutrality is not blanket.
- Global neutrality is applied only when policy allows it:
  - verified linked matches for eligible internal transfer classes
  - explicit manual overrides for selected subtypes (with warning in transaction details)
- Cash movement and liability/debt transfer subtypes remain conservative by default.
- Per-account ledgers still show the actual debit/credit movement for account-level truth.
- Transaction Details renders a linked transaction preview section only when a linked counterpart exists, and tapping it navigates to the counterpart details page.

## Consent expiry and reconfirmation continuity

- Consent expiry (`reauth_required` / `expired`) is treated as an access problem, not a history deletion event.
- Existing imported rows remain in NSFinance when consent expires.
- Reconfirmation can target an existing connection by passing `connectionId` in `POST /api/banking/truelayer/link`.
- Reconfirming on the same connection preserves account/transaction continuity and avoids creating a brand-new local ledger for the same bank link.

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
- provider-side max history remains provider-dependent; requesting a wider window cannot exceed what the institution exposes through TrueLayer
- card transactions can only be projected into the shared activity ledger when they can be linked to a clear projected account (`provider_account_id` match or single-account connection fallback)
