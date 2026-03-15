# Expense Plan Foundations

## Purpose

The Expenses mini-app is planning-first. An expense plan is a predictive budgeting object, not a transaction ledger.

Actuals are derived from real `ExpenseTrackerEntry` data during the selected plan period. Plans store expectations and canonical taxonomy targets; comparison output is derived and can be recalculated.

## Core entities

### `ExpensePlan`

Stored in `apps/api/src/NSFinance.Api/Persistence/Entities/ExpensePlan.cs`.

Key fields:

- `Id`
- `UserId`
- `CreatorDisplayNameSnapshot`
- `CreatorTagSnapshot`
- `Title`
- `Description`
- `Notes`
- `Status`
- `PlanType`
- `PlanOriginType`
- `PlanVersion`
- `StartDateUtc`
- `EndDateUtc`
- `CurrencyCode`
- `ExpectedIncomeTotal`
- `ExpectedSpendTotal`
- `ExpectedRemainingTotal`
- `TagsJson`
- `StatusReason`
- `CreatedAtUtc`
- `UpdatedAtUtc`
- `ActivatedAtUtc`
- `CompletedAtUtc`
- `LockedAtUtc`
- `ArchivedAtUtc`
- `CancelledAtUtc`
- `LastCalculatedAtUtc`
- `SourcePlanId`
- `IsTemplate`
- `IsRecurring`
- `RecurrenceRuleJson`
- `IsShared`
- `SharingMode`
- `SharedIdentity`

### `ExpensePlanLineItem`

Stored in `apps/api/src/NSFinance.Api/Persistence/Entities/ExpensePlanLineItem.cs`.

Key fields:

- `Id`
- `PlanId`
- `TaxonomyDomainId`
- `TaxonomyCategoryId`
- `TaxonomySubcategoryId`
- `DisplayNameSnapshot`
- `HierarchyPathSnapshot`
- `ExpectedAmount`
- `Notes`
- `SortOrder`
- `CreatedAtUtc`
- `UpdatedAtUtc`

Line items always persist canonical taxonomy references. The current validator only allows user-selectable subcategories for manual plan building, which keeps hidden/system taxonomy nodes out of the plan builder flow.

## Lifecycle states

Defined in `ExpensePlanStatuses`:

- `drafted`
- `scheduled`
- `active`
- `completed`
- `archived`
- `cancelled`

Mutable states:

- `drafted`
- `scheduled`

Locked states:

- `completed`
- `archived`
- `cancelled`

## Transition rules

Implemented in `ExpensePlanLifecycleService`.

Allowed transitions:

- `drafted -> scheduled`
- `drafted -> active`
- `drafted -> archived`
- `scheduled -> active`
- `scheduled -> cancelled`
- `scheduled -> archived`
- `active -> completed`
- `completed -> archived`
- `cancelled -> archived`

Disallowed transitions include:

- `completed -> drafted`
- `completed -> active`
- any edit-like reopening of a completed plan

When transitions are applied:

- `active` sets `ActivatedAtUtc`
- `completed` sets `CompletedAtUtc` and `LockedAtUtc`
- `cancelled` sets `CancelledAtUtc`
- `archived` sets `ArchivedAtUtc`

## Period types

Defined in `ExpensePlanTypes`:

- `weekly`
- `monthly`
- `seasonal`
- `custom_range`

Current validation rules:

- `weekly` must cover exactly 7 calendar days
- `monthly` must span a full calendar month
- `seasonal` must span three full calendar months
- `custom_range` is modeled and supported structurally, but intentionally left flexible for future product rules

## Comparison model

Comparison is built by `ExpensePlanComparisonService`.

Computed outputs include:

- actual spend total
- expected spend total
- variance amount
- variance percent
- remaining planned amount
- percent of plan used
- matched transaction count
- unexpected transaction count
- period progress metadata
- planned line-item comparisons
- unexpected category groupings

### Matching behavior

- If a line item has a subcategory target, matching is by `TaxonomySubcategoryId`.
- If category-level planning is introduced later, the comparison service already falls back to matching by `TaxonomyCategoryId` when `TaxonomySubcategoryId` is null.

### Unexpected category logic

Unexpected spending is derived, not stored.

Process:

1. Build effective comparison facts from completed transaction entries.
2. Match entries against planned line items.
3. Any in-period entry not matched to a planned line item is grouped by canonical taxonomy target.
4. Those grouped results are returned as `UnexpectedCategories`.

This allows the UI to highlight unplanned spending without mutating original plan assumptions.

## Refund/reimbursement compatibility

The comparison layer is designed to stay compatible with linked refund/reimbursement logic.

When an entry has:

- `LinkedOriginalEntryId`
- `LinkedOriginalOffsetAmount`

the comparison service prefers:

- the linked original occurrence date
- the linked original taxonomy target
- the offset amount as the effective amount

This keeps plan-period net-spend comparison future-safe, rather than hardcoding every transaction as ordinary positive spend on the transaction event date.

## Reuse / duplication semantics

Reuse is implemented as duplication in `ExpensePlanService`.

Rules:

- the original plan is never reopened
- a new plan object is created
- `SourcePlanId` points to the original
- duplicated plans start in `drafted`
- duplicated plans preserve line items and recurrence/share foundations
- duplicated plans are assigned to the current user and get fresh timestamps

## Templates, recurrence, and sharing foundations

### Templates

- `IsTemplate` distinguishes reusable structures from live plans
- template instances do not accumulate actual-vs-planned comparison history
- comparison currently returns a template-specific zeroed result for template objects

### Recurrence

Stored through `ExpensePlanRecurrenceSettings`:

- `RecurrenceType`
- `Interval`
- `NextGenerationAtUtc`
- `RecurrenceStartAtUtc`

This pass stores recurrence metadata and validates it, but does not yet implement a full automatic generation engine.

### Sharing

Foundational fields:

- `UserId`
- `CreatorDisplayNameSnapshot`
- `CreatorTagSnapshot`
- `IsShared`
- `SharingMode`
- `SharedIdentity`
- `SourcePlanId`
- `PlanOriginType`

This is enough to preserve creator attribution and shared-origin lineage now, while keeping the model ready for a future community/shared-plan browser.

## API surface

Current plan endpoints support:

- fetch plans by status
- fetch active plans
- fetch recent plans
- fetch plan detail
- create plan
- update mutable plans
- transition plan status
- duplicate plan

## Tests

Coverage added for:

- period validation
- lifecycle transition restrictions
- completed-plan locking
- duplication behavior
- canonical taxonomy persistence on line items
- recurrence metadata persistence
- comparison output
- unexpected category detection
- system taxonomy rejection in manual plan building
