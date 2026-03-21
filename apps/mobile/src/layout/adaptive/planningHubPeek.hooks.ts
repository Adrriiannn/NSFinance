import { AccessibilityInfo, Animated, AppState, Easing, Keyboard } from "react-native";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  AUTO_PEEK_IDLE_WINDOW_MS,
  BLOCKED_RETRY_DELAY_MS,
  FIRST_PEEK_DELAY_MAX_MS,
  FIRST_PEEK_DELAY_MIN_MS,
  MAX_PEEK_COUNT_PER_SESSION,
  PEEK_AUTO_HIDE_DURATION_MS,
  PEEK_AUTO_PAUSE_DURATION_MS,
  PEEK_AUTO_REVEALED_TRANSLATE_Y,
  PEEK_AUTO_REVEAL_DURATION_MS,
  PEEK_HIDE_DURATION_MS,
  PEEK_HIDDEN_OPACITY,
  PEEK_HIDDEN_SCALE,
  PEEK_HIDDEN_TRANSLATE_Y,
  PEEK_MANUAL_VISIBLE_DURATION_MS,
  PEEK_REVEAL_DURATION_MS,
  PEEK_REVEALED_OPACITY,
  PEEK_REVEALED_SCALE,
  PEEK_REVEALED_TRANSLATE_Y,
  SUBSEQUENT_PEEK_DELAY_MAX_MS,
  SUBSEQUENT_PEEK_DELAY_MIN_MS
} from "./planningHubPeek.constants";

type PeekSource = "auto" | "manual";
type HideReason = "timeout" | "outside" | "swipe-down" | "tab-change" | "disabled";

type UsePlanningHubPeekParams = {
  enabled: boolean;
  autoPeekEnabled?: boolean;
  getLastInteractionAt?: (() => number) | null;
  sharedRevealKey?: string | null;
};

type UsePlanningHubPeekResult = {
  isVisible: boolean;
  peekSource: PeekSource | null;
  translateY: Animated.Value;
  opacity: Animated.Value;
  scale: Animated.Value;
  revealPeek: (source?: PeekSource) => void;
  hidePeek: (reason?: HideReason) => void;
  handleButtonPress: (onPress: () => void) => void;
  cancelPendingTimers: () => void;
};

const planningHubPeekSession = {
  autoPeekCount: 0,
  autoPeekDisabled: false
};
const sharedManualRevealExpirations = new Map<string, number>();
const sharedManualRevealListeners = new Set<() => void>();

function notifySharedManualRevealListeners() {
  sharedManualRevealListeners.forEach((listener) => {
    listener();
  });
}

function randomDelay(min: number, max: number) {
  return Math.round(min + Math.random() * (max - min));
}

export function usePlanningHubPeek({
  enabled,
  autoPeekEnabled = true,
  getLastInteractionAt,
  sharedRevealKey
}: UsePlanningHubPeekParams): UsePlanningHubPeekResult {
  const [isVisible, setIsVisible] = useState(false);
  const [peekSource, setPeekSource] = useState<PeekSource | null>(null);
  const [keyboardVisible, setKeyboardVisible] = useState(false);
  const [isReduceMotionEnabled, setIsReduceMotionEnabled] = useState(false);
  const [sharedRevealRevision, setSharedRevealRevision] = useState(0);
  const appStateRef = useRef(AppState.currentState);
  const isVisibleRef = useRef(false);
  const peekSourceRef = useRef<PeekSource | null>(null);
  const translateY = useRef(new Animated.Value(PEEK_HIDDEN_TRANSLATE_Y)).current;
  const opacity = useRef(new Animated.Value(PEEK_HIDDEN_OPACITY)).current;
  const scale = useRef(new Animated.Value(PEEK_HIDDEN_SCALE)).current;
  const peekTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const hideTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const animationRef = useRef<Animated.CompositeAnimation | null>(null);
  const manualRevealExpiresAtRef = useRef<number | null>(null);

  const clearPeekTimer = useCallback(() => {
    if (peekTimerRef.current) {
      clearTimeout(peekTimerRef.current);
      peekTimerRef.current = null;
    }
  }, []);

  const clearHideTimer = useCallback(() => {
    if (hideTimerRef.current) {
      clearTimeout(hideTimerRef.current);
      hideTimerRef.current = null;
    }
  }, []);

  const cancelPendingTimers = useCallback(() => {
    clearPeekTimer();
    clearHideTimer();
  }, [clearHideTimer, clearPeekTimer]);

  const stopCurrentAnimation = useCallback(() => {
    animationRef.current?.stop();
    animationRef.current = null;
    translateY.stopAnimation();
    opacity.stopAnimation();
    scale.stopAnimation();
  }, [opacity, scale, translateY]);

  const getRemainingSharedManualRevealMs = useCallback(() => {
    if (!sharedRevealKey) {
      return 0;
    }

    const expiresAt = sharedManualRevealExpirations.get(sharedRevealKey);
    if (!expiresAt) {
      return 0;
    }

    const remaining = expiresAt - Date.now();
    if (remaining <= 0) {
      sharedManualRevealExpirations.delete(sharedRevealKey);
      return 0;
    }

    return remaining;
  }, [sharedRevealKey]);

  const persistSharedManualReveal = useCallback(
    (durationMs: number) => {
      if (!sharedRevealKey) {
        return;
      }

      sharedManualRevealExpirations.set(sharedRevealKey, Date.now() + durationMs);
      notifySharedManualRevealListeners();
    },
    [sharedRevealKey]
  );

  const clearSharedManualReveal = useCallback(() => {
    if (!sharedRevealKey) {
      return;
    }

    sharedManualRevealExpirations.delete(sharedRevealKey);
    notifySharedManualRevealListeners();
  }, [sharedRevealKey]);

  useEffect(() => {
    const handleSharedRevealChange = () => {
      setSharedRevealRevision((current) => current + 1);
    };

    sharedManualRevealListeners.add(handleSharedRevealChange);
    return () => {
      sharedManualRevealListeners.delete(handleSharedRevealChange);
    };
  }, []);

  useEffect(() => {
    let mounted = true;
    AccessibilityInfo.isReduceMotionEnabled()
      .then((value) => {
        if (mounted) {
          setIsReduceMotionEnabled(value);
        }
      })
      .catch(() => undefined);

    const reduceMotionSubscription = AccessibilityInfo.addEventListener?.(
      "reduceMotionChanged",
      setIsReduceMotionEnabled
    );

    return () => {
      mounted = false;
      reduceMotionSubscription?.remove?.();
    };
  }, []);

  useEffect(() => {
    const showSubscription = Keyboard.addListener("keyboardDidShow", () => {
      setKeyboardVisible(true);
    });
    const hideSubscription = Keyboard.addListener("keyboardDidHide", () => {
      setKeyboardVisible(false);
    });

    const appStateSubscription = AppState.addEventListener("change", (nextState) => {
      appStateRef.current = nextState;
    });

    return () => {
      showSubscription.remove();
      hideSubscription.remove();
      appStateSubscription.remove();
    };
  }, []);

  const runStateAnimation = useCallback(
    ({
      targetTranslateY,
      targetOpacity,
      targetScale,
      duration,
      easing,
      onComplete
    }: {
      targetTranslateY: number;
      targetOpacity: number;
      targetScale: number;
      duration: number;
      easing: (value: number) => number;
      onComplete?: () => void;
    }) => {
      stopCurrentAnimation();
      const animation = Animated.parallel([
        Animated.timing(translateY, {
          toValue: targetTranslateY,
          duration,
          easing,
          useNativeDriver: true
        }),
        Animated.timing(opacity, {
          toValue: targetOpacity,
          duration,
          easing,
          useNativeDriver: true
        }),
        Animated.timing(scale, {
          toValue: targetScale,
          duration,
          easing,
          useNativeDriver: true
        })
      ]);

      animationRef.current = animation;
      animation.start(({ finished }) => {
        if (animationRef.current === animation) {
          animationRef.current = null;
        }

        if (finished) {
          onComplete?.();
        }
      });
    },
    [opacity, scale, stopCurrentAnimation, translateY]
  );

  const setHiddenState = useCallback(() => {
    isVisibleRef.current = false;
    peekSourceRef.current = null;
    manualRevealExpiresAtRef.current = null;
    setIsVisible(false);
    setPeekSource(null);
  }, []);

  const runHideAnimation = useCallback(
    (onComplete?: () => void) => {
      const duration = isReduceMotionEnabled ? 80 : PEEK_HIDE_DURATION_MS;
      runStateAnimation({
        targetTranslateY: PEEK_HIDDEN_TRANSLATE_Y,
        targetOpacity: PEEK_HIDDEN_OPACITY,
        targetScale: PEEK_HIDDEN_SCALE,
        duration,
        easing: Easing.in(Easing.cubic),
        onComplete
      });
    },
    [isReduceMotionEnabled, runStateAnimation]
  );

  const runManualRevealAnimation = useCallback(() => {
    const duration = isReduceMotionEnabled ? 80 : PEEK_REVEAL_DURATION_MS;
    runStateAnimation({
      targetTranslateY: PEEK_REVEALED_TRANSLATE_Y,
      targetOpacity: PEEK_REVEALED_OPACITY,
      targetScale: PEEK_REVEALED_SCALE,
      duration,
      easing: Easing.out(Easing.cubic)
    });
  }, [isReduceMotionEnabled, runStateAnimation]);

  const runAutoPeekSequence = useCallback(
    (onComplete?: () => void) => {
      stopCurrentAnimation();

      const revealDuration = isReduceMotionEnabled ? 80 : PEEK_AUTO_REVEAL_DURATION_MS;
      const hideDuration = isReduceMotionEnabled ? 80 : PEEK_AUTO_HIDE_DURATION_MS;
      const pauseDuration = isReduceMotionEnabled ? 60 : PEEK_AUTO_PAUSE_DURATION_MS;

      const animation = Animated.sequence([
        Animated.parallel([
          Animated.timing(translateY, {
            toValue: PEEK_AUTO_REVEALED_TRANSLATE_Y,
            duration: revealDuration,
            easing: Easing.out(Easing.cubic),
            useNativeDriver: true
          }),
          Animated.timing(opacity, {
            toValue: PEEK_REVEALED_OPACITY,
            duration: revealDuration,
            easing: Easing.out(Easing.cubic),
            useNativeDriver: true
          }),
          Animated.timing(scale, {
            toValue: PEEK_REVEALED_SCALE,
            duration: revealDuration,
            easing: Easing.out(Easing.cubic),
            useNativeDriver: true
          })
        ]),
        Animated.delay(pauseDuration),
        Animated.parallel([
          Animated.timing(translateY, {
            toValue: PEEK_HIDDEN_TRANSLATE_Y,
            duration: hideDuration,
            easing: Easing.in(Easing.cubic),
            useNativeDriver: true
          }),
          Animated.timing(opacity, {
            toValue: PEEK_HIDDEN_OPACITY,
            duration: hideDuration,
            easing: Easing.in(Easing.cubic),
            useNativeDriver: true
          }),
          Animated.timing(scale, {
            toValue: PEEK_HIDDEN_SCALE,
            duration: hideDuration,
            easing: Easing.in(Easing.cubic),
            useNativeDriver: true
          })
        ]),
        Animated.delay(pauseDuration),
        Animated.parallel([
          Animated.timing(translateY, {
            toValue: PEEK_AUTO_REVEALED_TRANSLATE_Y,
            duration: revealDuration,
            easing: Easing.out(Easing.cubic),
            useNativeDriver: true
          }),
          Animated.timing(opacity, {
            toValue: PEEK_REVEALED_OPACITY,
            duration: revealDuration,
            easing: Easing.out(Easing.cubic),
            useNativeDriver: true
          }),
          Animated.timing(scale, {
            toValue: PEEK_REVEALED_SCALE,
            duration: revealDuration,
            easing: Easing.out(Easing.cubic),
            useNativeDriver: true
          })
        ]),
        Animated.delay(pauseDuration),
        Animated.parallel([
          Animated.timing(translateY, {
            toValue: PEEK_HIDDEN_TRANSLATE_Y,
            duration: hideDuration,
            easing: Easing.in(Easing.cubic),
            useNativeDriver: true
          }),
          Animated.timing(opacity, {
            toValue: PEEK_HIDDEN_OPACITY,
            duration: hideDuration,
            easing: Easing.in(Easing.cubic),
            useNativeDriver: true
          }),
          Animated.timing(scale, {
            toValue: PEEK_HIDDEN_SCALE,
            duration: hideDuration,
            easing: Easing.in(Easing.cubic),
            useNativeDriver: true
          })
        ])
      ]);

      animationRef.current = animation;
      animation.start(({ finished }) => {
        if (animationRef.current === animation) {
          animationRef.current = null;
        }

        if (finished) {
          onComplete?.();
        }
      });
    },
    [
      isReduceMotionEnabled,
      opacity,
      scale,
      stopCurrentAnimation,
      translateY
    ]
  );

  const isBlockedForAutoPeek = useCallback(() => {
    if (!enabled || !autoPeekEnabled) {
      return true;
    }

    if (keyboardVisible || appStateRef.current !== "active" || isVisibleRef.current) {
      return true;
    }

    if (!getLastInteractionAt) {
      return false;
    }

    return Date.now() - getLastInteractionAt() < AUTO_PEEK_IDLE_WINDOW_MS;
  }, [autoPeekEnabled, enabled, getLastInteractionAt, keyboardVisible]);

  const scheduleNextPeek = useCallback(function scheduleNextPeekImpl() {
    if (!enabled || !autoPeekEnabled || planningHubPeekSession.autoPeekDisabled) {
      return;
    }

    if (planningHubPeekSession.autoPeekCount >= MAX_PEEK_COUNT_PER_SESSION) {
      return;
    }

    if (peekTimerRef.current || isVisibleRef.current) {
      return;
    }

    const delay =
      planningHubPeekSession.autoPeekCount === 0
        ? randomDelay(FIRST_PEEK_DELAY_MIN_MS, FIRST_PEEK_DELAY_MAX_MS)
        : randomDelay(SUBSEQUENT_PEEK_DELAY_MIN_MS, SUBSEQUENT_PEEK_DELAY_MAX_MS);

    peekTimerRef.current = setTimeout(() => {
      peekTimerRef.current = null;

      if (isBlockedForAutoPeek()) {
        peekTimerRef.current = setTimeout(() => {
          peekTimerRef.current = null;
          scheduleNextPeekImpl();
        }, BLOCKED_RETRY_DELAY_MS);
        return;
      }

      planningHubPeekSession.autoPeekCount += 1;
      isVisibleRef.current = true;
      peekSourceRef.current = "auto";
      setIsVisible(true);
      setPeekSource("auto");
      runAutoPeekSequence(() => {
        setHiddenState();
        scheduleNextPeekImpl();
      });
    }, delay);
  }, [autoPeekEnabled, enabled, isBlockedForAutoPeek, runAutoPeekSequence, setHiddenState]);

  const hidePeek = useCallback(
    (reason: HideReason = "timeout") => {
      clearHideTimer();
      clearPeekTimer();

      if (!isVisibleRef.current && reason !== "disabled") {
        if (enabled && autoPeekEnabled && !planningHubPeekSession.autoPeekDisabled) {
          scheduleNextPeek();
        }
        return;
      }

      if (peekSourceRef.current === "manual" && reason !== "disabled") {
        clearSharedManualReveal();
      }

      setHiddenState();
      runHideAnimation(() => {
        if (
          reason !== "disabled" &&
          enabled &&
          autoPeekEnabled &&
          !planningHubPeekSession.autoPeekDisabled
        ) {
          scheduleNextPeek();
        }
      });
    },
    [
      clearHideTimer,
      clearPeekTimer,
      clearSharedManualReveal,
      autoPeekEnabled,
      enabled,
      runHideAnimation,
      scheduleNextPeek,
      setHiddenState
    ]
  );

  const revealPeek = useCallback(
    (source: PeekSource = "manual") => {
      cancelPendingTimers();
      isVisibleRef.current = true;
      peekSourceRef.current = source;
      setIsVisible(true);
      setPeekSource(source);

      if (source === "auto") {
        runAutoPeekSequence(() => {
          setHiddenState();
          if (enabled && autoPeekEnabled && !planningHubPeekSession.autoPeekDisabled) {
            scheduleNextPeek();
          }
        });
        return;
      }

      persistSharedManualReveal(PEEK_MANUAL_VISIBLE_DURATION_MS);
      manualRevealExpiresAtRef.current = Date.now() + PEEK_MANUAL_VISIBLE_DURATION_MS;
      runManualRevealAnimation();

      clearHideTimer();
      hideTimerRef.current = setTimeout(() => {
        hideTimerRef.current = null;
        hidePeek("timeout");
      }, PEEK_MANUAL_VISIBLE_DURATION_MS);
    },
    [
      cancelPendingTimers,
      clearHideTimer,
      autoPeekEnabled,
      enabled,
      hidePeek,
      persistSharedManualReveal,
      runAutoPeekSequence,
      runManualRevealAnimation,
      scheduleNextPeek,
      setHiddenState
    ]
  );

  const handleButtonPress = useCallback(
    (onPress: () => void) => {
      planningHubPeekSession.autoPeekDisabled = true;

      if (peekSourceRef.current === "manual") {
        const remainingMs = manualRevealExpiresAtRef.current
          ? manualRevealExpiresAtRef.current - Date.now()
          : getRemainingSharedManualRevealMs();

        if (remainingMs > 0) {
          persistSharedManualReveal(remainingMs);
        } else {
          clearSharedManualReveal();
        }
      }

      cancelPendingTimers();
      onPress();
    },
    [
      cancelPendingTimers,
      clearSharedManualReveal,
      getRemainingSharedManualRevealMs,
      persistSharedManualReveal
    ]
  );

  useEffect(() => {
    if (!enabled) {
      return;
    }

    const remainingMs = getRemainingSharedManualRevealMs();
    if (remainingMs <= 0) {
      if (isVisibleRef.current && peekSourceRef.current === "manual") {
        hidePeek("timeout");
      }
      return;
    }

    clearPeekTimer();
    clearHideTimer();
    if (!isVisibleRef.current || peekSourceRef.current !== "manual") {
      isVisibleRef.current = true;
      peekSourceRef.current = "manual";
      setIsVisible(true);
      setPeekSource("manual");
      stopCurrentAnimation();
      translateY.setValue(PEEK_REVEALED_TRANSLATE_Y);
      opacity.setValue(PEEK_REVEALED_OPACITY);
      scale.setValue(PEEK_REVEALED_SCALE);
    }

    manualRevealExpiresAtRef.current = Date.now() + remainingMs;
    hideTimerRef.current = setTimeout(() => {
      hideTimerRef.current = null;
      hidePeek("timeout");
    }, remainingMs);
  }, [
    clearHideTimer,
    clearPeekTimer,
    enabled,
    getRemainingSharedManualRevealMs,
    hidePeek,
    opacity,
    scale,
    sharedRevealRevision,
    stopCurrentAnimation,
    translateY
  ]);

  useEffect(() => {
    if (!enabled) {
      hidePeek("disabled");
      return;
    }

    if (autoPeekEnabled) {
      scheduleNextPeek();
    }

    return () => {
      cancelPendingTimers();
      stopCurrentAnimation();
    };
  }, [autoPeekEnabled, cancelPendingTimers, enabled, hidePeek, scheduleNextPeek, stopCurrentAnimation]);

  return useMemo(
    () => ({
      isVisible,
      peekSource,
      translateY,
      opacity,
      scale,
      revealPeek,
      hidePeek,
      handleButtonPress,
      cancelPendingTimers
    }),
    [cancelPendingTimers, handleButtonPress, hidePeek, isVisible, opacity, peekSource, revealPeek, scale, translateY]
  );
}
