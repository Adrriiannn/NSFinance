import { useQueryClient } from "@tanstack/react-query";
import {
  Redirect,
  useFocusEffect,
  useLocalSearchParams,
  useRouter
} from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AppState, Linking, StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import {
  ConnectionStatusIndicator,
  type ConnectionStatus
} from "../../src/components/ui/ConnectionStatusIndicator";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import {
  useBankConnectionsQuery,
  useStartTrueLayerLinkMutation,
  useSyncBankConnectionMutation
} from "../../src/features/banking/useBanking";
import { useAccountsQuery } from "../../src/features/accounts/useAccounts";
import { formatUnknownError } from "../../src/lib/api/errors";
import { queryKeys } from "../../src/lib/api/queryKeys";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";
import type {
  BankConnectionStatus,
  StartTrueLayerLinkResponse
} from "../../src/types/api";

const pendingActionStatuses = new Set<BankConnectionStatus>([
  "connection_started",
  "consent_in_progress"
]);

const syncableStatuses = new Set<BankConnectionStatus>([
  "connected",
  "synced",
  "failed"
]);

type PendingConsentLink = {
  authorizationUrl: string;
  expiresAtUtc: string;
};

function mapConnectionStatus(status?: BankConnectionStatus): ConnectionStatus {
  switch (status) {
    case "connection_started":
    case "consent_in_progress":
    case "sync_pending":
      return "connecting";
    case "connected":
    case "synced":
      return "connected";
    case "failed":
      return "sync_failed";
    case "reauth_required":
    case "expired":
    case "revoked":
      return "reconnect_required";
    default:
      return "not_connected";
  }
}

function formatDateAdded(createdUtc?: string | null) {
  if (!createdUtc) {
    return "Pending";
  }

  const parsed = new Date(createdUtc);
  if (Number.isNaN(parsed.getTime())) {
    return "Pending";
  }

  const parts = new Intl.DateTimeFormat("en-GB", {
    weekday: "long",
    day: "2-digit",
    month: "long",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false
  }).formatToParts(parsed);

  const byType = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((part) => part.type === type)?.value ?? "";

  return `${byType("weekday")}, ${byType("day")} ${byType("month")} ${byType("year")}, ${byType("hour")}:${byType("minute")}`;
}

function isLinkStillValid(link: PendingConsentLink | null, nowMs: number) {
  if (!link) {
    return false;
  }

  const expiresAtMs = Date.parse(link.expiresAtUtc);
  if (Number.isNaN(expiresAtMs)) {
    return false;
  }

  return expiresAtMs > nowMs + 10_000;
}

export default function AddAccountModalScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ bankingResult?: string }>();
  const queryClient = useQueryClient();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const { playSuccess } = useFeedbackSound();

  const connectionsQuery = useBankConnectionsQuery();
  const accountsQuery = useAccountsQuery();
  const startLinkMutation = useStartTrueLayerLinkMutation();
  const syncMutation = useSyncBankConnectionMutation();

  const [awaitingConsentReturn, setAwaitingConsentReturn] = useState(false);
  const [pendingConsentLink, setPendingConsentLink] = useState<PendingConsentLink | null>(null);
  const [lastManualSyncUtc, setLastManualSyncUtc] = useState<string | null>(null);
  const [statusClock, setStatusClock] = useState(() => Date.now());
  const successPlayedRef = useRef(false);

  const latestConnection = useMemo(() => {
    const list = connectionsQuery.data ?? [];
    return [...list].sort((a, b) => {
      const aTime = Date.parse(a.updatedUtc);
      const bTime = Date.parse(b.updatedUtc);
      return bTime - aTime;
    })[0] ?? null;
  }, [connectionsQuery.data]);

  const linkedAccountNames = useMemo(() => {
    const accounts = accountsQuery.data ?? [];
    const sorted = [...accounts].sort((a, b) => {
      const aTime = Date.parse(a.createdUtc);
      const bTime = Date.parse(b.createdUtc);
      return bTime - aTime;
    });

    return Array.from(
      new Set(
        sorted
          .map((account) => account.name.trim())
          .filter((name) => name.length > 0)
      )
    );
  }, [accountsQuery.data]);

  const pendingLinkValid = useMemo(
    () => isLinkStillValid(pendingConsentLink, statusClock),
    [pendingConsentLink, statusClock]
  );

  const refreshFromBackendTruth = useCallback(async () => {
    await Promise.all([
      connectionsQuery.refetch(),
      accountsQuery.refetch(),
      queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary }),
      queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all })
    ]);
  }, [accountsQuery, connectionsQuery, queryClient]);

  const launchConsentInBrowser = useCallback(async (url: string) => {
    const canOpen = await Linking.canOpenURL(url);
    if (!canOpen) {
      throw new Error("Could not open the bank consent page.");
    }

    await Linking.openURL(url);
  }, []);

  const beginConsentSession = useCallback(
    async (response: StartTrueLayerLinkResponse) => {
      successPlayedRef.current = false;
      setAwaitingConsentReturn(true);
      setPendingConsentLink({
        authorizationUrl: response.authorizationUrl,
        expiresAtUtc: response.expiresAtUtc
      });
      setStatusClock(Date.now());
      await launchConsentInBrowser(response.authorizationUrl);
    },
    [launchConsentInBrowser]
  );

  const handleConnectBank = async () => {
    const response = await startLinkMutation.mutateAsync();
    await beginConsentSession(response);
  };

  const handleResumeConsent = async () => {
    if (pendingConsentLink && pendingLinkValid) {
      setAwaitingConsentReturn(true);
      await launchConsentInBrowser(pendingConsentLink.authorizationUrl);
      return;
    }

    const response = await startLinkMutation.mutateAsync();
    await beginConsentSession(response);
  };

  const handleSyncNow = async () => {
    if (!latestConnection) {
      return;
    }

    const syncResult = await syncMutation.mutateAsync(latestConnection.id);
    setLastManualSyncUtc(syncResult.syncedAtUtc);
    await refreshFromBackendTruth();
    playSuccess();
  };

  useFocusEffect(
    useCallback(() => {
      void refreshFromBackendTruth();
      return undefined;
    }, [refreshFromBackendTruth])
  );

  useEffect(() => {
    if (!awaitingConsentReturn) {
      return;
    }

    const interval = setInterval(() => {
      setStatusClock(Date.now());
    }, 4_000);

    return () => clearInterval(interval);
  }, [awaitingConsentReturn]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextState) => {
      if (nextState !== "active") {
        return;
      }

      setStatusClock(Date.now());
      void refreshFromBackendTruth();
    });

    return () => subscription.remove();
  }, [refreshFromBackendTruth]);

  useEffect(() => {
    const interval = setInterval(() => {
      if (AppState.currentState !== "active") {
        return;
      }

      void refreshFromBackendTruth();
    }, 30_000);

    return () => clearInterval(interval);
  }, [refreshFromBackendTruth]);

  useEffect(() => {
    if (!params.bankingResult) {
      return;
    }

    setAwaitingConsentReturn(true);
    setStatusClock(Date.now());
    void refreshFromBackendTruth();
  }, [params.bankingResult, refreshFromBackendTruth]);

  useEffect(() => {
    const status = latestConnection?.status;
    if (!status) {
      return;
    }

    if (status === "connected" || status === "synced") {
      setAwaitingConsentReturn(false);
      setPendingConsentLink(null);
      if (!successPlayedRef.current) {
        playSuccess();
        successPlayedRef.current = true;
      }
      return;
    }

    if (
      status === "failed" ||
      status === "reauth_required" ||
      status === "expired" ||
      status === "revoked"
    ) {
      setAwaitingConsentReturn(false);
      successPlayedRef.current = false;
    }
  }, [latestConnection?.status, playSuccess]);

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={"/login" as never} />;
  }

  const mappedStatus = mapConnectionStatus(latestConnection?.status);
  const requiresBrowserCompletion = pendingActionStatuses.has(
    latestConnection?.status ?? "not_connected"
  );

  let status: ConnectionStatus = mappedStatus;
  if (startLinkMutation.isPending || syncMutation.isPending) {
    status = "connecting";
  } else if (awaitingConsentReturn && (requiresBrowserCompletion || !latestConnection)) {
    status = pendingLinkValid ? "connecting" : "reconnect_required";
  } else if (mappedStatus === "connecting" && !pendingLinkValid) {
    status = "reconnect_required";
  }

  const showResumeAction =
    (status === "connecting" || status === "reconnect_required") &&
    (awaitingConsentReturn || requiresBrowserCompletion);

  const statusHelperText =
    status === "connecting" && showResumeAction
      ? "You still need to complete the required tasks in the browser."
      : status === "reconnect_required" && requiresBrowserCompletion
        ? "Consent is still incomplete. Reopen the browser flow to continue."
      : status === "reconnect_required" && awaitingConsentReturn && !pendingLinkValid
        ? "Consent session expired before completion. Start again to continue."
        : undefined;

  const canSyncNow = latestConnection
    ? syncableStatuses.has(latestConnection.status)
    : false;

  const bankName =
    latestConnection?.providerDisplayName?.trim() || "Waiting for institution details";
  const lastSyncUtc =
    latestConnection?.lastSuccessfulSyncUtc ?? latestConnection?.lastSyncAttemptedUtc ?? null;
  const dateAdded = formatDateAdded(latestConnection?.createdUtc);
  const lastManualSyncLabel = formatDateAdded(
    lastManualSyncUtc ?? latestConnection?.lastSuccessfulSyncUtc ?? null
  );
  const lastSyncLabel = lastSyncUtc ? formatDateAdded(lastSyncUtc) : "Not synced yet";

  return (
    <ScreenContainer contentStyle={styles.content}>
      <View style={styles.mainContent}>
        <View style={styles.header}>
          <Text style={styles.title}>Bank Connection</Text>
        </View>

        {connectionsQuery.isError ? (
          <ErrorState
            title="Could not load connection status"
            message={formatUnknownError(connectionsQuery.error)}
            onRetry={() => {
              void refreshFromBackendTruth();
            }}
          />
        ) : null}

        {startLinkMutation.isError ? (
          <ErrorState
            title="Could not start bank connection"
            message={formatUnknownError(startLinkMutation.error)}
            onRetry={() => {
              void handleConnectBank();
            }}
            retryLabel="Try again"
          />
        ) : null}

        {syncMutation.isError ? (
          <ErrorState
            title="Sync failed"
            message={formatUnknownError(syncMutation.error)}
            onRetry={() => {
              void handleSyncNow();
            }}
            retryLabel="Retry sync"
          />
        ) : null}

        <ConnectionStatusIndicator status={status} helperText={statusHelperText} />

        <View style={styles.metadataCard}>
          <Text style={styles.metadataRow}>
            <Text style={styles.metadataLabel}>Bank: </Text>
            {bankName}
          </Text>
          <Text style={styles.metadataRow}>
            <Text style={styles.metadataLabel}>Bank account name(s): </Text>
          </Text>
          {linkedAccountNames.length > 0 ? (
            linkedAccountNames.map((accountName) => (
              <Text key={accountName} style={styles.metadataListItem}>
                - {accountName}
              </Text>
            ))
          ) : (
            <Text style={styles.metadataRow}>Waiting for account sync</Text>
          )}
          <Text style={styles.metadataRow}>
            <Text style={styles.metadataLabel}>Last manual sync: </Text>
            {lastManualSyncLabel}
          </Text>
          <Text style={styles.metadataRow}>
            <Text style={styles.metadataLabel}>Last sync: </Text>
            {lastSyncLabel}
          </Text>
          <Text style={styles.metadataRow}>
            <Text style={styles.metadataLabel}>Connection provider: </Text>
            TrueLayer
          </Text>
          <Text style={styles.metadataRow}>
            <Text style={styles.metadataLabel}>Date added: </Text>
            {dateAdded}
          </Text>
        </View>

        {showResumeAction ? (
          <View style={styles.resumeCard}>
            <Text style={styles.resumeTitle}>
              You still need to complete the required tasks in the browser.
            </Text>
            <SecondaryButton
              label="Take me there"
              onPress={() => {
                void handleResumeConsent();
              }}
              disabled={startLinkMutation.isPending}
            />
          </View>
        ) : null}

        <View style={styles.actionSpacer} />

        <View style={styles.primaryActions}>
          <PrimaryButton
            label="Connect to your bank"
            onPress={() => {
              void handleConnectBank();
            }}
            isLoading={startLinkMutation.isPending}
          />

          <PrimaryButton
            label="Sync now"
            onPress={() => {
              void handleSyncNow();
            }}
            isLoading={syncMutation.isPending}
            disabled={!canSyncNow}
          />
        </View>
      </View>

      <View style={styles.closeAction}>
        <SecondaryButton label="Close" onPress={() => router.back()} />
      </View>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    flex: 1,
    paddingTop: spacing[20]
  },
  mainContent: {
    flex: 1,
    gap: spacing[16]
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1
  },
  metadataCard: {
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    borderRadius: 12,
    padding: spacing[12],
    gap: spacing[8]
  },
  metadataRow: {
    color: palette.textSecondary,
    ...typography.body2
  },
  metadataLabel: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  metadataListItem: {
    color: palette.textSecondary,
    ...typography.body2,
    marginLeft: spacing[8]
  },
  resumeCard: {
    borderWidth: 1,
    borderColor: "rgba(255,154,102,0.45)",
    backgroundColor: "rgba(51,30,14,0.5)",
    borderRadius: 12,
    padding: spacing[12],
    gap: spacing[12]
  },
  resumeTitle: {
    color: palette.textPrimary,
    ...typography.body2
  },
  actionSpacer: {
    flex: 1
  },
  primaryActions: {
    gap: spacing[12]
  },
  closeAction: {
    paddingTop: spacing[16]
  }
});
