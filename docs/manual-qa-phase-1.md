# Phase 1 Manual QA Checklist

## Auth Happy Path
- Register new user with valid password.
- Login with same user.
- Confirm authenticated app shell loads.

## Wrong Password Behavior
- Attempt login with wrong password.
- Confirm generic invalid-credentials response.

## Duplicate Email Behavior
- Attempt second registration with same email.
- Confirm conflict-safe registration failure.

## Password Reset End-to-End
- Request forgot-password token.
- Use token in reset-password flow.
- Confirm old password fails and new password succeeds.
- Confirm reset token reuse fails.

## Legal Acceptance Tracking
- Accept current Terms/Privacy from account legal screen.
- Confirm acceptances list shows records with timestamps.

## Session Visibility and Revocation
- Open sessions/devices screen.
- Confirm current session is shown.
- Revoke another session and verify it disappears/marks revoked.

## Logout All Devices
- Login from multiple sessions.
- Trigger logout-all.
- Confirm other sessions are invalidated.

## Deletion and Export Scaffolding
- Submit deletion request.
- Submit export request.
- Confirm success responses and request IDs.

## Support Request
- Submit support ticket.
- Confirm request record is created and visible in account support history.
