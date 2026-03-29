import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { nearLiveFinanceQueryOptions } from "../../lib/api/liveQueryOptions";
import { queryKeys } from "../../lib/api/queryKeys";
import type {
  AccountDto,
  CreateTransactionRequest,
  DashboardSummaryDto,
  TransactionDto,
  UpdateTransactionMetadataRequest
} from "../../types/api";
import {
  createTransaction,
  getTransactionById,
  getTransactions,
  getTransactionsForAccount,
  updateTransactionMetadata
} from "./transactionsApi";
import { isReportableExpenseTransaction } from "./transferClassification";

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

function prependTransaction(list: TransactionDto[] | undefined, transaction: TransactionDto) {
  const existing = list ?? [];
  if (existing.some((item) => item.id === transaction.id)) {
    return existing;
  }

  return [transaction, ...existing];
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

export function useCreateTransactionMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: CreateTransactionRequest) => createTransaction(payload),
    onSuccess: async (transaction) => {
      queryClient.setQueryData<TransactionDto[]>(
        queryKeys.transactions.list(),
        (current) => prependTransaction(current, transaction)
      );
      queryClient.setQueryData<TransactionDto[]>(
        queryKeys.accounts.transactions(transaction.accountId),
        (current) => prependTransaction(current, transaction)
      );
      queryClient.setQueryData<TransactionDto[]>(
        queryKeys.transactions.list(transaction.accountId),
        (current) => prependTransaction(current, transaction)
      );

      queryClient.setQueryData<AccountDto | undefined>(
        queryKeys.accounts.detail(transaction.accountId),
        (current) =>
          current
            ? {
                ...current,
                currentBalance: Number((current.currentBalance + transaction.amount).toFixed(2)),
                transactionCount: current.transactionCount + 1
              }
            : current
      );

      queryClient.setQueryData<AccountDto[] | undefined>(queryKeys.accounts.all, (current) =>
        (current ?? []).map((account) =>
          account.id === transaction.accountId
            ? {
                ...account,
                currentBalance: Number((account.currentBalance + transaction.amount).toFixed(2)),
                transactionCount: account.transactionCount + 1
              }
            : account
        )
      );

      queryClient.setQueryData<DashboardSummaryDto | undefined>(
        queryKeys.dashboard.summary,
        (current) =>
          current
            ? {
                ...current,
                totalBalance: Number((current.totalBalance + transaction.amount).toFixed(2)),
                transactionCount: current.transactionCount + 1,
                recentOutflow:
                  isReportableExpenseTransaction(transaction)
                    ? Number((current.recentOutflow + Math.abs(transaction.amount)).toFixed(2))
                    : current.recentOutflow,
                recentTransactions: prependTransaction(current.recentTransactions, transaction).slice(0, 5)
              }
            : current
      );

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.transactions(transaction.accountId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.detail(transaction.accountId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary }),
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all })
      ]);
    }
  });
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

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.transactions(transaction.accountId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary }),
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.detail(transaction.id) })
      ]);
    }
  });
}
