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
  useBankEnrichmentProgressQuery,
  useBankConnectionQuery,
  useBankConnectionsQuery,
  useLinkedBankAccountsQuery,
  useLinkedBankCardsQuery,
  useStartTrueLayerLinkMutation
} from "../../../src/features/banking/useBanking";
import { useConnectBankCtaLabels } from "../../../src/features/banking/connectBankCta";
import {
  buildBankConnectReturnUri,
  sanitizeConnectBankReturnPath
} from "../../../src/features/banking/bankingLinking";
import { formatUnknownError } from "../../../src/lib/api/errors";
import { queryKeys } from "../../../src/lib/api/queryKeys";
import { useFeedbackSound } from "../../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../../src/providers/AuthProvider";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";
import type {
  BankConnectionDto,
  BankConnectionStatus,
  LinkedBankAccountDto,
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
const syncingStaleThresholdMs = 12 * 60 * 1000;
const BANKING_ONGOING_LOG_THROTTLE_MS = 5 * 60 * 1000;
const BANKING_ONGOING_LOG_EVENTS = new Set([
  "poll_tick",
  "refresh_joined",
  "refresh_coalesced"
]);
const queuedEnrichmentStages = new Set([
  "queued_for_sync",
  "needs_reclassification",
  "waiting_for_first_sync"
]);
const organizingEnrichmentStages = new Set([
  "categorizing",
  "waiting_for_counterparty"
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

type UiPhaseEvidence = {
  linkedAccountCount: number;
  importedTransactionCount: number;
  enrichmentStage: string | null;
  syncLifecyclePhase: string | null;
  lastSyncAttemptedUtc: string | null;
  lastSuccessfulSyncUtc: string | null;
  updatedUtc: string | null;
};

type StageVisualState = "complete" | "in_progress" | "pending" | "delayed" | "warning";

type ConnectionTimelineStage = {
  key: string;
  label: string;
  state: StageVisualState;
};

const timelineStageOrder = [
  "authorized",
  "connection_secured",
  "balances_fetched",
  "transactions_imported",
  "activity_organized"
] as const;

const timelineStageLabels: Record<(typeof timelineStageOrder)[number], string> = {
  authorized: "Authorized with bank",
  connection_secured: "Connection secured",
  balances_fetched: "Balances fetched",
  transactions_imported: "Transactions imported",
  activity_organized: "Activity organized"
};

function buildTimelineStages(
  stageState: Partial<Record<(typeof timelineStageOrder)[number], StageVisualState>>
): ConnectionTimelineStage[] {
  return timelineStageOrder.map((key) => ({
    key,
    label: timelineStageLabels[key],
    state: stageState[key] ?? "pending"
  }));
}

function deriveTimelineStages(
  uiState: ConnectionStatus,
  evidence: UiPhaseEvidence | null,
  connection: BankConnectionDto | null
): ConnectionTimelineStage[] {
  const hasImportedTransactions = (evidence?.importedTransactionCount ?? 0) > 0;

  switch (uiState) {
    case "opening_bank":
    case "awaiting_consent":
      return buildTimelineStages({
        authorized: "in_progress"
      });
    case "connected_pending_sync":
      return buildTimelineStages({
        authorized: "complete",
        connection_secured: "complete",
        balances_fetched: "in_progress"
      });
    case "syncing_data":
      return buildTimelineStages({
        authorized: "complete",
        connection_secured: "complete",
        balances_fetched: "complete",
        transactions_imported: "in_progress"
      });
    case "import_complete_enrichment_queued":
      return buildTimelineStages({
        authorized: "complete",
        connection_secured: "complete",
        balances_fetched: "complete",
        transactions_imported: "complete",
        activity_organized: "in_progress"
      });
    case "organizing_transactions":
      return buildTimelineStages({
        authorized: "complete",
        connection_secured: "complete",
        balances_fetched: "complete",
        transactions_imported: "complete",
        activity_organized: "in_progress"
      });
    case "sync_taking_longer_than_expected":
      return buildTimelineStages({
        authorized: "complete",
        connection_secured: "complete",
        balances_fetched: "complete",
        transactions_imported: hasImportedTransactions ? "complete" : "delayed",
        activity_organized: hasImportedTransactions ? "delayed" : "pending"
      });
    case "synced":
      return buildTimelineStages({
        authorized: "complete",
        connection_secured: "complete",
        balances_fetched: "complete",
        transactions_imported: "complete",
        activity_organized: "complete"
      });
    case "failed":
    case "reauth_required":
      return buildTimelineStages({
        authorized: "warning",
        connection_secured: connection ? "complete" : "warning",
        balances_fetched: hasImportedTransactions ? "complete" : "warning",
        transactions_imported: hasImportedTransactions ? "complete" : "warning",
        activity_organized: "warning"
      });
    case "not_connected":
    default:
      return buildTimelineStages({});
  }
}

function formatSafeCloseMessage(
  uiState: ConnectionStatus,
  safeToLeave: boolean,
  safeToClose: boolean,
  userActionRequired: boolean
) {
  if (userActionRequired) {
    return "Action is needed in NSFinance, but you can close this page now.";
  }

  if (!safeToLeave) {
    return "We are preparing your secure connection. Keep this page open for a moment.";
  }

  if (safeToClose) {
    return "You can close this page now. NSFinance will keep going in the background.";
  }

  if (uiState === "awaiting_consent" || uiState === "opening_bank") {
    return "Finish bank authorization in your browser. You can return to this page whenever you want.";
  }

  return "You can leave this page at any time while we finish the remaining steps.";
}

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

function normalizeText(value?: string | null) {
  if (!value) {
    return null;
  }

  const normalized = value.trim().replace(/\s+/g, " ");
  return normalized.length > 0 ? normalized : null;
}

function formatAccountFallback(accountType?: string | null, currency?: string | null) {
  const normalizedType = accountType?.trim().toLowerCase();
  const friendlyType =
    normalizedType === "transaction" || normalizedType === "current" || normalizedType === "checking"
      ? "current account"
      : normalizedType === "savings"
        ? "savings account"
        : normalizedType === "credit"
          ? "credit account"
          : normalizedType === "loan"
            ? "loan account"
            : "account";

  const resolvedCurrency = normalizeText(currency)?.toUpperCase() ?? "EUR";
  return `${resolvedCurrency} ${friendlyType}`;
}

function extractMaskedAccountHint(accountNumberMetadataJson?: string | null) {
  if (!accountNumberMetadataJson) {
    return null;
  }

  try {
    const parsed = JSON.parse(accountNumberMetadataJson) as Record<string, unknown>;
    const candidates = [
      parsed.iban,
      parsed.number,
      parsed.pan,
      parsed.masked_pan,
      (parsed.account_number as Record<string, unknown> | undefined)?.number,
      (parsed.sort_code_account_number as Record<string, unknown> | undefined)?.account_number
    ];

    for (const candidate of candidates) {
      if (typeof candidate !== "string") {
        continue;
      }

      const cleaned = candidate.replace(/[^a-z0-9]/gi, "");
      if (cleaned.length >= 4) {
        return cleaned.slice(-4).toUpperCase();
      }
    }
  } catch {
    return null;
  }

  return null;
}

function toTitleCase(value: string) {
  return value
    .split(" ")
    .map((word) => (word.length === 0 ? word : `${word[0].toUpperCase()}${word.slice(1).toLowerCase()}`))
    .join(" ");
}

function formatProviderName(value?: string | null) {
  const normalized = normalizeText(value);
  if (!normalized) {
    return "Bank";
  }

  const compact = normalized
    .replace(/^ob[-\s_]+/i, "")
    .replace(/[-_\s]+(ie|uk|gb|eu)$/i, "")
    .trim();

  const upper = compact.toUpperCase();
  const knownAcronyms = new Set(["AIB", "BOI", "PTSB", "TSB", "HSBC", "MBNA", "RBS"]);
  if (knownAcronyms.has(upper)) {
    return upper;
  }

  if (upper === "REVOLUT") {
    return "Revolut";
  }

  return toTitleCase(compact);
}

function looksLikeConnectedIdentity(candidate: string, connectedFullName?: string | null) {
  const normalizedConnected = normalizeText(connectedFullName);
  if (!normalizedConnected) {
    return false;
  }

  const tokenize = (value: string) =>
    value
      .toLowerCase()
      .split(" ")
      .map((token) => token.trim())
      .filter((token) => token.length > 0)
      .sort();

  const candidateTokens = tokenize(candidate);
  const connectedTokens = tokenize(normalizedConnected);
  if (candidateTokens.length < 2 || candidateTokens.length !== connectedTokens.length) {
    return false;
  }

  return candidateTokens.every((token, index) => token === connectedTokens[index]);
}

function formatLinkedAccountName(account: LinkedBankAccountDto, connectedFullName?: string | null) {
  const providerLabel = formatProviderName(account.providerDisplayName ?? account.providerId);
  const maskedHint = extractMaskedAccountHint(account.accountNumberMetadataJson);
  if (maskedHint) {
    return `${providerLabel} **${maskedHint}`;
  }

  const normalized = normalizeText(account.displayName);
  if (normalized && !looksLikeConnectedIdentity(normalized, connectedFullName)) {
    return providerLabel;
  }

  return providerLabel || formatAccountFallback(account.accountType, account.currency);
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

function mapSyncLifecyclePhaseToUiState(syncLifecyclePhase?: string | null): ConnectionStatus | null {
  switch (syncLifecyclePhase) {
    case "connecting":
      return "awaiting_consent";
    case "importing_bank_data":
      return "syncing_data";
    case "import_complete_enrichment_queued":
      return "import_complete_enrichment_queued";
    case "organizing_transactions":
      return "organizing_transactions";
    case "completed":
      return "synced";
    case "sync_taking_longer_than_expected":
      return "sync_taking_longer_than_expected";
    case "attention_required":
      return "reauth_required";
    default:
      return null;
  }
}

function parseIsoUtcToMs(value?: string | null): number | null {
  if (!value) {
    return null;
  }

  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}

function derivePostImportUiStateFromEvidence(
  connection: BankConnectionDto,
  evidence: UiPhaseEvidence
): ConnectionStatus | null {
  const stage = evidence.enrichmentStage?.trim().toLowerCase() ?? null;
  const hasLinkedAccounts = evidence.linkedAccountCount > 0;
  const importedTransactionCount = Math.max(0, evidence.importedTransactionCount);
  const hasImportedTransactions = importedTransactionCount > 0;
  const hasSyncEvidence = Boolean(evidence.lastSuccessfulSyncUtc || evidence.lastSyncAttemptedUtc);
  const hasPostImportEvidence = hasLinkedAccounts && (hasImportedTransactions || hasSyncEvidence);
  const statusIsSyncingLike =
    connection.status === "connected_pending_sync"
    || connection.status === "connected"
    || connection.status === "sync_pending";

  if (stage === "completed") {
    return "synced";
  }

  if (stage && organizingEnrichmentStages.has(stage) && hasPostImportEvidence) {
    return "organizing_transactions";
  }

  if (stage && queuedEnrichmentStages.has(stage) && hasPostImportEvidence) {
    return "import_complete_enrichment_queued";
  }

  if (!statusIsSyncingLike) {
    return null;
  }

  const syncEvidenceMs =
    parseIsoUtcToMs(evidence.lastSuccessfulSyncUtc)
    ?? parseIsoUtcToMs(evidence.lastSyncAttemptedUtc)
    ?? parseIsoUtcToMs(evidence.updatedUtc);
  const staleSyncing = syncEvidenceMs !== null && Date.now() - syncEvidenceMs >= syncingStaleThresholdMs;

  if (hasPostImportEvidence) {
    return "import_complete_enrichment_queued";
  }

  if (staleSyncing) {
    return "sync_taking_longer_than_expected";
  }

  return null;
}

function deriveUiState(
  browserPhase: BrowserPhase,
  awaitingConsentReturn: boolean,
  connection: BankConnectionDto | null,
  consentTimedOut: boolean,
  evidence: UiPhaseEvidence | null
): ConnectionStatus {
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

  if (connection && evidence) {
    const phaseMappedState = mapSyncLifecyclePhaseToUiState(evidence.syncLifecyclePhase);
    if (phaseMappedState) {
      return phaseMappedState;
    }

    const reconciledState = derivePostImportUiStateFromEvidence(connection, evidence);
    if (reconciledState) {
      return reconciledState;
    }
  }

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
  const params = useLocalSearchParams<{
    bankingResult?: string;
    connectionId?: string;
    intent?: string;
    returnTo?: string;
  }>();
  const queryClient = useQueryClient();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const { playSuccess } = useFeedbackSound();
  const connectBankCta = useConnectBankCtaLabels();

  const forceNewConnectionFlow = params.intent === "new";
  const cancelReturnPath = sanitizeConnectBankReturnPath(
    typeof params.returnTo === "string" ? params.returnTo : null
  );

  const [awaitingConsentReturn, setAwaitingConsentReturn] = useState(false);
  const [browserPhase, setBrowserPhase] = useState<BrowserPhase>("idle");
  const [pendingConsentLink, setPendingConsentLink] = useState<PendingConsentLink | null>(null);
  const [pendingConnectionId, setPendingConnectionId] = useState<string | null>(null);
  const [consentStartedAtMs, setConsentStartedAtMs] = useState<number | null>(null);
  const [returnStartedAtMs, setReturnStartedAtMs] = useState<number | null>(null);
  const [consentTimedOut, setConsentTimedOut] = useState(false);

  const connectionsQuery = useBankConnectionsQuery(!pendingConnectionId && !forceNewConnectionFlow);
  const activeConnectionQuery = useBankConnectionQuery(pendingConnectionId);
  const enrichmentProgressQuery = useBankEnrichmentProgressQuery(isAuthenticated && !isBootstrapping);
  const linkedBankAccountsQuery = useLinkedBankAccountsQuery();
  const linkedBankCardsQuery = useLinkedBankCardsQuery();
  const startLinkMutation = useStartTrueLayerLinkMutation();

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
      queryClient.invalidateQueries({ queryKey: queryKeys.banking.cards }),
      queryClient.invalidateQueries({ queryKey: queryKeys.banking.recurringPayments }),
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

  const reconnectConnectionId = useMemo(() => {
    if (forceNewConnectionFlow || !activeConnection) {
      return null;
    }

    return activeConnection.status === "reauth_required" || activeConnection.status === "expired"
      ? activeConnection.id
      : null;
  }, [activeConnection, forceNewConnectionFlow]);

  const activeLinkedAccountCount = useMemo(() => {
    if (!activeConnection || (forceNewConnectionFlow && !pendingConnectionId)) {
      return 0;
    }

    const accounts = linkedBankAccountsQuery.data ?? [];
    return accounts.filter((account) => account.connectionId === activeConnection.id).length;
  }, [
    activeConnection,
    forceNewConnectionFlow,
    linkedBankAccountsQuery.data,
    pendingConnectionId
  ]);

  const activeConnectionEnrichment = useMemo(() => {
    if (!activeConnection) {
      return null;
    }

    const perConnection = enrichmentProgressQuery.data?.connections ?? [];
    return perConnection.find((entry) => entry.connectionId === activeConnection.id) ?? null;
  }, [activeConnection, enrichmentProgressQuery.data?.connections]);

  const uiPhaseEvidence = useMemo<UiPhaseEvidence | null>(() => {
    if (!activeConnection) {
      return null;
    }

    return {
      linkedAccountCount: activeConnection.linkedAccountCount ?? activeLinkedAccountCount,
      importedTransactionCount:
        activeConnection.importedTransactionCount ?? activeConnectionEnrichment?.totalCount ?? 0,
      enrichmentStage: activeConnection.syncEnrichmentStage ?? activeConnectionEnrichment?.stage ?? null,
      syncLifecyclePhase: activeConnection.syncLifecyclePhase ?? null,
      lastSyncAttemptedUtc: activeConnection.lastSyncAttemptedUtc ?? null,
      lastSuccessfulSyncUtc: activeConnection.lastSuccessfulSyncUtc ?? null,
      updatedUtc: activeConnection.updatedUtc ?? null
    };
  }, [activeConnection, activeConnectionEnrichment, activeLinkedAccountCount]);

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
          .map((account) => formatLinkedAccountName(account, activeConnection?.connectedFullName))
          .filter((name) => name.length > 0)
      )
    );
  }, [activeConnection?.connectedFullName, forceNewConnectionFlow, linkedBankAccountsQuery.data, pendingConnectionId]);

  const linkedCardNames = useMemo(() => {
    if (forceNewConnectionFlow && !pendingConnectionId) {
      return [];
    }

    const cards = linkedBankCardsQuery.data ?? [];
    const filtered = pendingConnectionId
      ? cards.filter((card) => card.connectionId === pendingConnectionId)
      : cards;
    const sorted = [...filtered].sort((a, b) => Date.parse(b.createdUtc) - Date.parse(a.createdUtc));
    return Array.from(
      new Set(
        sorted
          .map((card) => card.displayName.trim())
          .filter((name) => name.length > 0)
      )
    );
  }, [forceNewConnectionFlow, linkedBankCardsQuery.data, pendingConnectionId]);

  const pendingLinkValid = useMemo(
    () => isLinkStillValid(pendingConsentLink, Date.now()),
    [pendingConsentLink]
  );

  const uiState = useMemo(
    () => deriveUiState(browserPhase, awaitingConsentReturn, activeConnection, consentTimedOut, uiPhaseEvidence),
    [activeConnection, awaitingConsentReturn, browserPhase, consentTimedOut, uiPhaseEvidence]
  );

  const shouldPoll =
    Boolean(pendingConnectionId) &&
    (uiState === "awaiting_consent" ||
      uiState === "connected_pending_sync" ||
      uiState === "syncing_data" ||
      uiState === "import_complete_enrichment_queued" ||
      uiState === "organizing_transactions" ||
      uiState === "sync_taking_longer_than_expected");

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

        await Promise.all([
          connectionRefresh,
          enrichmentProgressQuery.refetch(),
          linkedBankAccountsQuery.refetch(),
          linkedBankCardsQuery.refetch()
        ]);

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
      enrichmentProgressQuery,
      invalidatePortfolioQueries,
      linkedBankAccountsQuery,
      linkedBankCardsQuery,
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
        appReturnUri: buildBankConnectReturnUri(cancelReturnPath)
      });
      await launchConsentInBrowser(response.authorizationUrl, response.connectionId);
      setBrowserPhase("awaiting_consent");
      logBankingEvent("awaiting_consent", { connectionId: response.connectionId });
    },
    [cancelReturnPath, launchConsentInBrowser, logBankingEvent]
  );

  const handleConnectBank = async () => {
    try {
      setBrowserPhase("opening_bank");
      const response = await startLinkMutation.mutateAsync({
        appReturnUri: buildBankConnectReturnUri(cancelReturnPath),
        connectionId: reconnectConnectionId
      });
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

    const response = await startLinkMutation.mutateAsync({
      appReturnUri: buildBankConnectReturnUri(cancelReturnPath),
      connectionId: reconnectConnectionId
    });
    await beginConsentSession(response);
  };

  const handleManualRefresh = async () => {
    logBankingEvent("manual_refresh", { connectionId: pendingConnectionId, uiState });
    await refreshBankingState("manual_refresh", {
      fullInvalidate: activeConnection?.status === "synced",
      force: true
    });
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
  const userActionRequired = activeConnection?.userActionRequired === true;
  const safeToLeave = activeConnection?.safeToLeave ?? true;
  const safeToClose = activeConnection?.safeToClose
    ?? !(uiState === "opening_bank" || uiState === "awaiting_consent");
  const safeCloseMessage = formatSafeCloseMessage(
    uiState,
    safeToLeave,
    safeToClose,
    userActionRequired
  );
  const timelineStages = deriveTimelineStages(uiState, uiPhaseEvidence, activeConnection);
  const showResumeAction =
    uiState === "awaiting_consent" &&
    (pendingActionStatuses.has(activeConnection?.status ?? "not_connected") ||
      (awaitingConsentReturn && pendingLinkValid));
  const showRefreshAction =
    uiState === "awaiting_consent" ||
    uiState === "connected_pending_sync" ||
    uiState === "syncing_data" ||
    uiState === "import_complete_enrichment_queued" ||
    uiState === "organizing_transactions" ||
    uiState === "sync_taking_longer_than_expected";
  const isCompletedSynced = activeConnection?.status === "synced";
  const showPrimaryConnectAction =
    uiState === "not_connected"
    || uiState === "reauth_required"
    || uiState === "failed"
    || isCompletedSynced;
  const primaryActionLabel = uiState === "reauth_required" || uiState === "failed"
    ? "Reconnect bank account"
    : isCompletedSynced
    ? "Connect another bank account"
    : connectBankCta.primaryLabel;
  const secondaryActionLabel = completionReached ? "Return to activity" : "Close";

  const statusHelperText =
    uiState === "opening_bank"
      ? "Opening your secure bank authorization. If the browser does not open automatically, use the action below."
      : uiState === "awaiting_consent" && consentTimedOut
        ? "We have not received the completed authorization yet. If you already finished in the browser, refresh status."
        : uiState === "awaiting_consent"
          ? "Finish the secure bank authorization in your browser. We will continue automatically when you return."
        : uiState === "connected_pending_sync" || uiState === "syncing_data"
          ? "Your bank connection is secure. We are importing balances and transactions now."
          : uiState === "import_complete_enrichment_queued"
            ? "Import is complete and organization is queued. You can leave this page at any time."
            : uiState === "organizing_transactions"
              ? "Import is complete. NSFinance is now organizing your activity."
              : uiState === "sync_taking_longer_than_expected"
                ? "This bank is taking longer than usual. NSFinance will keep retrying and continue in the background."
              : uiState === "failed"
                ? "The connection was created, but sync did not finish. Reconnect or retry from the app."
                : uiState === "reauth_required"
                  ? "Bank access expired or was interrupted. Reconnect to resume syncing."
                  : undefined;
  const providerFreshnessNote =
    "Provider note: balances/transactions can be briefly cached, and pending payments appear only after booking.";

  const bankName = activeConnection?.providerDisplayName?.trim() || "Waiting for institution details";
  const lastSyncUtc =
    activeConnection?.lastSuccessfulSyncUtc ?? activeConnection?.lastSyncAttemptedUtc ?? null;
  const dateAdded = formatDateAdded(activeConnection?.createdUtc);
  const lastSyncLabel = lastSyncUtc ? formatDateAdded(lastSyncUtc) : "Not synced yet";
  const meaningfulCapabilities = [
    activeConnection?.supportsInfo ? "Identity info available" : null,
    activeConnection?.supportsCards ? "Cards available" : null,
    activeConnection?.supportsDirectDebits ? "Direct debits available" : null,
    activeConnection?.supportsStandingOrders ? "Standing orders available" : null
  ].filter((entry): entry is string => Boolean(entry));

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

        {linkedBankCardsQuery.isError ? (
          <ErrorState
            title="Could not load linked cards"
            message={formatUnknownError(linkedBankCardsQuery.error)}
            onRetry={() => {
              void refreshBankingState("linked_cards_error_retry", { force: true });
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

        <ConnectionStatusIndicator status={uiState} helperText={statusHelperText} />

        <View style={styles.safeCloseCard}>
          <Text style={styles.safeCloseTitle}>
            {safeToClose ? "Safe to close" : "Still finishing up"}
          </Text>
          <Text style={styles.safeCloseCopy}>{safeCloseMessage}</Text>
        </View>

        <View style={styles.timelineCard}>
          {timelineStages.map((stage) => (
            <View key={stage.key} style={styles.timelineRow}>
              <View
                style={[
                  styles.timelineDot,
                  stage.state === "complete"
                    ? styles.timelineDotComplete
                    : stage.state === "in_progress"
                      ? styles.timelineDotInProgress
                      : stage.state === "delayed"
                        ? styles.timelineDotDelayed
                        : stage.state === "warning"
                          ? styles.timelineDotWarning
                          : styles.timelineDotPending
                ]}
              />
              <Text
                style={[
                  styles.timelineLabel,
                  stage.state === "pending" ? styles.timelineLabelPending : null
                ]}
              >
                {stage.label}
              </Text>
            </View>
          ))}
        </View>

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
            <Text style={styles.metadataLabel}>Last sync: </Text>
            {lastSyncLabel}
          </Text>
          <Text style={styles.metadataRow}>
            <Text style={styles.metadataLabel}>Connection provider: </Text>
            TrueLayer
          </Text>
          {activeConnection?.connectedFullName ? (
            <Text style={styles.metadataRow}>
              <Text style={styles.metadataLabel}>Connected as: </Text>
              {activeConnection.connectedFullName}
            </Text>
          ) : null}
          {meaningfulCapabilities.length > 0 ? (
            <Text style={styles.metadataRow}>
              <Text style={styles.metadataLabel}>Capabilities: </Text>
              {meaningfulCapabilities.join(", ")}
            </Text>
          ) : null}
          <Text style={styles.metadataRow}>
            <Text style={styles.metadataLabel}>Date added: </Text>
            {dateAdded}
          </Text>
          {linkedCardNames.length > 0 ? (
            <>
              <Text style={styles.metadataRow}>
                <Text style={styles.metadataLabel}>Card name(s): </Text>
              </Text>
              {linkedCardNames.map((cardName) => (
                <Text key={cardName} style={styles.metadataListItem}>
                  - {cardName}
                </Text>
              ))}
            </>
          ) : null}
          <Text style={styles.metadataHint}>{providerFreshnessNote}</Text>
        </View>

        {showRefreshAction ? (
          <View style={styles.resumeCard}>
            <Text style={styles.resumeTitle}>
              {uiState === "connected_pending_sync"
                ? "Your connection is confirmed. Refresh is optional if you want to check progress right now."
                : uiState === "syncing_data"
                  ? "NSFinance is importing your bank data. You can keep using the app while this runs."
                  : uiState === "import_complete_enrichment_queued"
                    ? "Import finished and organization is queued. Refresh if you want a live update now."
                    : uiState === "organizing_transactions"
                      ? "Transactions are being organized. You can leave this page and come back later."
                      : uiState === "sync_taking_longer_than_expected"
                        ? "This provider is slower than usual right now. Refresh is optional while automatic retries continue."
                    : consentTimedOut
                      ? "If you already finished in the browser, refresh status. Otherwise reopen bank authorization."
                      : "Refresh is optional. NSFinance is already handling this flow."}
            </Text>
            <View style={styles.resumeActions}>
              <SecondaryButton
                label={
                  uiState === "syncing_data"
                  || uiState === "import_complete_enrichment_queued"
                  || uiState === "organizing_transactions"
                  || uiState === "sync_taking_longer_than_expected"
                    ? "Refresh sync status"
                    : "Refresh status"
                }
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
          {showPrimaryConnectAction ? (
            <PrimaryButton
              label={primaryActionLabel}
              onPress={() => {
                void handleConnectBank();
              }}
              isLoading={startLinkMutation.isPending}
              style={styles.connectBankButton}
            />
          ) : null}

          <SecondaryButton
            label={secondaryActionLabel}
            onPress={() => {
              logBankingEvent("modal_close", {
                connectionId: pendingConnectionId,
                uiState,
                backendStatus: activeConnection?.status ?? null,
                linkedAccountCount: linkedAccountNames.length,
                completionReached,
                returnTo: cancelReturnPath
              });
              if (cancelReturnPath) {
                router.replace(cancelReturnPath as never);
                return;
              }

              if (typeof router.canGoBack === "function" && router.canGoBack()) {
                router.back();
                return;
              }

              router.replace("/(tabs)" as never);
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
  safeCloseCard: {
    borderWidth: 1,
    borderColor: "rgba(29,186,114,0.35)",
    backgroundColor: "rgba(16,34,24,0.55)",
    borderRadius: 6,
    padding: spacing[12],
    gap: spacing[6]
  },
  safeCloseTitle: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  safeCloseCopy: {
    color: palette.textSecondary,
    ...typography.caption
  },
  timelineCard: {
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.card,
    borderRadius: 6,
    padding: spacing[12],
    gap: spacing[8]
  },
  timelineRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  timelineDot: {
    width: 10,
    height: 10,
    borderRadius: 5
  },
  timelineDotComplete: {
    backgroundColor: palette.success
  },
  timelineDotInProgress: {
    backgroundColor: palette.caution
  },
  timelineDotPending: {
    backgroundColor: palette.textMuted
  },
  timelineDotDelayed: {
    backgroundColor: "#F28C28"
  },
  timelineDotWarning: {
    backgroundColor: palette.negative
  },
  timelineLabel: {
    color: palette.textPrimary,
    ...typography.body2
  },
  timelineLabelPending: {
    color: palette.textSecondary
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
  metadataHint: {
    color: palette.textSecondary,
    ...typography.caption
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
  connectBankButton: {
    width: "100%"
  }
}));


