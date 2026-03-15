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
    accounts: ["banking", "accounts"] as const
  },
  expenseTracker: {
    root: ["expense-tracker"] as const,
    taxonomy: ["expense-tracker", "taxonomy"] as const,
    entries: ["expense-tracker", "entries"] as const,
    detail: (id: string) => ["expense-tracker", "entries", id] as const
  },
  transactions: {
    all: ["transactions"] as const,
    list: (accountId?: string) =>
      accountId ? (["transactions", "list", accountId] as const) : (["transactions", "list"] as const)
  },
  categories: {
    all: ["categories"] as const
  }
} as const;

