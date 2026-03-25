import { Stack } from "expo-router";
import { useThemeRuntime } from "../../../src/theme/runtime/ThemeRuntimeProvider";

export default function PlannerStackLayout() {
  const { theme } = useThemeRuntime();

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: theme.colors.canvas },
        animation: "none"
      }}
    >
      <Stack.Screen
        name="index"
        options={{
          animation: "none"
        }}
      />
      <Stack.Screen
        name="analytics"
        options={{
          animation: "none"
        }}
      />
      <Stack.Screen
        name="categories"
        options={{
          animation: "none"
        }}
      />
      <Stack.Screen
        name="browse"
        options={{
          animation: "none"
        }}
      />
    </Stack>
  );
}
