import { useMutation, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "../../lib/api/queryKeys";
import type {
  ReviewStatementImportRowsRequest,
  StatementImportMappingRequest
} from "../../types/api";
import {
  commitStatementImport,
  discardStatementImport,
  inspectStatementCsv,
  previewStatementCsv,
  reviewStatementImportRows,
  type StatementDocumentAsset,
  undoStatementImport
} from "./statementImportsApi";

export function useInspectStatementMutation() {
  return useMutation({
    mutationFn: ({ asset, delimiter }: { asset: StatementDocumentAsset; delimiter?: string | null }) =>
      inspectStatementCsv(asset, delimiter)
  });
}

export function usePreviewStatementMutation() {
  return useMutation({
    mutationFn: ({
      asset,
      mapping
    }: {
      asset: StatementDocumentAsset;
      mapping: StatementImportMappingRequest;
    }) => previewStatementCsv(asset, mapping)
  });
}

export function useReviewStatementImportMutation() {
  return useMutation({
    mutationFn: ({
      batchId,
      request
    }: {
      batchId: string;
      request: ReviewStatementImportRowsRequest;
    }) => reviewStatementImportRows(batchId, request)
  });
}

export function useStatementImportLifecycleMutations() {
  const queryClient = useQueryClient();

  const refreshFinanceData = async (accountId: string) => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
      queryClient.invalidateQueries({ queryKey: queryKeys.accounts.detail(accountId) }),
      queryClient.invalidateQueries({ queryKey: queryKeys.accounts.transactions(accountId) }),
      queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
      queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary })
    ]);
  };

  const commitMutation = useMutation({
    mutationFn: ({ batchId, revision }: { batchId: string; revision: number }) =>
      commitStatementImport(batchId, revision),
    onSuccess: async (_, variables) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.statementImports.batch(variables.batchId) });
    }
  });
  const discardMutation = useMutation({
    mutationFn: ({ batchId, revision }: { batchId: string; revision: number }) =>
      discardStatementImport(batchId, revision)
  });
  const undoMutation = useMutation({
    mutationFn: ({ batchId, revision }: { batchId: string; revision: number }) =>
      undoStatementImport(batchId, revision)
  });

  return {
    commitMutation,
    discardMutation,
    undoMutation,
    refreshFinanceData
  };
}
