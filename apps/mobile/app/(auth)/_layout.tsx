import { Redirect, Stack } from "expo-router";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { useThemeRuntime } from "../../src/theme/runtime/ThemeRuntimeProvider";

export default function AuthLayout() {
  const { isBootstrapping, isAuthenticated } = useAuthSession();
  const { theme } = useThemeRuntime();

  if (!isBootstrapping && isAuthenticated) {
    return <Redirect href="/(tabs)" />;
  }

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        animation: "none",
        contentStyle: { backgroundColor: theme.colors.canvas }
      }}
    />
  );
}
