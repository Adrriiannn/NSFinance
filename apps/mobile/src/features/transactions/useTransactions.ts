import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { nearLiveFinanceQueryOptions } from "../../lib/api/liveQueryOptions";
import { queryKeys } from "../../lib/api/queryKeys";
import type {
  DashboardSummaryDto,
  TransactionDto,
  UpdateTransactionMetadataRequest
} from "../../types/api";
import {
  getTransactionById,
  getTransactions,
  getTransactionsForAccount,
  updateTransactionMetadata
} from "./transactionsApi";

export function useTransactionsQuery(accountId?: string) {
  return useQuery({
    queryKey: queryKeys.transactions.list(accountId),
    queryFn: () => getTransactions(accountId),
    ...nearLiveFinanceQueryOptions
  });
}

export function useAccountTransactionsQuery(accountId: string) {
  return useQuery({
    queryKey: queryKeys.accounts.transactions(accountId),
    queryFn: () => getTransactionsForAccount(accountId),
    enabled: Boolean(accountId),
    ...nearLiveFinanceQueryOptions
  });
}

export function useTransactionDetailQuery(transactionId: string) {
  return useQuery({
    queryKey: queryKeys.transactions.detail(transactionId),
    queryFn: () => getTransactionById(transactionId),
    enabled: Boolean(transactionId)
  });
}

function replaceTransaction(list: TransactionDto[] | undefined, transaction: TransactionDto) {
  const existing = list ?? [];
  const index = existing.findIndex((item) => item.id === transaction.id);
  if (index < 0) {
    return existing;
  }

  const next = [...existing];
  next[index] = transaction;
  return next;
}

function applyTransactionCacheUpdate(
  queryClient: ReturnType<typeof useQueryClient>,
  transaction: TransactionDto
) {
  queryClient.setQueryData(queryKeys.transactions.detail(transaction.id), transaction);
  queryClient.setQueryData<TransactionDto[]>(
    queryKeys.transactions.list(),
    (current) => replaceTransaction(current, transaction)
  );
  queryClient.setQueryData<TransactionDto[]>(
    queryKeys.transactions.list(transaction.accountId),
    (current) => replaceTransaction(current, transaction)
  );
  queryClient.setQueryData<TransactionDto[]>(
    queryKeys.accounts.transactions(transaction.accountId),
    (current) => replaceTransaction(current, transaction)
  );
  queryClient.setQueryData<DashboardSummaryDto | undefined>(
    queryKeys.dashboard.summary,
    (current) =>
      current
        ? {
            ...current,
            recentTransactions: replaceTransaction(current.recentTransactions, transaction)
          }
        : current
  );
}

export function useUpdateTransactionMetadataMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      transactionId,
      payload
    }: {
      transactionId: string;
      payload: UpdateTransactionMetadataRequest;
    }) => updateTransactionMetadata(transactionId, payload),
    onSuccess: async (transaction) => {
      applyTransactionCacheUpdate(queryClient, transaction);

      let linkedCounterpartId: string | null = null;
      if (
        transaction.deterministicClassificationStatus === "classified_matched_rule"
        && transaction.deterministicRelationshipType === "internal_transfer"
        && transaction.deterministicLinkedTransactionId
      ) {
        linkedCounterpartId = transaction.deterministicLinkedTransactionId;
        try {
          const counterpart = await getTransactionById(linkedCounterpartId);
          applyTransactionCacheUpdate(queryClient, counterpart);
          console.info("[Transactions]", {
            event: "metadata_sync_linked_counterpart",
            transactionId: transaction.id,
            counterpartId: counterpart.id
          });
        } catch (error) {
          console.warn("[Transactions]", {
            event: "metadata_sync_linked_counterpart_failed",
            transactionId: transaction.id,
            counterpartId: linkedCounterpartId,
            message: error instanceof Error ? error.message : "Unknown error"
          });
        }
      }

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
        queryClient.invalidateQueries({ queryKey: ["accounts", "transactions"] }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary }),
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.detail(transaction.id) }),
        linkedCounterpartId
          ? queryClient.invalidateQueries({ queryKey: queryKeys.transactions.detail(linkedCounterpartId) })
          : Promise.resolve()
      ]);
    }
  });
}
