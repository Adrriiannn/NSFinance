import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useMemo, useRef } from "react";
import { PanResponder, useWindowDimensions } from "react-native";

type MainTabSwipeTarget = {
  path: "/(tabs)" | "/(tabs)/accounts" | "/(tabs)/activity" | "/(tabs)/planner";
};

const mainTabSwipeTargets: readonly MainTabSwipeTarget[] = [
  { path: "/(tabs)" },
  { path: "/(tabs)/accounts" },
  { path: "/(tabs)/activity" },
  { path: "/(tabs)/planner" }
] as const;

type MainTabPath = MainTabSwipeTarget["path"];

const activationDistance = 18;
const directionIntentRatio = 1.35;

export function useMainTabSwipeNavigation(
  currentPath: MainTabPath,
  options?: { isBlockedRef?: { current: boolean } }
) {
  const router = useRouter();
  const { width } = useWindowDimensions();
  const currentIndex = mainTabSwipeTargets.findIndex((target) => target.path === currentPath);
  const isNavigatingRef = useRef(false);
  const triggerDistance = Math.min(Math.max(width * 0.17, 64), 104);

  useFocusEffect(
    useCallback(() => {
      isNavigatingRef.current = false;
      return () => {
        isNavigatingRef.current = false;
      };
    }, [])
  );

  const gestureHandlers = useMemo(() => {
    if (currentIndex < 0) {
      return undefined;
    }

    return PanResponder.create({
      onMoveShouldSetPanResponder: (_event, gestureState) => {
        if (isNavigatingRef.current || options?.isBlockedRef?.current) {
          return false;
        }

        const horizontalIntent =
          Math.abs(gestureState.dx) > activationDistance &&
          Math.abs(gestureState.dx) > Math.abs(gestureState.dy) * directionIntentRatio;

        if (!horizontalIntent) {
          return false;
        }

        if (gestureState.dx < 0) {
          return currentIndex < mainTabSwipeTargets.length - 1;
        }

        return currentIndex > 0;
      },
      onPanResponderTerminationRequest: () => !isNavigatingRef.current,
      onPanResponderRelease: (_event, gestureState) => {
        if (isNavigatingRef.current || options?.isBlockedRef?.current) {
          return;
        }

        const horizontalIntent =
          Math.abs(gestureState.dx) > triggerDistance &&
          Math.abs(gestureState.dx) > Math.abs(gestureState.dy) * directionIntentRatio;

        const movingTowardPrevious = gestureState.dx > 0;
        const movingTowardNext = gestureState.dx < 0;
        const targetIndex = movingTowardPrevious
          ? currentIndex - 1
          : movingTowardNext
            ? currentIndex + 1
            : currentIndex;

        if (!horizontalIntent || targetIndex < 0 || targetIndex >= mainTabSwipeTargets.length) {
          return;
        }

        isNavigatingRef.current = true;
        router.replace(mainTabSwipeTargets[targetIndex].path as never);
      },
      onPanResponderTerminate: () => {
        isNavigatingRef.current = false;
      }
    }).panHandlers;
  }, [currentIndex, options?.isBlockedRef, router, triggerDistance]);

  return {
    gestureHandlers,
    animatedStyle: undefined
  };
}
