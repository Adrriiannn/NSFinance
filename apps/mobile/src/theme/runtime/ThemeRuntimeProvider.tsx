import * as Updates from "expo-updates";
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
  DevSettings,
  Easing,
  StyleSheet,
  useColorScheme,
  useWindowDimensions,
  View
} from "react-native";
import { themes, type SemanticTheme } from "../semantic";
import {
  cycleThemeMode,
  getStoredThemeModeSync,
  persistThemeMode,
  resolveThemeName,
  type ResolvedThemeName,
  type ThemeMode
} from "./themePreference";

type ThemeTransitionState = {
  from: ResolvedThemeName;
  to: ResolvedThemeName;
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

async function reloadAppRuntime() {
  try {
    await Updates.reloadAsync();
    return;
  } catch {
    if (__DEV__) {
      DevSettings.reload();
    }
  }
}

export function ThemeRuntimeProvider({ children }: ThemeRuntimeProviderProps) {
  const systemScheme = useColorScheme();
  const { width: viewportWidth } = useWindowDimensions();
  const startupModeRef = useRef<ThemeMode>(getStoredThemeModeSync());
  const [mode, setMode] = useState<ThemeMode>(startupModeRef.current);
  const [isTransitioning, setIsTransitioning] = useState(false);
  const [transitionState, setTransitionState] = useState<ThemeTransitionState | null>(null);
  const [reducedMotionEnabled, setReducedMotionEnabled] = useState(false);
  const transitionProgress = useRef(new Animated.Value(0)).current;

  const resolvedThemeName = useMemo(
    () => resolveThemeName(mode, systemScheme),
    [mode, systemScheme]
  );
  const theme = themes[resolvedThemeName];

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

  const commitThemeMode = useCallback(
    async (nextMode: ThemeMode) => {
      await persistThemeMode(nextMode);
      setMode(nextMode);
      await reloadAppRuntime();
    },
    [setMode]
  );

  const setThemeMode = useCallback(
    (nextMode: ThemeMode) => {
      if (isTransitioning || nextMode === mode) {
        return;
      }

      const nextResolvedTheme = resolveThemeName(nextMode, systemScheme);
      const resolvedAlreadyMatches = nextResolvedTheme === resolvedThemeName;

      if (resolvedAlreadyMatches) {
        setMode(nextMode);
        void persistThemeMode(nextMode);
        return;
      }

      setIsTransitioning(true);
      setTransitionState({
        from: resolvedThemeName,
        to: nextResolvedTheme
      });
      transitionProgress.setValue(0);

      Animated.timing(transitionProgress, {
        toValue: 1,
        duration: reducedMotionEnabled ? REDUCED_MOTION_DURATION_MS : FULL_MOTION_DURATION_MS,
        easing: Easing.out(Easing.cubic),
        useNativeDriver: false
      }).start(({ finished }) => {
        if (!finished) {
          setIsTransitioning(false);
          setTransitionState(null);
          return;
        }

        void commitThemeMode(nextMode).finally(() => {
          setIsTransitioning(false);
          setTransitionState(null);
        });
      });
    },
    [
      commitThemeMode,
      isTransitioning,
      mode,
      reducedMotionEnabled,
      resolvedThemeName,
      setMode,
      systemScheme,
      transitionProgress
    ]
  );

  const cycleTheme = useCallback(() => {
    setThemeMode(cycleThemeMode(mode));
  }, [mode, setThemeMode]);

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
            toThemeName={transitionState.to}
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
  toThemeName: ResolvedThemeName;
  progress: Animated.Value;
  viewportWidth: number;
  reducedMotionEnabled: boolean;
};

function ThemeRevealOverlay({
  toThemeName,
  progress,
  viewportWidth,
  reducedMotionEnabled
}: ThemeRevealOverlayProps) {
  const nextTheme = themes[toThemeName];
  const nextCanvas = nextTheme.colors.canvas;

  if (reducedMotionEnabled) {
    return (
      <Animated.View
        pointerEvents="none"
        style={[
          styles.overlayFill,
          {
            backgroundColor: nextCanvas,
            opacity: progress
          }
        ]}
      />
    );
  }

  const revealWidth = progress.interpolate({
    inputRange: [0, 1],
    outputRange: [0, viewportWidth + FEATHER_WIDTH]
  });

  return (
    <View pointerEvents="none" style={styles.overlayFill}>
      <View style={styles.revealAnchor}>
        <Animated.View
          style={[
            styles.revealBody,
            {
              width: revealWidth,
              backgroundColor: nextCanvas
            }
          ]}
        >
          <LinearGradient
            colors={[withAlpha(nextCanvas, 0), nextCanvas]}
            start={{ x: 0, y: 0.5 }}
            end={{ x: 1, y: 0.5 }}
            style={styles.revealFeather}
          />
        </Animated.View>
      </View>
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
  revealAnchor: {
    ...StyleSheet.absoluteFillObject,
    alignItems: "flex-end"
  },
  revealBody: {
    height: "100%"
  },
  revealFeather: {
    position: "absolute",
    left: -FEATHER_WIDTH,
    top: 0,
    bottom: 0,
    width: FEATHER_WIDTH
  }
});
