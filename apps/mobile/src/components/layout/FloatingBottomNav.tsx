import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { useEffect, useRef, useState } from "react";
import { Animated, Pressable, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { navigation as navMetrics, palette, radius, shadows, spacing, typography } from "../../theme/tokens";

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
};

export function FloatingBottomNav({ items, activeKey, onPressItem, hidden = false }: FloatingBottomNavProps) {
  const insets = useSafeAreaInsets();
  const bottomOffset = Math.max(insets.bottom, 8) + navMetrics.floatingTabBarOffset;
  const [itemLayouts, setItemLayouts] = useState<Partial<Record<string, { x: number; width: number }>>>({});
  const highlightLeft = useRef(new Animated.Value(0)).current;
  const highlightWidth = useRef(new Animated.Value(0)).current;
  const hasAnimatedRef = useRef(false);

  useEffect(() => {
    const layout = itemLayouts[activeKey];
    if (!layout) {
      return;
    }

    if (!hasAnimatedRef.current) {
      highlightLeft.setValue(layout.x);
      highlightWidth.setValue(layout.width);
      hasAnimatedRef.current = true;
      return;
    }

    Animated.parallel([
      Animated.timing(highlightLeft, {
        toValue: layout.x,
        duration: 210,
        useNativeDriver: false
      }),
      Animated.timing(highlightWidth, {
        toValue: layout.width,
        duration: 210,
        useNativeDriver: false
      })
    ]).start();
  }, [activeKey, highlightLeft, highlightWidth, itemLayouts]);

  if (hidden) {
    return null;
  }

  return (
    <View pointerEvents="box-none" style={styles.wrapper}>
      <View style={[styles.container, { bottom: bottomOffset }]}>
        {itemLayouts[activeKey] ? (
          <Animated.View
            pointerEvents="none"
            style={[
              styles.activeHighlight,
              {
                left: highlightLeft,
                width: highlightWidth
              }
            ]}
          />
        ) : null}
        {items.map((item) => {
          const isActive = item.key === activeKey;
          const color = isActive ? palette.textPrimary : palette.textSecondary;
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
              {item.iconFamily === "material" ? (
                <MaterialCommunityIcons name={item.icon as keyof typeof MaterialCommunityIcons.glyphMap} size={18} color={color} />
              ) : (
                <Ionicons name={item.icon as keyof typeof Ionicons.glyphMap} size={18} color={color} />
              )}
              <Text
                style={[styles.label, isActive ? styles.labelActive : null]}
                numberOfLines={1}
                adjustsFontSizeToFit
                minimumFontScale={0.82}
              >
                {item.label}
              </Text>
            </Pressable>
          );
        })}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    ...StyleSheet.absoluteFillObject
  },
  container: {
    position: "absolute",
    left: navMetrics.floatingTabBarSideInset,
    right: navMetrics.floatingTabBarSideInset,
    minHeight: navMetrics.floatingTabBarHeight,
    borderRadius: radius.hero,
    backgroundColor: "rgba(10,20,34,0.96)",
    borderWidth: 1,
    borderColor: palette.border,
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: spacing[8],
    paddingVertical: spacing[8],
    ...shadows.floating
  },
  activeHighlight: {
    position: "absolute",
    top: spacing[8],
    bottom: spacing[8],
    borderRadius: radius.medium,
    backgroundColor: "rgba(47,107,255,0.26)",
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.36)"
  },
  item: {
    flex: 1,
    minHeight: 58,
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
