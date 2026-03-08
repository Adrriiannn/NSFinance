export type AccountType = "Current" | "Savings" | "Credit" | "Cash" | "Other";
export type TransactionDirection = "Income" | "Expense";

export type AccountDto = {
  id: string;
  name: string;
  type: AccountType;
  currency: string;
  currentBalance: number;
  transactionCount: number;
  createdUtc: string;
};

export type CreateAccountRequest = {
  name: string;
  type: AccountType;
  currency: string;
  openingBalance?: number | null;
};

export type TransactionDto = {
  id: string;
  accountId: string;
  accountName: string;
  description: string;
  amount: number;
  currency: string;
  categoryId: string | null;
  categoryName: string | null;
  bookedAtUtc: string;
  createdUtc: string;
  direction: TransactionDirection;
};

export type CreateTransactionRequest = {
  accountId: string;
  description: string;
  amount: number;
  direction: TransactionDirection;
  currency?: string | null;
  categoryId?: string | null;
  bookedAtUtc?: string | null;
};

export type CategoryDto = {
  id: string;
  name: string;
  type: string;
  createdUtc: string;
};

export type DashboardSummaryDto = {
  totalBalance: number;
  accountCount: number;
  transactionCount: number;
  recentOutflow: number;
  accountPreview: AccountDto[];
  recentTransactions: TransactionDto[];
};

export type ValidationProblem = {
  title?: string;
  status?: number;
  errors?: Record<string, string[]>;
  message?: string;
};

export type ApiErrorResponse = {
  message?: string;
  code?: string;
};

export type UserProfileDto = {
  id: string;
  email: string;
  firstName: string | null;
  lastName: string | null;
  createdUtc: string;
  lastLoginUtc: string | null;
};

export type AuthTokenResponse = {
  accessToken: string;
  expiresAtUtc: string;
  user: UserProfileDto;
};

export type RegisterRequest = {
  email: string;
  password: string;
  firstName?: string | null;
  lastName?: string | null;
};

export type LoginRequest = {
  email: string;
  password: string;
};
