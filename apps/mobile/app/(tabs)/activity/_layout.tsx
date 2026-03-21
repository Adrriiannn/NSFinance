import { Stack } from "expo-router";
import { palette } from "../../../src/theme/tokens";

export default function ActivityStackLayout() {
  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: palette.appBackground },
        animation: "slide_from_right"
      }}
    />
  );
}
