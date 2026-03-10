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
  getMyDeletionRequests,
  getMyExportRequests,
  getMySupportRequests
} from "./supportApi";

const supportKeys = {
  myRequests: ["support", "my-requests"] as const,
  exportRequests: ["support", "export-requests"] as const,
  deletionRequests: ["support", "deletion-requests"] as const
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
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateDeletionRequestRequest) => createDeletionRequest(payload),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: supportKeys.deletionRequests });
    }
  });
}

export function useCreateExportRequestMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateExportRequestRequest) => createExportRequest(payload),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: supportKeys.exportRequests });
    }
  });
}

export function useMyExportRequestsQuery() {
  return useQuery({
    queryKey: supportKeys.exportRequests,
    queryFn: getMyExportRequests
  });
}

export function useMyDeletionRequestsQuery() {
  return useQuery({
    queryKey: supportKeys.deletionRequests,
    queryFn: getMyDeletionRequests
  });
}
