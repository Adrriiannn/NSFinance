import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "../../lib/api/queryKeys";
import {
  disconnectBankConnection,
  getBankConnections,
  getLinkedBankAccounts,
  startTrueLayerLink,
  syncBankConnection
} from "./bankingApi";

export function useBankConnectionsQuery() {
  return useQuery({
    queryKey: queryKeys.banking.connections,
    queryFn: getBankConnections
  });
}

export function useLinkedBankAccountsQuery() {
  return useQuery({
    queryKey: queryKeys.banking.accounts,
    queryFn: getLinkedBankAccounts
  });
}

export function useStartTrueLayerLinkMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: startTrueLayerLink,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.banking.connections });
    }
  });
}

export function useSyncBankConnectionMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (connectionId: string) => syncBankConnection(connectionId),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connections }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.accounts }),
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary })
      ]);
    }
  });
}

export function useDisconnectBankConnectionMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (connectionId: string) => disconnectBankConnection(connectionId),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connections }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.accounts }),
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary })
      ]);
    }
  });
}
