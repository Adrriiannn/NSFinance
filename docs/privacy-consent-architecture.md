# Phase 1 Privacy, Consent, and Rights Architecture

## Policy Versioning
- `PolicyDocuments` stores policy type identity (`terms_of_service`, `privacy_policy`, `ai_limitations_notice`, placeholders).
- `PolicyVersions` stores version string, effective date, content reference, and active state.
- Seeder initializes baseline policy documents/versions for local/dev startup.

## Policy Acceptance Tracking
- `PolicyAcceptances` records:
  - user
  - policy type
  - policy version
  - accepted timestamp
  - acceptance context
  - platform/app version metadata
- API supports accept and retrieval of acceptance history.
- Accept events are audited as legal events.

## Consent Model
- `ConsentRecords` tracks ongoing consent states (`granted`/`revoked`/`denied`) per consent type.
- Includes source, timestamps, and optional metadata JSON.
- API supports list/update for user-facing privacy preference controls.

## Data Rights Scaffolding
- `DeletionRequests` and `ExportRequests` create workflow records (no hard-delete/export artifact processing in Phase 1).
- `Users.DeletionRequested` and `Users.DeletionRequestedUtc` are set when deletion is requested.
- All rights requests are audited.

## Support Scaffolding
- `SupportRequests` supports user-linked or anonymous support submissions.
- Status-tracked records are created via API for support operations.

## Audit and Correlation
- Trust actions are logged via `AuditService` into `AuditEvents`.
- Correlation IDs are attached from middleware to audit entries and response headers.
