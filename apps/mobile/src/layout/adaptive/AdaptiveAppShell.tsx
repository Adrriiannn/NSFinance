import { useGlobalSearchParams, usePathname, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { StyleSheet, View } from "react-native";
import { GlobalAppMenu } from "../../components/layout/GlobalAppMenu";
import { getAccounts } from "../../features/accounts/accountsApi";
import { getDashboardSummary } from "../../features/dashboard/dashboardApi";
import { getExpenseTrackerEntries, getExpenseTrackerTaxonomy } from "../../features/expenseTracker/expenseTrackerApi";
import { getTransactions } from "../../features/transactions/transactionsApi";
import { queryKeys } from "../../lib/api/queryKeys";
import { completeLatestNavigationProbe, navigateWithProbe } from "../../lib/perf/navigationTiming";
import { useAuthSession } from "../../providers/AuthProvider";
import { queryClient } from "../../providers/QueryProvider";
import { borders, surfaces, zIndex } from "../../theme/tokens";
import { getEffectiveBottomSystemInset } from "../../theme/insets";
import { FloatingAssistantDock } from "./FloatingAssistantDock";
import { AdaptiveLayoutContext, useAdaptiveLayoutMetrics } from "./adaptive.hooks";
import type { AdaptiveAppShellProps, AdaptiveShellFrame } from "./adaptive.types";

const ROOT_TAB_PATHS = new Set(["/", "/accounts", "/activity", "/cashflow", "/calendar"]);
const TAB_BAR_SEAM_COLOR = "#263142";

function resolveSourceTab(pathname: string | null): "index" | "accounts" | "activity" | "cashflow" | "calendar" {
  if (pathname?.startsWith("/accounts")) {
    return "accounts";
  }

  if (pathname?.startsWith("/activity")) {
    return "activity";
  }

  if (pathname?.startsWith("/cashflow")) {
    return "cashflow";
  }

  if (pathname?.startsWith("/calendar")) {
    return "calendar";
  }

  return "index";
}

function resolvePlanningHubSourceTab(
  pathname: string | null,
  sourcePlanningHubTab?: string
): "overview" | "graphs" | "add" | "calendar" | "discover" {
  if (pathname?.startsWith("/planning/browse")) {
    return "discover";
  }

  if (pathname?.startsWith("/planning/analytics")) {
    return "graphs";
  }

  if (pathname?.startsWith("/planning/categories")) {
    return "add";
  }

  if (pathname?.startsWith("/planning")) {
    return "overview";
  }

  if (pathname?.startsWith("/calendar")) {
    return "calendar";
  }

  if (
    sourcePlanningHubTab === "overview" ||
    sourcePlanningHubTab === "graphs" ||
    sourcePlanningHubTab === "add" ||
    sourcePlanningHubTab === "calendar" ||
    sourcePlanningHubTab === "discover"
  ) {
    return sourcePlanningHubTab;
  }

  return "overview";
}

export function AdaptiveAppShell({ children }: AdaptiveAppShellProps) {
  const metrics = useAdaptiveLayoutMetrics();
  const effectiveBottomSystemInset = getEffectiveBottomSystemInset(metrics.safeAreaInsets.bottom);
  const pathname = usePathname();
  const params = useGlobalSearchParams<{ source?: string; sourcePlanningHubTab?: string }>();
  const router = useRouter();
  const { isAuthenticated } = useAuthSession();
  const [shellFrame, setShellFrame] = useState<AdaptiveShellFrame | null>(null);
  const lastInteractionAtRef = useRef(Date.now());
  const hasWarmedCachesRef = useRef(false);
  const lastPathnameRef = useRef<string>("");

  const markInteraction = useCallback(() => {
    lastInteractionAtRef.current = Date.now();
  }, []);

  const getLastInteractionAt = useCallback(() => lastInteractionAtRef.current, []);

  const contextValue = useMemo(
    () => ({
      metrics,
      shellFrame,
      setShellFrame,
      markInteraction,
      getLastInteractionAt
    }),
    [getLastInteractionAt, markInteraction, metrics, shellFrame]
  );

  const isPlanningHubCalendar =
    pathname === "/calendar" &&
    (params.source === "planningHub" || params.source === "expense");
  const isPlanningHubPath = (pathname?.startsWith("/planning") ?? false) || isPlanningHubCalendar;
  const showAssistantDock = ROOT_TAB_PATHS.has(pathname ?? "") || isPlanningHubPath;
  const sourceTab = resolveSourceTab(pathname);
  const sourcePlanningHubTab = resolvePlanningHubSourceTab(pathname, params.sourcePlanningHubTab);

  useEffect(() => {
    if (!isAuthenticated || hasWarmedCachesRef.current) {
      return;
    }

    hasWarmedCachesRef.current = true;
    const routerWithPrefetch = router as unknown as { prefetch?: (href: string) => void };

    const warmupTimer = setTimeout(() => {
      routerWithPrefetch.prefetch?.("/(tabs)/planning");
      routerWithPrefetch.prefetch?.("/(tabs)/companion?source=app&sourceTab=cashflow");

      void Promise.allSettled([
        queryClient.prefetchQuery({
          queryKey: queryKeys.dashboard.summary,
          queryFn: getDashboardSummary,
          staleTime: 30_000
        }),
        queryClient.prefetchQuery({
          queryKey: queryKeys.accounts.all,
          queryFn: getAccounts,
          staleTime: 30_000
        }),
        queryClient.prefetchQuery({
          queryKey: queryKeys.transactions.list(),
          queryFn: () => getTransactions(),
          staleTime: 30_000
        }),
        queryClient.prefetchQuery({
          queryKey: queryKeys.expenseTracker.entries,
          queryFn: getExpenseTrackerEntries,
          staleTime: 30_000
        }),
        queryClient.prefetchQuery({
          queryKey: queryKeys.expenseTracker.taxonomy,
          queryFn: getExpenseTrackerTaxonomy,
          staleTime: 12 * 60 * 60_000
        })
      ]);
    }, 60);

    return () => {
      clearTimeout(warmupTimer);
    };
  }, [isAuthenticated, router]);

  useEffect(() => {
    const currentPath = pathname ?? "";
    if (lastPathnameRef.current === currentPath) {
      return;
    }

    const committedAtMs = Date.now();
    lastPathnameRef.current = currentPath;
    const frame = requestAnimationFrame(() => {
      const perfProbesEnabled = process.env.EXPO_PUBLIC_PERF_PROBES === "1";
      const perfProbesVerbose = process.env.EXPO_PUBLIC_PERF_PROBES_VERBOSE === "1";
      const commitToFrameMs = Date.now() - committedAtMs;
      if (perfProbesEnabled && (perfProbesVerbose || commitToFrameMs >= 36)) {
        console.info("[Perf Probe]", {
          type: "route_commit",
          path: currentPath,
          commitToFrameMs,
          timestampUtc: new Date().toISOString()
        });
      }
      completeLatestNavigationProbe(currentPath);
    });

    return () => {
      cancelAnimationFrame(frame);
    };
  }, [pathname]);

  return (
    <AdaptiveLayoutContext.Provider value={contextValue}>
      <View style={styles.root} onTouchStart={markInteraction} onTouchMove={markInteraction}>
        <GlobalAppMenu
          topOffset={metrics.safeAreaInsets.top + metrics.headerTopGap}
          showTrigger={false}
        />
        {children}
        {effectiveBottomSystemInset > 0 ? (
          <View
            pointerEvents="none"
            style={[
              styles.systemBottomInsetMask,
              { height: effectiveBottomSystemInset }
            ]}
          />
        ) : null}
        <FloatingAssistantDock
          hidden={!showAssistantDock}
          accessibilityLabel="Open NS Companion"
          onPress={() => {
            if (isPlanningHubPath) {
              navigateWithProbe(
                router as unknown as {
                  push: (href: string) => void;
                  replace: (href: string) => void;
                  navigate?: (href: string) => void;
                },
                `/(tabs)/companion?source=planningHub&sourcePlanningHubTab=${sourcePlanningHubTab}`,
                "adaptive-dock",
                "push"
              );
              return;
            }

            navigateWithProbe(
              router as unknown as {
                push: (href: string) => void;
                replace: (href: string) => void;
                navigate?: (href: string) => void;
              },
              `/(tabs)/companion?source=app&sourceTab=${sourceTab}`,
              "adaptive-dock",
              "push"
            );
          }}
        />
      </View>
    </AdaptiveLayoutContext.Provider>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: surfaces.app
  },
  systemBottomInsetMask: {
    position: "absolute",
    left: 0,
    right: 0,
    bottom: 0,
    backgroundColor: surfaces.tabBar,
    borderTopWidth: borders.width.thin,
    borderTopColor: TAB_BAR_SEAM_COLOR,
    borderLeftWidth: borders.width.thin,
    borderLeftColor: TAB_BAR_SEAM_COLOR,
    borderRightWidth: borders.width.thin,
    borderRightColor: TAB_BAR_SEAM_COLOR,
    zIndex: zIndex.tabBar
  }
});

