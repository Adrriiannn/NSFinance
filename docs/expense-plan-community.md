# Expense Plan Community System

## Purpose

The community plan browser is a distinct publishing layer on top of private plans.

Private plans remain user-owned planning objects.
Public publications are separate entities with their own moderation, reporting, visibility, and metric lifecycle.

## Public/private relationship

- `ExpensePlan` stays private and editable according to private plan lifecycle rules.
- `ExpensePlanPublication` is the public record linked to `SourcePlanId`.
- Publications store a frozen `PlanSnapshotJson` so the public version does not rely on the source plan continuing to mutate safely.
- Downloading a public plan creates a new private `ExpensePlan` with `ImportedFromPublicPlanId` set.
- Downloaded copies do not mutate the original source plan or the public publication.

## Publication states

Current publication lifecycle:

- `draft_publication`
- `pending_review`
- `published`
- `blocked`
- `unpublished`
- `flagged`
- `removed`

Visibility rule:

- only `published` publications are shown in the public browser

## Moderation model

Moderation is centralized in `ExpensePlanPublicationModerationService`.

Inputs scanned:

- public title
- public description
- tags

Outcomes:

- `approved`
- `blocked`
- `needs_review`
- `flagged_after_publish`

Triggers recorded in `ExpensePlanPublicationModerationEvent`:

- `pre_publish`
- `metadata_update`
- `rescan`
- `report_threshold`

Blocked content is prevented from becoming public.
Needs-review content is held in review or flagged if it was already public.

## Reporting model

Reports are stored in `ExpensePlanPublicationReport`.

Each report captures:

- publication id
- reporter user id
- reason
- optional note
- report status
- timestamps

Current reasons:

- spam
- abusive/offensive
- misleading
- inappropriate
- duplicate
- dangerous financial advice
- other

Threshold behavior:

- repeated reports can move a public plan into `flagged`
- a moderation event is written for threshold-based flagging

## Likes and downloads

Likes are stored per user in `ExpensePlanPublicationLike`.

Rules:

- one active like per publication per user
- toggling removes the previous like
- `LikeCount` is stored on the publication for fast ranking and display

Downloads/uses are stored in `ExpensePlanPublicationDownload`.

Rules:

- each use creates a new private plan instance
- `DownloadCount` is stored on the publication
- created plan id is preserved on the download record

## Ranking and sorting

The service supports:

- trending
- most liked
- most downloaded
- recently added
- newest

Trending score is currently based on:

- likes
- downloads
- recency boost
- report/flag penalties

## Creator dashboard

The creator dashboard is built from the creator's publication set and exposes:

- published count
- pending review count
- flagged count
- total likes
- total downloads
- total reports
- per-publication state and metrics

## API surface

Current community endpoints:

- `GET /api/expense-tracker/community`
- `GET /api/expense-tracker/community/mine`
- `GET /api/expense-tracker/community/{id}`
- `POST /api/expense-tracker/community/publish`
- `PUT /api/expense-tracker/community/{id}`
- `POST /api/expense-tracker/community/{id}/like`
- `POST /api/expense-tracker/community/{id}/use`
- `POST /api/expense-tracker/community/{id}/report`
- `POST /api/expense-tracker/community/{id}/unpublish`
- `POST /api/expense-tracker/community/{id}/rescan`

## Mobile shell

The mobile mini-app now includes:

- public browser page
- publish/edit-publication page
- public detail page
- report page
- creator dashboard page

The existing Share button routes into the publish flow instead of the native share sheet.

## Deferred follow-up

Not yet implemented:

- admin moderation console
- backend-to-mobile API client refactor for community pages
- followed creators
- category-emphasis community filters
- community comments or richer social graph
