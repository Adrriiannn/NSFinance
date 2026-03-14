import { apiRequest } from "../../lib/api/client";
import type {
  BankConnectionDto,
  ConnectedBanksOverviewDto,
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

export function getBankConnection(connectionId: string): Promise<BankConnectionDto> {
  return apiRequest<BankConnectionDto>(`/api/banking/connections/${connectionId}`);
}

export function getLinkedBankAccounts(): Promise<LinkedBankAccountDto[]> {
  return apiRequest<LinkedBankAccountDto[]>("/api/banking/accounts");
}

export function startTrueLayerLink(payload: StartTrueLayerLinkRequest): Promise<StartTrueLayerLinkResponse> {
  return apiRequest<StartTrueLayerLinkResponse>("/api/banking/truelayer/link", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function syncBankConnection(connectionId: string): Promise<SyncConnectionResponse> {
  return apiRequest<SyncConnectionResponse>(`/api/banking/connections/${connectionId}/sync`, {
    method: "POST"
  });
}

export function disconnectBankConnection(connectionId: string): Promise<void> {
  return apiRequest<void>(`/api/banking/connections/${connectionId}/disconnect`, {
    method: "POST"
  });
}
