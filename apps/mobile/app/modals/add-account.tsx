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
  useLinkedBankAccountsQuery,
  useStartTrueLayerLinkMutation,
  useSyncBankConnectionMutation
} from "../../src/features/banking/useBanking";
import { formatUnknownError } from "../../src/lib/api/errors";
import { queryKeys } from "../../src/lib/api/queryKeys";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";
import type {
  BankConnectionDto,
  BankConnectionStatus,
  StartTrueLayerLinkResponse
} from "../../src/types/api";

const pendingActionStatuses = new Set<BankConnectionStatus>([
  "connection_started",
  "consent_in_progress"
]);

const completionStatuses = new Set<BankConnectionStatus>([
  "connected_pending_sync",
  "connected",
  "synced",
  "failed",
  "reauth_required",
  "expired",
  "revoked"
]);

const successStatuses = new Set<BankConnectionStatus>([
  "connected_pending_sync",
  "connected",
  "synced"
]);

const syncableStatuses = new Set<BankConnectionStatus>([
  "connected_pending_sync",
  "connected",
  "synced",
  "failed"
]);

const connectedFlowStatuses = new Set<BankConnectionStatus>([
  "connected_pending_sync",
  "connected",
  "sync_pending"
]);

const consentTimeoutMs = 90_000;
const aggressivePollDurationMs = 15_000;
const aggressivePollIntervalMs = 1_500;
const steadyPollIntervalMs = 5_000;

type PendingConsentLink = {
  authorizationUrl: string;
  expiresAtUtc: string;
};

type BrowserPhase = "idle" | "opening_bank" | "awaiting_consent";

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

function deriveUiState(
  browserPhase: BrowserPhase,
  awaitingConsentReturn: boolean,
  connection: BankConnectionDto | null,
  consentTimedOut: boolean
): ConnectionStatus {
  if (browserPhase === "opening_bank") {
    return "opening_bank";
  }

  if (consentTimedOut) {
    return "reauth_required";
  }

  if (awaitingConsentReturn && (browserPhase === "awaiting_consent" || !connection)) {
    return "awaiting_consent";
  }

  switch (connection?.status) {
    case "connection_started":
    case "consent_in_progress":
      return "awaiting_consent";
    case "connected_pending_sync":
    case "connected":
      return "connected_pending_sync";
    case "sync_pending":
      return "syncing_data";
    case "synced":
      return "synced";
    case "failed":
      return "failed";
    case "reauth_required":
    case "expired":
    case "revoked":
      return "reauth_required";
    default:
      return "not_connected";
  }
}

export default function AddAccountModalScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ bankingResult?: string; connectionId?: string }>();
  const queryClient = useQueryClient();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const { playSuccess } = useFeedbackSound();

  const connectionsQuery = useBankConnectionsQuery();
  const linkedBankAccountsQuery = useLinkedBankAccountsQuery();
  const startLinkMutation = useStartTrueLayerLinkMutation();
  const syncMutation = useSyncBankConnectionMutation();

  const [awaitingConsentReturn, setAwaitingConsentReturn] = useState(false);
  const [browserPhase, setBrowserPhase] = useState<BrowserPhase>("idle");
  const [pendingConsentLink, setPendingConsentLink] = useState<PendingConsentLink | null>(null);
  const [pendingConnectionId, setPendingConnectionId] = useState<string | null>(null);
  const [consentStartedAtMs, setConsentStartedAtMs] = useState<number | null>(null);
  const [returnStartedAtMs, setReturnStartedAtMs] = useState<number | null>(null);
  const [lastManualSyncUtc, setLastManualSyncUtc] = useState<string | null>(null);
  const [statusClock, setStatusClock] = useState(() => Date.now());
  const successPlayedRef = useRef(false);
  const lastPolledStatusRef = useRef<string | null>(null);
  const lastUiStateRef = useRef<string | null>(null);
  const interactiveReturnRef = useRef<number | null>(null);
  const syncedInvalidationRef = useRef<string | null>(null);

  const logBankingEvent = useCallback((event: string, metadata?: Record<string, unknown>) => {
    console.info("[Banking UX]", {
      event,
      timestampUtc: new Date().toISOString(),
      ...metadata
    });
  }, []);

  const invalidatePortfolioQueries = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
      queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
      queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary })
    ]);
  }, [queryClient]);

  const latestConnection = useMemo(() => {
    const list = connectionsQuery.data ?? [];
    return [...list].sort((a, b) => {
      const aTime = Date.parse(a.updatedUtc);
      const bTime = Date.parse(b.updatedUtc);
      return bTime - aTime;
    })[0] ?? null;
  }, [connectionsQuery.data]);

  const activeConnection = useMemo(() => {
    const list = connectionsQuery.data ?? [];
    if (pendingConnectionId) {
      return list.find((connection) => connection.id === pendingConnectionId) ?? null;
    }

    return latestConnection;
  }, [connectionsQuery.data, latestConnection, pendingConnectionId]);

  const linkedAccountNames = useMemo(() => {
    const accounts = linkedBankAccountsQuery.data ?? [];
    const filtered = pendingConnectionId
      ? accounts.filter((account) => account.connectionId === pendingConnectionId)
      : accounts;
    const sorted = [...filtered].sort((a, b) => {
      const aTime = Date.parse(a.createdUtc);
      const bTime = Date.parse(b.createdUtc);
      return bTime - aTime;
    });

    return Array.from(
      new Set(
        sorted
          .map((account) => account.displayName.trim())
          .filter((name) => name.length > 0)
      )
    );
  }, [linkedBankAccountsQuery.data, pendingConnectionId]);

  const pendingLinkValid = useMemo(
    () => isLinkStillValid(pendingConsentLink, statusClock),
    [pendingConsentLink, statusClock]
  );

  const consentTimedOut =
    awaitingConsentReturn &&
    consentStartedAtMs !== null &&
    statusClock - consentStartedAtMs >= consentTimeoutMs;

  const uiState = useMemo(
    () => deriveUiState(browserPhase, awaitingConsentReturn, activeConnection, consentTimedOut),
    [activeConnection, awaitingConsentReturn, browserPhase, consentTimedOut]
  );

  const shouldPoll =
    uiState === "awaiting_consent" ||
    uiState === "connected_pending_sync" ||
    uiState === "syncing_data";

  const refreshBankingState = useCallback(
    async (reason: string, options?: { fullInvalidate?: boolean }) => {
      const startedAt = Date.now();
      logBankingEvent("refresh_start", {
        reason,
        connectionId: pendingConnectionId,
        fullInvalidate: Boolean(options?.fullInvalidate)
      });

      await Promise.all([
        connectionsQuery.refetch(),
        linkedBankAccountsQuery.refetch()
      ]);

      if (options?.fullInvalidate) {
        await invalidatePortfolioQueries();
      }

      logBankingEvent("refresh_complete", {
        reason,
        connectionId: pendingConnectionId,
        elapsedMs: Date.now() - startedAt
      });
    },
    [connectionsQuery, invalidatePortfolioQueries, linkedBankAccountsQuery, logBankingEvent, pendingConnectionId]
  );

  const markReturnAttempt = useCallback(
    (reason: string, connectionId?: string | null) => {
      const startedAt = Date.now();
      setReturnStartedAtMs(startedAt);
      interactiveReturnRef.current = startedAt;
      setBrowserPhase("idle");
      setStatusClock(startedAt);
      logBankingEvent(reason, {
        connectionId: connectionId ?? pendingConnectionId,
        uiState
      });
    },
    [logBankingEvent, pendingConnectionId, uiState]
  );

  const launchConsentInBrowser = useCallback(async (url: string) => {
    logBankingEvent("browser_open_start", { connectionId: pendingConnectionId });
    const canOpen = await Linking.canOpenURL(url);
    if (!canOpen) {
      throw new Error("Could not open the bank consent page.");
    }

    await Linking.openURL(url);
    logBankingEvent("browser_open_complete", { connectionId: pendingConnectionId });
  }, [logBankingEvent, pendingConnectionId]);

  const beginConsentSession = useCallback(
    async (response: StartTrueLayerLinkResponse) => {
      successPlayedRef.current = false;
      setAwaitingConsentReturn(true);
      setBrowserPhase("opening_bank");
      setPendingConnectionId(response.connectionId);
      setPendingConsentLink({
        authorizationUrl: response.authorizationUrl,
        expiresAtUtc: response.expiresAtUtc
      });
      setConsentStartedAtMs(Date.now());
      setStatusClock(Date.now());
      logBankingEvent("connect_start", {
        connectionId: response.connectionId,
        expiresAtUtc: response.expiresAtUtc
      });
      await launchConsentInBrowser(response.authorizationUrl);
      setBrowserPhase("awaiting_consent");
      logBankingEvent("awaiting_consent", { connectionId: response.connectionId });
    },
    [launchConsentInBrowser, logBankingEvent]
  );

  const handleConnectBank = async () => {
    try {
      setBrowserPhase("opening_bank");
      const response = await startLinkMutation.mutateAsync();
      await beginConsentSession(response);
    } catch (error) {
      setBrowserPhase("idle");
      throw error;
    }
  };

  const handleResumeConsent = async () => {
    if (pendingConsentLink && pendingLinkValid) {
      setAwaitingConsentReturn(true);
      setBrowserPhase("opening_bank");
      setStatusClock(Date.now());
      logBankingEvent("resume_browser_flow", { connectionId: pendingConnectionId });
      await launchConsentInBrowser(pendingConsentLink.authorizationUrl);
      setBrowserPhase("awaiting_consent");
      return;
    }

    const response = await startLinkMutation.mutateAsync();
    await beginConsentSession(response);
  };

  const handleManualRefresh = async () => {
    setStatusClock(Date.now());
    logBankingEvent("manual_refresh", { connectionId: pendingConnectionId, uiState });
    await refreshBankingState("manual_refresh", {
      fullInvalidate: activeConnection?.status === "synced"
    });
  };

  const handleSyncNow = async () => {
    if (!activeConnection) {
      return;
    }

    logBankingEvent("manual_sync_start", { connectionId: activeConnection.id });
    const syncResult = await syncMutation.mutateAsync(activeConnection.id);
    setLastManualSyncUtc(syncResult.syncedAtUtc);
    await refreshBankingState("manual_sync_complete", { fullInvalidate: true });
    playSuccess();
  };

  useEffect(() => {
    logBankingEvent("screen_mount", { route: "modals/add-account" });
  }, [logBankingEvent]);

  useFocusEffect(
    useCallback(() => {
      void refreshBankingState("focus");
      return undefined;
    }, [refreshBankingState])
  );

  useEffect(() => {
    if (!shouldPoll) {
      return;
    }

    const interval = setInterval(() => {
      setStatusClock(Date.now());
    }, 1000);

    return () => clearInterval(interval);
  }, [shouldPoll]);

  useEffect(() => {
    if (!shouldPoll) {
      return;
    }

    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | null = null;

    const scheduleNextPoll = () => {
      if (cancelled) {
        return;
      }

      const elapsedSinceReturn = returnStartedAtMs === null ? 0 : Date.now() - returnStartedAtMs;
      const aggressive = returnStartedAtMs !== null && elapsedSinceReturn < aggressivePollDurationMs;
      const delayMs = aggressive ? aggressivePollIntervalMs : steadyPollIntervalMs;

      timer = setTimeout(async () => {
        if (cancelled) {
          return;
        }

        setStatusClock(Date.now());
        logBankingEvent("poll_tick", {
          connectionId: pendingConnectionId,
          uiState,
          aggressive,
          delayMs
        });

        if (AppState.currentState === "active") {
          await refreshBankingState("poll");
        }

        scheduleNextPoll();
      }, delayMs);
    };

    scheduleNextPoll();

    return () => {
      cancelled = true;
      if (timer) {
        clearTimeout(timer);
      }
    };
  }, [logBankingEvent, pendingConnectionId, refreshBankingState, returnStartedAtMs, shouldPoll, uiState]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextState) => {
      if (nextState !== "active") {
        return;
      }

      markReturnAttempt("app_resume", pendingConnectionId);
      void refreshBankingState("app_resume");
    });

    return () => subscription.remove();
  }, [markReturnAttempt, pendingConnectionId, refreshBankingState]);

  useEffect(() => {
    if (!params.bankingResult) {
      return;
    }

    const returnedConnectionId =
      typeof params.connectionId === "string" && params.connectionId.trim().length > 0
        ? params.connectionId.trim()
        : pendingConnectionId;

    setAwaitingConsentReturn(true);
    if (returnedConnectionId) {
      setPendingConnectionId(returnedConnectionId);
    }
    markReturnAttempt("deep_link_return", returnedConnectionId);
    void refreshBankingState("deep_link_return");
  }, [markReturnAttempt, params.bankingResult, params.connectionId, pendingConnectionId, refreshBankingState]);

  useEffect(() => {
    const status = activeConnection?.status ?? "missing";
    const snapshot = `${pendingConnectionId ?? "none"}:${status}`;
    if (lastPolledStatusRef.current === snapshot) {
      return;
    }

    lastPolledStatusRef.current = snapshot;
    logBankingEvent("backend_status_transition", {
      connectionId: pendingConnectionId,
      status,
      awaitingConsentReturn
    });
  }, [activeConnection?.status, awaitingConsentReturn, logBankingEvent, pendingConnectionId]);

  useEffect(() => {
    if (lastUiStateRef.current === uiState) {
      return;
    }

    lastUiStateRef.current = uiState;
    logBankingEvent("ui_state_transition", {
      connectionId: pendingConnectionId,
      uiState
    });
  }, [logBankingEvent, pendingConnectionId, uiState]);

  useEffect(() => {
    if (interactiveReturnRef.current === null) {
      return;
    }

    const activeMarker = interactiveReturnRef.current;
    const frameId = requestAnimationFrame(() => {
      logBankingEvent("interactive_after_return", {
        connectionId: pendingConnectionId,
        elapsedMs: Date.now() - activeMarker,
        uiState
      });
      if (interactiveReturnRef.current === activeMarker) {
        interactiveReturnRef.current = null;
      }
    });

    return () => cancelAnimationFrame(frameId);
  }, [logBankingEvent, pendingConnectionId, uiState]);

  useEffect(() => {
    const status = activeConnection?.status;
    if (!status) {
      return;
    }

    if (connectedFlowStatuses.has(status)) {
      setAwaitingConsentReturn(false);
      setBrowserPhase("idle");
      setPendingConsentLink(null);
      if (!successPlayedRef.current && successStatuses.has(status)) {
        playSuccess();
        successPlayedRef.current = true;
      }
      return;
    }

    if (status === "synced") {
      setAwaitingConsentReturn(false);
      setBrowserPhase("idle");
      setPendingConsentLink(null);
      if (syncedInvalidationRef.current !== activeConnection.id) {
        syncedInvalidationRef.current = activeConnection.id;
        void invalidatePortfolioQueries();
      }
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
      setBrowserPhase("idle");
      successPlayedRef.current = false;
    }
  }, [activeConnection, invalidatePortfolioQueries, playSuccess]);

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={"/login" as never} />;
  }

  const completionReached = completionStatuses.has(activeConnection?.status ?? "not_connected");
  const showResumeAction =
    (uiState === "awaiting_consent" || !completionReached) &&
    (pendingActionStatuses.has(activeConnection?.status ?? "not_connected") ||
      (awaitingConsentReturn && pendingLinkValid));
  const showRefreshAction =
    uiState === "awaiting_consent" ||
    uiState === "connected_pending_sync" ||
    uiState === "syncing_data" ||
    consentTimedOut;

  const statusHelperText =
    consentTimedOut
      ? "If you already finished in the browser, tap Refresh. Otherwise reopen the bank consent page and try again."
      : uiState === "opening_bank"
        ? "Opening the secure bank consent page now. Stay here if your browser does not launch immediately."
        : uiState === "awaiting_consent"
          ? "Finish the bank consent flow in your browser. As soon as you return, we will start checking the saved connection." 
          : uiState === "connected_pending_sync"
            ? "Connection confirmed. We are waiting for the first sync to begin."
            : uiState === "syncing_data"
              ? "Connected to your bank. We are importing account details and recent transactions now."
              : uiState === "failed"
                ? "The bank connection exists, but data sync failed. You can retry sync without reconnecting."
                : undefined;

  const canSyncNow = activeConnection
    ? syncableStatuses.has(activeConnection.status)
    : false;

  const bankName =
    activeConnection?.providerDisplayName?.trim() || "Waiting for institution details";
  const lastSyncUtc =
    activeConnection?.lastSuccessfulSyncUtc ?? activeConnection?.lastSyncAttemptedUtc ?? null;
  const dateAdded = formatDateAdded(activeConnection?.createdUtc);
  const lastManualSyncLabel = formatDateAdded(
    lastManualSyncUtc ?? activeConnection?.lastSuccessfulSyncUtc ?? null
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
              void refreshBankingState("connections_error_retry");
            }}
          />
        ) : null}

        {linkedBankAccountsQuery.isError ? (
          <ErrorState
            title="Could not load linked bank accounts"
            message={formatUnknownError(linkedBankAccountsQuery.error)}
            onRetry={() => {
              void refreshBankingState("linked_accounts_error_retry");
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

        <ConnectionStatusIndicator status={uiState} helperText={statusHelperText} />

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

        {showRefreshAction ? (
          <View style={styles.resumeCard}>
            <Text style={styles.resumeTitle}>
              {uiState === "connected_pending_sync"
                ? "The bank connection is confirmed. Tap Refresh if you want to check whether the first sync has started."
                : uiState === "syncing_data"
                  ? "We are still syncing your bank data. You can keep waiting or tap Refresh to reconcile now."
                  : "If you already finished in the browser, tap Refresh to check the latest bank connection status."}
            </Text>
            <View style={styles.resumeActions}>
              <SecondaryButton
                label={uiState === "syncing_data" ? "Refresh sync status" : "Refresh status"}
                onPress={() => {
                  void handleManualRefresh();
                }}
              />
              {showResumeAction ? (
                <SecondaryButton
                  label="Open browser again"
                  onPress={() => {
                    void handleResumeConsent();
                  }}
                  disabled={startLinkMutation.isPending}
                />
              ) : null}
            </View>
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
  resumeActions: {
    gap: spacing[12]
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
