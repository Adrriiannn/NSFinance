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
   - pending transactions (raw ingest only, no booked-ledger projection)
   - cards (+ card balances/transactions when available)
   - direct debits
   - standing orders
   - connection identity info

## Freshness and sync orchestration

NSFinance now uses a global sync orchestration model:

- `POST /api/banking/sync` is the shared sync endpoint for manual and auto triggers.
- Request body:
  - `trigger`: `manual` or `auto`
  - `source`: optional source tag (for diagnostics)
  - `force`: optional debug override for manual cooldown (still respects in-progress/provider safety)
- Response includes:
  - `outcome` (`completed`, `skipped_cooldown`, `skipped_not_due`, `skipped_provider_backoff`, `skipped_no_eligible_connections`, `failed_unexpected`)
  - per-connection sync results
  - cooldown + next-eligible metadata
  - provider backoff metadata
  - fetch-freshness metadata (`latestFetchedRowUtc`, `hasFetchedRowNewerThanCheckpoint`, `freshnessSummary`)
  - changed/no-change counts

Timing is config-driven under `Banking:Sync`:

- `ManualCooldownMinutes`
- `AutoSyncIntervalMinutes`
- `UnattendedSyncEnabled`
- `UnattendedSyncIntervalMinutes`
- `UnattendedSyncSweepMinutes`

The manual cooldown defaults to 10 minutes. Unattended discovery defaults to a
15-minute sweep and considers an eligible connection due every 12 hours. The
runtime clamps the unattended interval to at least six hours.

### Manual sync

- Manual sync is now available from global headers (Accounts + Activity), not the connect-bank screen.
- Backend enforces a 10-minute cooldown for user-triggered manual sync requests.
- Cooldown truth is API-side (not client-only throttling).
- Global/manual sync execution is detached from request-abort cancellation so long-running provider syncs are not terminated just because the mobile request times out or disconnects.
- Provider-aware backoff is also applied when a connection is rate-limited (for example `provider_too_many_requests` / `provider_request_limit_exceeded`), returning `skipped_provider_backoff` until the backoff window expires.

### Auto sync

- Mobile triggers auto-sync checks while the app is active in foreground.
- Auto sync is also evaluated immediately on:
  - app resume/foreground
  - post-login session entry
- Due/not-due decisions are made on backend connection freshness state, not guessed on-device.
- Auto sync skips when not due (calm behavior, no API hammering), and also skips provider-rate-limited connections while backoff is active.
- Connections in `sync_pending` are evaluated with stale-state recovery:
  - fresh `sync_pending` connections are skipped as `skipped_sync_in_progress`
  - stale `sync_pending` connections (older than recovery threshold) are reconciled back to a syncable state and retried in the same global run
  - logs include current status, last sync-attempt timestamp, stale/fresh decision, and whether stale recovery was applied

### Unattended scheduled sync

- The TrueLayer worker discovers stale connections from persisted database state; it does not depend on the mobile app being open.
- A connection is eligible only after initial backfill has completed, while it has an active non-revoked protected refresh token and a syncable `connected`, `synced`, or `failed` status.
- Discovery enqueues one coalesced `scheduled_sync` job per connection in `BankingOperationJobs`. The normal job lease and per-connection execution lease protect restart and multi-instance execution.
- Scheduled jobs are polled every 30 seconds. Discovery runs every 15 minutes by default, while the connection itself is due every 12 hours.
- Each linked account and card carries its own successful transaction-scan coverage checkpoint. An unattended run advances each source through one seven-day window with a one-day overlap, including when the provider returns zero rows; a long outage is therefore caught up in bounded chunks instead of silently skipping to the newest week.
- Adaptive historical splitting is disabled for unattended work so a capped provider such as AIB cannot turn one scheduled cycle into a burst of transaction requests. Newly discovered sources start from their provider-policy backfill horizon rather than inheriting another account's checkpoint.
- Provider failures are not rapidly retried. Only lease-contention outcomes, where no provider request was made, use the durable retry path; a terminal scheduled failure records the attempt time and waits for the next due interval.
- User-initiated manual/foreground sync keeps the wider provider-aware path. When request context exists, the TrueLayer client supplies the user IP header used to distinguish attended requests.
- Every actual attempt records `LastSyncAttemptedUtc` before configuration or token refresh work, while `LastSuccessfulSyncUtc` advances only after a completed sync.
- The API host must remain running for this hosted scheduler. The production App Service has Always On enabled, but this scheduler still requires exact-candidate deployment and controlled production evidence.

### Last synced semantics

- `LastSuccessfulSyncUtc` is the ledger-facing "last synced" source.
- Sync attempts that do not complete successfully do not advance this value.
- Sync responses also expose `dataChanged` to distinguish successful/no-change runs.

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

- Transaction persistence is now layered:
  - `RawBankTransactions` preserves provider-truth ingest details.
  - `NormalizedBankTransactions` stores normalized banking-layer records with provenance and policy metadata.
  - `Transactions` remains the user-visible ledger projection layer.
- Timestamp provenance is preserved per transaction:
  - raw provider timestamp string
  - value timestamp string
  - timestamp source field used
  - precision classification (`precise_datetime`, `date_only_midnight`, `unknown_needs_verification`)
  - policy key used for normalization
- Timestamp selection uses a best-available strategy:
  - evaluate known booked timestamp fields (for example `transaction_timestamp`, `booked_timestamp`, `timestamp`, `booking_date`)
  - prefer the most precise parseable value, not simply the first parseable field
  - preserve original source field + raw string for auditability
  - do not fabricate time for date-only provider payloads

- Initial sync uses an explicit backfill window (`from`/`to`) instead of relying on provider defaults.
- Backfill windows are provider-aware where we have known constraints/opportunities:
  - Revolut: target up to 6 years on initial sync
  - Ulster: target up to 6 years on initial sync
  - Bank of Ireland: target up to 1 year on initial sync
  - PTSB: target about 90 days on initial sync
  - AIB: target up to 1 year on initial sync
  - fallback for others: target up to 6 years on initial sync
- Ongoing syncs switch to incremental mode using the latest imported checkpoint with a guarded lookback window to catch late-posted items.
- For capped providers (for example AIB at up to 100 transactions), incremental sync no longer relies on one broad window request:
  - requests are split into smaller incremental sub-windows
  - if a window still appears capped, sync adaptively splits again until a safe minimum window size
  - results are merged idempotently before upsert so metadata/category continuity is preserved
- Pending endpoints are queried where available and ingested as raw pending activity; pending rows are intentionally not projected as booked ledger entries.
- Fetch-truth diagnostics now explicitly separate provider freshness from projection behavior:
  - fetched logs include `latestImportedCheckpointUtcBefore`, `hasFetchedRowNewerThanCheckpoint`, `latestReturnedLagHours`, and `staleReturnedSlice`
  - this makes it explicit whether missing activity was absent from provider payload vs fetched and filtered later in projection
- Projection diagnostics now include reconciliation cost signals:
  - `projectedDuplicateCheckAttempts`
  - `projectedBackfillRowsEvaluated`
  - `projectedBackfillRowsDeferred`
  - `projectedCandidatePoolSize`
- Legacy raw→projected backfill reconciliation is now bounded per sync run to avoid runaway duplicate-reconcile cost on high-volume accounts; deferred rows are reconciled progressively in later syncs.
- Transaction status normalization is endpoint-aware:
  - rows from `/transactions` (settled/booked endpoint) are normalized as booked for ledger projection, even if provider payload status strings are noisy
  - rows from `/transactions/pending` are normalized as pending and remain raw-only until a booked version arrives
  - each fetched row carries normalization metadata (`sourceEndpoint`, provider status, normalization reason) for diagnostics
- Raw-to-ledger projection now uses explicit linkage:
  - each `RawBankTransaction` can store `ProjectedTransactionId`
  - projection reconciliation first checks this durable link before any duplicate heuristics
  - legacy unlinked raw rows are reconciled once against existing ledger rows; duplicate matches log the exact collided `Transaction.Id`
  - this avoids repeated replay/backfill fingerprint collisions swallowing visibility of legitimate new rows
- Distinct same-time transactions are preserved:
  - dedupe identity no longer relies on `normalised_provider_transaction_id` alone
  - when normalized IDs are reused for related provider events (for example merchant + round-up/pocket movement), identity now includes extra signature components so both rows stay distinct
  - this prevents legitimate same-time rows being collapsed into one visible ledger entry
- Transfer linking now emits explicit confidence metadata on linked ledger rows:
  - `TransferMatchConfidenceScore`
  - `TransferMatchConfidenceTier`
  - `TransferMatchReason`
  - auto-linking only proceeds for high-confidence matches
- Connection metadata tracks:
  - initial backfill started/completed
  - requested initial backfill window start
  - earliest/latest imported transaction timestamps

## Provider-aware sync policy and durability

- Sync behavior is now selected through an explicit provider policy catalog (not ad hoc conditionals).
- Policies model provider transaction visibility mode, count limits, initial backfill shape, card backfill shape, incremental re-scan strategy, pending-support expectations, timestamp precision, and consent-window sensitivity hints.
- Provider profiles are grouped into provider families (for example Irish capped-slice, NatWest family, Lloyds family, fintech families) so new provider variants can be onboarded without scattering service-layer conditionals.
- The provider policy catalog is grounded in the TrueLayer Supported Providers table exported from Console (`Supported Providers.xlsx`) and kept explicit in code.
- AIB is modeled as a capped visible-slice provider (`up to 100`), so incremental sync re-scans overlapping visible slices safely rather than assuming full historical replay access.
- Consumer AIB is currently treated as `date_only_midnight` timestamp precision based on observed payload behavior and live diagnostics; if precise fields are observed in future payload captures, policy should be reclassified with evidence.
- Sync persistence is stage-based:
  - account/balance refresh stage is persisted durably
  - transaction/commitments stage is persisted separately
  - card stage is persisted separately
- If a later stage fails, successfully persisted earlier stages (for example balance snapshots) are retained and logs include the failed stage name.
- Known unsupported optional endpoints (for example pending for providers where pending is explicitly unsupported) are policy-skipped up front to reduce noisy failures and unnecessary calls.
- Initial sync, disconnect cleanup, and scheduled sync are persisted operation types. Jobs coalesce per connection/type, use renewable atomic claims, and remain recoverable after host restart.
- A separate renewable lease on each bank connection prevents manual, foreground-auto, initial, scheduled, and disconnect-adjacent sync work from overlapping across API instances.
- Audit event names preserve `initial_sync`, `scheduled_sync`, `auto_sync`, and `manual_sync` provenance.

Provider limits still apply. NSFinance requests the widest practical range, but provider/API caps may return less.

## Internal transfer semantics (linked accounts)

NSFinance distinguishes true economic activity from money moved between a user’s own linked accounts:

- `Transfer` remains a visible taxonomy domain/category that users can assign manually.
- Sync applies linked-account internal transfer matching using same-user, opposite-sign, same amount/currency, timing tolerance, and transfer-hint signals.
- Matching now uses provider-aware confidence weighting:
  - weaker provider timestamp precision (`date-only`/midnight rows) is treated as lower-confidence for auto-linking
  - savings-pocket descriptors (`round up`, `pocket`, `vault`, `cash fund`) are deferred unless strong counterparty confidence exists
  - counterparty account hints and strong name-token overlap increase confidence
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

## Transaction relationship layer (savings and pockets)

NSFinance now persists a first-class `TransactionRelationships` layer so inferred movement semantics are explicit and auditable instead of hidden in ad-hoc projection logic.

- Relationship records store:
  - relationship type/status/direction
  - source/target projected transaction ids
  - source/target raw-bank transaction ids (when available)
  - confidence score/tier and reason payload
  - analytics treatment and optional virtual destination labels
- Supported relationship types include:
  - `internal_account_transfer`
  - `savings_roundup`
  - `savings_manual_deposit`
  - `savings_manual_withdrawal`
  - `possible_*_suggestion` (medium/low-confidence suggestions)
- Savings movement detection is currently Revolut-first but provider-extensible:
  - pocket/vault/flexible-cash/spare-change descriptors are detected
  - round-up patterns are linked to nearby merchant outflows when confidence is high
  - manual moves to/from savings destinations are marked as savings flow
- Visibility remains non-destructive:
  - savings rows stay visible in Activity
  - high-confidence savings movements are globally neutralized for income/expense math
  - savings movement rows route to savings allocation reporting (`SavingsAllocation`) instead of spending/income

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

TrueLayer is live-only in the current NSFinance setup.

- `TrueLayer__Environment=live`
- `TrueLayer__AuthBaseUrl=https://auth.truelayer.com`
- `TrueLayer__ApiBaseUrl=https://api.truelayer.com`
- callback: `https://api.finance.nsireland.ie/api/banking/truelayer/callback`

## Provider market targeting

Auth-link generation is backend-driven and deterministic. The backend is the source of truth, not the TrueLayer Console auth-link builder UI.

Current targeting rules:

- `providers=ie-ob-all`
- `country_id=IE`

This ensures live bank chooser flows open with Ireland providers instead of UK defaults.

## Safety checks

- callback state validation
- live TrueLayer host validation
- secure callback HTML response with safe status messaging
- strict redirect URI validation (`/api/banking/truelayer/callback`; HTTPS + non-localhost required)
- structured lifecycle logging for link, callback, token exchange, queueing, and sync
- persistent ASP.NET DataProtection key-ring support via `DataProtection:KeysPath` / `NSFINANCE_DATA_PROTECTION_KEYS_PATH` (production path auto-detected if unset)

## Known limitations

- provider-side token revocation is limited in current implementation
- advanced enrichment/categorization pipeline is deferred
- the durable job ledger, connection execution lease, readiness aggregation, per-source transaction checkpoints, and unattended scheduler are verified locally but are not yet deployed or proven against a controlled production QA connection
- provider reachability, scheduled-sync lag, terminal-job alert routing, and operator recovery still need production observability and a runbook
- provider-side max history remains provider-dependent; requesting a wider window cannot exceed what the institution exposes through TrueLayer
- card transactions can only be projected into the shared activity ledger when they can be linked to a clear projected account (`provider_account_id` match or single-account connection fallback)
- pending transactions are currently a freshness layer in raw banking ingest and are not yet rendered as first-class pending rows in the main booked activity feed
