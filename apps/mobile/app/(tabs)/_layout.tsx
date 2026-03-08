import { Ionicons } from "@expo/vector-icons";
import { Redirect, Tabs } from "expo-router";
import { PremiumTabBar } from "../../src/components/layout/PremiumTabBar";
import { palette } from "../../src/theme/tokens";
import { useAuthSession } from "../../src/providers/AuthProvider";

export default function TabsLayout() {
  const { isBootstrapping, isAuthenticated } = useAuthSession();

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={"/login" as never} />;
  }

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        sceneStyle: {
          backgroundColor: palette.appBackground
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
              name={focused ? "sparkles" : "sparkles-outline"}
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
        name="planner"
        options={{
          title: "Planner",
          tabBarIcon: ({ color, size, focused }) => (
            <Ionicons
              name={focused ? "calendar" : "calendar-outline"}
              color={color}
              size={size}
            />
          )
        }}
      />
    </Tabs>
  );
}

