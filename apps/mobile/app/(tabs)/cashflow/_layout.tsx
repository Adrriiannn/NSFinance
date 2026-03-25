import { Stack } from "expo-router";
import { useThemeRuntime } from "../../../src/theme/runtime/ThemeRuntimeProvider";

export default function CashflowStackLayout() {
  const { theme } = useThemeRuntime();

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: theme.colors.canvas },
        animation: "none"
      }}
    />
  );
}
