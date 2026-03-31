# Banking Sync Provider Audit (AIB + TrueLayer)

## Scope

This note captures the sync-pipeline audit outcomes that motivated the provider-aware sync-policy and staged durability refactor.

## Findings

1. Revolut and AIB were routed through the same generic transaction import mental model, with only limited provider specialization.
2. The previous incremental model was checkpoint-based and could issue broad windows that are acceptable for history-friendly providers but fragile for count-limited providers.
3. For AIB-like providers, broad windows can repeatedly return capped visible slices and starve newer transactions from import if not split/re-scanned safely.
4. The pipeline did not persist stage boundaries explicitly enough for operational clarity: while many failures still saved state through status transitions, stage durability was implicit rather than intentional.
5. Logs were improved but still needed explicit stage naming to remove ambiguity during incident debugging.

## AIB divergence from generic assumptions

- AIB is now treated as a **capped visible-slice** provider in policy, not as broad-history-complete.
- Incremental sync strategy now re-scans overlapping visible slices with chunking and adaptive split behavior when responses appear capped.
- This avoids the brittle assumption that a single broad request window is always reliable.

## Design correction implemented

1. Introduced explicit provider sync policy catalog (`ProviderSyncPolicyCatalog`) with provider mode/cap/backfill/incremental strategy fields.
2. Added staged persistence checkpoints inside sync:
   - account/balance stage persists independently,
   - account transaction/commitments stage persists independently,
   - card stage persists independently.
3. Added stage-aware failure metadata (`stage`) for failure logs/audit to support deterministic root-cause tracing.
4. Added tests for:
   - AIB capped-window incremental recovery,
   - balance persistence despite downstream transaction-stage failure,
   - provider policy resolution behavior.

## Remaining provider-truth limitation

Count-limited providers can only expose their visible slice through upstream APIs. The importer now reconciles that slice robustly, but cannot synthesize provider-inaccessible history.
