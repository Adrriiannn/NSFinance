import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { nearLiveFinanceQueryOptions } from "../../lib/api/liveQueryOptions";
import { queryKeys } from "../../lib/api/queryKeys";
import type {
  AccountDto,
  CreateAccountRequest,
  UpdateAccountRequest
} from "../../types/api";
import { createAccount, deleteAccount, getAccountById, getAccounts, updateAccount } from "./accountsApi";

export function useAccountsQuery() {
  return useQuery({
    queryKey: queryKeys.accounts.all,
    queryFn: getAccounts,
    ...nearLiveFinanceQueryOptions
  });
}

export function useAccountDetailQuery(accountId: string) {
  return useQuery({
    queryKey: queryKeys.accounts.detail(accountId),
    queryFn: () => getAccountById(accountId),
    enabled: Boolean(accountId),
    ...nearLiveFinanceQueryOptions
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

      queryClient.setQueryData(queryKeys.accounts.detail(account.id), account);
    }
  });
}

export function useUpdateAccountMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ accountId, payload }: { accountId: string; payload: UpdateAccountRequest }) =>
      updateAccount(accountId, payload),
    onSuccess: async (account) => {
      queryClient.setQueryData<AccountDto[] | undefined>(queryKeys.accounts.all, (current) =>
        (current ?? []).map((item) => (item.id === account.id ? account : item))
      );
      queryClient.setQueryData(queryKeys.accounts.detail(account.id), account);

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary }),
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.detail(account.id) })
      ]);
    }
  });
}

export function useDeleteAccountMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (accountId: string) => deleteAccount(accountId),
    onSuccess: async (_, accountId) => {
      queryClient.setQueryData<AccountDto[] | undefined>(queryKeys.accounts.all, (current) =>
        (current ?? []).filter((item) => item.id !== accountId)
      );
      queryClient.removeQueries({ queryKey: queryKeys.accounts.detail(accountId) });

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary }),
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all })
      ]);
    }
  });
}
