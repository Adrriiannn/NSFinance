import type { TransactionPageRequest } from "../../types/api";

export const queryKeys = {
  auth: {
    me: ["auth", "me"] as const
  },
  dashboard: {
    summary: ["dashboard", "summary"] as const
  },
  accounts: {
    all: ["accounts"] as const,
    detail: (id: string) => ["accounts", "detail", id] as const,
    transactions: (id: string) => ["accounts", "transactions", id] as const
  },
  banking: {
    connections: ["banking", "connections"] as const,
    connection: (id: string) => ["banking", "connections", id] as const,
    connectedBanks: ["banking", "connected-banks"] as const,
    enrichmentProgress: ["banking", "enrichment-progress"] as const,
    accounts: ["banking", "accounts"] as const,
    cards: ["banking", "cards"] as const,
    recurringPayments: ["banking", "recurring-payments"] as const,
    commitments: ["banking", "commitments"] as const,
    recurringPaymentsByAccount: (accountId: string) => ["banking", "recurring-payments", accountId] as const
  },
  expenseTracker: {
    root: ["expense-tracker"] as const,
    taxonomy: ["expense-tracker", "taxonomy"] as const,
    entries: ["expense-tracker", "entries"] as const,
    detail: (id: string) => ["expense-tracker", "entries", id] as const
  },
  transactions: {
    all: ["transactions"] as const,
    detail: (id: string) => ["transactions", "detail", id] as const,
    list: (accountId?: string) =>
      accountId ? (["transactions", "list", accountId] as const) : (["transactions", "list"] as const),
    page: (request: TransactionPageRequest = {}) =>
      [
        "transactions",
        "page",
        {
          pageSize: request.pageSize ?? null,
          cursor: request.cursor ?? null,
          accountId: request.accountId ?? null,
          fromUtc: request.fromUtc ?? null,
          toUtc: request.toUtc ?? null,
          direction: request.direction ?? null
        }
      ] as const,
    pages: (request: Omit<TransactionPageRequest, "cursor"> = {}) =>
      [
        "transactions",
        "pages",
        {
          pageSize: request.pageSize ?? null,
          accountId: request.accountId ?? null,
          fromUtc: request.fromUtc ?? null,
          toUtc: request.toUtc ?? null,
          direction: request.direction ?? null
        }
      ] as const
  },
  statementImports: {
    all: ["statement-imports"] as const,
    batch: (batchId: string) => ["statement-imports", "batch", batchId] as const,
    rows: (batchId: string) => ["statement-imports", "rows", batchId] as const
  },
  categories: {
    all: ["categories"] as const
  }
} as const;

