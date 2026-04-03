# Manual QA

## Auth and trust baseline

1. Register with valid password.
2. Login and confirm authenticated shell loads.
3. Login with wrong password and confirm generic invalid-credentials behavior.
4. Register duplicate email and confirm conflict-safe failure.
5. Forgot/reset password end-to-end:
   - old password fails, new password succeeds
   - reset token reuse fails
6. Accept terms/privacy and confirm acceptance records exist.
7. Review sessions and revoke non-current session.
8. Logout all sessions and verify invalidation.
9. Submit deletion and export requests.
10. Submit support request and verify persisted record.

## TrueLayer sandbox

Preconditions:

- `apps/api/src/NSFinance.Api/appsettings.Local.json` configured with:
  - `TrueLayer:ClientId`
  - `TrueLayer:ClientSecret`
  - `TrueLayer:RedirectUri=http://localhost:5080/api/banking/truelayer/callback`
  - `TrueLayer:Environment=sandbox`
  - `TrueLayer:AuthBaseUrl=https://auth.truelayer-sandbox.com`
  - `TrueLayer:ApiBaseUrl=https://api.truelayer-sandbox.com`
- callback URI registered in TrueLayer sandbox

Happy path:

1. Start connect flow from mobile Accounts.
2. Complete provider consent.
3. Return and refresh status.
4. Verify connection reaches `Connected`/`synced`.
5. Trigger sync and verify imported account activity.
6. Validate callback return route handling:
   - preferred route: `nsfinance://accounts/connect-bank?...`
   - legacy compatibility route: `nsfinance://modals/add-account?...`
7. Validate initial backfill behavior:
   - first sync should request an explicit transactions `from`/`to` window
   - verify wider-than-default history is attempted (provider-dependent limits still apply)
8. Validate incremental behavior:
   - trigger another sync
   - verify follow-up transaction fetches use incremental windows (not full initial backfill again)
   - for capped providers (for example AIB), verify incremental sync uses sub-window requests instead of one broad request
   - verify logs include per-window `settledCount`, earliest/latest returned timestamps, and capped-window split diagnostics when applicable
9. Validate global sync endpoint behavior:
   - call `POST /api/banking/sync` with `{ "trigger": "manual", "source": "qa_manual" }`
   - verify `outcome=completed` on first run
   - call again within 10 minutes and verify `outcome=skipped_cooldown` with remaining cooldown seconds
   - call with `{ "trigger": "auto", "source": "qa_auto" }` shortly after a successful run and verify `outcome=skipped_not_due`
   - verify manual cooldown and auto due behavior re-open after about 10 minutes
   - if provider rate limiting is simulated, verify `outcome=skipped_provider_backoff` and per-connection `providerBackoffUntilUtc`
10. Validate stage durability:
   - simulate transaction endpoint failure after a successful balance fetch
   - verify balance snapshot persists in storage
   - verify logs identify the failed stage name (`account_transactions_import`)

Data checks (authenticated API):

- `GET /api/banking/connections`
- `GET /api/banking/accounts`
- `GET /api/banking/cards`
- `GET /api/banking/accounts/{accountId}/balances`
- `GET /api/banking/accounts/{accountId}/transactions?page=1&pageSize=50`
- `GET /api/banking/recurring-payments`
- `GET /api/banking/accounts/{accountId}/recurring-payments`

Expanded scope checks:

1. Start a new link flow and confirm the returned auth URL `scope` includes:
   - `info`
   - `accounts`
   - `cards`
   - `balance`
   - `transactions`
   - `offline_access`
   - `direct_debits`
   - `standing_orders`
2. After first sync, validate:
   - connection summary includes capability flags (cards/direct debits/standing orders/info)
   - "Connected as" is populated when `/info.full_name` is returned
   - account labels remain account/card display labels (identity name must not replace them)
   - account details page shows grouped "Account info" and "Connection info" and does not show recurring commitments there
   - recurring commitments are visible through cashflow/planning recurring surfaces instead

Consent expiry / reconfirmation continuity:

1. Move a connection to `reauth_required` or `expired` (simulate token expiry/revoke).
2. Confirm previously imported transactions remain visible in NSFinance.
3. Reconfirm from app flow and ensure `POST /api/banking/truelayer/link` is called with the existing `connectionId`.
4. Confirm status returns to connected/synced path without wiping historical imported rows.
5. Confirm no duplicate explosion after reconfirmation (dedupe still holds).

Negative checks:

- invalid/reused auth code handling
- invalid credentials config error behavior
- sandbox/live environment mismatch rejection behavior

Live-market verification (before device run):

1. Call `POST /api/banking/truelayer/link` with:
   - `{ "appReturnUri": "nsfinance://accounts/connect-bank" }`
2. Confirm `authorizationUrl` contains:
   - `providers=ie-ob-all`
   - `country_id=IE`

Disconnect:

1. `POST /api/banking/connections/{connectionId}/disconnect`
2. Verify status transitions to `disconnect_pending` quickly.
3. Verify final transition to `revoked` after background cleanup.
4. Verify token cleanup and imported banking-data cleanup behavior.
5. Retry disconnect while pending and confirm idempotent behavior (no corruption/duplicate failures).

Linked transfer coherence:

1. Open a transaction that is part of a verified linked internal transfer pair.
2. Change category/subcategory within the Transfers taxonomy and save.
3. Open the linked counterpart transaction and verify it reflects transfer taxonomy coherently.
4. In Transaction Details, verify the `Linked transaction` section appears only for linked rows.
5. Tap the linked transaction preview row and verify navigation opens the counterpart details page.
6. Open a normal non-linked merchant transaction and verify no `Linked transaction` section is rendered.

Savings movement relationship coherence:

1. Trigger a Revolut-style merchant + spare-change scenario (for example merchant `-14.47` and `spare change` `-0.53`).
2. Verify both rows remain visible in Activity.
3. Verify merchant row remains a normal expense candidate.
4. Verify spare-change row is marked as savings movement (`savings_roundup`) and does not count toward expense/income totals.
5. Trigger a manual move to savings destination (for example `To Internal Savings Pocket`).
6. Verify row is marked `savings_manual_deposit`, remains visible, and is globally neutralized for income/expense.
7. Trigger a manual move from savings destination (for example `From Internal Savings Pocket`).
8. Verify row is marked `savings_manual_withdrawal`, remains visible, and is globally neutralized for income/expense.
9. Confirm logs/inspectors expose relationship confidence, reasons, and analytics treatment for each savings movement.

Mobile freshness UX:

1. Open Accounts tab and verify a global sync icon exists in header (orange-border style).
2. Open Activity tab and verify the same global sync icon exists in header.
3. Tap header sync and verify:
   - first run returns success/info feedback
   - icon spins only during a real in-flight sync
4. Tap again during cooldown and verify:
   - cooldown message with mm:ss remaining
   - no long-running fake spinner
5. Keep app in foreground for the auto-sync cadence window and verify backend receives `trigger=auto`.
6. Background app and resume after >1 hour; verify auto-sync runs immediately on resume when due.
7. Login after a long idle period; verify post-login session entry triggers due auto-sync without waiting for interval timer.
8. Open connect-bank screen and verify no standalone `Sync now` button is shown there.
9. Validate status normalization/projection with logs for at least one provider account:
   - `settledFetched`, `pendingFetched`, `rawInserted/rawUpdated`, `projectedFromNewRaw/projectedFromStatusTransition`, and skip counters are present in lifecycle logs
   - for rows fetched from settled endpoint, normalization logs should show `sourceEndpoint=settled` and ledger projection should not be blocked by provider `status` noise
   - when projection dedupe fires, logs should include the collided existing `Transaction.Id` and the exact fingerprint values used
   - verify per-row raw upsert diagnostics show explicit outcomes (`raw_inserted`, `raw_updated_existing`, `raw_skipped_provider_id_unchanged`, `raw_skipped_dedupe_unchanged`)
10. Validate same-time distinct transaction inclusion (Revolut-like):
   - use a scenario with a merchant payment and a spare-change/round-up transfer at the same timestamp
   - confirm both rows are fetched, stored as separate raw rows, and projected as separate ledger rows
   - confirm merchant row is not hidden by the round-up/pocket row
11. Validate pending behavior:
   - rows fetched from pending endpoint remain unprojected (raw-only) until a booked counterpart arrives
   - when a pending row later arrives as booked, it is projected once without duplicate ledger creation
12. Validate timestamp provenance + normalized layer:
   - for newly fetched account transactions, confirm `RawBankTransactions` stores:
     - `ProviderTimestampRaw`, `TimestampSource`, `TimestampPrecision`
   - confirm corresponding `NormalizedBankTransactions` rows are created/updated with matching provenance and policy metadata
   - when payload includes both date-only and precise booked fields, verify `TimestampSource` points to the precise field (for example `booked_timestamp` or `transaction_timestamp`)
   - for date-only provider rows, verify UI/API does not imply an exact local-time precision that was not present upstream
13. Validate stuck-sync recovery behavior:
   - force one connection into `sync_pending` with a recent `LastSyncAttemptedUtc` and run global manual sync
   - verify that connection is skipped as `skipped_sync_in_progress`
   - force one connection into stale `sync_pending` (older than threshold) and run global manual sync
   - verify stale state is recovered and connection runs sync instead of being skipped indefinitely
14. Validate long-running manual sync resiliency:
   - trigger manual sync on a higher-volume connection and keep the app open
   - confirm endpoint logs include request-cancellation metadata and still complete sync even if the client request is interrupted
   - confirm connection-level result is returned as structured outcome rather than raw request-canceled crash
15. Validate projection reconcile cost bounds:
   - run sync on an account with many legacy raw rows lacking `ProjectedTransactionId`
   - verify lifecycle logs include `projectedDuplicateCheckAttempts`, `projectedBackfillRowsEvaluated`, `projectedBackfillRowsDeferred`, and `projectedCandidatePoolSize`
   - verify deferred backfill rows decrease across subsequent sync runs (bounded progress, no unbounded single-run spike)
