import { useEffect, useRef } from "react";
import { Animated } from "react-native";
import type { StyleProp, ViewStyle } from "react-native";
import { useFeedbackPresets } from "./feedback.presets";

type SkeletonProps = {
  style?: StyleProp<ViewStyle>;
};

export function Skeleton({ style }: SkeletonProps) {
  const { feedbackPresets } = useFeedbackPresets();
  const opacity = useRef(new Animated.Value(0.45)).current;

  useEffect(() => {
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, {
          toValue: 0.85,
          duration: 700,
          useNativeDriver: true
        }),
        Animated.timing(opacity, {
          toValue: 0.45,
          duration: 700,
          useNativeDriver: true
        })
      ])
    );

    loop.start();
    return () => loop.stop();
  }, [opacity]);

  return <Animated.View style={[feedbackPresets.skeleton, style, { opacity }]} />;
}
