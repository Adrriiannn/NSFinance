import { Stack } from "expo-router";
import { palette } from "../../../src/theme/tokens";

export default function PlannerStackLayout() {
  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: palette.appBackground },
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
    </Stack>
  );
}
