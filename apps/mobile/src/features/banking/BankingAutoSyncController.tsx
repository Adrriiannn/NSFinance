import { useCallback, useEffect, useMemo, useRef } from "react";
import { AppState, type AppStateStatus } from "react-native";
import { useAuthSession } from "../../providers/AuthProvider";
import { useConnectedBanksQuery, useGlobalBankSyncMutation } from "./useBanking";

const DEFAULT_FOREGROUND_AUTO_SYNC_CHECK_INTERVAL_MINUTES = 10;
const MIN_FOREGROUND_AUTO_SYNC_CHECK_INTERVAL_MINUTES = 1;
const AUTO_SYNC_MIN_TRIGGER_GAP_MS = 30_000;

const configuredAutoSyncCheckIntervalMinutes = Number(
  process.env.EXPO_PUBLIC_BANKING_AUTO_SYNC_INTERVAL_MINUTES
);

const resolvedAutoSyncCheckIntervalMinutes = Number.isFinite(configuredAutoSyncCheckIntervalMinutes)
  ? Math.max(
      MIN_FOREGROUND_AUTO_SYNC_CHECK_INTERVAL_MINUTES,
      Math.trunc(configuredAutoSyncCheckIntervalMinutes)
    )
  : DEFAULT_FOREGROUND_AUTO_SYNC_CHECK_INTERVAL_MINUTES;

const FOREGROUND_AUTO_SYNC_CHECK_INTERVAL_MS = resolvedAutoSyncCheckIntervalMinutes * 60 * 1000;

export function BankingAutoSyncController() {
  const { isAuthenticated, isBootstrapping, session } = useAuthSession();
  const connectedBanksQuery = useConnectedBanksQuery();
  const globalSyncMutation = useGlobalBankSyncMutation();
  const lastAutoSyncTriggerAtRef = useRef(0);
  const activeSessionIdRef = useRef<string | null>(null);

  const hasLinkedConnections = useMemo(() => {
    const connectedBanks = connectedBanksQuery.data;
    if (!connectedBanks) {
      return false;
    }

    return connectedBanks.activeConnections.length + connectedBanks.attentionConnections.length > 0;
  }, [connectedBanksQuery.data]);

  const triggerAutoSync = useCallback(
    async (source: string) => {
      if (!isAuthenticated || isBootstrapping || !hasLinkedConnections) {
        return;
      }

      if (globalSyncMutation.isPending) {
        return;
      }

      const nowMs = Date.now();
      if (nowMs - lastAutoSyncTriggerAtRef.current < AUTO_SYNC_MIN_TRIGGER_GAP_MS) {
        return;
      }

      lastAutoSyncTriggerAtRef.current = nowMs;
      console.info("[Banking Sync]", {
        event: "auto_sync_evaluated",
        source,
        hasLinkedConnections
      });

      try {
        const result = await globalSyncMutation.mutateAsync({
          trigger: "auto",
          source
        });
        console.info("[Banking Sync]", {
          event: "auto_sync_result",
          source,
          outcome: result.outcome,
          dueNow: result.dueNow,
          eligibleConnectionCount: result.eligibleConnectionCount,
          changedConnectionCount: result.changedConnectionCount
        });
      } catch (error) {
        console.info("[Banking Sync]", {
          event: "auto_sync_failed",
          source,
          message: error instanceof Error ? error.message : "unknown_auto_sync_error"
        });
      }
    },
    [
      globalSyncMutation,
      hasLinkedConnections,
      isAuthenticated,
      isBootstrapping
    ]
  );

  useEffect(() => {
    if (!isAuthenticated || isBootstrapping || !hasLinkedConnections) {
      return;
    }

    if (AppState.currentState === "active") {
      void triggerAutoSync("session_active_mount");
    }

    const intervalId = setInterval(() => {
      if (AppState.currentState !== "active") {
        return;
      }

      void triggerAutoSync("foreground_interval");
    }, FOREGROUND_AUTO_SYNC_CHECK_INTERVAL_MS);

    const appStateSubscription = AppState.addEventListener("change", (nextState: AppStateStatus) => {
      if (nextState !== "active") {
        return;
      }

      void triggerAutoSync("app_resume");
    });

    return () => {
      clearInterval(intervalId);
      appStateSubscription.remove();
    };
  }, [hasLinkedConnections, isAuthenticated, isBootstrapping, triggerAutoSync]);

  useEffect(() => {
    if (!isAuthenticated || !session?.sessionId || isBootstrapping || !hasLinkedConnections) {
      activeSessionIdRef.current = null;
      return;
    }

    if (activeSessionIdRef.current === session.sessionId) {
      return;
    }

    activeSessionIdRef.current = session.sessionId;
    void triggerAutoSync("post_login_session_entry");
  }, [hasLinkedConnections, isAuthenticated, isBootstrapping, session?.sessionId, triggerAutoSync]);

  return null;
}
