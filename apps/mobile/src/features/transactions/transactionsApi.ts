import { apiRequest } from "../../lib/api/client";
import type { CreateTransactionRequest, TransactionDto } from "../../types/api";

export function getTransactions(accountId?: string): Promise<TransactionDto[]> {
  const suffix = accountId ? `?accountId=${encodeURIComponent(accountId)}` : "";
  return apiRequest<TransactionDto[]>(`/api/transactions${suffix}`);
}

export function getTransactionById(transactionId: string): Promise<TransactionDto> {
  return apiRequest<TransactionDto>(`/api/transactions/${transactionId}`);
}

export function getTransactionsForAccount(accountId: string): Promise<TransactionDto[]> {
  return apiRequest<TransactionDto[]>(`/api/accounts/${accountId}/transactions`);
}

export function createTransaction(
  payload: CreateTransactionRequest
): Promise<TransactionDto> {
  return apiRequest<TransactionDto>("/api/transactions", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

