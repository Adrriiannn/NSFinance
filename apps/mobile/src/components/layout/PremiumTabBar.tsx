import type { BottomTabBarProps } from "@react-navigation/bottom-tabs";
import { usePathname, useRouter } from "expo-router";
import { appBottomNavItems } from "./bottomNavConfigs";
import { FloatingBottomNav } from "./FloatingBottomNav";
import { navigateWithProbe } from "../../lib/perf/navigationTiming";

const appBottomNavHrefMap: Record<string, string> = {
  index: "/(tabs)",
  accounts: "/(tabs)/accounts",
  activity: "/(tabs)/activity",
  cashflow: "/(tabs)/cashflow"
};

export function PremiumTabBar({ state, descriptors, navigation }: BottomTabBarProps) {
  const pathname = usePathname();
  const router = useRouter();
  const hidden = pathname?.startsWith("/companion");
  const activeKey = state.routes[state.index]?.name.split("/")[0] ?? "index";

  if (hidden) {
    return null;
  }

  return (
    <FloatingBottomNav
      items={appBottomNavItems}
      activeKey={activeKey}
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

        const href = appBottomNavHrefMap[item.key];
        if (!href) {
          return;
        }

        navigateWithProbe(
          router as unknown as {
            push: (href: string) => void;
            replace: (href: string) => void;
            navigate?: (href: string) => void;
          },
          href,
          "premium-tab-item"
        );
      }}
    />
  );
}

