# Manual QA Checklist: Phase 2 TrueLayer Sandbox

## Preconditions
1. Set backend env vars:
   - `TRUELAYER_CLIENT_ID`
   - `TRUELAYER_CLIENT_SECRET`
   - `TRUELAYER_REDIRECT_URI=http://localhost:5080/api/banking/truelayer/callback`
   - `TRUELAYER_ENVIRONMENT=sandbox`
   - `TRUELAYER_AUTH_BASE_URL=https://auth.truelayer-sandbox.com`
   - `TRUELAYER_API_BASE_URL=https://api.truelayer-sandbox.com`
2. Ensure `http://localhost:5080/api/banking/truelayer/callback` is registered in the TrueLayer sandbox console.
3. Run API migrations and start API.
4. Log into mobile app with a test user.

## Connect Flow (Happy Path)
1. Open Accounts tab.
2. Tap `Connect bank` (opens add-account modal).
3. Tap `Connect bank` in modal.
4. Verify browser opens TrueLayer hosted auth flow.
5. Complete sandbox consent.
6. Verify callback page shows successful completion message.
7. Return to app.
8. Tap `I completed consent, refresh`.
9. Verify status shows `Connected`.
10. Tap `Sync now`.
11. Verify accounts screen shows imported linked account(s) and activity.

## Data Validation
1. Call `GET /api/banking/connections` (authenticated):
   - verify connection status is `synced` after sync
2. Call `GET /api/banking/accounts`:
   - verify linked account exists
3. Call `GET /api/banking/accounts/{accountId}/balances`:
   - verify latest snapshot exists
4. Call `GET /api/banking/accounts/{accountId}/transactions?page=1&pageSize=50`:
   - verify imported raw transactions exist

## Negative Cases
1. Reuse/invalid authorization code:
   - verify callback returns safe error message
   - verify connection moves to `reauth_required`
2. Invalid TrueLayer credentials:
   - verify link/callback returns actionable config error
3. Environment mismatch (`sandbox` env with live URLs or reverse):
   - verify operation fails with mismatch guidance

## Production Reference
1. Production redirect URI must be `https://api.finance.nsireland.ie/api/banking/truelayer/callback`.
2. TrueLayer production console registration must exactly match that URI.

## Disconnect
1. Call `POST /api/banking/connections/{connectionId}/disconnect`.
2. Verify response is success (`204`).
3. Verify connection status is `revoked`.
4. Verify refresh token is cleared/revoked locally.
