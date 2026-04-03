# Banking Fixture Standards

This file defines mandatory standards for banking-sync test fixtures.

## Privacy-safe fixture rules

- Never use real user names, real account holder names, or copied personal transaction text.
- Never commit raw bank-export text from live user data.
- Prefer synthetic tokens and references:
  - `Outbound Transfer Holder Alpha`
  - `Inbound Transfer Holder Alpha`
  - `Internal Savings Pocket`
- If provider-specific wording is required for a documented provider capability test, isolate it to a clearly named provider-quirk scenario and document why.
- Public-provider terminology is allowed only when all are true:
  - It is publicly documented provider branding/wording (not user-private text).
  - The test specifically validates a provider quirk/capability.
  - The provider wording is isolated to provider-quirk fixtures, not generic transfer tests.

## Provider-family fixtures

Generic banking fixtures should model provider families/capabilities rather than one named bank snapshot:

- `PreciseDateTimeBank`
- `DateOnlyMidnightBank`
- `PendingUnsupportedBank`
- `CappedVisibleSliceBank`
- `SavingsProductProvider`

Tests should assert behavior from structured capability inputs, not provider name coincidence.

## Scenario-builder approach

Banking transfer tests should be expressed through scenario builders rather than ad-hoc literal JSON coupling.

Current reusable builders in `OpenBankingIntegrationTests`:

- `TransferPairScenarioBuilder`
- `RepeatedAmountClusterScenarioBuilder`
- `AmbiguousClusterScenarioBuilder`

These builders encode structured matching signals:

- amount and currency parity
- directionality (outbound vs inbound)
- timestamp precision style (date-only vs precise datetime)
- proximity and repeated-amount cluster shape
- savings-movement signals

Prefer stable semantic IDs in fixtures:

- `tx-chain-inbound-a`, `tx-chain-outbound-a`, `tx-ambiguous-inbound-a`

Avoid brittle date-encoded identifiers when dates are already represented by transaction timestamps.

## Test design goals

- Validate matching behavior through structured signals, not person/provider-specific literals.
- Keep fixtures provider-extensible and user-agnostic.
- Cover ambiguity and conservative no-force-link behavior.
- Ensure same-day preference and repeated-amount chain handling stay regression-safe.

## Runtime policy note

Runtime matching logic must remain generic and policy-driven. Personal-name literals are prohibited in runtime code.

## Historical cleanup note

This hygiene policy applies to current files. If any sensitive literal appears in repository history, treat history rewriting as a separate explicit maintenance task.
