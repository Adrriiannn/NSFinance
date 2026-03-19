import { usePathname, useRouter } from "expo-router";
import { useCallback, useMemo, useRef, useState } from "react";
import { StyleSheet, View } from "react-native";
import { surfaces } from "../../theme/tokens";
import { FloatingAssistantDock } from "./FloatingAssistantDock";
import { AdaptiveLayoutContext, useAdaptiveLayoutMetrics } from "./adaptive.hooks";
import type { AdaptiveAppShellProps, AdaptiveShellFrame } from "./adaptive.types";

const ROOT_TAB_PATHS = new Set(["/", "/accounts", "/activity", "/planner", "/calendar"]);

function resolveSourceTab(pathname: string | null): "index" | "accounts" | "activity" | "planner" {
  if (pathname?.startsWith("/accounts")) {
    return "accounts";
  }

  if (pathname?.startsWith("/activity")) {
    return "activity";
  }

  if (pathname?.startsWith("/planner")) {
    return "planner";
  }

  return "index";
}

export function AdaptiveAppShell({ children }: AdaptiveAppShellProps) {
  const metrics = useAdaptiveLayoutMetrics();
  const pathname = usePathname();
  const router = useRouter();
  const [shellFrame, setShellFrame] = useState<AdaptiveShellFrame | null>(null);
  const lastInteractionAtRef = useRef(Date.now());

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

  return (
    <AdaptiveLayoutContext.Provider value={contextValue}>
      <View style={styles.root} onTouchStart={markInteraction} onTouchMove={markInteraction}>
        <View pointerEvents="none" style={styles.backgroundGlowTop} />
        <View pointerEvents="none" style={styles.backgroundGlowBottom} />
        {children}
        {showAssistantDock ? (
          <FloatingAssistantDock
            accessibilityLabel="Open NS Companion"
            onPress={() =>
              router.push(`/companion?source=app&sourceTab=${sourceTab}` as never)
            }
          />
        ) : null}
      </View>
    </AdaptiveLayoutContext.Provider>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: surfaces.app
  },
  backgroundGlowTop: {
    position: "absolute",
    top: -100,
    right: -60,
    width: 260,
    height: 260,
    borderRadius: 130,
    backgroundColor: "rgba(47,107,255,0.09)"
  },
  backgroundGlowBottom: {
    position: "absolute",
    bottom: -160,
    left: -100,
    width: 260,
    height: 260,
    borderRadius: 130,
    backgroundColor: "rgba(111,215,255,0.04)"
  }
});
