import { apiRequest } from "../../lib/api/client";
import type {
  BankRecurringPaymentsDto,
  BankConnectionDto,
  BankConnectionAttemptStatusDto,
  ConnectedBanksOverviewDto,
  BankEnrichmentProgressDto,
  GlobalBankSyncRequest,
  GlobalBankSyncResponse,
  LinkedBankCardDto,
  LinkedBankAccountDto,
  StartTrueLayerLinkRequest,
  StartTrueLayerLinkResponse,
  SyncConnectionResponse
} from "../../types/api";

export function getBankConnections(): Promise<BankConnectionDto[]> {
  return apiRequest<BankConnectionDto[]>("/api/banking/connections");
}

export function getConnectedBanks(): Promise<ConnectedBanksOverviewDto> {
  return apiRequest<ConnectedBanksOverviewDto>("/api/banking/connected-banks");
}

export function getBankEnrichmentProgress(): Promise<BankEnrichmentProgressDto> {
  return apiRequest<BankEnrichmentProgressDto>("/api/banking/enrichment-progress");
}

export function getBankConnection(connectionId: string): Promise<BankConnectionDto> {
  return apiRequest<BankConnectionDto>(`/api/banking/connections/${connectionId}`);
}

export function getLinkedBankAccounts(): Promise<LinkedBankAccountDto[]> {
  return apiRequest<LinkedBankAccountDto[]>("/api/banking/accounts");
}

export function getLinkedBankCards(): Promise<LinkedBankCardDto[]> {
  return apiRequest<LinkedBankCardDto[]>("/api/banking/cards");
}

export function getRecurringPayments(): Promise<BankRecurringPaymentsDto> {
  return apiRequest<BankRecurringPaymentsDto>("/api/banking/recurring-payments");
}

export function getRecurringPaymentsForAccount(accountId: string): Promise<BankRecurringPaymentsDto> {
  return apiRequest<BankRecurringPaymentsDto>(`/api/banking/accounts/${accountId}/recurring-payments`);
}

export function startTrueLayerLink(payload: StartTrueLayerLinkRequest): Promise<StartTrueLayerLinkResponse> {
  return apiRequest<StartTrueLayerLinkResponse>("/api/banking/truelayer/link", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function confirmBankConnectionAttemptReturn(
  attemptId: string
): Promise<BankConnectionAttemptStatusDto> {
  return apiRequest<BankConnectionAttemptStatusDto>(
    `/api/banking/attempts/${attemptId}/app-return-confirmed`,
    { method: "POST" }
  );
}

export function syncBankConnection(connectionId: string): Promise<SyncConnectionResponse> {
  return apiRequest<SyncConnectionResponse>(`/api/banking/connections/${connectionId}/sync`, {
    method: "POST"
  });
}

export function syncAllBankConnections(payload?: GlobalBankSyncRequest): Promise<GlobalBankSyncResponse> {
  return apiRequest<GlobalBankSyncResponse>("/api/banking/sync", {
    method: "POST",
    body: JSON.stringify(payload ?? {})
  });
}

export function disconnectBankConnection(connectionId: string): Promise<void> {
  return apiRequest<void>(`/api/banking/connections/${connectionId}/disconnect`, {
    method: "POST"
  });
}
