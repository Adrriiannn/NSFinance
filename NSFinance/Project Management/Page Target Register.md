---
type: page-register
status: accepted
updated: 2026-07-19
tags:
  - nsfinance/project
  - ux
  - product-foundation
---

# Page Target Register

Authority document for [[Work Items/UX-003 - Define Page Targets And Overhaul Every Surface]]. Each entry defines one surface: its **job**, **primary blocks**, **links**, **secondary surfaces**, and **data authority**. Every page also owes the standard states (loading / empty / error / stale / partial) from the design system. **Accepted by the user on 2026-07-19** ("I agree with whatever you wrote in those 2, just apply them"). The two `[DECISION]` items resolved to the register defaults: Today leads with the balance headline followed by Needs attention, and Insights keeps the cumulative comparison as hero until categorization lands category bars as the secondary block.

## Navigation Frame (target)

Five destinations (accepted architecture): **Today · Plan · Activity · Accounts · Insights**, plus the menu (profile/settings) and the Companion as a contextual Ask. Migration is staged: current Home/Cashflow evolve into Today/Insights; Plan appears when the budget domain exists (Phase 4).

---

## Authentication

### Login
- **Job**: return the user to their money in under five seconds, with provider or password.
- **Blocks**: brand moment; email+password; remember-me; provider buttons (Google/Microsoft); forgot-password; sign-up; legal links.
- **Secondary**: CAPTCHA gate after repeated failures; lockout countdown banner; MFA handoff.
- **Authority**: auth API; no financial data may render here.
- **Target refinement**: reduce the empty vertical band above the fields (visual rhythm), keep sub-2s provider warm-up, decorated packs may theme the brand moment only.

### Register / Verify Email / MFA / Forgot Password
- **Job**: one continuous, honest ceremony from "new" to "verified and in".
- **Blocks**: full name/email/password with live policy feedback; terms consent; Turnstile; six-digit verification; TOTP entry with trusted-device offer; recovery entry.
- **Authority**: auth + policy APIs.
- **Target refinement**: stepper continuity (register → verify → in) instead of discrete screens; resend timers visible; recovery paths never dead-end.

---

## Today (evolves from Home)

- **Job**: answer "where am I now and is anything needed from me today?" in one glance.
- **Blocks (target order)**: greeting + as-of freshness; balance headline (per-currency portfolio, source-honest); **Needs attention** (uncategorized count, stale connections, upcoming within 3 days); Upcoming strip (canonical commitments); Recent activity preview (5 rows, honest dates); one insight card with drill-through.
- **Links**: Activity (view all), Accounts (balance headline tap), Insights (insight card), Companion (contextual Ask).
- **Secondary**: none owned; everything drills into owning surfaces.
- **Authority**: dashboard summary (INS-001 upgrade), commitments, canonical paging preview.
- **[DECISION]**: does "Needs attention" rank above balance for you, Monarch-style, or below?

## Activity

- **Job**: the complete, truthful ledger — find anything, understand anything, fix anything.
- **Blocks**: search+filters (server-backed once INS-001/Phase 6 lands); week/month grouped paged list (canonical contract, live); per-row: merchant (cleaned name once CAT-001 lands), category chip, honest date/time, amount with direction semantics.
- **Links**: Transaction detail; Categories picker (filter mode).
- **Secondary**: filter sheet; category picker; (later) bulk actions, review queue entry.
- **Authority**: canonical paged reads only; legacy list retires after server search.
- **Target refinement**: swipe actions kept; add "Uncategorized" quick filter chip; empty search state suggests removing tokens.

### Transaction Detail
- **Job**: everything true about one transaction; correct what the machine got wrong.
- **Blocks**: amount+direction hero; merchant vs original statement text (both visible, hierarchy clear); honest date (time only when real); account; category with change action; notes/reason; provenance (import batch/provider, freshness); linked transfer counterpart when present.
- **Secondary**: category picker; (CAT-001) "why this category" explanation + correction feedback.
- **Authority**: canonical detail read; metadata mutation API.

## Accounts

- **Job**: every place money lives, its health, and its plumbing.
- **Blocks**: per-account cards (masked identity, balance envelope with available/current, freshness); connection health strip; add-connection CTA.
- **Links**: Account detail; Connect bank; Import statement.
- **Secondary**: account switcher (current dropdown → cleaner segmented header); Get Help.
- **Authority**: accounts + banking APIs (already canonical).
- **Target refinement**: "Check your spendings" widget moves to Insights; the account page stays about accounts.

### Account Detail
- **Job**: one account's truth: identity, balance provenance, recent movement, tools.
- **Blocks**: masked identity + appearance; balance envelope (current/available/overdraft, source, as-of); connection info + last sync; bounded recent transactions (paged); tools (import statement, export, help).
- **Authority**: already migrated to canonical contracts — keep as reference implementation.

### Connect Bank / Import Statement / Statements
- **Job**: plumbing ceremonies — link a bank (TrueLayer), backfill history (CSV), review documents.
- **Target refinement**: [IRE-001 later] curated Irish institution list; import wizard keeps its staged preview/commit/undo honesty; statements page becomes export/history home.

## Insights (evolves from Cashflow)

- **Job**: understand patterns — where money goes, how months compare, what changed.
- **Blocks (target)**: period selector; income vs spend vs net series (INS-001 contracts); category breakdown (post-CAT-001); month-vs-month comparison with **semantic good/bad color logic** (spending more = attention color, regardless of series); recurring subscriptions; upcoming (full list).
- **Links**: drill-through to filtered Activity everywhere.
- **Secondary**: period picker; category drill sheets.
- **Authority**: INS-001 server aggregates only — the current client-computed comparisons retire.
- **[DECISION]**: keep the cumulative-line month comparison as the hero, or lead with category bars once categorization lands?

## Plan (future, Phase 4)
- Placeholder register entry: Guided Flex (fixed/flexible/non-monthly) with safe-to-spend; enters the register properly at Phase 4 discovery with its own acceptance pass.

## Companion

- **Job**: bounded financial Q&A grounded in the user's own context packet; contextual Ask from any surface.
- **Blocks**: thread view (server-backed); finance-first prompt carousel; input; (AI-001/AI-002) context chips showing what the answer used.
- **Target refinement**: prompt copy leads with money, not food; Back always returns (fixed); entry becomes contextual per-surface Ask actions plus the dock.

## Menu / Profile / Security / Support / Legal / About

- **Job**: identity, safety, and boring-but-vital plumbing, compact.
- **Target refinement**: menu slims to navigation + theme row (done) + profile chip; social fields (NS Tag/bio/Instagram) leave the core profile per the audit's P2; Security keeps password/MFA/devices/biometrics; Support keeps requests/export/deletion with plain language.

---

## Acceptance Tracking

| Page | Register accepted | Overhaul state |
| --- | --- | --- |
| Login / auth ceremony | 2026-07-19 | consolidated primitives shipped; rhythm polish pending |
| Today | 2026-07-19 | awaiting acceptance + INS-001 |
| Activity | 2026-07-19 | paging shipped; search/server filters pending |
| Transaction detail | 2026-07-19 | honest dates shipped |
| Accounts | 2026-07-19 | canonical envelopes shipped |
| Account detail | 2026-07-19 | reference implementation |
| Insights | 2026-07-19 | commitments strip shipped; aggregates pending |
| Companion | 2026-07-19 | back/session fixed; scope reframe pending |
| Menu cluster | 2026-07-19 | theme row shipped |
