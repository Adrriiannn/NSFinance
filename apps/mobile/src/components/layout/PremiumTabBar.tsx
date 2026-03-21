import type { BottomTabBarProps } from "@react-navigation/bottom-tabs";
import { useGlobalSearchParams, usePathname, useRouter } from "expo-router";
import { appBottomNavItems } from "./bottomNavConfigs";
import { FloatingBottomNav } from "./FloatingBottomNav";
import { navigateWithProbe } from "../../lib/perf/navigationTiming";

const rootTabScreens: Record<string, string | undefined> = {
  accounts: "index",
  cashflow: "index"
};

export function PremiumTabBar({ state, descriptors, navigation }: BottomTabBarProps) {
  const pathname = usePathname();
  const params = useGlobalSearchParams<{ source?: string }>();
  const router = useRouter();
  const hidden =
    pathname?.startsWith("/planning") ||
    pathname?.startsWith("/companion") ||
    (pathname === "/calendar" && (params.source === "planningHub" || params.source === "expense"));
  const activeKey = state.routes[state.index]?.name.split("/")[0] ?? "index";
  const autoPeekEligiblePath =
    pathname === "/" ||
    pathname === "/accounts" ||
    pathname === "/activity" ||
    pathname === "/cashflow";

  if (hidden) {
    return null;
  }

  return (
    <FloatingBottomNav
      items={appBottomNavItems}
      activeKey={activeKey}
      switcherAction={{
        label: "Planning Hub",
        icon: "notebook-outline",
        iconFamily: "material",
        accessibilityLabel: "Open planning hub",
        behavior: "peek",
        autoPeekEnabled: autoPeekEligiblePath,
        sharedRevealKey: "hub-switcher",
        onPress: () => {
          navigateWithProbe(
            router as unknown as {
              push: (href: string) => void;
              replace: (href: string) => void;
              navigate?: (href: string) => void;
            },
            "/(tabs)/planning",
            "premium-tab-switcher-planning"
          );
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

