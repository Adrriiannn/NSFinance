import { apiRequest } from "../../lib/api/client";
import type {
  TransactionDto,
  TransactionPageDto,
  TransactionPageRequest,
  UpdateTransactionMetadataRequest
} from "../../types/api";

const transactionPagePath = "/api/transactions/page";

function appendPageParameter(
  searchParams: URLSearchParams,
  name: string,
  value: string | number | null | undefined
) {
  if (value !== null && value !== undefined) {
    searchParams.append(name, String(value));
  }
}

export function buildTransactionPagePath(request: TransactionPageRequest = {}): string {
  const searchParams = new URLSearchParams();

  appendPageParameter(searchParams, "pageSize", request.pageSize);
  appendPageParameter(searchParams, "cursor", request.cursor);
  appendPageParameter(searchParams, "accountId", request.accountId);
  appendPageParameter(searchParams, "fromUtc", request.fromUtc);
  appendPageParameter(searchParams, "toUtc", request.toUtc);
  appendPageParameter(searchParams, "direction", request.direction);

  const query = searchParams.toString();
  return query ? `${transactionPagePath}?${query}` : transactionPagePath;
}

export function getTransactionPage(
  request: TransactionPageRequest = {}
): Promise<TransactionPageDto> {
  return apiRequest<TransactionPageDto>(buildTransactionPagePath(request));
}

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

export function updateTransactionMetadata(
  transactionId: string,
  payload: UpdateTransactionMetadataRequest
): Promise<TransactionDto> {
  return apiRequest<TransactionDto>(`/api/transactions/${transactionId}`, {
    method: "PATCH",
    body: JSON.stringify(payload)
  });
}

