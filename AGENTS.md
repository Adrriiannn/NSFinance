# NSFinance Agent Guide

This file applies to the entire NSFinance repository. It is the durable entry
point for Codex and other coding agents on every computer that opens this
folder. Assume a new task has no knowledge of earlier chats, local memories, or
work performed on another machine.

## Mandatory Context Bootstrap

Before planning, editing, testing, or changing an external system:

1. Confirm the working folder is the repository root containing `apps`, `libs`,
   `docs`, and the `NSFinance` Obsidian vault.
2. Read `NSFinance/00 - Start Here.md`.
3. Read both habitat maps:
   - `NSFinance/User Habitat/00 - User Habitat Map.md`
   - `NSFinance/Developer Habitat/00 - Developer Habitat Map.md`
4. Read `NSFinance/Project Management/00 - Delivery Control Center.md`.
5. Read the applicable live board and linked work-item note:
   - `NSFinance/Project Management/Release Readiness Board.md`
   - `NSFinance/Project Management/Product Quality Board.md`
6. Follow the links from those notes into the relevant user, architecture,
   quality, data, AI, banking, identity, or deployment notes. Do not make the
   user repeat information that is already documented.
7. Inspect the relevant implementation, tests, and current Git state before
   deciding what is true today.

Do not read every vault file indiscriminately. Start from the maps and follow
the links for the task. If OneDrive has not downloaded a required note, wait for
it to become locally available instead of inventing the missing context.

## Product Mission

NSFinance is an Ireland-first, mobile-first personal finance companion. It
should help a person answer three questions exceptionally well:

- Where am I now?
- What is coming next?
- What is the smartest choice I can make with my current money?

The product combines budgeting, financial tracking, cash-flow awareness,
transaction intelligence, and a tightly bounded AI companion. The experience
should feel airy, fresh, warm, calm, trustworthy, and precise. Visual polish
must never obscure financial truth, safety, or usability.

Monarch, YNAB, and Rocket Money are useful competitive references, not product
specifications. NSFinance should combine polished, approachable interaction
with serious budgeting depth and stronger, user-grounded financial guidance.

## Product Boundaries

- The AI companion is an extension of the user's budget, not a general-purpose
  chatbot.
- AI answers must be grounded in fresh, authorized user financial context and
  must distinguish facts, estimates, assumptions, and missing data.
- Nearby place recommendations are in scope only when framed as spending or
  planning decisions and constrained by the user's budget, location, and
  preferences.
- Transaction categorization must be constrained by the governed domain,
  category, and subcategory ontology, expose confidence, and support correction.
- Balances, transactions, recurring commitments, linked transfers, budgets,
  plans, and classifications must be explainable and reconcilable.
- Trust, consent, privacy, accessibility, and recovery behavior are core product
  functionality, not finishing work.

## Source Of Truth

Use the following authority order when sources disagree:

1. The user's current explicit instruction.
2. Current code, tests, database schema, and safely verified live-system evidence
   for what the product actually does.
3. The applicable work-item note for accepted scope, dependencies, status,
   decisions, evidence, and acceptance criteria.
4. The User Habitat for product promise, journeys, language, and experience.
5. The Developer Habitat for architecture, integrations, operations, risks, and
   quality strategy.
6. Repository documentation and README files for supporting procedures.

The Obsidian vault is the project knowledge and planning system, but snapshots
can become stale when external state changes. Verify time-sensitive claims and
update the affected note rather than silently working around a discrepancy.
Kanban cards are navigation and status views; their linked work-item notes are
authoritative.

## Repository And Architecture

- `apps/api`: .NET 10 ASP.NET Core modular API and backend tests.
- `apps/mobile`: Expo React Native, TypeScript, and Expo Router mobile app.
- `apps/worker`: .NET 10 worker service.
- `libs`: shared domain, connector, infrastructure, and utility libraries.
- `docs`: repository-facing architecture, setup, deployment, feature, and QA
  documentation.
- `tools`: controlled Postman, DBeaver, and project tooling assets.
- `NSFinance`: Obsidian product, engineering, QA, and delivery knowledge base.

Primary runtime relationships:

- Mobile app -> ASP.NET Core API.
- API -> Azure PostgreSQL through EF Core and Npgsql.
- API -> TrueLayer live open banking.
- API -> Azure OpenAI and Google Places where the bounded AI experience needs
  them.
- GitHub Actions -> production migration and Azure deployment workflow.

Prefer established module boundaries and existing patterns. Add an abstraction
only when it removes real complexity or matches a proven local pattern.

## Production-Only Operating Model

NSFinance currently uses one production-connected operating model. Do not
introduce parallel environment splits, alternate provider modes, container
orchestration, or a standalone PostgreSQL installation unless the user changes
this direction explicitly.

- Keep TrueLayer on its live endpoints and registered production callback.
- Keep the mobile default API target on `https://api.finance.nsireland.ie`.
- Use named, bounded QA identities and identifiable test data for controlled
  validation against live services.
- Begin Azure, database, provider, and deployment investigations read-only.
- Prefer least-privilege access, especially for DBeaver and cloud inspection.
- Back up and document a restore path before destructive schema, reconciliation,
  or data-repair work.
- Never run load tests, bulk deletion, mass reclassification, or speculative
  repair logic against live user data.
- Record every external mutation, reason, evidence, and rollback path in the
  relevant work item.
- Do not use another real user's financial data for testing.

Production-only does not mean bypassing quality controls. Automated tests,
contract fixtures, named QA accounts, reversible smoke checks, approvals, and
release gates remain mandatory.

## Secrets And Sensitive Data

- Never print, paste, commit, or document credentials, tokens, connection
  strings, private keys, OAuth secrets, one-time codes, or production user data.
- Local API secrets belong only in the ignored
  `apps/api/src/NSFinance.Api/appsettings.Local.json`.
- Local mobile values belong only in ignored local environment files. Treat all
  `EXPO_PUBLIC_*` values as publicly shipped client configuration.
- Production secrets belong in Azure App Service settings, Key Vault, or an
  approved secret store.
- Postman token values and DBeaver credentials remain local or in an approved
  private workspace; do not place them in the Obsidian vault.
- Redact subscription IDs, tenant IDs, account identifiers, email addresses,
  bank details, transaction descriptions, and personal data from evidence unless
  the exact value is essential and the destination is approved.
- Inspect database schemas, migration history, health, and aggregate evidence
  before opening row-level financial data.

## Delivery And Obsidian Workflow

For material product, code, infrastructure, or QA work:

1. Identify the existing work item before changing anything. Create one from
   `NSFinance/Project Management/Templates/Work Item Template.md` only when the
   work is genuinely new.
2. Keep the work-item outcome, scope, dependencies, acceptance criteria,
   verification plan, risks, rollback, and current evidence accurate.
3. Use only the shared status model: `Blocked`, `Ready`, `In Progress`, `Verify`,
   and `Done`.
4. Update the work-item note before or with a Kanban move. Never move a card
   without updating its authoritative note.
5. Add dated entries to the work-item change log for every material status,
   scope, decision, implementation, external-state, or evidence change.
6. Update the relevant habitat notes when current behavior, target behavior,
   architecture, risks, tool access, or verified health changes.
7. Record meaningful daily progress in `NSFinance/Daily/YYYY-MM-DD.md`, using
   `NSFinance/Project Management/Templates/Daily Project Log.md` when a new log
   is needed.
8. Link new notes from a habitat map, board, work item, or related system note.
   Add reciprocal links where they improve navigation. Do not create orphan
   notes.
9. Preserve Obsidian-compatible Markdown, YAML frontmatter, Wikilinks, Mermaid,
   Kanban syntax, and existing graph conventions.

`Done` means the acceptance criteria have linked evidence. Code completion by
itself is not enough.

## Engineering Workflow

- Start by reading `git status` and relevant diffs. The working tree may contain
  intentional user or prior-agent changes; do not revert or overwrite them.
- This repository lives in OneDrive. Never perform an unbounded whole-file read
  against large or generated files in the synchronized tree. Prefer `git show`
  for tracked content, `git diff -- <path>` for changes, `Select-String` for
  targeted inspection, or short line windows such as `Get-Content -TotalCount`
  and `Select-Object -Skip/-First`.
- Give ordinary shell commands a hard 10-second timeout. Use
  `tools/Invoke-BoundedCommand.ps1` when a child command must enforce its own
  timeout and terminate its process tree. Builds and test suites may receive a
  longer explicit timeout, but poll them in short intervals and report progress
  at least every 30 seconds. Terminate and investigate any command that stops
  producing useful progress instead of waiting indefinitely.
- Never run raw `rg` against one known large OneDrive-backed file. Use a short
  line window, `git grep` against indexed content, or
  `tools/Invoke-BoundedCommand.ps1 -FilePath rg -TimeoutSeconds 10`. If any
  non-build command produces no useful output for 30 seconds, terminate it even
  when the outer tool call advertises a longer timeout.
- Run only one shell operation per tool call in this OneDrive workspace. Do not
  batch sequential file reads or inspections behind one awaited tool script; a
  later OneDrive or permission wait can otherwise hide completed output and
  prevent timely progress updates.
- Do not enumerate `node_modules` or run interactive scaffold/generator commands
  in this OneDrive workspace. Read known dependency files directly and create
  project-owned scaffolds with `apply_patch`.
- `tools/Invoke-BoundedCommand.ps1` must close child stdin and bound output-pipe
  draining as well as process execution. Treat any operation that genuinely
  requires an interactive answer as a user-owned step instead of leaving it
  waiting in the background.
- Keep edits scoped to the requested behavior and established ownership
  boundaries. Avoid unrelated refactors and generated-file churn.
- Use structured parsers and typed APIs for structured data.
- Treat database migrations, mobile/API contracts, authentication, transaction
  relationships, taxonomy identifiers, AI policies, and shared services as
  high-blast-radius changes requiring broader verification.
- Make financial calculations deterministic, currency-aware, time-zone-aware,
  and explicit about pending versus booked data.
- Preserve idempotency for provider ingestion, sync, reconciliation, retries,
  callbacks, and background processing.
- Do not silently weaken authentication, authorization, consent, rate limits,
  auditability, or safety policies to make a test pass.
- Do not claim a live integration works solely because configuration exists.
  Capture controlled end-to-end evidence.
- Do not claim a check passed unless it was run on the current relevant state.

## Verification Baseline

Install dependencies from the repository root when needed:

```powershell
pnpm install
```

Run the API locally against the approved production-connected configuration:

```powershell
dotnet run --project .\apps\api\src\NSFinance.Api\NSFinance.Api.csproj
```

Run the mobile app:

```powershell
pnpm --filter @nsfinance/mobile start
```

Use these focused quality checks as a baseline:

```powershell
dotnet test .\apps\api\NSFinance.Api.slnx
pnpm --filter @nsfinance/mobile typecheck
pnpm --filter @nsfinance/mobile lint
pnpm --filter @nsfinance/mobile test:node
```

The mobile `test:node` script runs all current `.node.test.ts` files through the
pinned `tsx` runner and its static-asset preload. Report its result separately
from type-check and lint.

Scale verification with risk:

- Narrow logic change: focused unit tests plus affected static checks.
- Shared API or data change: affected unit and integration tests, solution test,
  migration review, and contract checks.
- Auth, banking, AI, or categorization change: policy and integration coverage
  plus the relevant controlled user journey.
- Mobile UI change: type-check, lint, component or route checks where available,
  and real-device visual/interaction evidence at relevant screen sizes.
- Deployment change: pre-deploy gates, migration evidence, controlled production
  smoke checks, observability, and rollback evidence.

Consult `NSFinance/Developer Habitat/Testing And Current Health.md` for the
latest verified result and known failures. Re-run relevant checks instead of
treating that snapshot as permanent truth.

## External Tools And Access

The authoritative connection table is
`NSFinance/Developer Habitat/Access And Tool Connections.md`. Tool installation,
authentication, cloud state, database connectivity, and provider availability
are machine-specific and time-sensitive.

- Verify current access before reporting a blocker or success.
- Interactive authentication and sensitive permission grants remain user-owned.
- Use Azure CLI, GitHub CLI, EAS CLI, Postman/Newman, DBeaver, and `adb` only for
  the scope required by the active work item.
- Prefer schema and metadata inspection over personal data inspection.
- Keep redacted evidence in the relevant work item and update the connection
  table when status changes.

## Cross-Computer Continuity

- Codex local projects, tasks, memories, credentials, and app state do not
  automatically mirror between computers.
- Every computer must open this repository root as its own local Codex project.
- This `AGENTS.md` and the Obsidian vault provide durable shared context. A fresh
  task must follow the Mandatory Context Bootstrap rather than relying on chat
  memory.
- Ensure OneDrive has fully downloaded the Markdown notes needed for a task.
  Do not edit a placeholder or partially synchronized copy.
- Source code and documentation should converge through intentional Git or
  OneDrive synchronization. Avoid simultaneous edits to the same file on two
  computers and resolve sync conflicts before continuing.
- Dependencies, ignored secret files, CLI authentication, DBeaver connections,
  Postman secrets, device authorization, and other machine-local setup must be
  established separately and verified on each computer.
- Never copy or continuously synchronize the user-level `.codex` directory. It
  contains machine-specific authentication, installation IDs, absolute paths,
  task databases, and active state files.
- A task handoff or fork can carry conversation continuity between connected
  hosts, but the repository and Obsidian notes remain the long-term source of
  project context.

## Definition Of A Trustworthy Change

Before closing a material task, confirm that:

- The requested user or operational outcome is actually achieved.
- Production, privacy, financial-data, cost, and rollback risks were addressed.
- Relevant automated and manual checks were run and their results are reported
  honestly.
- Current code, contracts, migrations, and live behavior agree, or any mismatch
  is explicitly documented.
- The applicable work item, board, habitat notes, and daily log reflect the new
  state.
- Evidence is linked and contains no secrets or unnecessary personal data.
- Remaining limitations and next dependencies are clear.
