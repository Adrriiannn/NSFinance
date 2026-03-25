import { Ionicons } from "@expo/vector-icons";
import { Redirect, Tabs } from "expo-router";
import { PremiumTabBar } from "../../src/components/layout/PremiumTabBar";
import { AdaptiveAppShell } from "../../src/layout/adaptive/AdaptiveAppShell";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { useThemeRuntime } from "../../src/theme/runtime/ThemeRuntimeProvider";

export default function TabsLayout() {
  const { isBootstrapping, isAuthenticated } = useAuthSession();
  const { theme } = useThemeRuntime();

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={"/login" as never} />;
  }

  return (
    <AdaptiveAppShell>
      <Tabs
        screenOptions={{
          headerShown: false,
          sceneStyle: {
            backgroundColor: theme.colors.canvas
          }
        }}
        tabBar={(props) => <PremiumTabBar {...props} />}
      >
        <Tabs.Screen
          name="index"
          options={{
            title: "Home",
            tabBarIcon: ({ color, size, focused }) => (
              <Ionicons
                name={focused ? "apps" : "apps-outline"}
                color={color}
                size={size}
              />
            )
          }}
        />
        <Tabs.Screen
          name="accounts"
          options={{
            title: "Accounts",
            tabBarIcon: ({ color, size, focused }) => (
              <Ionicons
                name={focused ? "wallet" : "wallet-outline"}
                color={color}
                size={size}
              />
            )
          }}
        />
        <Tabs.Screen
          name="activity"
          options={{
            title: "Activity",
            tabBarIcon: ({ color, size, focused }) => (
              <Ionicons
                name={focused ? "swap-horizontal" : "swap-horizontal-outline"}
                color={color}
                size={size}
              />
            )
          }}
        />
        <Tabs.Screen
          name="cashflow"
          options={{
            title: "Cashflow",
            tabBarIcon: ({ color, size, focused }) => (
              <Ionicons
                name={focused ? "calendar" : "calendar-outline"}
                color={color}
                size={size}
              />
            )
          }}
        />
        <Tabs.Screen
          name="calendar"
          options={{
            title: "Calendar",
            tabBarIcon: ({ color, size, focused }) => (
              <Ionicons
                name={focused ? "today" : "today-outline"}
                color={color}
                size={size}
              />
            )
          }}
        />
        <Tabs.Screen
          name="planning"
          options={{
            href: null
          }}
        />
        <Tabs.Screen
          name="companion"
          options={{
            href: null
          }}
        />
      </Tabs>
    </AdaptiveAppShell>
  );
}

