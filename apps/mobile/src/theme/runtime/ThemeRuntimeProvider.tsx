import * as SystemUI from "expo-system-ui";
import { LinearGradient } from "expo-linear-gradient";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode
} from "react";
import {
  AccessibilityInfo,
  Animated,
  Easing,
  StyleSheet,
  useColorScheme,
  useWindowDimensions,
  View
} from "react-native";
import { themes, type SemanticTheme } from "../semantic";
import { setRuntimeThemeSnapshot } from "./themeSnapshot";
import {
  cycleThemeMode,
  getStoredThemeModeSync,
  persistThemeMode,
  resolveThemeName,
  type ResolvedThemeName,
  type ThemeMode
} from "./themePreference";

type ThemeTransitionPhase = "idle" | "starting" | "running" | "finishing";

type ThemeTransitionState = {
  id: number;
  from: ResolvedThemeName;
  to: ResolvedThemeName;
  phase: Exclude<ThemeTransitionPhase, "idle">;
};

type ThemeRuntimeContextValue = {
  mode: ThemeMode;
  resolvedThemeName: ResolvedThemeName;
  theme: SemanticTheme;
  isTransitioning: boolean;
  setThemeMode: (mode: ThemeMode) => void;
  cycleTheme: () => void;
};

const ThemeRuntimeContext = createContext<ThemeRuntimeContextValue | null>(null);

type ThemeRuntimeProviderProps = {
  children: ReactNode;
};

const FEATHER_WIDTH = 44;
const REDUCED_MOTION_DURATION_MS = 220;
const FULL_MOTION_DURATION_MS = 560;
const TRANSITION_CLEANUP_TIMEOUT_BUFFER_MS = 420;
const FINISH_PHASE_MS = 60;

function withAlpha(hexColor: string, alpha: number) {
  const normalized = hexColor.replace("#", "");
  const parsed =
    normalized.length === 3
      ? normalized
          .split("")
          .map((char) => `${char}${char}`)
          .join("")
      : normalized;

  const red = Number.parseInt(parsed.slice(0, 2), 16);
  const green = Number.parseInt(parsed.slice(2, 4), 16);
  const blue = Number.parseInt(parsed.slice(4, 6), 16);

  if ([red, green, blue].some((value) => Number.isNaN(value))) {
    return `rgba(0,0,0,${alpha})`;
  }

  return `rgba(${red},${green},${blue},${alpha})`;
}

export function ThemeRuntimeProvider({ children }: ThemeRuntimeProviderProps) {
  const systemScheme = useColorScheme();
  const { width: viewportWidth } = useWindowDimensions();
  const startupModeRef = useRef<ThemeMode>(getStoredThemeModeSync());
  const [mode, setMode] = useState<ThemeMode>(startupModeRef.current);
  const [reducedMotionEnabled, setReducedMotionEnabled] = useState(false);
  const [transitionState, setTransitionState] = useState<ThemeTransitionState | null>(null);

  const transitionProgress = useRef(new Animated.Value(0)).current;
  const transitionIdRef = useRef(0);
  const isTransitioningRef = useRef(false);
  const transitionAnimationRef = useRef<Animated.CompositeAnimation | null>(null);
  const transitionTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const finishTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const resolvedThemeName = useMemo(
    () => resolveThemeName(mode, systemScheme),
    [mode, systemScheme]
  );
  const theme = themes[resolvedThemeName];
  const isTransitioning = transitionState !== null;

  setRuntimeThemeSnapshot(theme);

  useEffect(() => {
    void SystemUI.setBackgroundColorAsync(theme.colors.canvas).catch(() => {
      // Non-fatal on unsupported targets.
    });
  }, [theme.colors.canvas]);

  useEffect(() => {
    let isMounted = true;
    AccessibilityInfo.isReduceMotionEnabled()
      .then((enabled) => {
        if (!isMounted) {
          return;
        }

        setReducedMotionEnabled(enabled);
      })
      .catch(() => {
        if (isMounted) {
          setReducedMotionEnabled(false);
        }
      });

    const subscription = AccessibilityInfo.addEventListener("reduceMotionChanged", (enabled) => {
      setReducedMotionEnabled(enabled);
    });

    return () => {
      isMounted = false;
      subscription.remove();
    };
  }, []);

  const clearTransitionTimers = useCallback(() => {
    if (transitionTimeoutRef.current) {
      clearTimeout(transitionTimeoutRef.current);
      transitionTimeoutRef.current = null;
    }

    if (finishTimeoutRef.current) {
      clearTimeout(finishTimeoutRef.current);
      finishTimeoutRef.current = null;
    }
  }, []);

  const resetTransition = useCallback(
    (transitionId: number) => {
      if (transitionIdRef.current !== transitionId) {
        return;
      }

      clearTransitionTimers();
      transitionAnimationRef.current?.stop();
      transitionAnimationRef.current = null;
      isTransitioningRef.current = false;
      transitionProgress.stopAnimation(() => {
        transitionProgress.setValue(0);
      });
      setTransitionState((current) => {
        if (!current || current.id !== transitionId) {
          return current;
        }

        return null;
      });
    },
    [clearTransitionTimers, transitionProgress]
  );

  const finishTransition = useCallback(
    (transitionId: number, reason: "finished" | "interrupted" | "timeout") => {
      if (transitionIdRef.current !== transitionId) {
        return;
      }

      transitionAnimationRef.current = null;
      clearTransitionTimers();

      if (reason === "finished") {
        setTransitionState((current) => {
          if (!current || current.id !== transitionId) {
            return current;
          }

          return { ...current, phase: "finishing" };
        });

        finishTimeoutRef.current = setTimeout(() => {
          resetTransition(transitionId);
        }, FINISH_PHASE_MS);
        return;
      }

      resetTransition(transitionId);
    },
    [clearTransitionTimers, resetTransition]
  );

  const startTransitionAnimation = useCallback(
    (transitionId: number, duration: number) => {
      if (transitionIdRef.current !== transitionId) {
        return;
      }

      setTransitionState((current) => {
        if (!current || current.id !== transitionId) {
          return current;
        }

        return { ...current, phase: "running" };
      });

      transitionAnimationRef.current = Animated.timing(transitionProgress, {
        toValue: 1,
        duration,
        easing: Easing.out(Easing.cubic),
        useNativeDriver: true
      });

      transitionAnimationRef.current.start(({ finished }) => {
        finishTransition(transitionId, finished ? "finished" : "interrupted");
      });

      transitionTimeoutRef.current = setTimeout(() => {
        finishTransition(transitionId, "timeout");
      }, duration + TRANSITION_CLEANUP_TIMEOUT_BUFFER_MS);
    },
    [finishTransition, transitionProgress]
  );

  const setThemeMode = useCallback(
    (nextMode: ThemeMode) => {
      if (isTransitioningRef.current || nextMode === mode) {
        return;
      }

      const nextResolvedTheme = resolveThemeName(nextMode, systemScheme);
      const resolvedAlreadyMatches = nextResolvedTheme === resolvedThemeName;

      if (resolvedAlreadyMatches) {
        setMode(nextMode);
        void persistThemeMode(nextMode);
        return;
      }

      const transitionId = transitionIdRef.current + 1;
      transitionIdRef.current = transitionId;
      isTransitioningRef.current = true;
      clearTransitionTimers();
      transitionAnimationRef.current?.stop();
      transitionAnimationRef.current = null;
      transitionProgress.setValue(0);

      setTransitionState({
        id: transitionId,
        from: resolvedThemeName,
        to: nextResolvedTheme,
        phase: "starting"
      });

      setMode(nextMode);
      void persistThemeMode(nextMode);

      const transitionDuration = reducedMotionEnabled
        ? REDUCED_MOTION_DURATION_MS
        : FULL_MOTION_DURATION_MS;
      requestAnimationFrame(() => {
        startTransitionAnimation(transitionId, transitionDuration);
      });
    },
    [
      clearTransitionTimers,
      mode,
      reducedMotionEnabled,
      resolvedThemeName,
      startTransitionAnimation,
      systemScheme,
      transitionProgress
    ]
  );

  const cycleTheme = useCallback(() => {
    setThemeMode(cycleThemeMode(mode));
  }, [mode, setThemeMode]);

  useEffect(() => {
    return () => {
      clearTransitionTimers();
      transitionAnimationRef.current?.stop();
      transitionAnimationRef.current = null;
      isTransitioningRef.current = false;
    };
  }, [clearTransitionTimers]);

  const contextValue = useMemo<ThemeRuntimeContextValue>(
    () => ({
      mode,
      resolvedThemeName,
      theme,
      isTransitioning,
      setThemeMode,
      cycleTheme
    }),
    [cycleTheme, isTransitioning, mode, resolvedThemeName, setThemeMode, theme]
  );

  return (
    <ThemeRuntimeContext.Provider value={contextValue}>
      <View style={styles.container}>
        {children}
        {transitionState ? (
          <ThemeRevealOverlay
            fromThemeName={transitionState.from}
            progress={transitionProgress}
            viewportWidth={viewportWidth}
            reducedMotionEnabled={reducedMotionEnabled}
          />
        ) : null}
      </View>
    </ThemeRuntimeContext.Provider>
  );
}

type ThemeRevealOverlayProps = {
  fromThemeName: ResolvedThemeName;
  progress: Animated.Value;
  viewportWidth: number;
  reducedMotionEnabled: boolean;
};

function ThemeRevealOverlay({
  fromThemeName,
  progress,
  viewportWidth,
  reducedMotionEnabled
}: ThemeRevealOverlayProps) {
  const fromTheme = themes[fromThemeName];
  const fromCanvas = fromTheme.colors.canvas;

  if (reducedMotionEnabled) {
    const fadeOut = progress.interpolate({
      inputRange: [0, 1],
      outputRange: [1, 0]
    });

    return (
      <Animated.View
        pointerEvents="none"
        style={[
          styles.overlayFill,
          {
            backgroundColor: fromCanvas,
            opacity: fadeOut
          }
        ]}
      />
    );
  }

  const translateX = progress.interpolate({
    inputRange: [0, 1],
    outputRange: [0, -(viewportWidth + FEATHER_WIDTH)]
  });

  const opacity = progress.interpolate({
    inputRange: [0, 0.96, 1],
    outputRange: [1, 1, 0]
  });

  return (
    <View pointerEvents="none" style={styles.overlayFill}>
      <Animated.View
        pointerEvents="none"
        style={[
          styles.revealBody,
          {
            width: viewportWidth + FEATHER_WIDTH,
            backgroundColor: fromCanvas,
            opacity,
            transform: [{ translateX }]
          }
        ]}
      >
        <LinearGradient
          pointerEvents="none"
          colors={[fromCanvas, withAlpha(fromCanvas, 0)]}
          start={{ x: 0, y: 0.5 }}
          end={{ x: 1, y: 0.5 }}
          style={styles.revealFeather}
        />
      </Animated.View>
    </View>
  );
}

export function useThemeRuntime() {
  const context = useContext(ThemeRuntimeContext);
  if (!context) {
    throw new Error("useThemeRuntime must be used within ThemeRuntimeProvider.");
  }

  return context;
}

const styles = StyleSheet.create({
  container: {
    flex: 1
  },
  overlayFill: {
    ...StyleSheet.absoluteFillObject
  },
  revealBody: {
    position: "absolute",
    left: 0,
    top: 0,
    bottom: 0
  },
  revealFeather: {
    position: "absolute",
    right: 0,
    top: 0,
    bottom: 0,
    width: FEATHER_WIDTH
  }
});
