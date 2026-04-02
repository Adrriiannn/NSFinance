import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { AppState, Pressable, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useBankEnrichmentProgressQuery } from "../../features/banking/useBanking";
import { subscribeToEnrichmentTooltip } from "../../features/banking/enrichmentUxEvents";
import { useAuthSession } from "../../providers/AuthProvider";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../theme/tokens";

const TOOLTIP_VISIBLE_MS = 10_000;
const LIVE_INVALIDATION_INTERVAL_MS = 4_000;

function formatCompactNumber(value: number) {
  return new Intl.NumberFormat("en-GB").format(value);
}

export function GlobalEnrichmentProgressDial() {
  const insets = useSafeAreaInsets();
  const queryClient = useQueryClient();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const [detailsVisible, setDetailsVisible] = useState(false);
  const [tooltipVisible, setTooltipVisible] = useState(false);
  const tooltipTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const enrichmentQuery = useBankEnrichmentProgressQuery(isAuthenticated && !isBootstrapping);
  const progress = enrichmentQuery.data;
  const inProgress = Boolean(progress?.inProgress);
  const progressPercent = Math.max(0, Math.min(100, Math.round(progress?.progressPercent ?? 0)));
  const stageLabel = progress?.stage === "historical_backfill"
    ? "Organizing history"
    : progress?.stage === "awaiting_sync"
      ? "Waiting for sync"
      : progress?.stage === "completed"
        ? "Completed"
        : "Preparing";

  useEffect(() => {
    return subscribeToEnrichmentTooltip(() => {
      setTooltipVisible(true);
      if (tooltipTimerRef.current) {
        clearTimeout(tooltipTimerRef.current);
      }

      tooltipTimerRef.current = setTimeout(() => {
        setTooltipVisible(false);
      }, TOOLTIP_VISIBLE_MS);
    });
  }, []);

  useEffect(() => {
    if (inProgress) {
      return;
    }

    setDetailsVisible(false);
    setTooltipVisible(false);
  }, [inProgress]);

  useEffect(() => {
    if (!inProgress) {
      return;
    }

    const intervalId = setInterval(() => {
      if (AppState.currentState !== "active") {
        return;
      }

      void Promise.all([
        queryClient.invalidateQueries({
          predicate: (query) =>
            Array.isArray(query.queryKey)
            && query.queryKey[0] === "transactions"
        }),
        queryClient.invalidateQueries({
          predicate: (query) =>
            Array.isArray(query.queryKey)
            && (query.queryKey[0] === "accounts" || query.queryKey[0] === "dashboard")
        })
      ]);
    }, LIVE_INVALIDATION_INTERVAL_MS);

    return () => {
      clearInterval(intervalId);
    };
  }, [inProgress, queryClient]);

  useEffect(() => {
    return () => {
      if (tooltipTimerRef.current) {
        clearTimeout(tooltipTimerRef.current);
      }
    };
  }, []);

  if (!isAuthenticated || isBootstrapping || !progress || !inProgress) {
    return null;
  }

  const topOffset = insets.top + 88;
  const processedCount = progress.processedCount ?? 0;
  const totalCount = progress.totalCount ?? 0;
  const remainingCount = progress.remainingCount ?? Math.max(0, totalCount - processedCount);

  return (
    <View pointerEvents="box-none" style={styles.host}>
      <View style={[styles.anchor, { top: topOffset }]}>
        {tooltipVisible ? (
          <View style={styles.tooltipCard}>
            <Text style={styles.tooltipTitle}>Organizing your transactions</Text>
            <Text style={styles.tooltipBody}>
              We&apos;re reviewing your transaction history and applying links and categories. You can keep using the app while this finishes.
            </Text>
          </View>
        ) : null}

        {detailsVisible ? (
          <View style={styles.detailsCard}>
            <Text style={styles.detailsTitle}>Transaction organization</Text>
            <Text style={styles.detailsLine}>Stage: {stageLabel}</Text>
            <Text style={styles.detailsLine}>
              Progress: {formatCompactNumber(processedCount)} / {formatCompactNumber(totalCount)}
            </Text>
            <Text style={styles.detailsLine}>Remaining: {formatCompactNumber(remainingCount)}</Text>
            <Text style={styles.detailsHint}>Newest transactions are processed first.</Text>
          </View>
        ) : null}

        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Transaction organization progress"
          onPress={() => setDetailsVisible((current) => !current)}
          style={({ pressed }) => [styles.dialWrap, pressed ? styles.dialWrapPressed : null]}
        >
          <View style={styles.dialOuter}>
            <View style={styles.dialInner}>
              <Text style={styles.dialPercent}>{progressPercent}%</Text>
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
    zIndex: 999
  },
  anchor: {
    position: "absolute",
    right: spacing[16],
    alignItems: "flex-end",
    gap: spacing[8]
  },
  dialWrap: {
    borderRadius: 22
  },
  dialWrapPressed: {
    opacity: 0.86
  },
  dialOuter: {
    width: 44,
    height: 44,
    borderRadius: 22,
    borderWidth: 2,
    borderColor: palette.borderStrong,
    backgroundColor: surfaces.card
  },
  dialInner: {
    flex: 1,
    borderRadius: 20,
    margin: 3,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: surfaces.field
  },
  dialPercent: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  tooltipCard: {
    width: 264,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.card,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[10],
    gap: spacing[6]
  },
  tooltipTitle: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "700"
  },
  tooltipBody: {
    color: palette.textSecondary,
    ...typography.caption
  },
  detailsCard: {
    width: 220,
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
  detailsHint: {
    color: palette.textMuted,
    ...typography.caption
  }
}));
