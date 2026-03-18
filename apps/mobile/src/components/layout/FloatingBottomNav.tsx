import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { useEffect, useRef, useState } from "react";
import { Animated, Easing, Pressable, StyleSheet, Text, View } from "react-native";
import Svg, { Defs, LinearGradient, Path, Stop } from "react-native-svg";
import { borders, palette, radius, spacing, typography } from "../../theme/tokens";
import { TabBarShell } from "../ui/surfaces/TabBarShell";
import { surfacePresets } from "../ui/surfaces/surface.presets";

export type FloatingBottomNavItem = {
  key: string;
  label: string;
  icon: string;
  iconFamily?: "ionicons" | "material";
};

type FloatingBottomNavProps = {
  items: readonly FloatingBottomNavItem[];
  activeKey: string;
  onPressItem: (item: FloatingBottomNavItem) => void;
  hidden?: boolean;
  suppressActiveStateForKeys?: readonly string[];
  switcherAction?: {
    label: string;
    icon: string;
    iconFamily?: "ionicons" | "material";
    onPress: () => void;
    accessibilityLabel?: string;
  };
};

type NavLayoutCache = {
  activeKey: string;
  layouts: Partial<Record<string, { x: number; width: number }>>;
};

const navHighlightCache = new Map<string, NavLayoutCache>();
const HIGHLIGHT_ANIMATION_DURATION = 240;
const TAB_BAR_SEAM_COLOR = "#263142";
const SWITCHER_WIDTH = 272;
const SWITCHER_HEIGHT = 70;
const SWITCHER_VISIBLE_WIDTH = 152;
const SWITCHER_BOTTOM_CROP = 0.5;
const SWITCHER_BUTTON_WIDTH = 108;
const SWITCHER_BUTTON_HEIGHT = 56;
const SWITCHER_FILL_INSET = 34;
const SWITCHER_FILL_BASELINE = 64;
const SWITCHER_STROKE_HEIGHT = 64;
const SWITCHER_SIDE_CROP = (SWITCHER_WIDTH - SWITCHER_VISIBLE_WIDTH) / 2;
const SWITCHER_SHAPE_STROKE_PATH =
  "M0 70 C36 70 58 69 72 58 C82 50 88 40 92 24 C96 10 102 4 114 4 L158 4 C170 4 176 10 180 24 C184 40 190 50 200 58 C214 69 236 70 272 70";
const SWITCHER_SHAPE_FILL_PATH = `${SWITCHER_SHAPE_STROKE_PATH} L${SWITCHER_WIDTH - SWITCHER_FILL_INSET} ${SWITCHER_FILL_BASELINE} L${SWITCHER_FILL_INSET} ${SWITCHER_FILL_BASELINE} Z`;
const SWITCHER_SEAM_PATH = `M${SWITCHER_SIDE_CROP} ${SWITCHER_FILL_BASELINE} H${SWITCHER_WIDTH - SWITCHER_SIDE_CROP}`;

function renderNavIcon(
  item: Pick<FloatingBottomNavItem, "icon" | "iconFamily">,
  color: string,
  size: number
) {
  if (item.iconFamily === "material") {
    return (
      <MaterialCommunityIcons
        name={item.icon as keyof typeof MaterialCommunityIcons.glyphMap}
        size={size}
        color={color}
      />
    );
  }

  return <Ionicons name={item.icon as keyof typeof Ionicons.glyphMap} size={size} color={color} />;
}

export function FloatingBottomNav({
  items,
  activeKey,
  onPressItem,
  hidden = false,
  suppressActiveStateForKeys = [],
  switcherAction
}: FloatingBottomNavProps) {
  const shellContentPaddingBottom = spacing[8];
  const switcherContentColor = palette.textSecondary;
  const [itemLayouts, setItemLayouts] = useState<Partial<Record<string, { x: number; width: number }>>>({});
  const highlightLeft = useRef(new Animated.Value(0)).current;
  const highlightWidth = useRef(new Animated.Value(0)).current;
  const hasAnimatedRef = useRef(false);
  const navCacheKey = items.map((item) => item.key).join("|");
  const centerItemKey = items[Math.floor(items.length / 2)]?.key;
  const centerItemLayout = centerItemKey ? itemLayouts[centerItemKey] : undefined;
  const switcherLeft = centerItemLayout
    ? centerItemLayout.x + centerItemLayout.width / 2 - SWITCHER_WIDTH / 2
    : null;

  useEffect(() => {
    const layout = itemLayouts[activeKey];
    if (!layout) {
      return;
    }

    if (!hasAnimatedRef.current) {
      const cachedState = navHighlightCache.get(navCacheKey);
      const previousLayout =
        cachedState && cachedState.activeKey !== activeKey
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
  }, [activeKey, highlightLeft, highlightWidth, itemLayouts, navCacheKey]);

  useEffect(() => {
    navHighlightCache.set(navCacheKey, {
      activeKey,
      layouts: itemLayouts
    });
  }, [activeKey, itemLayouts, navCacheKey]);

  if (hidden) {
    return null;
  }

  return (
    <View pointerEvents="box-none" style={styles.wrapper}>
      <TabBarShell
        style={[
          surfacePresets.tabBarDocked,
          {
            borderColor: TAB_BAR_SEAM_COLOR,
            bottom: -2,
            paddingBottom: shellContentPaddingBottom
          }
        ]}
      >
        {switcherAction ? (
          <View
            pointerEvents="box-none"
            style={[
              styles.switcherMount,
              switcherLeft !== null
                ? { left: switcherLeft, marginLeft: 0 }
                : null
            ]}
          >
            <View pointerEvents="none" style={styles.switcherShape}>
              <Svg
                width={SWITCHER_WIDTH}
                height={SWITCHER_HEIGHT + 1}
                viewBox={`0 0 ${SWITCHER_WIDTH} ${SWITCHER_HEIGHT + 1}`}
                style={styles.switcherSvg}
              >
                <Path
                  d={SWITCHER_SHAPE_FILL_PATH}
                  fill={palette.tabBarSurface}
                />
              </Svg>
              <View style={styles.switcherStrokeCrop}>
                <Svg
                  width={SWITCHER_WIDTH}
                  height={SWITCHER_HEIGHT + 1}
                  viewBox={`0 0 ${SWITCHER_WIDTH} ${SWITCHER_HEIGHT + 1}`}
                  style={styles.switcherSvg}
                >
                  <Path
                    d={SWITCHER_SHAPE_STROKE_PATH}
                    fill="none"
                    stroke={TAB_BAR_SEAM_COLOR}
                    strokeWidth={borders.width.thin}
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                  <Path
                    d={SWITCHER_SEAM_PATH}
                    fill="none"
                    stroke={TAB_BAR_SEAM_COLOR}
                    strokeWidth={borders.width.thin}
                    strokeLinecap="butt"
                  />
                </Svg>
              </View>
            </View>

            <Pressable
              accessibilityRole="button"
              accessibilityLabel={switcherAction.accessibilityLabel ?? switcherAction.label}
              onPress={switcherAction.onPress}
              style={({ pressed }) => [
                styles.switcherButton,
                pressed ? styles.switcherButtonPressed : null
              ]}
            >
              {renderNavIcon(switcherAction, switcherContentColor, 18)}
              <Text style={[styles.switcherLabel, { color: switcherContentColor }]}>{switcherAction.label}</Text>
            </Pressable>
          </View>
        ) : null}
        {itemLayouts[activeKey] && !suppressActiveStateForKeys.includes(activeKey) ? (
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
          const isActive = item.key === activeKey;
          const isVisuallyActive = isActive && !suppressActiveStateForKeys.includes(item.key);
          const color = isVisuallyActive ? palette.textPrimary : palette.textSecondary;
          return (
            <Pressable
              key={item.key}
              accessibilityRole="button"
              accessibilityState={isActive ? { selected: true } : {}}
              onPress={() => onPressItem(item)}
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
              style={({ pressed }) => [
                styles.item,
                pressed ? styles.itemPressed : null
              ]}
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

const styles = StyleSheet.create({
  wrapper: {
    ...StyleSheet.absoluteFillObject
  },
  switcherMount: {
    position: "absolute",
    bottom: "100%",
    left: "50%",
    marginLeft: -SWITCHER_WIDTH / 2,
    width: SWITCHER_WIDTH,
    height: SWITCHER_HEIGHT,
    alignItems: "center",
    justifyContent: "flex-start",
    zIndex: 3,
    transform: [{ translateY: -9.7 }]
  },
  switcherShape: {
    position: "absolute",
    bottom: 0,
    left: "50%",
    marginLeft: -SWITCHER_VISIBLE_WIDTH / 2,
    width: SWITCHER_VISIBLE_WIDTH,
    height: SWITCHER_HEIGHT + 1 - SWITCHER_BOTTOM_CROP,
    overflow: "hidden"
  },
  switcherSvg: {
    marginLeft: -SWITCHER_SIDE_CROP
  },
  switcherStrokeCrop: {
    position: "absolute",
    left: 0,
    right: 0,
    top: 0,
    height: SWITCHER_STROKE_HEIGHT,
    overflow: "hidden"
  },
  switcherButton: {
    position: "absolute",
    top: spacing[10],
    left: "50%",
    marginLeft: -SWITCHER_BUTTON_WIDTH / 2,
    width: SWITCHER_BUTTON_WIDTH,
    minHeight: SWITCHER_BUTTON_HEIGHT,
    paddingHorizontal: spacing[10],
    paddingTop: spacing[0],
    paddingBottom: spacing[8],
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[4],
    borderRadius: radius.hero
  },
  switcherButtonPressed: {
    opacity: 0.74,
    transform: [{ scale: 0.98 }]
  },
  switcherLabel: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700",
    textAlign: "center"
  },
  activeHighlight: {
    position: "absolute",
    top: spacing[6],
    bottom: spacing[4],
    borderRadius: radius.medium,
    backgroundColor: "rgba(47,107,255,0.10)",
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.3)"
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
    color: palette.textPrimary,
    fontWeight: "700"
  }
});
