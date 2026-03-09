import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { AcceptPolicyRequest, UpdateConsentRequest } from "../../types/api";
import {
  acceptPolicy,
  getAiLimitationsPolicy,
  getConsents,
  getPolicyAcceptances,
  getPrivacyPolicy,
  getTermsPolicy,
  updateConsent
} from "./policiesApi";

const policyKeys = {
  terms: ["policies", "terms"] as const,
  privacy: ["policies", "privacy"] as const,
  ai: ["policies", "ai"] as const,
  acceptances: ["policies", "acceptances"] as const,
  consents: ["policies", "consents"] as const
};

export function useTermsPolicyQuery() {
  return useQuery({
    queryKey: policyKeys.terms,
    queryFn: getTermsPolicy
  });
}

export function usePrivacyPolicyQuery() {
  return useQuery({
    queryKey: policyKeys.privacy,
    queryFn: getPrivacyPolicy
  });
}

export function useAiLimitationsPolicyQuery() {
  return useQuery({
    queryKey: policyKeys.ai,
    queryFn: getAiLimitationsPolicy
  });
}

export function usePolicyAcceptancesQuery() {
  return useQuery({
    queryKey: policyKeys.acceptances,
    queryFn: getPolicyAcceptances
  });
}

export function useAcceptPolicyMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: AcceptPolicyRequest) => acceptPolicy(payload),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: policyKeys.acceptances });
    }
  });
}

export function useConsentsQuery() {
  return useQuery({
    queryKey: policyKeys.consents,
    queryFn: getConsents
  });
}

export function useUpdateConsentMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: UpdateConsentRequest) => updateConsent(payload),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: policyKeys.consents });
    }
  });
}
