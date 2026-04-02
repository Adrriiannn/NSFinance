import { useEffect, useRef, useState } from "react";
import { Animated, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { subscribeToFlashMessages, type FlashMessagePayload } from "../../lib/flashMessage";
import { spacing, zIndex, createRuntimeStyleSheet } from "../../theme/tokens";
import { Snackbar } from "../ui/feedback/Snackbar";

const DEFAULT_TOAST_DURATION_MS = 2400;
const TOAST_QUEUE_GAP_MS = 500;

export function GlobalFlashToast() {
  const insets = useSafeAreaInsets();
  const [payload, setPayload] = useState<FlashMessagePayload | null>(null);
  const toastTranslateY = useRef(new Animated.Value(-60)).current;
  const toastOpacity = useRef(new Animated.Value(0)).current;
  const toastShadowProgress = useRef(new Animated.Value(0)).current;
  const hideTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const gapTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const queueRef = useRef<FlashMessagePayload[]>([]);
  const isRunningRef = useRef(false);
  const toastTopOffset = insets.top + 40;

  const clearTimers = () => {
    if (hideTimerRef.current) {
      clearTimeout(hideTimerRef.current);
      hideTimerRef.current = null;
    }

    if (gapTimerRef.current) {
      clearTimeout(gapTimerRef.current);
      gapTimerRef.current = null;
    }
  };

  const runQueue = () => {
    if (isRunningRef.current) {
      return;
    }

    const nextPayload = queueRef.current.shift();
    if (!nextPayload) {
      setPayload(null);
      return;
    }

    isRunningRef.current = true;
    setPayload(nextPayload);
    toastTranslateY.setValue(-60);
    toastOpacity.setValue(0);
    toastShadowProgress.setValue(0);

    Animated.parallel([
      Animated.timing(toastTranslateY, {
        toValue: 0,
        duration: 180,
        useNativeDriver: false
      }),
      Animated.timing(toastOpacity, {
        toValue: 1,
        duration: 180,
        useNativeDriver: false
      }),
      Animated.timing(toastShadowProgress, {
        toValue: 1,
        duration: 180,
        useNativeDriver: false
      })
    ]).start(() => {
      hideTimerRef.current = setTimeout(() => {
        Animated.parallel([
          Animated.timing(toastTranslateY, {
            toValue: -60,
            duration: 180,
            useNativeDriver: false
          }),
          Animated.timing(toastOpacity, {
            toValue: 0,
            duration: 180,
            useNativeDriver: false
          }),
          Animated.timing(toastShadowProgress, {
            toValue: 0,
            duration: 180,
            useNativeDriver: false
          })
        ]).start(() => {
          setPayload((current) => (current?.id === nextPayload.id ? null : current));
          gapTimerRef.current = setTimeout(() => {
            isRunningRef.current = false;
            runQueue();
          }, TOAST_QUEUE_GAP_MS);
        });
      }, nextPayload.durationMs ?? DEFAULT_TOAST_DURATION_MS);
    });
  };

  useEffect(() => {
    return subscribeToFlashMessages((nextPayload) => {
      queueRef.current.push(nextPayload);
      runQueue();
    });
  }, [toastOpacity, toastShadowProgress, toastTranslateY]);

  useEffect(() => {
    return () => {
      clearTimers();
      queueRef.current = [];
      isRunningRef.current = false;
    };
  }, []);

  if (!payload) {
    return null;
  }

  return (
    <View pointerEvents="none" style={styles.host}>
      <Animated.View
        style={{
          marginTop: toastTopOffset,
          marginHorizontal: spacing[16],
          opacity: toastOpacity,
          shadowColor: "#000000",
          shadowOffset: { width: 0, height: 4 },
          shadowRadius: 10,
          shadowOpacity: toastShadowProgress.interpolate({
            inputRange: [0, 1],
            outputRange: [0, 0.16]
          }),
          elevation: toastShadowProgress.interpolate({
            inputRange: [0, 1],
            outputRange: [0, 4]
          }),
          transform: [{ translateY: toastTranslateY }]
        }}
      >
        <Snackbar message={payload.message} tone={payload.tone} />
      </Animated.View>
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  host: {
    position: "absolute",
    top: 0,
    left: 0,
    right: 0,
    zIndex: zIndex.toast
  }
}));

