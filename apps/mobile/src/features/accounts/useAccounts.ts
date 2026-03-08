import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "../../lib/api/queryKeys";
import type { AccountDto, CreateAccountRequest, DashboardSummaryDto } from "../../types/api";
import { createAccount, getAccountById, getAccounts } from "./accountsApi";

export function useAccountsQuery() {
  return useQuery({
    queryKey: queryKeys.accounts.all,
    queryFn: getAccounts
  });
}

export function useAccountDetailQuery(accountId: string) {
  return useQuery({
    queryKey: queryKeys.accounts.detail(accountId),
    queryFn: () => getAccountById(accountId),
    enabled: Boolean(accountId)
  });
}

export function useCreateAccountMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: CreateAccountRequest) => createAccount(payload),
    onSuccess: async (account) => {
      queryClient.setQueryData<AccountDto[] | undefined>(queryKeys.accounts.all, (current) => {
        const existing = current ?? [];
        if (existing.some((item) => item.id === account.id)) {
          return existing;
        }

        return [...existing, account];
      });

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary }),
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all })
      ]);

      queryClient.setQueryData<DashboardSummaryDto | undefined>(
        queryKeys.dashboard.summary,
        (current) =>
          current
            ? {
                ...current,
                totalBalance: Number((current.totalBalance + account.currentBalance).toFixed(2)),
                accountCount: current.accountCount + 1,
                accountPreview: [...current.accountPreview, account].slice(-3)
              }
            : current
      );

      queryClient.setQueryData(queryKeys.accounts.detail(account.id), account);
    }
  });
}
