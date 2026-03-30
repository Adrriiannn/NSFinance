import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { nearLiveFinanceQueryOptions } from "../../lib/api/liveQueryOptions";
import { queryKeys } from "../../lib/api/queryKeys";
import type {
  BankConnectionDto,
  ConnectedBanksOverviewDto
} from "../../types/api";
import {
  disconnectBankConnection,
  getBankConnection,
  getBankConnections,
  getLinkedBankCards,
  getConnectedBanks,
  getLinkedBankAccounts,
  getRecurringPayments,
  getRecurringPaymentsForAccount,
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

export function useLinkedBankCardsQuery() {
  return useQuery({
    queryKey: queryKeys.banking.cards,
    queryFn: getLinkedBankCards,
    ...nearLiveFinanceQueryOptions
  });
}

export function useRecurringPaymentsQuery() {
  return useQuery({
    queryKey: queryKeys.banking.recurringPayments,
    queryFn: getRecurringPayments,
    ...nearLiveFinanceQueryOptions
  });
}

export function useAccountRecurringPaymentsQuery(accountId: string | null) {
  return useQuery({
    queryKey: accountId
      ? queryKeys.banking.recurringPaymentsByAccount(accountId)
      : queryKeys.banking.recurringPayments,
    queryFn: () => getRecurringPaymentsForAccount(accountId as string),
    enabled: Boolean(accountId),
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
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.cards }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.recurringPayments }),
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary })
      ]);

      await Promise.all([
        queryClient.refetchQueries({ queryKey: queryKeys.banking.connections, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.banking.connectedBanks, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.banking.connection(connectionId), type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.banking.accounts, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.banking.cards, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.banking.recurringPayments, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.accounts.all, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.transactions.all, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.dashboard.summary, type: "all" })
      ]);

      console.info("[Banking Sync]", {
        event: "post_sync_queries_refetched",
        connectionId
      });
    }
  });
}

export function useDisconnectBankConnectionMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (connectionId: string) => disconnectBankConnection(connectionId),
    onMutate: async (connectionId: string) => {
      await Promise.all([
        queryClient.cancelQueries({ queryKey: queryKeys.banking.connections }),
        queryClient.cancelQueries({ queryKey: queryKeys.banking.connectedBanks }),
        queryClient.cancelQueries({ queryKey: queryKeys.banking.connection(connectionId) })
      ]);

      const previousConnections =
        queryClient.getQueryData<BankConnectionDto[]>(queryKeys.banking.connections);
      const previousConnectedBanks =
        queryClient.getQueryData<ConnectedBanksOverviewDto>(queryKeys.banking.connectedBanks);
      const previousConnectionDetail =
        queryClient.getQueryData<BankConnectionDto>(queryKeys.banking.connection(connectionId));
      const disconnectRequestedAtUtc = new Date().toISOString();

      queryClient.setQueryData<BankConnectionDto[] | undefined>(
        queryKeys.banking.connections,
        (current) =>
          current?.map((connection) =>
            connection.id === connectionId
              ? {
                  ...connection,
                  status: "disconnect_pending",
                  updatedUtc: disconnectRequestedAtUtc
                }
              : connection
          ) ?? current
      );

      queryClient.setQueryData<BankConnectionDto | undefined>(
        queryKeys.banking.connection(connectionId),
        (current) =>
          current
            ? {
                ...current,
                status: "disconnect_pending",
                updatedUtc: disconnectRequestedAtUtc
              }
            : current
      );

      queryClient.setQueryData<ConnectedBanksOverviewDto | undefined>(
        queryKeys.banking.connectedBanks,
        (current) => {
          if (!current) {
            return current;
          }

          const allConnections = [
            ...current.activeConnections,
            ...current.attentionConnections
          ];
          const targetConnection = allConnections.find((connection) => connection.id === connectionId);
          if (!targetConnection) {
            return current;
          }

          const disconnectPendingConnection: BankConnectionDto = {
            ...targetConnection,
            status: "disconnect_pending",
            updatedUtc: disconnectRequestedAtUtc
          };

          return {
            activeConnections: current.activeConnections.filter(
              (connection) => connection.id !== connectionId
            ),
            attentionConnections: [
              disconnectPendingConnection,
              ...current.attentionConnections.filter((connection) => connection.id !== connectionId)
            ]
          };
        }
      );

      return {
        previousConnections,
        previousConnectedBanks,
        previousConnectionDetail
      };
    },
    onError: (_error, connectionId, context) => {
      if (context?.previousConnections) {
        queryClient.setQueryData(queryKeys.banking.connections, context.previousConnections);
      }

      if (context?.previousConnectedBanks) {
        queryClient.setQueryData(queryKeys.banking.connectedBanks, context.previousConnectedBanks);
      }

      if (context?.previousConnectionDetail) {
        queryClient.setQueryData(
          queryKeys.banking.connection(connectionId),
          context.previousConnectionDetail
        );
      }
    },
    onSettled: async (_, __, connectionId) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connections }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connectedBanks }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.connection(connectionId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.accounts }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.cards }),
        queryClient.invalidateQueries({ queryKey: queryKeys.banking.recurringPayments }),
        queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary })
      ]);

      await Promise.all([
        queryClient.refetchQueries({ queryKey: queryKeys.banking.connections, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.banking.connectedBanks, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.banking.connection(connectionId), type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.banking.accounts, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.banking.cards, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.banking.recurringPayments, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.accounts.all, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.transactions.all, type: "all" }),
        queryClient.refetchQueries({ queryKey: queryKeys.dashboard.summary, type: "all" })
      ]);

      console.info("[Banking Sync]", {
        event: "post_disconnect_queries_refetched",
        connectionId
      });
    }
  });
}
