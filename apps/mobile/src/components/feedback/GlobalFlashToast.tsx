import { useEffect, useRef, useState } from "react";
import { Animated, StyleSheet, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { subscribeToFlashMessages, type FlashMessagePayload } from "../../lib/flashMessage";
import { spacing, zIndex } from "../../theme/tokens";
import { Snackbar } from "../ui/feedback/Snackbar";

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
          transform: [{ translateY: toastTranslateY }]
        }}
      >
        <Snackbar message={payload.message} tone={payload.tone} />
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
    zIndex: zIndex.toast
  }
});
