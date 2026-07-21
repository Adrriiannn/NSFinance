import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  AppState,
  PanResponder,
  Pressable,
  Text,
  useWindowDimensions,
  View
} from "react-native";
import Svg, { Circle, G } from "react-native-svg";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { getActivityFeedInteracting } from "../../features/activity/activityFeedRuntime";
import {
  getEnrichmentDialState,
  setEnrichmentDialState
} from "../../features/banking/enrichmentDial.storage";
import {
  enrichmentCompletionInvalidationRoots,
  shouldRunEnrichmentCompletionInvalidation
} from "../../features/banking/enrichmentLiveInvalidation";
import { showFlashMessage } from "../../lib/flashMessage";
import {
  useBankEnrichmentProgressQuery,
  useConnectedBanksQuery
} from "../../features/banking/useBanking";
import { useAuthSession } from "../../providers/AuthProvider";
import {
  createRuntimeStyleSheet,
  palette,
  spacing,
  surfaces,
  typography
} from "../../theme/tokens";

const AUTO_OPEN_VISIBLE_MS = 5_000;
const LIVE_INVALIDATION_INTERVAL_MS = 6_000;
const DIAL_SIZE = 52;
const DIAL_MARGIN = spacing[12];
const DETAILS_WIDTH = 236;
const DETAILS_HEIGHT = 110;
const POPUP_GAP = 10;
const DIAL_BOTTOM_CLEARANCE = 88;
const RING_SIZE = DIAL_SIZE - 4;
const RING_STROKE_WIDTH = 3.5;
const PROGRESS_ANIMATION_TICK_MS = 33;
const COMPLETION_AUTO_DISMISS_MS = 1_000;

type DialPosition = {
  left: number;
  top: number;
};

function formatCompactNumber(value: number) {
  return new Intl.NumberFormat("en-GB").format(value);
}

function buildProgressSignature(progress: {
  stage: string;
  totalCount: number;
  processedCount: number;
  remainingCount: number;
  lastUpdatedUtc: string | null;
  connections: ReadonlyArray<{
    connectionId: string;
    stage: string;
    totalCount: number;
    processedCount: number;
    remainingCount: number;
    lastUpdatedUtc: string | null;
  }>;
}) {
  const connectionParts = progress.connections
    .map((connection) =>
      [
        connection.connectionId,
        connection.stage,
        connection.totalCount,
        connection.processedCount,
        connection.remainingCount,
        connection.lastUpdatedUtc ?? ""
      ].join(":"))
    .sort();

  return [
    progress.stage,
    progress.totalCount,
    progress.processedCount,
    progress.remainingCount,
    progress.lastUpdatedUtc ?? "",
    connectionParts.join("|")
  ].join("::");
}

function clampDialPosition(
  position: DialPosition,
  screenWidth: number,
  screenHeight: number,
  minTop: number,
  maxTop: number
): DialPosition {
  const minLeft = DIAL_MARGIN;
  const maxLeft = Math.max(minLeft, screenWidth - DIAL_SIZE - DIAL_MARGIN);
  return {
    left: Math.max(minLeft, Math.min(position.left, maxLeft)),
    top: Math.max(minTop, Math.min(position.top, maxTop))
  };
}

function resolveDetailsPlacement(
  position: DialPosition,
  screenWidth: number,
  screenHeight: number
) {
  const spaceLeft = position.left - DIAL_MARGIN;
  const spaceRight = screenWidth - (position.left + DIAL_SIZE) - DIAL_MARGIN;
  const canPlaceLeft = spaceLeft >= DETAILS_WIDTH + POPUP_GAP;
  const canPlaceRight = spaceRight >= DETAILS_WIDTH + POPUP_GAP;

  const minTop = DIAL_MARGIN;
  const maxTop = Math.max(minTop, screenHeight - DETAILS_HEIGHT - DIAL_MARGIN);
  const alignedTop = Math.max(minTop, Math.min(position.top - 8, maxTop));

  if (canPlaceLeft) {
    return {
      left: position.left - DETAILS_WIDTH - POPUP_GAP,
      top: alignedTop
    };
  }

  if (canPlaceRight) {
    return {
      left: position.left + DIAL_SIZE + POPUP_GAP,
      top: alignedTop
    };
  }

  const centeredLeft = Math.max(
    DIAL_MARGIN,
    Math.min(position.left + DIAL_SIZE / 2 - DETAILS_WIDTH / 2, screenWidth - DETAILS_WIDTH - DIAL_MARGIN)
  );
  const aboveTop = position.top - DETAILS_HEIGHT - POPUP_GAP;
  if (aboveTop >= DIAL_MARGIN) {
    return { left: centeredLeft, top: aboveTop };
  }

  const belowTop = position.top + DIAL_SIZE + POPUP_GAP;
  const clampedBelowTop = Math.max(DIAL_MARGIN, Math.min(belowTop, screenHeight - DETAILS_HEIGHT - DIAL_MARGIN));
  return { left: centeredLeft, top: clampedBelowTop };
}

function stageIndicatesActiveWork(stage: string | null | undefined) {
  if (!stage) {
    return false;
  }

  return stage === "queued_for_sync"
    || stage === "waiting_for_first_sync"
    || stage === "needs_reclassification"
    || stage === "categorizing"
    || stage === "waiting_for_counterparty"
    || stage === "sync_stalled"
    || stage === "historical_backfill";
}

export function GlobalEnrichmentProgressDial() {
  const insets = useSafeAreaInsets();
  const queryClient = useQueryClient();
  const { width: screenWidth, height: screenHeight } = useWindowDimensions();
  const { isAuthenticated, isBootstrapping, session } = useAuthSession();
  const userId = session?.user.id ?? null;

  const [detailsVisible, setDetailsVisible] = useState(false);
  const [dotCount, setDotCount] = useState(1);
  const [dismissedCompletedSignature, setDismissedCompletedSignature] = useState<string | null>(null);
  const [animatedProcessedCount, setAnimatedProcessedCount] = useState(0);

  const minTop = insets.top + spacing[16];
  const maxTop = Math.max(minTop, screenHeight - insets.bottom - DIAL_SIZE - DIAL_BOTTOM_CLEARANCE);
  const defaultPosition = useMemo<DialPosition>(
    () => ({
      left: Math.max(DIAL_MARGIN, screenWidth - DIAL_SIZE - spacing[16]),
      top: insets.top + DIAL_BOTTOM_CLEARANCE
    }),
    [insets.top, screenWidth]
  );
  const [dialPosition, setDialPosition] = useState<DialPosition>(defaultPosition);
  const dialPositionRef = useRef<DialPosition>(defaultPosition);
  const dragStartRef = useRef<DialPosition>(defaultPosition);
  const autoCloseTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const completionAutoDismissTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const previousHasActiveWorkRef = useRef(false);
  const previousInvalidationWorkRef = useRef(false);
  const runTotalRef = useRef<number | null>(null);
  const didHydrateRef = useRef(false);
  const announcedCompletionSignatureRef = useRef<string | null>(null);

  const enrichmentQuery = useBankEnrichmentProgressQuery(isAuthenticated && !isBootstrapping);
  const connectedBanksQuery = useConnectedBanksQuery(isAuthenticated && !isBootstrapping);
  const progress = enrichmentQuery.data;
  const activeConnections = connectedBanksQuery.data?.activeConnections ?? [];
  const attentionConnections = connectedBanksQuery.data?.attentionConnections ?? [];
  const hasConnectedBanks = Boolean(
    activeConnections.length > 0
  );
  const hasDisconnectLifecycleState = Boolean(
    [...activeConnections, ...attentionConnections].some((connection) =>
      connection.status === "disconnect_pending"
      || connection.status === "disconnect_failed"
      || connection.status === "revoked")
  );

  useEffect(() => {
    dialPositionRef.current = dialPosition;
  }, [dialPosition]);

  useEffect(() => {
    if (!isAuthenticated || isBootstrapping || didHydrateRef.current) {
      return;
    }

    didHydrateRef.current = true;
    let cancelled = false;

    void (async () => {
      const persisted = await getEnrichmentDialState(userId);
      if (cancelled) {
        return;
      }

      if (persisted) {
        const clampedPosition = clampDialPosition(
          { left: persisted.left, top: persisted.top },
          screenWidth,
          screenHeight,
          minTop,
          maxTop
        );
        dialPositionRef.current = clampedPosition;
        setDialPosition(clampedPosition);
        setDismissedCompletedSignature(persisted.dismissedCompletedSignature ?? null);
      } else {
        const clampedDefault = clampDialPosition(defaultPosition, screenWidth, screenHeight, minTop, maxTop);
        dialPositionRef.current = clampedDefault;
        setDialPosition(clampedDefault);
        setDismissedCompletedSignature(null);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [defaultPosition, isAuthenticated, isBootstrapping, maxTop, minTop, screenHeight, screenWidth, userId]);

  useEffect(() => {
    const clamped = clampDialPosition(dialPositionRef.current, screenWidth, screenHeight, minTop, maxTop);
    if (clamped.left !== dialPositionRef.current.left || clamped.top !== dialPositionRef.current.top) {
      dialPositionRef.current = clamped;
      setDialPosition(clamped);
    }
  }, [maxTop, minTop, screenHeight, screenWidth]);

  const pendingStage = progress ? stageIndicatesActiveWork(progress.stage) : false;

  const hasPendingConnection = Boolean(
    progress?.connections?.some((connection) =>
      connection.inProgress
      || connection.remainingCount > 0
      || stageIndicatesActiveWork(connection.stage))
  );

  const hasIncompleteSnapshot = Boolean(
    progress
    && progress.totalCount > 0
    && progress.processedCount < progress.totalCount
    && !progress.completed
  );

  const hasActiveWork = Boolean(
    progress && (
      progress.inProgress
      || hasPendingConnection
      || progress.remainingCount > 0
      || pendingStage
      || hasIncompleteSnapshot
    )
  );

  useEffect(() => {
    if (!progress) {
      return;
    }

    const observedTotal = Math.max(
      0,
      progress.totalCount ?? 0,
      progress.remainingCount ?? 0,
      progress.processedCount ?? 0
    );

    if (hasActiveWork) {
      runTotalRef.current = Math.max(runTotalRef.current ?? 0, observedTotal);
      return;
    }

    if (progress.completed && progress.remainingCount === 0) {
      runTotalRef.current = null;
    }
  }, [
    hasActiveWork,
    progress?.completed,
    progress?.processedCount,
    progress?.remainingCount,
    progress?.totalCount
  ]);

  const hasCompletionSnapshot = Boolean(progress && (progress.totalCount > 0 || progress.processedCount > 0));
  const isCompletedState = Boolean(progress && !hasActiveWork && progress.completed && hasCompletionSnapshot);
  const progressSignature = progress
    ? buildProgressSignature(progress)
    : null;

  const completedDismissed = Boolean(
    isCompletedState
    && progressSignature
    && dismissedCompletedSignature
    && dismissedCompletedSignature === progressSignature
  );

  const shouldShowDial = Boolean(
    (hasConnectedBanks || hasActiveWork)
    && (
      hasActiveWork
      || (isCompletedState && !completedDismissed && !hasDisconnectLifecycleState)
    )
  );
  const rawProcessedCount = progress?.processedCount ?? 0;
  const rawTotalCount = progress?.totalCount ?? 0;
  const rawRemainingCount = progress?.remainingCount ?? Math.max(0, rawTotalCount - rawProcessedCount);
  const runTotal = runTotalRef.current ?? 0;
  const displayTotalCount = Math.max(rawTotalCount, rawRemainingCount, runTotal);
  const derivedProcessedCount = hasActiveWork
    ? Math.max(0, displayTotalCount - rawRemainingCount)
    : rawProcessedCount;
  const targetProcessedCount = Math.max(rawProcessedCount, derivedProcessedCount);
  const displayRemainingCount = rawRemainingCount;

  useEffect(() => {
    setAnimatedProcessedCount((current) => {
      if (!hasActiveWork) {
        return targetProcessedCount;
      }

      if (targetProcessedCount < current) {
        return targetProcessedCount;
      }

      return current;
    });
  }, [targetProcessedCount, hasActiveWork]);

  useEffect(() => {
    if (!progress) {
      return;
    }

    if (animatedProcessedCount >= targetProcessedCount) {
      return;
    }

    const timer = setInterval(() => {
      setAnimatedProcessedCount((current) => {
        if (current >= targetProcessedCount) {
          return current;
        }

        const delta = targetProcessedCount - current;
        const minStep = isCompletedState ? 16 : 1;
        const maxStep = isCompletedState ? 500 : 120;
        const scaledStep = Math.ceil(delta * (isCompletedState ? 0.38 : 0.16));
        const step = Math.max(minStep, Math.min(maxStep, scaledStep, delta));

        return current + step;
      });
    }, PROGRESS_ANIMATION_TICK_MS);

    return () => {
      clearInterval(timer);
    };
  }, [animatedProcessedCount, isCompletedState, progress, targetProcessedCount]);

  const liveProcessedCount = hasActiveWork
    ? Math.min(animatedProcessedCount, targetProcessedCount)
    : targetProcessedCount;
  const liveRemainingCount = hasActiveWork
    ? Math.max(0, displayTotalCount - liveProcessedCount)
    : displayRemainingCount;
  const waitingForVisualCompletion = Boolean(
    isCompletedState && displayTotalCount > 0 && liveProcessedCount < displayTotalCount
  );
  const activeVisualState = hasActiveWork || waitingForVisualCompletion;
  const progressPercent = displayTotalCount > 0
    ? Math.max(0, Math.min(100, Math.round((liveProcessedCount / displayTotalCount) * 100)))
    : 0;
  const stageLabel = !hasConnectedBanks
    ? "Connect a bank account"
    : isCompletedState && !waitingForVisualCompletion
      ? "Sync complete"
      : liveProcessedCount > 0
        ? "Syncing transactions"
        : "Starting sync";
  const popupMode: "before" | "active" | "completed" = !hasConnectedBanks
    ? "before"
    : activeVisualState
      ? "active"
      : "completed";
  const titleText = activeVisualState
    ? `Organizing transactions${".".repeat(dotCount)}`
    : isCompletedState
      ? "Transactions organized."
      : "No transactions.";

  useEffect(() => {
    if (!activeVisualState) {
      setDotCount(1);
      return;
    }

    const timer = setInterval(() => {
      setDotCount((current) => (current >= 3 ? 1 : current + 1));
    }, 450);

    return () => {
      clearInterval(timer);
    };
  }, [activeVisualState]);

  useEffect(() => {
    if (!hasActiveWork || !dismissedCompletedSignature) {
      return;
    }

    setDismissedCompletedSignature(null);
    void setEnrichmentDialState(
      {
        left: dialPositionRef.current.left,
        top: dialPositionRef.current.top,
        dismissedCompletedSignature: null
      },
      userId
    );
  }, [dismissedCompletedSignature, hasActiveWork, userId]);

  useEffect(() => {
    if (!isCompletedState || !progressSignature || completedDismissed || hasDisconnectLifecycleState) {
      if (completionAutoDismissTimerRef.current) {
        clearTimeout(completionAutoDismissTimerRef.current);
        completionAutoDismissTimerRef.current = null;
      }
      return;
    }

    if (announcedCompletionSignatureRef.current !== progressSignature) {
      announcedCompletionSignatureRef.current = progressSignature;
      showFlashMessage("Transactions were categorized successfully.");
    }

    if (completionAutoDismissTimerRef.current) {
      clearTimeout(completionAutoDismissTimerRef.current);
    }

    completionAutoDismissTimerRef.current = setTimeout(() => {
      setDetailsVisible(false);
      setDismissedCompletedSignature(progressSignature);
      void setEnrichmentDialState(
        {
          left: dialPositionRef.current.left,
          top: dialPositionRef.current.top,
          dismissedCompletedSignature: progressSignature
        },
        userId
      );
      completionAutoDismissTimerRef.current = null;
    }, COMPLETION_AUTO_DISMISS_MS);

    return () => {
      if (completionAutoDismissTimerRef.current) {
        clearTimeout(completionAutoDismissTimerRef.current);
        completionAutoDismissTimerRef.current = null;
      }
    };
  }, [completedDismissed, hasDisconnectLifecycleState, isCompletedState, progressSignature, userId]);

  useEffect(() => {
    const becameActive = hasActiveWork && !previousHasActiveWorkRef.current;
    previousHasActiveWorkRef.current = hasActiveWork;

    if (!hasActiveWork) {
      if (autoCloseTimerRef.current) {
        clearTimeout(autoCloseTimerRef.current);
        autoCloseTimerRef.current = null;
      }
      return;
    }

    if (!becameActive || detailsVisible) {
      return;
    }

    setDetailsVisible(true);
    if (autoCloseTimerRef.current) {
      clearTimeout(autoCloseTimerRef.current);
    }

    autoCloseTimerRef.current = setTimeout(() => {
      setDetailsVisible((current) => (current ? false : current));
      autoCloseTimerRef.current = null;
    }, AUTO_OPEN_VISIBLE_MS);
  }, [detailsVisible, hasActiveWork]);

  useEffect(() => {
    if (shouldShowDial) {
      return;
    }

    setDetailsVisible(false);
  }, [shouldShowDial]);

  useEffect(() => {
    if (!hasActiveWork) {
      return;
    }

    const intervalId = setInterval(() => {
      if (AppState.currentState !== "active") {
        return;
      }

      const queryOperations: Promise<unknown>[] = [
        queryClient.invalidateQueries({
          predicate: (query) =>
            Array.isArray(query.queryKey)
            && (query.queryKey[0] === "accounts" || query.queryKey[0] === "dashboard")
        })
      ];

      if (!getActivityFeedInteracting()) {
        queryOperations.push(
          queryClient.invalidateQueries({
            predicate: (query) =>
              Array.isArray(query.queryKey)
              && query.queryKey[0] === "transactions"
          })
        );
      }

      void Promise.all(queryOperations);
    }, LIVE_INVALIDATION_INTERVAL_MS);

    return () => {
      clearInterval(intervalId);
    };
  }, [hasActiveWork, queryClient]);

  useEffect(() => {
    const hadActiveWork = previousInvalidationWorkRef.current;
    previousInvalidationWorkRef.current = hasActiveWork;
    if (!shouldRunEnrichmentCompletionInvalidation(hadActiveWork, hasActiveWork)) {
      return;
    }

    // Rows categorized between the last interval tick (or a tick skipped
    // mid-scroll) and completion are never pulled by the loop above; one final
    // pass converges every root that loop refreshes - transactions, accounts,
    // and dashboard. Ungated on feed interaction — the Activity feed's own
    // commit gate defers re-rendering.
    void Promise.all(
      enrichmentCompletionInvalidationRoots.map((root) =>
        queryClient.invalidateQueries({ queryKey: root })
      )
    );
  }, [hasActiveWork, queryClient]);

  useEffect(() => {
    return () => {
      if (autoCloseTimerRef.current) {
        clearTimeout(autoCloseTimerRef.current);
      }
      if (completionAutoDismissTimerRef.current) {
        clearTimeout(completionAutoDismissTimerRef.current);
      }
    };
  }, []);

  const panResponder = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponder: (_event, gestureState) =>
          Math.abs(gestureState.dx) > 6 || Math.abs(gestureState.dy) > 6,
        onPanResponderGrant: () => {
          dragStartRef.current = dialPositionRef.current;
        },
        onPanResponderMove: (_event, gestureState) => {
          const next = clampDialPosition(
            {
              left: dragStartRef.current.left + gestureState.dx,
              top: dragStartRef.current.top + gestureState.dy
            },
            screenWidth,
            screenHeight,
            minTop,
            maxTop
          );
          dialPositionRef.current = next;
          setDialPosition(next);
        },
        onPanResponderRelease: () => {
          void setEnrichmentDialState(
            {
              left: dialPositionRef.current.left,
              top: dialPositionRef.current.top,
              dismissedCompletedSignature
            },
            userId
          );
        },
        onPanResponderTerminate: () => {
          void setEnrichmentDialState(
            {
              left: dialPositionRef.current.left,
              top: dialPositionRef.current.top,
              dismissedCompletedSignature
            },
            userId
          );
        }
      }),
    [dismissedCompletedSignature, maxTop, minTop, screenHeight, screenWidth, userId]
  );

  const detailsPlacement = useMemo(
    () => resolveDetailsPlacement(dialPosition, screenWidth, screenHeight),
    [dialPosition, screenHeight, screenWidth]
  );

  const ringProgress = isCompletedState && !waitingForVisualCompletion ? 100 : progressPercent;
  const ringRadius = (RING_SIZE / 2) - 2;
  const ringCircumference = 2 * Math.PI * ringRadius;
  const ringStrokeOffset = ringCircumference - ((Math.max(0, Math.min(100, ringProgress)) / 100) * ringCircumference);

  const handleDialPress = useCallback(() => {
    if (autoCloseTimerRef.current) {
      clearTimeout(autoCloseTimerRef.current);
      autoCloseTimerRef.current = null;
    }

    if (isCompletedState && progressSignature) {
      setDetailsVisible(false);
      setDismissedCompletedSignature(progressSignature);
      void setEnrichmentDialState(
        {
          left: dialPositionRef.current.left,
          top: dialPositionRef.current.top,
          dismissedCompletedSignature: progressSignature
        },
        userId
      );
      return;
    }

    setDetailsVisible((current) => !current);
  }, [isCompletedState, progressSignature, userId]);

  const handleDetailsPress = useCallback(() => {
    if (autoCloseTimerRef.current) {
      clearTimeout(autoCloseTimerRef.current);
      autoCloseTimerRef.current = null;
    }

    setDetailsVisible(false);
  }, []);

  if (!isAuthenticated || isBootstrapping || !progress || !shouldShowDial) {
    return null;
  }

  const processedCount = liveProcessedCount;
  const totalCount = displayTotalCount;
  const remainingCount = liveRemainingCount;
  const isDone = isCompletedState && !waitingForVisualCompletion;

  return (
    <View pointerEvents="box-none" style={styles.host}>
      {detailsVisible ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Close enrichment status"
          onPress={handleDetailsPress}
          style={[styles.detailsCard, { left: detailsPlacement.left, top: detailsPlacement.top }]}
        >
          <Text style={styles.detailsTitle}>{titleText}</Text>
          <Text style={styles.detailsLine}>Stage: {stageLabel}</Text>
          {popupMode !== "before" ? (
            <>
              <View style={styles.progressCompactRow}>
                <Text style={[styles.detailsLine, styles.progressCompactText]}>
                  Progress: {formatCompactNumber(processedCount)} / {formatCompactNumber(totalCount)}
                </Text>
                <Text style={[styles.detailsLine, styles.progressCompactText]}>
                  Remaining: {formatCompactNumber(remainingCount)}
                </Text>
              </View>
              {popupMode === "active" ? (
                <Text style={styles.detailsHint}>Newest transactions are processed first.</Text>
              ) : (
                <Text style={styles.detailsHint}>Closing automatically...</Text>
              )}
            </>
          ) : null}
        </Pressable>
      ) : null}

      <View
        pointerEvents="box-none"
        style={[styles.dialAnchor, { left: dialPosition.left, top: dialPosition.top }]}
        {...panResponder.panHandlers}
      >
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Transaction organization progress"
          onPress={handleDialPress}
          style={({ pressed }) => [styles.dialWrap, pressed ? styles.dialWrapPressed : null]}
        >
          <View style={[styles.dialOuter, isDone ? styles.dialOuterDone : null]}>
            <View style={styles.ringLayer}>
              <Svg width={RING_SIZE} height={RING_SIZE}>
                <G rotation={-90} originX={RING_SIZE / 2} originY={RING_SIZE / 2}>
                  <Circle
                    cx={RING_SIZE / 2}
                    cy={RING_SIZE / 2}
                    r={ringRadius}
                    stroke={isDone ? palette.accentStrong : "rgba(242, 140, 40, 0.26)"}
                    strokeWidth={RING_STROKE_WIDTH}
                    fill="transparent"
                  />
                  <Circle
                    cx={RING_SIZE / 2}
                    cy={RING_SIZE / 2}
                    r={ringRadius}
                    stroke={isDone ? palette.accentStrong : palette.accent}
                    strokeWidth={RING_STROKE_WIDTH}
                    strokeLinecap="round"
                    strokeDasharray={`${ringCircumference} ${ringCircumference}`}
                    strokeDashoffset={ringStrokeOffset}
                    fill="transparent"
                  />
                </G>
              </Svg>
            </View>
            <View style={styles.dialInner}>
              <Text style={[styles.dialPercent, isDone ? styles.dialDoneText : null]}>
                {isDone ? "Done" : `${progressPercent}%`}
              </Text>
            </View>
          </View>
        </Pressable>
      </View>
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  host: {
    position: "absolute",
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    zIndex: 999
  },
  dialAnchor: {
    position: "absolute"
  },
  dialWrap: {
    borderRadius: DIAL_SIZE / 2
  },
  dialWrapPressed: {
    opacity: 0.86
  },
  dialOuter: {
    width: DIAL_SIZE,
    height: DIAL_SIZE,
    borderRadius: DIAL_SIZE / 2,
    borderWidth: 1.5,
    borderColor: palette.borderStrong,
    backgroundColor: surfaces.card,
    alignItems: "center",
    justifyContent: "center"
  },
  dialOuterDone: {
    borderColor: palette.accentStrong
  },
  ringLayer: {
    position: "absolute",
    width: RING_SIZE,
    height: RING_SIZE,
    alignItems: "center",
    justifyContent: "center"
  },
  dialInner: {
    width: DIAL_SIZE - 16,
    height: DIAL_SIZE - 16,
    borderRadius: (DIAL_SIZE - 16) / 2,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: surfaces.field
  },
  dialPercent: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  dialDoneText: {
    color: palette.accentStrong
  },
  detailsCard: {
    position: "absolute",
    width: DETAILS_WIDTH,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.card,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[10],
    gap: spacing[4]
  },
  detailsTitle: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "700"
  },
  detailsLine: {
    color: palette.textSecondary,
    ...typography.caption
  },
  progressCompactRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8]
  },
  progressCompactText: {
    flexShrink: 1
  },
  detailsHint: {
    color: palette.textMuted,
    ...typography.caption
  }
}));
