import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { nearLiveFinanceQueryOptions } from "../../lib/api/liveQueryOptions";
import { queryKeys } from "../../lib/api/queryKeys";
import {
  disconnectBankConnection,
  getBankConnection,
  getBankConnections,
  getConnectedBanks,
  getLinkedBankAccounts,
  startTrueLayerLink,
  syncBankConnection
} from "./bankingApi";

const {
  refetchInterval: _defaultRefetchInterval,
  refetchIntervalInBackground: _defaultRefetchIntervalInBackground,
  ...nearLiveOptionsWithoutInterval
} = nearLiveFinanceQueryOptions;

export function useBankConnectionsQuery(enabled = true) {
  return useQuery({
    queryKey: queryKeys.banking.connections,
    queryFn: getBankConnections,
    enabled,
    ...nearLiveFinanceQueryOptions
  });
}

export function useConnectedBanksQuery() {
  return useQuery({
    queryKey: queryKeys.banking.connectedBanks,
    queryFn: getConnectedBanks,
    ...nearLiveOptionsWithoutInterval,
    refetchInterval: (query) => {
      const data = query.state.data;
      if (!data) {
        return false;
      }

      const hasDisconnectPending =
        data.activeConnections.some((connection) => connection.status === "disconnect_pending")
        || data.attentionConnections.some((connection) => connection.status === "disconnect_pending");

      return hasDisconnectPending ? 3_000 : false;
    },
    refetchIntervalInBackground: false
  });
}

export function useBankConnectionQuery(connectionId: string | null) {
  return useQuery({
    queryKey: connectionId ? queryKeys.banking.connection(connectionId) : queryKeys.banking.connections,
    queryFn: () => getBankConnection(connectionId as string),
    enabled: Boolean(connectionId),
    ...nearLiveFinanceQueryOptions
  });
}

export function useLinkedBankAccountsQuery() {
  return useQuery({
    queryKey: queryKeys.banking.accounts,
    queryFn: getLinkedBankAccounts,
    ...nearLiveFinanceQueryOptions
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
    onSettled: async (_, __, connectionId) => {
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
