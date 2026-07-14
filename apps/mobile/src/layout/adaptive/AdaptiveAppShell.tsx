import { usePathname, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { View } from "react-native";
import { GlobalAppMenu } from "../../components/layout/GlobalAppMenu";
import { getAccounts } from "../../features/accounts/accountsApi";
import { getDashboardSummary } from "../../features/dashboard/dashboardApi";
import { getExpenseTrackerEntries, getExpenseTrackerTaxonomy } from "../../features/expenseTracker/expenseTrackerApi";
import { queryKeys } from "../../lib/api/queryKeys";
import { navigateWithProbe } from "../../lib/perf/navigationTiming";
import { useAuthSession } from "../../providers/AuthProvider";
import { queryClient } from "../../providers/QueryProvider";
import { useThemeRuntime } from "../../theme/runtime/ThemeRuntimeProvider";
import { surfaces, zIndex, createRuntimeStyleSheet } from "../../theme/tokens";
import { getEffectiveBottomSystemInset } from "../../theme/insets";
import { FloatingAssistantDock } from "./FloatingAssistantDock";
import { AdaptiveLayoutContext, useAdaptiveLayoutMetrics } from "./adaptive.hooks";
import type { AdaptiveAppShellProps, AdaptiveShellFrame } from "./adaptive.types";

const ROOT_TAB_PATHS = new Set(["/", "/accounts", "/activity", "/cashflow"]);

function resolveSourceTab(pathname: string | null): "index" | "accounts" | "activity" | "cashflow" {
  if (pathname?.startsWith("/accounts")) {
    return "accounts";
  }

  if (pathname?.startsWith("/activity")) {
    return "activity";
  }

  if (pathname?.startsWith("/cashflow")) {
    return "cashflow";
  }

  return "index";
}

export function AdaptiveAppShell({ children }: AdaptiveAppShellProps) {
  useThemeRuntime();
  const metrics = useAdaptiveLayoutMetrics();
  const effectiveBottomSystemInset = getEffectiveBottomSystemInset(metrics.safeAreaInsets.bottom);
  const pathname = usePathname();
  const router = useRouter();
  const { isAuthenticated } = useAuthSession();
  const [shellFrame, setShellFrame] = useState<AdaptiveShellFrame | null>(null);
  const lastInteractionAtRef = useRef(Date.now());
  const hasWarmedCachesRef = useRef(false);

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

  const showAssistantDock = ROOT_TAB_PATHS.has(pathname ?? "");
  const sourceTab = resolveSourceTab(pathname);

  useEffect(() => {
    if (!isAuthenticated || hasWarmedCachesRef.current) {
      return;
    }

    hasWarmedCachesRef.current = true;
    const routerWithPrefetch = router as unknown as { prefetch?: (href: string) => void };

    const warmupTimer = setTimeout(() => {
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
          queryKey: queryKeys.expenseTracker.entries,
          queryFn: getExpenseTrackerEntries,
          staleTime: 30_000
        }),
        queryClient.prefetchQuery({
          queryKey: [...queryKeys.expenseTracker.taxonomy, "visible-only"],
          queryFn: () => getExpenseTrackerTaxonomy(),
          staleTime: 12 * 60 * 60_000
        })
      ]);
    }, 60);

    return () => {
      clearTimeout(warmupTimer);
    };
  }, [isAuthenticated, router]);

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

const styles = createRuntimeStyleSheet(() => ({
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
    zIndex: zIndex.tabBar
  }
}));


