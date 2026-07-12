import { Ionicons } from "@expo/vector-icons";
import { Redirect, Tabs, useSegments } from "expo-router";
import { useEffect, useState } from "react";
import { BackHandler, Platform, StyleSheet, View } from "react-native";
import { PremiumTabBar } from "../../src/components/layout/PremiumTabBar";
import { LocationPermissionPromptModal } from "../../src/features/ai/location/LocationPermissionPromptModal";
import { canRenderProtectedRoutes } from "../../src/features/auth/sessionProtectionPolicy";
import {
  getLocationUxState,
  markBootExplainerShown,
  requestForegroundLocationAccess
} from "../../src/features/ai/location/locationPermissionService";
import { AdaptiveAppShell } from "../../src/layout/adaptive/AdaptiveAppShell";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { useThemeRuntime } from "../../src/theme/runtime/ThemeRuntimeProvider";

export default function TabsLayout() {
  const { isBootstrapping, isAuthenticated, isAppLocked, prepareForAppExit } = useAuthSession();
  const { theme } = useThemeRuntime();
  const segments = useSegments();
  const [bootLocationPromptVisible, setBootLocationPromptVisible] = useState(false);
  const [bootPromptBusy, setBootPromptBusy] = useState(false);

  useEffect(() => {
    if (isBootstrapping || !isAuthenticated) {
      return;
    }

    let cancelled = false;
    const loadBootPromptState = async () => {
      const state = await getLocationUxState();
      if (!cancelled && !state.bootExplainerShown) {
        setBootLocationPromptVisible(true);
      }
    };

    void loadBootPromptState();
    return () => {
      cancelled = true;
    };
  }, [isAuthenticated, isBootstrapping]);

  const handleBootAllowLocation = async () => {
    if (bootPromptBusy) {
      return;
    }

    setBootPromptBusy(true);
    try {
      await markBootExplainerShown();
      await requestForegroundLocationAccess();
      setBootLocationPromptVisible(false);
    } finally {
      setBootPromptBusy(false);
    }
  };

  const handleBootNotNow = async () => {
    if (bootPromptBusy) {
      return;
    }

    setBootPromptBusy(true);
    try {
      await markBootExplainerShown();
      setBootLocationPromptVisible(false);
    } finally {
      setBootPromptBusy(false);
    }
  };

  const canRenderTabs = canRenderProtectedRoutes({
    isBootstrapping,
    isLocked: isAppLocked,
    isAuthenticated
  });
  const isTopLevelTabRoute = segments[0] === "(tabs)" && segments.length <= 2;

  useEffect(() => {
    if (Platform.OS !== "android" || !canRenderTabs || !isTopLevelTabRoute) {
      return;
    }

    const subscription = BackHandler.addEventListener("hardwareBackPress", () => {
      prepareForAppExit();
      requestAnimationFrame(() => BackHandler.exitApp());
      return true;
    });

    return () => subscription.remove();
  }, [canRenderTabs, isTopLevelTabRoute, prepareForAppExit]);

  if (!canRenderTabs && (isBootstrapping || isAppLocked)) {
    return <View style={[styles.secureBoundary, { backgroundColor: theme.colors.canvas }]} />;
  }

  if (!canRenderTabs) {
    return <Redirect href={"/login" as never} />;
  }

  return (
    <AdaptiveAppShell>
      <LocationPermissionPromptModal
        visible={bootLocationPromptVisible}
        title="Location helps nearby recommendations"
        message="Location is optional. If you allow it, NSFinance can ground nearby dining and café suggestions using your current area."
        onRequestClose={handleBootNotNow}
        actions={[
          {
            label: "Allow location",
            onPress: () => {
              void handleBootAllowLocation();
            },
            variant: "primary",
            disabled: bootPromptBusy
          },
          {
            label: "Not now",
            onPress: () => {
              void handleBootNotNow();
            },
            variant: "secondary",
            disabled: bootPromptBusy
          }
        ]}
      />
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

const styles = StyleSheet.create({
  secureBoundary: {
    flex: 1
  }
});

