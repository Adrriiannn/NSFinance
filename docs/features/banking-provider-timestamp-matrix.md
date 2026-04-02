# Banking Provider Timestamp Precision Matrix

## Purpose

This matrix defines the transaction timestamp precision class used by NSFinance policy and matching logic.

Classes:

- `precise_datetime`
- `date_only_midnight`
- `unknown_needs_verification`

## Sources

- TrueLayer supported-provider export (`Supported Providers.xlsx`, local attachment).
- Public TrueLayer timestamp precision guidance.
- NSFinance observed payload behavior from live/sandbox diagnostics.

`SourceType` meaning:

- `documented`: explicitly documented by provider guidance.
- `observed`: seen in NSFinance payloads/logs.
- `inferred`: mapped via provider family where direct evidence is incomplete.

## Matrix

| Provider | PrecisionClass | SourceType | Policy Family |
|---|---|---|---|
| AIB | `date_only_midnight` | observed | `irish_capped_slice` |
| American Express | `date_only_midnight` | documented | `uk_card_first_mix` |
| Bank of Scotland | `date_only_midnight` | documented | `uk_lloyds_halifax_bos_mbna_family` |
| Barclaycard | `precise_datetime` | documented | `uk_barclaycard` |
| Barclays | `date_only_midnight` | documented | `uk_barclays_barclaycard_family` |
| Bank of Ireland | `unknown_needs_verification` | inferred | `irish_retail_standard` |
| Capital One | `precise_datetime` | documented | `uk_capital_one` |
| Chelsea Building Society | `unknown_needs_verification` | inferred | `uk_building_society` |
| Danske Bank | `date_only_midnight` | documented | `uk_danske` |
| First Direct | `date_only_midnight` | documented | `uk_hsbc_firstdirect_ms_family` |
| Halifax | `date_only_midnight` | documented | `uk_lloyds_halifax_bos_mbna_family` |
| HSBC | `date_only_midnight` | documented | `uk_hsbc_firstdirect_ms_family` |
| Lloyds Bank | `date_only_midnight` | documented | `uk_lloyds_halifax_bos_mbna_family` |
| MBNA | `date_only_midnight` | documented | `uk_lloyds_halifax_bos_mbna_family` |
| Mettle | `unknown_needs_verification` | inferred | `fintech_business_banking` |
| Monzo | `precise_datetime` | documented | `fintech_monzo` |
| M&S Bank | `date_only_midnight` | documented | `uk_hsbc_firstdirect_ms_family` |
| Nationwide | `date_only_midnight` | documented | `uk_nationwide` |
| NatWest | `date_only_midnight` | documented | `uk_natwest_rbs_ulster_family` |
| PTSB | `unknown_needs_verification` | inferred | `irish_mixed_history` |
| Revolut | `precise_datetime` | documented | `fintech_revolut` |
| Santander | `date_only_midnight` | documented | `uk_retail_santander` |
| Starling | `unknown_needs_verification` | inferred | `fintech_starling` |
| Tesco Bank | `date_only_midnight` | documented | `uk_card_first_mix` |
| The Royal Bank of Scotland | `date_only_midnight` | documented | `uk_natwest_rbs_ulster_family` |
| Tide | `precise_datetime` | documented | `fintech_tide_business` |
| TSB | `precise_datetime` | documented | `uk_tsb` |
| Ulster Bank | `date_only_midnight` | documented | `uk_natwest_rbs_ulster_family` |
| Virgin Money | `date_only_midnight` | documented | `uk_card_first_mix` |
| Wise | `precise_datetime` | documented | `fintech_wise` |
| Yorkshire Building Society | `unknown_needs_verification` | inferred | `uk_building_society` |
| Zempler Bank | `unknown_needs_verification` | inferred | `fintech_business_banking` |

## How the matrix is used

- Transfer matching applies a penalty/deferral path when precision is weak and counterpart confidence is low.
- Timestamp provenance (`source`, `raw`, `precision`) is persisted on raw and normalized transaction layers.
- Low-confidence pairing is prevented from auto-linking into analytics-neutral transfer handling.

## AIB timestamp evidence

Current evidence for consumer AIB in NSFinance points to date-only precision in the Data API payload:

- Integration fixture that mirrors the problematic cross-bank scenario uses:
  - `provider_id: ob-aib`
  - transaction `timestamp: "2026-04-01"` (date-only)
  - matching Revolut counterpart rows include full datetime (`2026-04-01T09:07:00Z`)
- Live sync diagnostics for AIB repeatedly showed:
  - `earliestReturnedUtc=03/29/2026 23:00:00`
  - `latestReturnedUtc=03/30/2026 23:00:00`
  - no newer row beyond checkpoint in the same run

These are consistent with date-level booking timestamps rendered in UTC/BST boundaries.  
If future payload captures show precise consumer AIB transaction timestamps, reclassify `ob-aib` in `ProviderSyncPolicyCatalog` and update this matrix.
