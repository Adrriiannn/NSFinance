# Mobile Page Inventory and Category Map

This inventory maps every routed surface under `apps/mobile/app` and clarifies where each page belongs in product structure.

## Categories
- Main tab root
- Accounts
- Banking connection
- Security
- Plans
- Categories
- Menu / settings / legal
- Utility/system/auth
- Modal-only flows

## Route Inventory
| Route | Current location | Recommended category | Notes |
|---|---|---|---|
| `/` | `app/index.tsx` | Utility/system/auth | Root redirect/bootstrap entry. |
| `/(auth)` | `app/(auth)` | Utility/system/auth | Auth stack wrapper. |
| `/(auth)/index` | `app/(auth)/index.tsx` | Utility/system/auth | Auth entry chooser. |
| `/(auth)/login` | `app/(auth)/login.tsx` | Utility/system/auth | Login screen. |
| `/(auth)/register` | `app/(auth)/register.tsx` | Utility/system/auth | Registration screen. |
| `/(auth)/forgot-password` | `app/(auth)/forgot-password.tsx` | Utility/system/auth | Password recovery flow. |
| `/(tabs)` | `app/(tabs)/_layout.tsx` | Main tab root | Main app shell. |
| `/(tabs)/index` | `app/(tabs)/index.tsx` | Main tab root | Default tab redirect/landing. |
| `/(tabs)/activity` | `app/(tabs)/activity/index.tsx` | Main tab root | Activity root tab. |
| `/(tabs)/activity/add` | `app/(tabs)/activity/add.tsx` | Modal-only flows | Add transaction flow (tab-scoped route). |
| `/(tabs)/activity/[id]` | `app/(tabs)/activity/[id].tsx` | Utility/system/auth | Transaction detail route. |
| `/(tabs)/cashflow` | `app/(tabs)/cashflow/index.tsx` | Main tab root | Cashflow root tab. |
| `/(tabs)/cashflow/upcoming-payments` | `app/(tabs)/cashflow/upcoming-payments.tsx` | Accounts | Payment schedule surface. |
| `/(tabs)/cashflow/recurring-subscriptions` | `app/(tabs)/cashflow/recurring-subscriptions.tsx` | Accounts | Recurring obligations surface. |
| `/(tabs)/planning` | `app/(tabs)/planning/index.tsx` | Main tab root | Planning root tab. |
| `/(tabs)/planning/browse` | `app/(tabs)/planning/browse.tsx` | Plans | Plan discovery. |
| `/(tabs)/planning/builder` | `app/(tabs)/planning/builder.tsx` | Plans | Plan creation/editor. |
| `/(tabs)/planning/analytics` | `app/(tabs)/planning/analytics.tsx` | Plans | Planning analytics. |
| `/(tabs)/planning/categories` | `app/(tabs)/planning/categories.tsx` | Categories | Category management under planning. |
| `/(tabs)/planning/my-published` | `app/(tabs)/planning/my-published.tsx` | Plans | Published plans owned by user. |
| `/(tabs)/planning/publish` | `app/(tabs)/planning/publish.tsx` | Plans | Publish flow. |
| `/(tabs)/planning/[planId]` | `app/(tabs)/planning/[planId].tsx` | Plans | Plan detail/edit route. |
| `/(tabs)/planning/published/[publicationId]` | `app/(tabs)/planning/published/[publicationId].tsx` | Plans | Public plan detail route. |
| `/(tabs)/planning/published/report` | `app/(tabs)/planning/published/report.tsx` | Plans | Public plan reporting. |
| `/(tabs)/calendar` | `app/(tabs)/calendar/index.tsx` | Main tab root | Calendar root tab. |
| `/(tabs)/companion` | `app/(tabs)/companion/index.tsx` | Main tab root | Companion root tab. |
| `/(tabs)/accounts` | `app/(tabs)/accounts/index.tsx` | Main tab root | Accounts root tab. |
| `/(tabs)/accounts/[id]` | `app/(tabs)/accounts/[id].tsx` | Accounts | Account detail route. |
| `/(tabs)/accounts/transfer` | `app/(tabs)/accounts/transfer.tsx` | Accounts | Transfer entry flow. |
| `/(tabs)/accounts/statements` | `app/(tabs)/accounts/statements.tsx` | Accounts | Statements surface. |
| `/(tabs)/accounts/profile` | `app/(tabs)/accounts/profile.tsx` | Menu / settings / legal | User profile/settings. |
| `/(tabs)/accounts/security` | `app/(tabs)/accounts/security.tsx` | Security | Security and sessions. |
| `/(tabs)/accounts/support` | `app/(tabs)/accounts/support.tsx` | Menu / settings / legal | Help and support. |
| `/(tabs)/accounts/about` | `app/(tabs)/accounts/about.tsx` | Menu / settings / legal | About screen. |
| `/(tabs)/accounts/legal-privacy` | `app/(tabs)/accounts/legal-privacy.tsx` | Menu / settings / legal | Legal/privacy hub under accounts. |
| `/(tabs)/accounts/connect-bank` | `app/(tabs)/accounts/connect-bank.tsx` | Banking connection | Bank connect/sync state flow. |
| `/legal/terms` | `app/legal/terms.tsx` | Menu / settings / legal | Legal standalone route. |
| `/legal/privacy-policy` | `app/legal/privacy-policy.tsx` | Menu / settings / legal | Legal standalone route. |
| `/legal/open-banking` | `app/legal/open-banking.tsx` | Menu / settings / legal | Legal standalone route. |
| `/legal/data-rights` | `app/legal/data-rights.tsx` | Menu / settings / legal | Legal standalone route. |
| `/legal/ai-disclosure` | `app/legal/ai-disclosure.tsx` | Menu / settings / legal | Legal standalone route. |
| `/modals/add-account` | `app/modals/add-account.tsx` | Modal-only flows | Explicit modal route. |
| `/oauthredirect` | `app/oauthredirect.tsx` | Utility/system/auth | OAuth callback handler. |

## Misplacements and Follow-ups
- `/(tabs)/accounts/security`: route placement is acceptable, but navigation must not leak users back into `connect-bank`; this pass fixed that back navigation behavior.
- Legal routes are split between `/(tabs)/accounts/legal-privacy` and `/legal/*`; keep both for now, but unify legal entry points in a follow-up to reduce duplication.
- `/(tabs)/activity/add` currently behaves as a page route; if product wants full modal semantics, consider moving under `/modals/*` in a dedicated navigation cleanup pass.
- Planning categories are under planning (`/(tabs)/planning/categories`); if categories become global product objects, consider promoting to a dedicated categories stack.

## Current Pass Scope Outcome
- Structural inventory is now documented.
- High-risk navigation bug (`Security -> back -> connect-bank`) is fixed.
- Broad route relocation is intentionally deferred to avoid destabilizing existing deep-link and stack behavior.
