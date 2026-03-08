import type { BottomTabBarProps } from "@react-navigation/bottom-tabs";
import { Ionicons } from "@expo/vector-icons";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import {
  navigation as navMetrics,
  palette,
  radius,
  shadows,
  spacing,
  surfaces,
  typography
} from "../../theme/tokens";

const iconMap: Record<string, keyof typeof Ionicons.glyphMap> = {
  index: "sparkles-outline",
  accounts: "wallet-outline",
  activity: "swap-horizontal-outline",
  planner: "calendar-outline"
};

const iconMapActive: Record<string, keyof typeof Ionicons.glyphMap> = {
  index: "sparkles",
  accounts: "wallet",
  activity: "swap-horizontal",
  planner: "calendar"
};

const rootTabScreens: Record<string, string | undefined> = {
  accounts: "index",
  planner: "index"
};

export function PremiumTabBar({ state, descriptors, navigation }: BottomTabBarProps) {
  const insets = useSafeAreaInsets();
  const bottomOffset = Math.max(insets.bottom, 8) + navMetrics.floatingTabBarOffset;

  return (
    <View pointerEvents="box-none" style={styles.wrapper}>
      <View style={[styles.container, { bottom: bottomOffset }]}>
        {state.routes.map((route, index) => {
          const { options } = descriptors[route.key];
          const isFocused = state.index === index;

          const label =
            typeof options.tabBarLabel === "string"
              ? options.tabBarLabel
              : typeof options.title === "string"
                ? options.title
                : route.name;

          const onPress = () => {
            const event = navigation.emit({
              type: "tabPress",
              target: route.key,
              canPreventDefault: true
            });

            if (!isFocused && !event.defaultPrevented) {
              const rootScreen = rootTabScreens[routeName];
              if (rootScreen) {
                navigation.navigate(route.name, { screen: rootScreen } as never);
                return;
              }

              navigation.navigate(route.name, route.params);
            }
          };

          const onLongPress = () => {
            navigation.emit({
              type: "tabLongPress",
              target: route.key
            });
          };

          const routeName = route.name.split("/")[0];
          const iconName = isFocused ? iconMapActive[routeName] : iconMap[routeName];

          return (
            <View key={route.key} style={styles.itemWrap}>
              <Pressable
                accessibilityRole="button"
                accessibilityState={isFocused ? { selected: true } : {}}
                onPress={onPress}
                onLongPress={onLongPress}
                style={({ pressed }) => [
                  styles.item,
                  isFocused ? styles.itemActive : null,
                  pressed ? styles.itemPressed : null
                ]}
              >
                <Ionicons
                  name={iconName ?? "ellipse-outline"}
                  size={18}
                  color={isFocused ? palette.textPrimary : palette.textSecondary}
                />
                <Text style={[styles.label, isFocused ? styles.labelActive : null]}>{label}</Text>
              </Pressable>
              {index < state.routes.length - 1 ? <View style={styles.separator} /> : null}
            </View>
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
    borderRadius: radius.large,
    backgroundColor: surfaces.tabBar,
    borderWidth: 1,
    borderColor: "rgba(2, 8, 17, 0.95)",
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: spacing[8],
    paddingVertical: spacing[8],
    ...shadows.floating
  },
  itemWrap: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center"
  },
  item: {
    flex: 1,
    minHeight: 54,
    borderRadius: radius.medium,
    alignItems: "center",
    justifyContent: "center",
    gap: 3
  },
  itemActive: {
    backgroundColor: "rgba(47,107,255,0.2)"
  },
  itemPressed: {
    opacity: 0.88,
    transform: [{ scale: 0.98 }]
  },
  separator: {
    width: 1,
    height: 26,
    backgroundColor: "rgba(226,236,255,0.04)"
  },
  label: {
    color: palette.textSecondary,
    ...typography.caption
  },
  labelActive: {
    color: palette.textPrimary,
    fontWeight: "700"
  }
});
