import { useEffect, useMemo, useRef, useState } from "react";
import { Animated, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { subscribeToFlashMessages, type FlashMessagePayload } from "../../lib/flashMessage";
import { palette, spacing, typography } from "../../theme/tokens";

const toneStyles = {
  success: {
    borderColor: "rgba(28,197,131,0.5)",
    backgroundColor: "rgba(10,58,40,0.92)"
  },
  error: {
    borderColor: "rgba(255,120,120,0.54)",
    backgroundColor: "rgba(71,22,22,0.94)"
  },
  info: {
    borderColor: "rgba(127,174,255,0.52)",
    backgroundColor: "rgba(12,34,68,0.94)"
  }
} as const;

export function GlobalFlashToast() {
  const insets = useSafeAreaInsets();
  const [payload, setPayload] = useState<FlashMessagePayload | null>(null);
  const toastTranslateY = useRef(new Animated.Value(-60)).current;
  const toastOpacity = useRef(new Animated.Value(0)).current;
  const hideTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const toastTopOffset = insets.top + 40;

  useEffect(() => {
    return subscribeToFlashMessages((nextPayload) => {
      if (hideTimerRef.current) {
        clearTimeout(hideTimerRef.current);
        hideTimerRef.current = null;
      }

      setPayload(nextPayload);
      toastTranslateY.setValue(-60);
      toastOpacity.setValue(0);

      Animated.parallel([
        Animated.timing(toastTranslateY, {
          toValue: 0,
          duration: 180,
          useNativeDriver: true
        }),
        Animated.timing(toastOpacity, {
          toValue: 1,
          duration: 180,
          useNativeDriver: true
        })
      ]).start(() => {
        hideTimerRef.current = setTimeout(() => {
          Animated.parallel([
            Animated.timing(toastTranslateY, {
              toValue: -60,
              duration: 180,
              useNativeDriver: true
            }),
            Animated.timing(toastOpacity, {
              toValue: 0,
              duration: 180,
              useNativeDriver: true
            })
          ]).start(() => {
            setPayload((current) => (current?.id === nextPayload.id ? null : current));
          });
        }, nextPayload.durationMs ?? 1800);
      });
    });
  }, [toastOpacity, toastTranslateY]);

  useEffect(() => {
    return () => {
      if (hideTimerRef.current) {
        clearTimeout(hideTimerRef.current);
      }
    };
  }, []);

  const toneStyle = useMemo(() => {
    if (!payload) {
      return toneStyles.success;
    }

    return toneStyles[payload.tone];
  }, [payload]);

  if (!payload) {
    return null;
  }

  return (
    <View pointerEvents="none" style={styles.host}>
      <Animated.View
        style={[
          styles.toast,
          toneStyle,
          {
            marginTop: toastTopOffset,
            opacity: toastOpacity,
            transform: [{ translateY: toastTranslateY }]
          }
        ]}
      >
        <Text style={styles.toastText}>{payload.message}</Text>
      </Animated.View>
    </View>
  );
}

const styles = StyleSheet.create({
  host: {
    position: "absolute",
    top: 0,
    left: 0,
    right: 0,
    zIndex: 100
  },
  toast: {
    marginHorizontal: spacing[16],
    borderRadius: 999,
    borderWidth: 1,
    minHeight: 38,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[16]
  },
  toastText: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700",
    textAlign: "center"
  }
});
