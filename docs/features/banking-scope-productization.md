# Banking Scope Productization and Transaction Truth Model

## Principles

- Preserve provider truth first, derive interpretations second.
- Never hide legitimate bank events to make derived logic look cleaner.
- Keep scope behavior explicit and capability-driven.
- Keep provider-specific behavior in policy, not scattered service conditionals.

## Scope-by-scope model

### `accounts`

- Persist provider account identity (`provider_account_id`) and durable NSFinance identity (`LinkedBankAccount.Id`, `FinancialAccount.Id`).
- Keep provider raw labels/metadata, but expose clean display labels using:
  - provider name
  - cleaned account label or friendly account type
  - masked account hint (`••1234`) when available
- Names are normalization outputs, not destructive replacements of provider truth.

### `balance`

- Persist snapshots with `available`, `current`, `overdraft`, currency, and capture time.
- Treat balance freshness separately from booked transaction freshness.
- A balance change does not imply booked row availability.

### `cards`

- Cards are optional and capability-driven.
- Card balances/transactions are ingested when supported.
- Unsupported card/pending paths are skipped gracefully (policy-driven), not surfaced as hard failures.

### `direct_debits`

- Ingest direct debits into dedicated entities.
- Expose as recurring-payment inputs for planning/upcoming-payment surfaces.
- Keep support-state explicit per connection/provider.

### `info`

- Ingest identity info conservatively (`full_name`, fetch timestamp, raw payload).
- Keep provenance explicit; do not treat optional fields as universal truth.

### `offline_access`

- Required for durable refresh-token based sync continuity.
- Missing/invalid refresh token transitions connection to reauth-required states.

### `standing_orders`

- Ingest standing orders into dedicated entities.
- Expose for recurring scheduled-outgoing/planning cashflow scaffolding.
- Keep support-state explicit per connection/provider.

### `transactions` (highest-priority truth layer)

- Keep raw fetched events distinct unless identity proves same event.
- Use durable raw-to-projected linkage (`ProjectedTransactionId`) for reconciliation.
- Keep pending/booked endpoint semantics explicit.
- Keep projection inclusive: rows remain visible even when linked/interpreted.

## Transaction linking hardening

The internal-transfer linker now prioritizes confidence and reversibility:

- Provider-aware timestamp precision weighting:
  - weak precision providers (`DateOnlyOrMixed`) with midnight timestamps are treated as lower-confidence anchors
- Savings/pocket movement guardrails:
  - pocket/vault/round-up/cash-fund descriptors are not allowed to auto-link cross-account without strong counterparty confidence
- Counterparty confidence requirements:
  - account-hint match and strong name-token overlap improve confidence materially
  - weak timestamp + low counterparty confidence causes deferral (no auto-link)
- Higher auto-link threshold to reduce false positives

Result: same-amount nearby events (for example spare-change/pocket vs real cross-bank transfer) are less likely to be mislinked.

## Provider matrix inputs

Provider capabilities and history behavior are modeled using:

- TrueLayer Supported Providers table export (`Supported Providers.xlsx`)
- documented public TrueLayer behavior (timestamp precision variation, pending support variance, caching constraints)
- observed runtime behavior (labeled as observed/inferred when not explicitly documented)

See also:

- `features/banking-truelayer.md`
- `features/banking-sync-architecture-cleanup.md`
