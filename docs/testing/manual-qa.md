# Manual QA

## Auth and trust baseline

1. Register with valid password.
2. Login and confirm authenticated shell loads.
3. Login with wrong password and confirm generic invalid-credentials behavior.
4. Register duplicate email and confirm conflict-safe failure.
5. Forgot/reset password end-to-end:
   - old password fails, new password succeeds
   - reset token reuse fails
6. Accept terms/privacy and confirm acceptance records exist.
7. Review sessions and revoke non-current session.
8. Logout all sessions and verify invalidation.
9. Submit deletion and export requests.
10. Submit support request and verify persisted record.

## TrueLayer sandbox

Preconditions:

- `apps/api/src/NSFinance.Api/appsettings.Local.json` configured with:
  - `TrueLayer:ClientId`
  - `TrueLayer:ClientSecret`
  - `TrueLayer:RedirectUri=http://localhost:5080/api/banking/truelayer/callback`
  - `TrueLayer:Environment=sandbox`
  - `TrueLayer:AuthBaseUrl=https://auth.truelayer-sandbox.com`
  - `TrueLayer:ApiBaseUrl=https://api.truelayer-sandbox.com`
- callback URI registered in TrueLayer sandbox

Happy path:

1. Start connect flow from mobile Accounts.
2. Complete provider consent.
3. Return and refresh status.
4. Verify connection reaches `Connected`/`synced`.
5. Trigger sync and verify imported account activity.
6. Validate callback return route handling:
   - preferred route: `nsfinance://accounts/connect-bank?...`
   - legacy compatibility route: `nsfinance://modals/add-account?...`

Data checks (authenticated API):

- `GET /api/banking/connections`
- `GET /api/banking/accounts`
- `GET /api/banking/accounts/{accountId}/balances`
- `GET /api/banking/accounts/{accountId}/transactions?page=1&pageSize=50`

Negative checks:

- invalid/reused auth code handling
- invalid credentials config error behavior
- sandbox/live environment mismatch rejection behavior

Disconnect:

1. `POST /api/banking/connections/{connectionId}/disconnect`
2. Verify revoked status and token cleanup behavior
