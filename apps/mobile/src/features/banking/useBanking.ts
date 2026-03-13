import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "../../lib/api/queryKeys";
import type { ConnectedBanksOverviewDto } from "../../types/api";
import {
  disconnectBankConnection,
  getBankConnection,
  getBankConnections,
  getConnectedBanks,
  getLinkedBankAccounts,
  startTrueLayerLink,
  syncBankConnection
} from "./bankingApi";

export function useBankConnectionsQuery(enabled = true) {
  return useQuery({
    queryKey: queryKeys.banking.connections,
    queryFn: getBankConnections,
    enabled
  });
}

export function useConnectedBanksQuery() {
  return useQuery({
    queryKey: queryKeys.banking.connectedBanks,
    queryFn: getConnectedBanks
  });
}

export function useBankConnectionQuery(connectionId: string | null) {
  return useQuery({
    queryKey: connectionId ? queryKeys.banking.connection(connectionId) : queryKeys.banking.connections,
    queryFn: () => getBankConnection(connectionId as string),
    enabled: Boolean(connectionId)
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
      await queryClient.invalidateQueries({ queryKey: queryKeys.banking.connectedBanks });
    }
  });
}

export function useSyncBankConnectionMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (connectionId: string) => syncBankConnection(connectionId),
    onSuccess: async (_, connectionId) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connections }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connectedBanks }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connection(connectionId) }),
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
    onMutate: async (connectionId: string) => {
      await queryClient.cancelQueries({ queryKey: queryKeys.banking.connectedBanks });
      const previousConnectedBanks = queryClient.getQueryData<ConnectedBanksOverviewDto>(
        queryKeys.banking.connectedBanks
      );

      if (previousConnectedBanks) {
        queryClient.setQueryData<ConnectedBanksOverviewDto>(queryKeys.banking.connectedBanks, {
          activeConnections: previousConnectedBanks.activeConnections.filter(
            (connection) => connection.id !== connectionId
          ),
          attentionConnections: previousConnectedBanks.attentionConnections.filter(
            (connection) => connection.id !== connectionId
          )
        });
      }

      return { previousConnectedBanks };
    },
    onError: (_error, _connectionId, context) => {
      if (context?.previousConnectedBanks) {
        queryClient.setQueryData(queryKeys.banking.connectedBanks, context.previousConnectedBanks);
      }
    },
    onSuccess: async (_, connectionId) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connections }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connectedBanks }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connection(connectionId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.accounts }),
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary })
      ]);
    }
  });
}
