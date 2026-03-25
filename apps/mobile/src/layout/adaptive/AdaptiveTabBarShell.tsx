import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { useEffect } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { borders, palette, shadows, spacing, typography } from "../../theme/tokens";
import { useAdaptiveShell } from "./adaptive.hooks";
import { PlanningHubDockButton } from "./PlanningHubDockButton";
import type { AdaptiveTabBarItem, AdaptiveTabBarShellProps } from "./adaptive.types";

function renderIcon(
  item: Pick<AdaptiveTabBarItem, "icon" | "iconFamily">,
  color: string
) {
  if (item.iconFamily === "material") {
    return (
      <MaterialCommunityIcons
        name={item.icon as keyof typeof MaterialCommunityIcons.glyphMap}
        size={18}
        color={color}
      />
    );
  }

  return (
    <Ionicons
      name={item.icon as keyof typeof Ionicons.glyphMap}
      size={18}
      color={color}
    />
  );
}

export function AdaptiveTabBarShell({
  items,
  activeKey,
  onPressItem,
  hidden = false,
  planningHubAction
}: AdaptiveTabBarShellProps) {
  const { metrics, setShellFrame } = useAdaptiveShell();

  useEffect(() => {
    if (hidden) {
      setShellFrame(null);
    }
  }, [hidden, setShellFrame]);

  if (hidden) {
    return null;
  }

  return (
    <View pointerEvents="box-none" style={StyleSheet.absoluteFill}>
      <View
        style={[
          styles.shell,
          {
            left: metrics.tabBarMargins.horizontal,
            right: metrics.tabBarMargins.horizontal,
            bottom: metrics.tabBarMargins.bottom,
            height: metrics.tabBarHeight + metrics.planningHubLift
          }
        ]}
        onLayout={(event) => {
          const layout = event.nativeEvent.layout;
          setShellFrame({
            ...layout,
            top: layout.y,
            bottom: layout.y + layout.height
          });
        }}
      >
        <PlanningHubDockButton
          size={metrics.planningHubButtonSize}
          onPress={planningHubAction.onPress}
          accessibilityLabel={planningHubAction.accessibilityLabel}
        />
        <View
          style={[
            styles.tabBar,
            {
              marginTop: metrics.planningHubLift,
              minHeight: metrics.tabBarHeight,
              borderRadius: metrics.tabBarRadius,
              paddingBottom: spacing[8]
            }
          ]}
        >
          {items.map((item) => {
            const isActive = item.key === activeKey;
            const color = isActive ? palette.textPrimary : palette.textSecondary;

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
                {renderIcon(item, color)}
                <Text style={[styles.label, isActive ? styles.labelActive : null]} numberOfLines={1}>
                  {item.label}
                </Text>
              </Pressable>
            );
          })}
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  shell: {
    position: "absolute"
  },
  tabBar: {
    flex: 1,
    borderWidth: borders.width.thin,
    borderColor: palette.border,
    backgroundColor: palette.tabBarSurface,
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: spacing[8],
    paddingTop: spacing[10],
    ...shadows.floating
  },
  item: {
    flex: 1,
    minHeight: 52,
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[4],
    paddingHorizontal: 2
  },
  itemActive: {
    backgroundColor: "rgba(242,140,40,0.12)",
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.28)"
  },
  itemPressed: {
    opacity: 0.9,
    transform: [{ scale: 0.98 }]
  },
  label: {
    color: palette.textSecondary,
    ...typography.caption,
    textAlign: "center"
  },
  labelActive: {
    color: palette.textPrimary,
    fontWeight: "600"
  }
});
