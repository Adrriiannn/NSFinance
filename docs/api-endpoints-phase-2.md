# Phase 2 Banking API Endpoints

## TrueLayer Link and Callback
- `POST /api/banking/truelayer/link` (auth required)
  - starts connection and returns hosted authorization URL
- `GET /api/banking/truelayer/callback` (provider callback)
  - validates callback query
  - exchanges code for tokens
  - runs initial sync
  - returns safe HTML result

## Connection and Data Endpoints
- `GET /api/banking/connections` (auth required)
- `GET /api/banking/accounts` (auth required)
- `GET /api/banking/accounts/{accountId}/balances` (auth required)
- `GET /api/banking/accounts/{accountId}/transactions?page=1&pageSize=50` (auth required)
- `POST /api/banking/connections/{connectionId}/sync` (auth required)
- `POST /api/banking/connections/{connectionId}/disconnect` (auth required)
