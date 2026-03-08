import { Redirect, Stack } from "expo-router";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette } from "../../src/theme/tokens";

export default function AuthLayout() {
  const { isBootstrapping, isAuthenticated } = useAuthSession();

  if (!isBootstrapping && isAuthenticated) {
    return <Redirect href="/(tabs)" />;
  }

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        animation: "none",
        contentStyle: { backgroundColor: palette.appBackground }
      }}
    />
  );
}
