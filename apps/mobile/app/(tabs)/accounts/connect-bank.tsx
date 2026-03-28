import { useQueryClient } from "@tanstack/react-query";
import { Redirect, useLocalSearchParams, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AppState, Linking as NativeLinking, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import {
  ConnectionStatusIndicator,
  type ConnectionStatus
} from "../../../src/components/ui/ConnectionStatusIndicator";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import {
  useBankConnectionQuery,
  useBankConnectionsQuery,
  useLinkedBankAccountsQuery,
  useStartTrueLayerLinkMutation,
  useSyncBankConnectionMutation
} from "../../../src/features/banking/useBanking";
import { buildBankConnectReturnUri } from "../../../src/features/banking/bankingLinking";
import { formatUnknownError } from "../../../src/lib/api/errors";
import { queryKeys } from "../../../src/lib/api/queryKeys";
import { useFeedbackSound } from "../../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../../src/providers/AuthProvider";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";
import type {
  BankConnectionDto,
  BankConnectionStatus,
  StartTrueLayerLinkResponse
} from "../../../src/types/api";

const pendingActionStatuses = new Set<BankConnectionStatus>([
  "connection_started",
  "consent_in_progress"
]);

const completionStatuses = new Set<BankConnectionStatus>([
  "connected_pending_sync",
  "connected",
  "synced",
  "disconnect_pending",
  "disconnect_failed",
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

const failureStatuses = new Set<BankConnectionStatus>([
  "disconnect_failed",
  "failed",
  "reauth_required",
  "expired",
  "revoked"
]);

const consentTimeoutMs = 90_000;
const aggressivePollDurationMs = 15_000;
const aggressivePollIntervalMs = 1_500;
const steadyPollIntervalMs = 5_000;
const refreshCoalesceWindowMs = 750;
const BANKING_ONGOING_LOG_THROTTLE_MS = 5 * 60 * 1000;
const BANKING_ONGOING_LOG_EVENTS = new Set([
  "poll_tick",
  "refresh_joined",
  "refresh_coalesced"
]);
const BANKING_ONGOING_REFRESH_REASONS = new Set([
  "poll",
  "app_resume",
  "deep_link_return"
]);
const bankingOngoingLogLastAt = new Map<string, number>();

type PendingConsentLink = {
  authorizationUrl: string;
  expiresAtUtc: string;
};

type BrowserPhase = "idle" | "opening_bank" | "awaiting_consent";

function shouldThrottleBankingLog(event: string, metadata?: Record<string, unknown>) {
  if (BANKING_ONGOING_LOG_EVENTS.has(event)) {
    return true;
  }

  if (event === "refresh_start" || event === "refresh_complete") {
    const reason = typeof metadata?.reason === "string" ? metadata.reason : null;
    return reason ? BANKING_ONGOING_REFRESH_REASONS.has(reason) : false;
  }

  return false;
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

function deriveUiState(
  browserPhase: BrowserPhase,
  awaitingConsentReturn: boolean,
  connection: BankConnectionDto | null,
  consentTimedOut: boolean
): ConnectionStatus {
  switch (connection?.status) {
    case "connected_pending_sync":
    case "connected":
      return "connected_pending_sync";
    case "sync_pending":
      return "syncing_data";
    case "synced":
      return "synced";
    case "failed":
    case "reauth_required":
    case "expired":
    case "revoked":
      return "reauth_required";
  }

  if (browserPhase === "opening_bank") {
    return "opening_bank";
  }

  const pendingOrMissing = !connection || pendingActionStatuses.has(connection.status);
  if (awaitingConsentReturn && (browserPhase === "awaiting_consent" || pendingOrMissing)) {
    return "awaiting_consent";
  }

  if (consentTimedOut && pendingOrMissing) {
    return "awaiting_consent";
  }

  switch (connection?.status) {
    case "connection_started":
    case "consent_in_progress":
      return "awaiting_consent";
    default:
      return "not_connected";
  }
}

export default function AddAccountModalScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ bankingResult?: string; connectionId?: string; intent?: string }>();
  const queryClient = useQueryClient();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const { playSuccess } = useFeedbackSound();

  const forceNewConnectionFlow = params.intent === "new";

  const [awaitingConsentReturn, setAwaitingConsentReturn] = useState(false);
  const [browserPhase, setBrowserPhase] = useState<BrowserPhase>("idle");
  const [pendingConsentLink, setPendingConsentLink] = useState<PendingConsentLink | null>(null);
  const [pendingConnectionId, setPendingConnectionId] = useState<string | null>(null);
  const [consentStartedAtMs, setConsentStartedAtMs] = useState<number | null>(null);
  const [returnStartedAtMs, setReturnStartedAtMs] = useState<number | null>(null);
  const [lastManualSyncUtc, setLastManualSyncUtc] = useState<string | null>(null);
  const [consentTimedOut, setConsentTimedOut] = useState(false);

  const connectionsQuery = useBankConnectionsQuery(!pendingConnectionId && !forceNewConnectionFlow);
  const activeConnectionQuery = useBankConnectionQuery(pendingConnectionId);
  const linkedBankAccountsQuery = useLinkedBankAccountsQuery();
  const startLinkMutation = useStartTrueLayerLinkMutation();
  const syncMutation = useSyncBankConnectionMutation();

  const successPlayedRef = useRef(false);
  const lastPolledStatusRef = useRef<string | null>(null);
  const lastUiStateRef = useRef<string | null>(null);
  const interactiveReturnRef = useRef<number | null>(null);
  const syncedInvalidationRef = useRef<string | null>(null);
  const earlyPortfolioInvalidationRef = useRef<string | null>(null);
  const refreshInFlightRef = useRef<Promise<void> | null>(null);
  const lastRefreshStartedAtRef = useRef(0);
  const processedDeepLinkRef = useRef<string | null>(null);

  const logBankingEvent = useCallback((event: string, metadata?: Record<string, unknown>) => {
    if (shouldThrottleBankingLog(event, metadata)) {
      const reasonKey = typeof metadata?.reason === "string" ? metadata.reason : "none";
      const throttleKey = `${event}:${reasonKey}`;
      const now = Date.now();
      const lastLoggedAt = bankingOngoingLogLastAt.get(throttleKey) ?? 0;
      if (now - lastLoggedAt < BANKING_ONGOING_LOG_THROTTLE_MS) {
        return;
      }

      bankingOngoingLogLastAt.set(throttleKey, now);
    }

    console.info("[Banking UX]", {
      event,
      timestampUtc: new Date().toISOString(),
      ...metadata
    });
  }, []);

  const invalidatePortfolioQueries = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.banking.connections }),
      queryClient.invalidateQueries({ queryKey: queryKeys.banking.connectedBanks }),
      queryClient.invalidateQueries({ queryKey: queryKeys.banking.accounts }),
      queryClient.invalidateQueries({ queryKey: queryKeys.accounts.all }),
      queryClient.invalidateQueries({ queryKey: queryKeys.transactions.all }),
      queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.summary })
    ]);
  }, [queryClient]);

  const latestConnection = useMemo(() => {
    if (forceNewConnectionFlow && !pendingConnectionId) {
      return null;
    }

    const list = connectionsQuery.data ?? [];
    return [...list].sort((a, b) => Date.parse(b.updatedUtc) - Date.parse(a.updatedUtc))[0] ?? null;
  }, [connectionsQuery.data, forceNewConnectionFlow, pendingConnectionId]);

  const activeConnection = useMemo(() => {
    if (pendingConnectionId) {
      return activeConnectionQuery.data ?? null;
    }

    if (forceNewConnectionFlow) {
      return null;
    }

    return latestConnection;
  }, [activeConnectionQuery.data, forceNewConnectionFlow, latestConnection, pendingConnectionId]);

  const linkedAccountNames = useMemo(() => {
    if (forceNewConnectionFlow && !pendingConnectionId) {
      return [];
    }

    const accounts = linkedBankAccountsQuery.data ?? [];
    const filtered = pendingConnectionId
      ? accounts.filter((account) => account.connectionId === pendingConnectionId)
      : accounts;
    const sorted = [...filtered].sort((a, b) => Date.parse(b.createdUtc) - Date.parse(a.createdUtc));

    return Array.from(
      new Set(
        sorted
          .map((account) => account.displayName.trim())
          .filter((name) => name.length > 0)
      )
    );
  }, [forceNewConnectionFlow, linkedBankAccountsQuery.data, pendingConnectionId]);

  const pendingLinkValid = useMemo(
    () => isLinkStillValid(pendingConsentLink, Date.now()),
    [pendingConsentLink]
  );

  const uiState = useMemo(
    () => deriveUiState(browserPhase, awaitingConsentReturn, activeConnection, consentTimedOut),
    [activeConnection, awaitingConsentReturn, browserPhase, consentTimedOut]
  );

  const shouldPoll =
    Boolean(pendingConnectionId) &&
    (uiState === "awaiting_consent" ||
      uiState === "connected_pending_sync" ||
      uiState === "syncing_data");

  const refreshBankingState = useCallback(
    async (reason: string, options?: { fullInvalidate?: boolean; force?: boolean }) => {
      const now = Date.now();
      if (refreshInFlightRef.current && !options?.force) {
        logBankingEvent("refresh_joined", {
          reason,
          connectionId: pendingConnectionId
        });
        return refreshInFlightRef.current;
      }

      if (
        !options?.force &&
        lastRefreshStartedAtRef.current > 0 &&
        now - lastRefreshStartedAtRef.current < refreshCoalesceWindowMs
      ) {
        logBankingEvent("refresh_coalesced", {
          reason,
          connectionId: pendingConnectionId,
          elapsedMs: now - lastRefreshStartedAtRef.current
        });
        return refreshInFlightRef.current ?? Promise.resolve();
      }

      lastRefreshStartedAtRef.current = now;

      const refreshPromise = (async () => {
        logBankingEvent("refresh_start", {
          reason,
          connectionId: pendingConnectionId,
          fullInvalidate: Boolean(options?.fullInvalidate)
        });

        const connectionRefresh = pendingConnectionId
          ? activeConnectionQuery.refetch()
          : connectionsQuery.refetch();

        await Promise.all([connectionRefresh, linkedBankAccountsQuery.refetch()]);

        if (options?.fullInvalidate) {
          await invalidatePortfolioQueries();
        }

        logBankingEvent("refresh_complete", {
          reason,
          connectionId: pendingConnectionId,
          elapsedMs: Date.now() - now
        });
      })();

      const trackedPromise = refreshPromise.finally(() => {
        if (refreshInFlightRef.current === trackedPromise) {
          refreshInFlightRef.current = null;
        }
      });

      refreshInFlightRef.current = trackedPromise;
      return trackedPromise;
    },
    [
      activeConnectionQuery,
      connectionsQuery,
      invalidatePortfolioQueries,
      linkedBankAccountsQuery,
      logBankingEvent,
      pendingConnectionId
    ]
  );

  const markReturnAttempt = useCallback(
    (reason: string, connectionId?: string | null) => {
      const startedAt = Date.now();
      setReturnStartedAtMs(startedAt);
      interactiveReturnRef.current = startedAt;
      setBrowserPhase("idle");
      logBankingEvent(reason, {
        connectionId: connectionId ?? pendingConnectionId,
        uiState
      });
    },
    [logBankingEvent, pendingConnectionId, uiState]
  );

  const launchConsentInBrowser = useCallback(
    async (url: string, connectionId?: string | null) => {
      logBankingEvent("browser_open_start", { connectionId: connectionId ?? pendingConnectionId });
      const canOpen = await NativeLinking.canOpenURL(url);
      if (!canOpen) {
        throw new Error("Could not open the bank consent page.");
      }

      await NativeLinking.openURL(url);
      logBankingEvent("browser_open_complete", { connectionId: connectionId ?? pendingConnectionId });
    },
    [logBankingEvent, pendingConnectionId]
  );

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
      setConsentTimedOut(false);
      processedDeepLinkRef.current = null;
      logBankingEvent("connect_start", {
        connectionId: response.connectionId,
        expiresAtUtc: response.expiresAtUtc,
        appReturnUri: buildBankConnectReturnUri()
      });
      await launchConsentInBrowser(response.authorizationUrl, response.connectionId);
      setBrowserPhase("awaiting_consent");
      logBankingEvent("awaiting_consent", { connectionId: response.connectionId });
    },
    [launchConsentInBrowser, logBankingEvent]
  );

  const handleConnectBank = async () => {
    try {
      setBrowserPhase("opening_bank");
      const response = await startLinkMutation.mutateAsync({ appReturnUri: buildBankConnectReturnUri() });
      await beginConsentSession(response);
    } catch (error) {
      setBrowserPhase("idle");
      throw error;
    }
  };

  const handleResumeConsent = async () => {
    if (pendingConsentLink && isLinkStillValid(pendingConsentLink, Date.now())) {
      setAwaitingConsentReturn(true);
      setBrowserPhase("opening_bank");
      logBankingEvent("resume_browser_flow", { connectionId: pendingConnectionId });
      await launchConsentInBrowser(pendingConsentLink.authorizationUrl, pendingConnectionId);
      setBrowserPhase("awaiting_consent");
      return;
    }

    const response = await startLinkMutation.mutateAsync({ appReturnUri: buildBankConnectReturnUri() });
    await beginConsentSession(response);
  };

  const handleManualRefresh = async () => {
    logBankingEvent("manual_refresh", { connectionId: pendingConnectionId, uiState });
    await refreshBankingState("manual_refresh", {
      fullInvalidate: activeConnection?.status === "synced",
      force: true
    });
  };

  const handleSyncNow = async () => {
    if (!activeConnection) {
      return;
    }

    logBankingEvent("manual_sync_start", { connectionId: activeConnection.id });
    const syncResult = await syncMutation.mutateAsync(activeConnection.id);
    setLastManualSyncUtc(syncResult.syncedAtUtc);
    await refreshBankingState("manual_sync_complete", { fullInvalidate: true, force: true });
    playSuccess();
  };

  useEffect(() => {
    logBankingEvent("screen_mount", { route: "accounts/connect-bank" });
  }, [logBankingEvent]);

  useEffect(() => {
    if (!awaitingConsentReturn || consentStartedAtMs === null) {
      setConsentTimedOut(false);
      return;
    }

    const elapsedMs = Date.now() - consentStartedAtMs;
    if (elapsedMs >= consentTimeoutMs) {
      setConsentTimedOut(true);
      return;
    }

    setConsentTimedOut(false);
    const timer = setTimeout(() => {
      setConsentTimedOut(true);
    }, consentTimeoutMs - elapsedMs);

    return () => clearTimeout(timer);
  }, [awaitingConsentReturn, consentStartedAtMs]);

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
      if (nextState !== "active" || (!awaitingConsentReturn && !pendingConnectionId)) {
        return;
      }

      markReturnAttempt("app_resume", pendingConnectionId);
      void refreshBankingState("app_resume");
    });

    return () => subscription.remove();
  }, [awaitingConsentReturn, markReturnAttempt, pendingConnectionId, refreshBankingState]);

  useEffect(() => {
    if (!params.bankingResult) {
      return;
    }

    const deepLinkSnapshot = `${params.bankingResult}:${typeof params.connectionId === "string" ? params.connectionId : ""}`;
    if (processedDeepLinkRef.current === deepLinkSnapshot) {
      return;
    }

    processedDeepLinkRef.current = deepLinkSnapshot;

    const returnedConnectionId =
      typeof params.connectionId === "string" && params.connectionId.trim().length > 0
        ? params.connectionId.trim()
        : pendingConnectionId;
    const bankingResult = params.bankingResult.toLowerCase();

    if (returnedConnectionId) {
      setPendingConnectionId(returnedConnectionId);
    }

    if (bankingResult === "success") {
      setAwaitingConsentReturn(true);
      markReturnAttempt("deep_link_return_success", returnedConnectionId);
      void refreshBankingState("deep_link_return_success", { force: true });
      return;
    }

    setAwaitingConsentReturn(false);
    setBrowserPhase("idle");
    setPendingConsentLink(null);
    setConsentTimedOut(false);
    markReturnAttempt("deep_link_return_error", returnedConnectionId);
    void refreshBankingState("deep_link_return_error", { force: true });
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
      awaitingConsentReturn,
      linkedAccountCount: linkedAccountNames.length
    });
  }, [activeConnection?.status, awaitingConsentReturn, linkedAccountNames.length, logBankingEvent, pendingConnectionId]);

  useEffect(() => {
    if (lastUiStateRef.current === uiState) {
      return;
    }

    lastUiStateRef.current = uiState;
    logBankingEvent("ui_state_transition", {
      connectionId: pendingConnectionId,
      uiState,
      consentTimedOut
    });
  }, [consentTimedOut, logBankingEvent, pendingConnectionId, uiState]);

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

    if (connectedFlowStatuses.has(status) && activeConnection) {
      setAwaitingConsentReturn(false);
      setBrowserPhase("idle");
      setPendingConsentLink(null);
      setConsentTimedOut(false);
      if (earlyPortfolioInvalidationRef.current !== activeConnection.id) {
        earlyPortfolioInvalidationRef.current = activeConnection.id;
        void invalidatePortfolioQueries();
      }
      if (!successPlayedRef.current && successStatuses.has(status)) {
        playSuccess();
        successPlayedRef.current = true;
      }
      return;
    }

    if (status === "synced" && activeConnection) {
      setAwaitingConsentReturn(false);
      setBrowserPhase("idle");
      setPendingConsentLink(null);
      setConsentTimedOut(false);
      if (earlyPortfolioInvalidationRef.current !== activeConnection.id) {
        earlyPortfolioInvalidationRef.current = activeConnection.id;
        void invalidatePortfolioQueries();
      }
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

    if (failureStatuses.has(status)) {
      setAwaitingConsentReturn(false);
      setBrowserPhase("idle");
      setConsentTimedOut(false);
      successPlayedRef.current = false;
    }
  }, [activeConnection, invalidatePortfolioQueries, playSuccess]);

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={"/login" as never} />;
  }

  const canUseConnectionsQueryState = !pendingConnectionId && !forceNewConnectionFlow;
  const connectionQueryError = pendingConnectionId
    ? activeConnectionQuery.error
    : canUseConnectionsQueryState
      ? connectionsQuery.error
      : null;
  const connectionQueryIsError = pendingConnectionId
    ? activeConnectionQuery.isError
    : canUseConnectionsQueryState
      ? connectionsQuery.isError
      : false;
  const completionReached = completionStatuses.has(activeConnection?.status ?? "not_connected");
  const showResumeAction =
    uiState === "awaiting_consent" &&
    (pendingActionStatuses.has(activeConnection?.status ?? "not_connected") ||
      (awaitingConsentReturn && pendingLinkValid));
  const showRefreshAction =
    uiState === "awaiting_consent" ||
    uiState === "connected_pending_sync" ||
    uiState === "syncing_data";

  const statusHelperText =
    uiState === "opening_bank"
      ? "Opening the secure bank consent page now. Stay here if your browser does not launch immediately."
      : uiState === "awaiting_consent" && consentTimedOut
        ? "We still have not seen the completed bank connection. If you already finished in the browser, tap Refresh. Otherwise reopen the bank consent page and try again."
        : uiState === "awaiting_consent"
          ? "Finish the bank consent flow in your browser. As soon as you return, we will start checking the saved connection."
          : uiState === "connected_pending_sync"
            ? "Connection confirmed. We are syncing the first account details now."
            : uiState === "syncing_data"
              ? "Connected to your bank. We are importing account details and recent transactions now."
              : uiState === "failed"
                ? "The bank connection exists, but data sync failed. You can retry sync without reconnecting."
                : uiState === "reauth_required"
                  ? "Provider access expired or failed. Reconnect your bank to continue syncing."
                  : undefined;

  const canSyncNow = activeConnection ? syncableStatuses.has(activeConnection.status) : false;
  const bankName = activeConnection?.providerDisplayName?.trim() || "Waiting for institution details";
  const lastSyncUtc =
    activeConnection?.lastSuccessfulSyncUtc ?? activeConnection?.lastSyncAttemptedUtc ?? null;
  const dateAdded = formatDateAdded(activeConnection?.createdUtc);
  const lastManualSyncLabel = formatDateAdded(
    lastManualSyncUtc ?? activeConnection?.lastSuccessfulSyncUtc ?? null
  );
  const lastSyncLabel = lastSyncUtc ? formatDateAdded(lastSyncUtc) : "Not synced yet";

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <View style={styles.mainContent}>
        <View style={styles.header}>
          <Text style={styles.title}>Bank Connection</Text>
        </View>

        {connectionQueryIsError ? (
          <ErrorState
            title="Could not load connection status"
            message={formatUnknownError(connectionQueryError)}
            onRetry={() => {
              void refreshBankingState("connections_error_retry", { force: true });
            }}
          />
        ) : null}

        {linkedBankAccountsQuery.isError ? (
          <ErrorState
            title="Could not load linked bank accounts"
            message={formatUnknownError(linkedBankAccountsQuery.error)}
            onRetry={() => {
              void refreshBankingState("linked_accounts_error_retry", { force: true });
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
                ? "The bank connection is confirmed. Tap Refresh if you want to check the projected accounts and balances now."
                : uiState === "syncing_data"
                  ? "We are still syncing your bank data. You can keep waiting or tap Refresh to reconcile now."
                  : consentTimedOut
                    ? "If you already finished in the browser, tap Refresh. Otherwise reopen the bank consent page and try again."
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

        <View style={styles.primaryActions}>
          <View style={styles.primaryActionRow}>
            <PrimaryButton
              label="Connect to your bank"
              onPress={() => {
                void handleConnectBank();
              }}
              isLoading={startLinkMutation.isPending}
              style={styles.connectBankButton}
            />

            <PrimaryButton
              label="Sync now"
              onPress={() => {
                void handleSyncNow();
              }}
              isLoading={syncMutation.isPending}
              disabled={!canSyncNow}
              style={styles.syncNowButton}
            />
          </View>

          <SecondaryButton
            label="Cancel"
            onPress={() => {
              logBankingEvent("modal_close", {
                connectionId: pendingConnectionId,
                uiState,
                backendStatus: activeConnection?.status ?? null,
                linkedAccountCount: linkedAccountNames.length,
                completionReached
              });
              router.back();
            }}
          />
        </View>
      </View>
    </ScreenContainer>
  );
}

const styles = createRuntimeStyleSheet(() => ({
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
    backgroundColor: surfaces.card,
    borderRadius: 6,
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
    borderRadius: 6,
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
  primaryActions: {
    gap: spacing[12]
  },
  primaryActionRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[10]
  },
  connectBankButton: {
    flex: 0.65
  },
  syncNowButton: {
    flex: 0.35
  }
}));


