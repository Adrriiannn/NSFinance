import { apiRequest } from "../../lib/api/client";
import type { AccountDto, CreateAccountRequest, UpdateAccountRequest } from "../../types/api";

export function getAccounts(): Promise<AccountDto[]> {
  return apiRequest<AccountDto[]>("/api/accounts");
}

export function getAccountById(accountId: string): Promise<AccountDto> {
  return apiRequest<AccountDto>(`/api/accounts/${accountId}`);
}

export function createAccount(payload: CreateAccountRequest): Promise<AccountDto> {
  return apiRequest<AccountDto>("/api/accounts", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function updateAccount(accountId: string, payload: UpdateAccountRequest): Promise<AccountDto> {
  return apiRequest<AccountDto>(`/api/accounts/${accountId}`, {
    method: "PUT",
    body: JSON.stringify(payload)
  });
}

export function deleteAccount(accountId: string): Promise<void> {
  return apiRequest<void>(`/api/accounts/${accountId}`, {
    method: "DELETE"
  });
}

