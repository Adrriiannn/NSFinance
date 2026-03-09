import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type {
  CreateDeletionRequestRequest,
  CreateExportRequestRequest,
  CreateSupportRequestRequest
} from "../../types/api";
import {
  createDeletionRequest,
  createExportRequest,
  createSupportRequest,
  getMySupportRequests
} from "./supportApi";

const supportKeys = {
  myRequests: ["support", "my-requests"] as const
};

export function useMySupportRequestsQuery() {
  return useQuery({
    queryKey: supportKeys.myRequests,
    queryFn: getMySupportRequests
  });
}

export function useCreateSupportRequestMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateSupportRequestRequest) => createSupportRequest(payload),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: supportKeys.myRequests });
    }
  });
}

export function useCreateDeletionRequestMutation() {
  return useMutation({
    mutationFn: (payload: CreateDeletionRequestRequest) => createDeletionRequest(payload)
  });
}

export function useCreateExportRequestMutation() {
  return useMutation({
    mutationFn: (payload: CreateExportRequestRequest) => createExportRequest(payload)
  });
}
