# Expense Plans

## Foundations

Expense plans are predictive budgeting objects, separate from transaction ledgers.
Actuals are derived during comparison windows from real tracker entries.

Core entities:

- `ExpensePlan`
- `ExpensePlanLineItem`

Lifecycle statuses include drafted, scheduled, active, completed, archived, cancelled.
Completed/cancelled plans are treated as locked outcomes.

## Comparison model

Comparison computes expected vs actual, variance, remaining budget, and unplanned categories.
Unexpected spend is derived from unmatched entries, not stored as a mutable plan edit.

## Duplication and reuse

Reusing a plan creates a new plan with lineage (`SourcePlanId`) rather than reopening historical plans.

## Community publishing layer

Community uses a separate publication model:

- private plan remains user-owned
- public publication stores frozen snapshot (`PlanSnapshotJson`)
- moderation/reporting state is independent

Publication capabilities include:

- publish/edit/unpublish
- likes
- download/use into private copies
- moderation rescans and reporting

## Deferred items

- admin moderation console
- richer social graph/following
- advanced recommendation and ranking evolution
