import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Animated,
  PanResponder,
  Pressable,
  StyleSheet,
  View
} from "react-native";
import { palette, spacing, zIndex } from "../../theme/tokens";
import { surfacePresets } from "../../components/ui/surfaces/surface.presets";
import { useAdaptiveShell } from "./adaptive.hooks";
import { useAuthSession } from "../../providers/AuthProvider";
import {
  getAssistantDockState,
  setAssistantDockState,
  type AssistantDockMode,
  type AssistantDockSide
} from "./assistantDock.storage";
import type { FloatingAssistantDockProps } from "./adaptive.types";

export function FloatingAssistantDock({
  onPress,
  accessibilityLabel = "Open NS Companion",
  hidden = false
}: FloatingAssistantDockProps) {
  const { metrics } = useAdaptiveShell();
  const { session } = useAuthSession();
  const userId = session?.user.id ?? null;
  const dockedTravel =
    metrics.floatingAssistantSize - metrics.floatingAssistantDockedVisibleWidth;
  const expandedHorizontalInset = spacing[12];
  const expandedMinLeft = Math.max(metrics.safeAreaInsets.left, expandedHorizontalInset);
  const expandedMaxLeft = Math.max(
    expandedMinLeft,
    metrics.screenWidth -
      metrics.floatingAssistantSize -
      Math.max(metrics.safeAreaInsets.right, expandedHorizontalInset)
  );
  const minTop = metrics.safeAreaInsets.top + spacing[12];
  const maxTop = Math.max(
    minTop,
    metrics.screenHeight - metrics.floatingAssistantBottomOffset - metrics.floatingAssistantSize
  );
  const [dockMode, setDockMode] = useState<AssistantDockMode>("docked");
  const [dockSide, setDockSide] = useState<AssistantDockSide>("right");
  const [verticalRatio, setVerticalRatio] = useState(1);
  const [hasHydratedDockState, setHasHydratedDockState] = useState(false);
  const [isDraggingDock, setIsDraggingDock] = useState(false);
  const left = useRef(new Animated.Value(0)).current;
  const top = useRef(new Animated.Value(maxTop)).current;
  const isExpanded = dockMode === "expanded";
  const isDockedLeft = dockSide === "left";
  const dragStartLeftRef = useRef(0);
  const dragStartTopRef = useRef(maxTop);

  const clampTop = useCallback(
    (value: number) => Math.max(minTop, Math.min(value, maxTop)),
    [maxTop, minTop]
  );

  const getExpandedLeft = useCallback(
    (side: AssistantDockSide) =>
      side === "left"
        ? expandedMinLeft
        : expandedMaxLeft,
    [expandedMaxLeft, expandedMinLeft]
  );

  const getDockedLeft = useCallback(
    (side: AssistantDockSide) =>
      side === "left"
        ? metrics.safeAreaInsets.left - dockedTravel
        : metrics.screenWidth - metrics.safeAreaInsets.right - metrics.floatingAssistantDockedVisibleWidth,
    [
      dockedTravel,
      metrics.floatingAssistantDockedVisibleWidth,
      metrics.safeAreaInsets.left,
      metrics.safeAreaInsets.right,
      metrics.screenWidth
    ]
  );

  const topFromRatio = useCallback(
    (ratio: number) => {
      const boundedRatio = Math.max(0, Math.min(ratio, 1));
      return clampTop(minTop + (maxTop - minTop) * boundedRatio);
    },
    [clampTop, maxTop, minTop]
  );

  const ratioFromTop = useCallback(
    (value: number) => {
      if (maxTop <= minTop) {
        return 1;
      }

      return (clampTop(value) - minTop) / (maxTop - minTop);
    },
    [clampTop, maxTop, minTop]
  );

  useEffect(() => {
    let cancelled = false;

    const loadDockMode = async () => {
      try {
        const persistedState = await getAssistantDockState(userId);
        if (cancelled) {
          return;
        }

        const nextState = persistedState ?? {
          mode: "docked" as AssistantDockMode,
          side: "right" as AssistantDockSide,
          verticalRatio: 1
        };
        const nextTop = topFromRatio(nextState.verticalRatio);
        setDockMode(nextState.mode);
        setDockSide(nextState.side);
        setVerticalRatio(nextState.verticalRatio);
        left.setValue(
          nextState.mode === "expanded"
            ? getExpandedLeft(nextState.side)
            : getDockedLeft(nextState.side)
        );
        top.setValue(nextTop);
      } finally {
        if (!cancelled) {
          setHasHydratedDockState(true);
        }
      }
    };

    void loadDockMode();

    return () => {
      cancelled = true;
    };
  }, [getDockedLeft, getExpandedLeft, top, topFromRatio, left, userId]);

  const persistDockState = useCallback(
    (
      nextMode: AssistantDockMode,
      nextSide: AssistantDockSide,
      nextVerticalRatio: number
    ) => {
      void setAssistantDockState(
        {
          mode: nextMode,
          side: nextSide,
          verticalRatio: nextVerticalRatio
        },
        userId
      );
    },
    [userId]
  );

  const animateToMode = useCallback(
    (nextMode: AssistantDockMode) => {
      setDockMode(nextMode);
      setIsDraggingDock(false);
      persistDockState(nextMode, dockSide, verticalRatio);
      Animated.parallel([
        Animated.timing(left, {
          toValue: nextMode === "expanded" ? getExpandedLeft(dockSide) : getDockedLeft(dockSide),
          duration: metrics.floatingAssistantDockAnimationDurationMs,
          useNativeDriver: false
        }),
        Animated.timing(top, {
          toValue: topFromRatio(verticalRatio),
          duration: metrics.floatingAssistantDockAnimationDurationMs,
          useNativeDriver: false
        })
      ]).start();
    },
    [
      dockSide,
      getDockedLeft,
      getExpandedLeft,
      left,
      metrics.floatingAssistantDockAnimationDurationMs,
      persistDockState,
      top,
      topFromRatio,
      verticalRatio
    ]
  );

  const snapDock = useCallback(
    (nextSide: AssistantDockSide, nextTopValue: number) => {
      const boundedTop = clampTop(nextTopValue);
      const nextRatio = ratioFromTop(boundedTop);
      setDockSide(nextSide);
      setVerticalRatio(nextRatio);
      Animated.parallel([
        Animated.timing(left, {
          toValue: getDockedLeft(nextSide),
          duration: metrics.floatingAssistantDockAnimationDurationMs,
          useNativeDriver: false
        }),
        Animated.timing(top, {
          toValue: boundedTop,
          duration: metrics.floatingAssistantDockAnimationDurationMs,
          useNativeDriver: false
        })
      ]).start(({ finished }) => {
        if (!finished) {
          return;
        }

        setDockMode("docked");
        setIsDraggingDock(false);
        persistDockState("docked", nextSide, nextRatio);
      });
    },
    [
      clampTop,
      getDockedLeft,
      left,
      metrics.floatingAssistantDockAnimationDurationMs,
      persistDockState,
      ratioFromTop,
      top
    ]
  );

  const panResponder = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponder: (_event, gestureState) =>
          Math.abs(gestureState.dx) > 10 || Math.abs(gestureState.dy) > 10,
        onPanResponderGrant: () => {
          const startTop = topFromRatio(verticalRatio);
          const startLeft = getExpandedLeft(dockSide);
          dragStartLeftRef.current = startLeft;
          dragStartTopRef.current = startTop;
          if (!isExpanded) {
            setIsDraggingDock(true);
          }
          left.setValue(startLeft);
          top.setValue(startTop);
        },
        onPanResponderMove: (_event, gestureState) => {
          const nextLeft = Math.max(
            expandedMinLeft,
            Math.min(
              dragStartLeftRef.current + gestureState.dx,
              expandedMaxLeft
            )
          );
          const nextTop = clampTop(dragStartTopRef.current + gestureState.dy);
          left.setValue(nextLeft);
          top.setValue(nextTop);
        },
        onPanResponderRelease: (_event, gestureState) => {
          const currentTop = clampTop(dragStartTopRef.current + gestureState.dy);
          const nextRatio = ratioFromTop(currentTop);
          const releaseX =
            dragStartLeftRef.current + gestureState.dx + metrics.floatingAssistantSize / 2;
          const nextSide = releaseX < metrics.screenWidth / 2 ? "left" : "right";

          if (!isExpanded) {
            snapDock(nextSide, currentTop);
            return;
          }

          const towardWallDistance =
            dockSide === "left" ? -gestureState.dx : gestureState.dx;
          if (nextSide === dockSide && towardWallDistance >= metrics.floatingAssistantSwipeToDockThreshold) {
            snapDock(nextSide, currentTop);
            return;
          }

          setDockSide(nextSide);
          setVerticalRatio(nextRatio);
          persistDockState("expanded", nextSide, nextRatio);
          Animated.parallel([
            Animated.timing(left, {
              toValue: getExpandedLeft(nextSide),
              duration: metrics.floatingAssistantDockAnimationDurationMs,
              useNativeDriver: false
            }),
            Animated.timing(top, {
              toValue: currentTop,
              duration: metrics.floatingAssistantDockAnimationDurationMs,
              useNativeDriver: false
            })
          ]).start();
        },
        onPanResponderTerminate: () => {
          if (!isExpanded) {
            snapDock(dockSide, topFromRatio(verticalRatio));
            return;
          }

          Animated.parallel([
            Animated.timing(left, {
              toValue: getExpandedLeft(dockSide),
              duration: metrics.floatingAssistantDockAnimationDurationMs,
              useNativeDriver: false
            }),
            Animated.timing(top, {
              toValue: topFromRatio(verticalRatio),
              duration: metrics.floatingAssistantDockAnimationDurationMs,
              useNativeDriver: false
            })
          ]).start();
        }
      }),
    [
      clampTop,
      dockSide,
      getExpandedLeft,
      isExpanded,
      left,
      metrics.floatingAssistantDockAnimationDurationMs,
      metrics.floatingAssistantSize,
      metrics.screenWidth,
      metrics.floatingAssistantSwipeToDockThreshold,
      expandedMaxLeft,
      expandedMinLeft,
      persistDockState,
      ratioFromTop,
      snapDock,
      top,
      topFromRatio,
      verticalRatio
    ]
  );

  const showFullCircle = isExpanded || isDraggingDock;

  if (!hasHydratedDockState) {
    return null;
  }

  return (
    <Animated.View
      pointerEvents={hidden ? "none" : "box-none"}
      style={[
        styles.wrapper,
        {
          left,
          top,
          opacity: hidden ? 0 : 1
        }
      ]}
      {...(hidden ? {} : panResponder.panHandlers)}
    >
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={isExpanded ? accessibilityLabel : "Show NS Companion"}
        onPress={() => {
          if (isExpanded) {
            onPress();
            return;
          }

          animateToMode("expanded");
        }}
        style={({ pressed }) => [
          surfacePresets.fab,
          styles.button,
          styles.buttonShadow,
          {
            width: metrics.floatingAssistantSize,
            height: metrics.floatingAssistantSize,
            borderRadius: metrics.floatingAssistantSize / 2
          },
          !showFullCircle && (isDockedLeft
              ? styles.dockedButtonLeft
              : styles.dockedButtonRight),
          pressed ? styles.buttonPressed : null
        ]}
      >
        {showFullCircle ? (
          <MaterialCommunityIcons
            name="robot-happy-outline"
            size={22}
            color={palette.accent}
          />
        ) : (
          <View
            style={[
              styles.handleWrap,
              isDockedLeft ? styles.handleWrapLeft : styles.handleWrapRight
            ]}
          >
            <Ionicons
              name={isDockedLeft ? "arrow-forward" : "arrow-back"}
              size={16}
              color={palette.accent}
            />
          </View>
        )}
      </Pressable>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    position: "absolute",
    zIndex: zIndex.fab,
    elevation: zIndex.fab
  },
  button: {
    paddingHorizontal: 0,
    alignItems: "center",
    justifyContent: "center",
    overflow: "hidden"
  },
  buttonShadow: {
    shadowColor: "#000000",
    shadowOpacity: 0.12,
    shadowRadius: 10,
    shadowOffset: { width: 0, height: 2 },
    elevation: 6
  },
  dockedButtonLeft: {
    borderTopLeftRadius: 0,
    borderBottomLeftRadius: 0
  },
  dockedButtonRight: {
    borderTopRightRadius: 0,
    borderBottomRightRadius: 0
  },
  handleWrap: {
    width: "100%",
    height: "100%",
    justifyContent: "center",
    paddingLeft: 5
  },
  handleWrapLeft: {
    alignItems: "flex-end",
    paddingLeft: 0,
    paddingRight: 5
  },
  handleWrapRight: {
    alignItems: "flex-start",
    paddingLeft: 5
  },
  buttonPressed: {
    opacity: 0.94,
    transform: [{ scale: 0.97 }]
  }
});
