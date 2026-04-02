# Banking Sync Architecture Cleanup (Generic Core + Provider Policies)

## Scope

This note captures the cleanup pass that separates the banking sync system into:

- a **generic sync core** (shared platform logic)
- a **provider policy catalog** (explicit provider capability/profile rules)

The goal is to remove workaround drift and keep provider-specific behavior centralized.

## Layer split

### Generic core (shared across all providers)

- global/manual/auto sync orchestration and cooldown gating
- stale `sync_pending` recovery and overlap protection
- per-connection outcome shaping and sync result semantics
- staged persistence boundaries (`account_balance_refresh`, `account_transactions_import`, `card_sync`)
- raw transaction upsert, projection linkage, duplicate protection, and transfer matching
- pending/booked normalization pipeline
- phase timing, lifecycle diagnostics, and audit hooks

### Provider policy layer (catalog-driven)

- transaction visibility model (`DateHistory` vs `CappedVisibleSlice`)
- settled response cap assumptions where known (for example AIB ~100)
- initial account/card backfill horizons
- incremental lookback/fallback/chunk strategy
- adaptive split depth + minimum split window
- pending support expectation (`Supported` / `Unsupported` / `Partial` / `Unknown`)
- timestamp precision expectation (`FullTimestamp` vs `DateOnlyOrMixed`)
- optional initial long-history grace window (for consent-window-sensitive providers)
- policy metadata (`ProviderKey`, `ProviderFamily`, `HistoryNotes`)

## Policy family matrix (current)

Policy families are grounded in current TrueLayer provider table data (`Supported Providers.xlsx`) and recent sync forensics.

- `irish_capped_slice`: AIB
- `irish_retail_standard`: Bank of Ireland
- `irish_mixed_history`: PTSB
- `fintech_revolut`: Revolut
- `fintech_monzo`: Monzo
- `fintech_starling`: Starling
- `uk_retail_santander`: Santander
- `uk_natwest_rbs_ulster_family`: NatWest, Royal Bank of Scotland, Ulster Bank
- `uk_lloyds_halifax_bos_mbna_family`: Lloyds, Halifax, Bank of Scotland, MBNA
- `uk_hsbc_firstdirect_ms_family`: HSBC, First Direct, M&S Bank
- `uk_barclays_barclaycard_family`: Barclays, Barclaycard
- `uk_card_first_mix`: American Express, Capital One, Tesco Bank, Virgin Money
- `fintech_wise`: Wise
- `fintech_tide_business`: Tide
- `fintech_business_banking`: Mettle, Zempler (Cashplus)
- `uk_building_society`: Chelsea Building Society, Yorkshire Building Society, TSB
- `uk_danske`: Danske Bank
- `uk_nationwide`: Nationwide
- `generic_date_history`: default fallback for unknown/new providers

## Special behavior inventory and classification

### Bucket 1: Generic core hardening

- durable sync stage persistence and stage-level failure visibility
- connection-level outcome summaries (`completed_changed`, `completed_no_change`, `skipped_*`, `failed`)
- raw-to-projected durable linkage (`ProjectedTransactionId`)
- endpoint-aware pending/booked status normalization
- duplicate-safe projection/backfill reconciliation controls
- stale `sync_pending` recovery and in-progress skip behavior
- provider-rate-limit backoff at orchestration layer

### Bucket 2: Valid provider policy

- AIB capped visible-slice + adaptive split strategy
- Revolut/Monzo consent-window-sensitive long-history note + grace metadata
- Santander pending unsupported expectation
- provider-family-specific initial history baselines (account/card)
- timestamp precision expectations by provider family

### Bucket 3: Workarounds removed or converted

- removed scattered provider string checks in `BankSyncService` card window logic
- removed hardcoded Revolut grace warning branch based on policy-name literal
- converted provider-specific transaction/card backfill tuning into `ProviderSyncPolicyCatalog`
- converted pending endpoint skip behavior to policy-driven decision for known unsupported providers

## Diagnostics contract

The sync logs now expose both core and policy truth:

- selected policy key/family for each account sync
- pending support and timestamp precision expectations
- fetched settled/pending counts and freshness comparison against checkpoint
- raw-change counts vs projected-change counts
- per-connection global outcomes with skip/failure reasons

This makes provider-side limitation vs app-side pipeline issue easier to distinguish.

## Remaining provider-truth limits

- provider-returned history remains upstream-constrained; NSFinance cannot synthesize inaccessible rows
- pending support is provider-dependent and may vary by country/provider variant
- some providers may only expose date-level timestamps; same-time reconciliation remains identity-driven, not timestamp-driven

## Next extension path

When onboarding a new bank/provider variant:

1. add/adjust policy hints in `ProviderSyncPolicyCatalog`
2. update family test coverage in `ProviderSyncPolicyCatalogTests`
3. document behavior deltas in this file and `banking-truelayer.md`
