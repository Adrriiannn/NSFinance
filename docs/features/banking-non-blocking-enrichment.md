# Banking Non-Blocking Enrichment

## Overview
Bank sync and transaction enrichment are now intentionally split:

1. **Sync/Ingestion (foreground)**  
   Fetches accounts, balances, and transactions, then returns quickly.
2. **Deterministic enrichment (background)**  
   Runs in batches for relationship linking and deterministic transaction organization.

This keeps bank connection UX responsive while still allowing full-history enrichment to complete.

## Backend behavior

### New progress endpoint
- `GET /api/banking/enrichment-progress`
- Returns overall + per-connection enrichment state:
  - `inProgress`
  - `stage`
  - `processedCount`
  - `totalCount`
  - `remainingCount`
  - `progressPercent`
  - timestamps (`startedUtc`, `lastUpdatedUtc`, `completedUtc`)

### Background worker
- `BankDeterministicEnrichmentBackgroundWorker` manages queued enrichment work.
- Global/manual sync now performs non-historical deterministic pass inline and queues historical processing.
- Worker resumes pending in-progress connections and processes historical batches until complete.

### Progress semantics
- Progress represents **deterministic enrichment progress**, not raw bank sync completion.
- Historical enrichment state is persisted on `OpenBankingConnection` and is resumable.

## Mobile behavior

### Global progress dial
- `GlobalEnrichmentProgressDial` is mounted at app shell level and appears while enrichment is in progress.
- It is non-blocking and visible across main app surfaces.
- Tap opens a compact details card (stage + counts).
- It auto-hides when progress completes.

### Post-connection explainer tooltip
- After leaving connect-bank via **Done**, the app emits a one-shot UX event.
- A tooltip appears for 10 seconds:
  - “Organizing your transactions”
  - explains that background organization continues while app usage remains available.

### Live updates
- While enrichment is in progress, the app periodically invalidates transaction/account/dashboard queries.
- Transaction rows/details and analytics can update without requiring manual refresh.

## Processing strategy
- Deterministic enrichment prioritizes recent context and then continues historical backfill in batches.
- Background processing is resumable and avoids re-running fully current rows unnecessarily.

## Notes
- This system is the deterministic substrate for future AI categorization.
- AI enrichment should layer on top of this pipeline, not replace deterministic transfer/savings organization.
