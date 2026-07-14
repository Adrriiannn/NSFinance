import { Ionicons } from "@expo/vector-icons";
import { useEffect, useMemo, useRef, useState } from "react";
import { Animated, Easing, Platform, Pressable, StyleSheet, Text, View } from "react-native";
import Svg, { Defs, LinearGradient, Path, Stop } from "react-native-svg";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { getEffectiveBottomSystemInset } from "../../theme/insets";
import { useThemeTokens, type ThemeTokens } from "../../theme/tokens";
import { TabBarShell } from "../ui/surfaces/TabBarShell";
import { useSurfacePresets } from "../ui/surfaces/surface.presets";

export type FloatingBottomNavItem = {
  key: string;
  label: string;
  icon: string;
};

type FloatingBottomNavProps = {
  items: readonly FloatingBottomNavItem[];
  activeKey: string;
  onPressItem: (item: FloatingBottomNavItem) => void;
  hidden?: boolean;
  suppressActiveStateForKeys?: readonly string[];
};

type NavLayoutCache = {
  activeKey: string;
  layouts: Partial<Record<string, { x: number; width: number }>>;
};

const navHighlightCache = new Map<string, NavLayoutCache>();
const HIGHLIGHT_ANIMATION_DURATION = 240;

function renderNavIcon(item: FloatingBottomNavItem, color: string, size: number) {
  return <Ionicons name={item.icon as keyof typeof Ionicons.glyphMap} size={size} color={color} />;
}

export function FloatingBottomNav({
  items,
  activeKey,
  onPressItem,
  hidden = false,
  suppressActiveStateForKeys = []
}: FloatingBottomNavProps) {
  const { palette, radius, spacing, typography } = useThemeTokens();
  const surfacePresets = useSurfacePresets();
  const styles = useMemo(
    () =>
      createStyles({
        palette,
        radius,
        spacing,
        typography
      }),
    [palette, radius, spacing, typography]
  );
  const insets = useSafeAreaInsets();
  const androidBottomInset =
    Platform.OS === "android" ? getEffectiveBottomSystemInset(insets.bottom) : 0;
  const shellContentPaddingBottom = spacing[8];
  const tabBarBottomOffset =
    Platform.OS === "android"
      ? androidBottomInset > 0
        ? -2 + androidBottomInset
        : -1
      : -2;
  const [optimisticActiveKey, setOptimisticActiveKey] = useState<string | null>(null);
  const [itemLayouts, setItemLayouts] = useState<
    Partial<Record<string, { x: number; width: number }>>
  >({});
  const hasExplicitActiveKey = items.some((item) => item.key === activeKey);
  const resolvedActiveKey = hasExplicitActiveKey ? (optimisticActiveKey ?? activeKey) : activeKey;
  const highlightLeft = useRef(new Animated.Value(0)).current;
  const highlightWidth = useRef(new Animated.Value(0)).current;
  const hasAnimatedRef = useRef(false);
  const navCacheKey = items.map((item) => item.key).join("|");

  useEffect(() => {
    const layout = itemLayouts[resolvedActiveKey];
    if (!layout) {
      return;
    }

    if (!hasAnimatedRef.current) {
      const cachedState = navHighlightCache.get(navCacheKey);
      const previousLayout =
        cachedState && cachedState.activeKey !== resolvedActiveKey
          ? cachedState.layouts[cachedState.activeKey]
          : null;

      if (previousLayout) {
        highlightLeft.setValue(previousLayout.x);
        highlightWidth.setValue(previousLayout.width);

        Animated.parallel([
          Animated.timing(highlightLeft, {
            toValue: layout.x,
            duration: HIGHLIGHT_ANIMATION_DURATION,
            easing: Easing.out(Easing.cubic),
            useNativeDriver: false
          }),
          Animated.timing(highlightWidth, {
            toValue: layout.width,
            duration: HIGHLIGHT_ANIMATION_DURATION,
            easing: Easing.out(Easing.cubic),
            useNativeDriver: false
          })
        ]).start();
      } else {
        highlightLeft.setValue(layout.x);
        highlightWidth.setValue(layout.width);
      }

      hasAnimatedRef.current = true;
      return;
    }

    Animated.parallel([
      Animated.timing(highlightLeft, {
        toValue: layout.x,
        duration: HIGHLIGHT_ANIMATION_DURATION,
        easing: Easing.out(Easing.cubic),
        useNativeDriver: false
      }),
      Animated.timing(highlightWidth, {
        toValue: layout.width,
        duration: HIGHLIGHT_ANIMATION_DURATION,
        easing: Easing.out(Easing.cubic),
        useNativeDriver: false
      })
    ]).start();
  }, [highlightLeft, highlightWidth, itemLayouts, navCacheKey, resolvedActiveKey]);

  useEffect(() => {
    navHighlightCache.set(navCacheKey, {
      activeKey: resolvedActiveKey,
      layouts: itemLayouts
    });
  }, [itemLayouts, navCacheKey, resolvedActiveKey]);

  useEffect(() => {
    setOptimisticActiveKey(null);
  }, [activeKey]);

  if (hidden) {
    return null;
  }

  return (
    <View pointerEvents="box-none" style={styles.wrapper}>
      <TabBarShell
        style={[
          surfacePresets.tabBarDocked,
          {
            borderColor: palette.border,
            bottom: tabBarBottomOffset,
            paddingBottom: shellContentPaddingBottom
          }
        ]}
      >
        {itemLayouts[resolvedActiveKey] && !suppressActiveStateForKeys.includes(resolvedActiveKey) ? (
          <Animated.View
            pointerEvents="none"
            style={[
              styles.activeHighlight,
              {
                left: highlightLeft,
                width: highlightWidth,
                bottom: shellContentPaddingBottom
              }
            ]}
          />
        ) : null}
        {items.length > 1 ? (
          <View
            pointerEvents="none"
            style={[
              styles.separatorLayer,
              {
                left: spacing[8],
                right: spacing[8],
                top: spacing[8],
                bottom: shellContentPaddingBottom
              }
            ]}
          >
            {items.slice(0, -1).map((item, index) => (
              <Svg
                key={`${item.key}-separator`}
                width={4}
                height={22}
                viewBox="0 0 2 24"
                style={[
                  styles.separator,
                  {
                    left: `${((index + 1) / items.length) * 100}%`
                  }
                ]}
              >
                <Defs>
                  <LinearGradient id={`nav-separator-${index}`} x1="0" y1="0" x2="0" y2="1">
                    <Stop offset="0" stopColor={palette.border} stopOpacity="0" />
                    <Stop offset="0.28" stopColor={palette.border} stopOpacity="0.28" />
                    <Stop offset="0.5" stopColor={palette.border} stopOpacity="0.5" />
                    <Stop offset="0.72" stopColor={palette.border} stopOpacity="0.28" />
                    <Stop offset="1" stopColor={palette.border} stopOpacity="0" />
                  </LinearGradient>
                </Defs>
                <Path
                  d="M1 2 L1 20"
                  stroke={`url(#nav-separator-${index})`}
                  strokeWidth={1}
                  strokeLinecap="round"
                />
              </Svg>
            ))}
          </View>
        ) : null}
        {items.map((item) => {
          const isActive = item.key === resolvedActiveKey;
          const isVisuallyActive = isActive && !suppressActiveStateForKeys.includes(item.key);
          const color = isVisuallyActive ? palette.accent : palette.textSecondary;
          return (
            <Pressable
              key={item.key}
              accessibilityRole="button"
              accessibilityState={isActive ? { selected: true } : {}}
              onPress={() => {
                if (hasExplicitActiveKey && item.key !== activeKey) {
                  setOptimisticActiveKey(item.key);
                }
                onPressItem(item);
              }}
              onLayout={(event) => {
                const { x, width } = event.nativeEvent.layout;
                setItemLayouts((current) => {
                  const previous = current[item.key];
                  if (previous?.x === x && previous?.width === width) {
                    return current;
                  }

                  return {
                    ...current,
                    [item.key]: { x, width }
                  };
                });
              }}
              style={({ pressed }) => [styles.item, pressed ? styles.itemPressed : null]}
            >
              {renderNavIcon(item, color, 18)}
              <Text
                style={[styles.label, isVisuallyActive ? styles.labelActive : null]}
                numberOfLines={1}
                adjustsFontSizeToFit
                minimumFontScale={0.82}
              >
                {item.label}
              </Text>
            </Pressable>
          );
        })}
      </TabBarShell>
    </View>
  );
}

type FloatingBottomNavStyles = Pick<
  ThemeTokens,
  "palette" | "radius" | "spacing" | "typography"
>;

function createStyles({ palette, radius, spacing, typography }: FloatingBottomNavStyles) {
  return StyleSheet.create({
    wrapper: {
      ...StyleSheet.absoluteFillObject
    },
    activeHighlight: {
      position: "absolute",
      top: spacing[6],
      bottom: spacing[4],
      borderRadius: radius.medium,
      backgroundColor: "rgba(242,140,40,0.10)",
      borderWidth: 1,
      borderColor: "rgba(242,140,40,0.32)"
    },
    separatorLayer: {
      position: "absolute",
      justifyContent: "center"
    },
    separator: {
      position: "absolute",
      top: "28%",
      height: "30%",
      marginLeft: -1
    },
    item: {
      flex: 1,
      minHeight: 56,
      borderRadius: radius.medium,
      alignItems: "center",
      justifyContent: "center",
      gap: spacing[4],
      paddingHorizontal: 2,
      zIndex: 1
    },
    itemPressed: {
      opacity: 0.88,
      transform: [{ scale: 0.98 }]
    },
    label: {
      color: palette.textSecondary,
      ...typography.caption,
      textAlign: "center"
    },
    labelActive: {
      color: palette.accent,
      fontWeight: "500"
    }
  });
}
