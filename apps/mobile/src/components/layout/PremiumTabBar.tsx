import type { BottomTabBarProps } from "@react-navigation/bottom-tabs";
import { usePathname, useRouter } from "expo-router";
import { appBottomNavItems } from "./bottomNavConfigs";
import { FloatingBottomNav } from "./FloatingBottomNav";

const rootTabScreens: Record<string, string | undefined> = {
  accounts: "index",
  planner: "index"
};

export function PremiumTabBar({ state, descriptors, navigation }: BottomTabBarProps) {
  const pathname = usePathname();
  const router = useRouter();
  const hidden = pathname?.includes("/planner/expense-tracker") || pathname?.startsWith("/companion");
  const activeKey = state.routes[state.index]?.name.split("/")[0] ?? "index";

  return (
    <FloatingBottomNav
      items={appBottomNavItems}
      activeKey={activeKey}
      hidden={hidden}
      switcherAction={{
        label: "Planning Hub",
        icon: "notebook-outline",
        iconFamily: "material",
        accessibilityLabel: "Open planning hub",
        onPress: () => {
          router.replace("/(tabs)/planner/expense-tracker/overview" as never);
        }
      }}
      onPressItem={(item) => {
        const routeIndex = state.routes.findIndex((route) => route.name.split("/")[0] === item.key);
        if (routeIndex === -1) {
          return;
        }

        const route = state.routes[routeIndex];
        const isFocused = state.index === routeIndex;
        const event = navigation.emit({
          type: "tabPress",
          target: route.key,
          canPreventDefault: true
        });

        if (isFocused || event.defaultPrevented) {
          return;
        }

        const rootScreen = rootTabScreens[item.key];
        if (rootScreen) {
          navigation.navigate(route.name, { screen: rootScreen } as never);
          return;
        }

        navigation.navigate(route.name, route.params);
      }}
    />
  );
}
