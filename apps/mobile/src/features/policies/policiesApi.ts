import { apiRequest } from "../../lib/api/client";
import type {
  AcceptPolicyRequest,
  ConsentRecordDto,
  PolicyAcceptanceDto,
  PolicyVersionDto,
  UpdateConsentRequest
} from "../../types/api";

export function getActivePolicies(): Promise<PolicyVersionDto[]> {
  return apiRequest<PolicyVersionDto[]>("/api/policies/active");
}

export function getTermsPolicy(): Promise<PolicyVersionDto> {
  return apiRequest<PolicyVersionDto>("/api/legal/terms");
}

export function getPrivacyPolicy(): Promise<PolicyVersionDto> {
  return apiRequest<PolicyVersionDto>("/api/legal/privacy");
}

export function getAiLimitationsPolicy(): Promise<PolicyVersionDto> {
  return apiRequest<PolicyVersionDto>("/api/legal/ai-limitations");
}

export function getOpenBankingDisclosurePolicy(): Promise<PolicyVersionDto> {
  return apiRequest<PolicyVersionDto>("/api/legal/open-banking");
}

export function getAiDisclosurePolicy(): Promise<PolicyVersionDto> {
  return apiRequest<PolicyVersionDto>("/api/legal/ai-disclosure");
}

export function getDataRightsPolicy(): Promise<PolicyVersionDto> {
  return apiRequest<PolicyVersionDto>("/api/legal/data-rights");
}

export function getPolicyAcceptances(): Promise<PolicyAcceptanceDto[]> {
  return apiRequest<PolicyAcceptanceDto[]>("/api/policies/acceptances");
}

export function acceptPolicy(payload: AcceptPolicyRequest): Promise<PolicyAcceptanceDto> {
  return apiRequest<PolicyAcceptanceDto>("/api/policies/accept", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function getConsents(): Promise<ConsentRecordDto[]> {
  return apiRequest<ConsentRecordDto[]>("/api/policies/consents");
}

export function updateConsent(payload: UpdateConsentRequest): Promise<ConsentRecordDto> {
  return apiRequest<ConsentRecordDto>("/api/policies/consents", {
    method: "PUT",
    body: JSON.stringify(payload)
  });
}
