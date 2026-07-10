# Manual QA

## Auth And Trust Baseline

1. Register with a valid password.
2. Login and confirm authenticated shell loads.
3. Login with the wrong password and confirm invalid-credentials behavior.
4. Register a duplicate email and confirm conflict-safe failure.
5. Request password reset and confirm the code/link arrives through the configured delivery path.
6. Reset password and confirm old password fails, new password succeeds, and code reuse fails.
7. Accept terms/privacy and confirm acceptance records exist.
8. Review sessions and revoke a non-current session.
9. Logout all sessions and verify invalidation.
10. Submit deletion, export, and support requests.

## TrueLayer Live Banking

Preconditions:

- API is configured with live TrueLayer credentials.
- `TrueLayer:RedirectUri=https://api.finance.nsireland.ie/api/banking/truelayer/callback`.
- `TrueLayer:Environment=live`.
- `TrueLayer:AuthBaseUrl=https://auth.truelayer.com`.
- `TrueLayer:ApiBaseUrl=https://api.truelayer.com`.
- Callback URI is registered in the TrueLayer console.

Happy path:

1. Start connect flow from mobile Accounts.
2. Complete provider consent with a real supported card/account.
3. Return to the app through `nsfinance://accounts/connect-bank?...`.
4. Verify connection reaches connected/synced state.
5. Trigger sync and verify imported account activity.
6. Confirm returned authorization URL contains `providers=ie-ob-all` and `country_id=IE`.

Data checks:

- `GET /api/banking/connections`
- `GET /api/banking/accounts`
- `GET /api/banking/cards`
- `GET /api/banking/accounts/{accountId}/balances`
- `GET /api/banking/accounts/{accountId}/transactions?page=1&pageSize=50`
- `GET /api/banking/recurring-payments`
- `GET /api/banking/accounts/{accountId}/recurring-payments`

Expanded scope checks:

- `info`
- `accounts`
- `cards`
- `balance`
- `transactions`
- `offline_access`
- `direct_debits`
- `standing_orders`

Continuity checks:

1. Simulate token expiry/revoke and confirm imported transactions remain visible.
2. Reconfirm from the app flow and ensure the existing `connectionId` is reused.
3. Confirm status returns to connected/synced without duplicate imported rows.

Disconnect checks:

1. `POST /api/banking/connections/{connectionId}/disconnect`.
2. Verify status transitions to `disconnect_pending`.
3. Verify final transition to `revoked` after background cleanup.
4. Retry disconnect while pending and confirm idempotent behavior.

## Activity And Classification

Linked transfer coherence:

1. Open a transaction that is part of a verified internal transfer pair.
2. Change category/subcategory within the Transfers taxonomy and save.
3. Open the linked counterpart and verify coherent transfer taxonomy.
4. Confirm normal merchant transactions do not render a linked transaction section.

Savings movement coherence:

1. Trigger a merchant plus spare-change scenario.
2. Verify both rows remain visible in Activity.
3. Verify spare-change row is classified as savings movement and neutralized from income/expense totals.
4. Verify manual savings deposits and withdrawals remain visible and globally neutralized.

## Mobile Freshness UX

1. Open Accounts and Activity tabs and verify the global sync icon exists.
2. Tap sync and verify success/info feedback.
3. Tap again during cooldown and verify remaining cooldown is shown.
4. Keep app foregrounded for the auto-sync cadence window and confirm backend receives `trigger=auto`.
5. Background and resume after a long interval; verify sync runs when due.
6. Login after a long idle period and verify post-login sync starts when due.

## Timestamp And Projection Checks

1. Confirm `RawBankTransactions` stores timestamp provenance fields.
2. Confirm `NormalizedBankTransactions` rows are created or updated with matching provenance.
3. Confirm pending rows remain raw-only until a booked counterpart arrives.
4. Confirm booked rows project once without duplicate ledger creation.
5. Confirm same-timestamp distinct rows remain separate when provider payloads represent separate movements.
