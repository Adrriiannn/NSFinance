import { Ionicons } from "@expo/vector-icons";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { navigation as navMetrics, palette, radius, shadows, spacing, typography } from "../../theme/tokens";

export type FloatingBottomNavItem = {
  key: string;
  label: string;
  icon: keyof typeof Ionicons.glyphMap;
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

  if (hidden) {
    return null;
  }

  return (
    <View pointerEvents="box-none" style={styles.wrapper}>
      <View style={[styles.container, { bottom: bottomOffset }]}>
        {items.map((item) => {
          const isActive = item.key === activeKey;
          return (
            <Pressable
              key={item.key}
              accessibilityRole="button"
              accessibilityState={isActive ? { selected: true } : {}}
              onPress={() => onPressItem(item)}
              style={({ pressed }) => [
                styles.item,
                isActive ? styles.itemActive : null,
                pressed ? styles.itemPressed : null
              ]}
            >
              <Ionicons
                name={item.icon}
                size={18}
                color={isActive ? palette.textPrimary : palette.textSecondary}
              />
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
  item: {
    flex: 1,
    minHeight: 58,
    borderRadius: radius.medium,
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[4],
    paddingHorizontal: 2
  },
  itemActive: {
    backgroundColor: "rgba(47,107,255,0.26)",
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.36)"
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
