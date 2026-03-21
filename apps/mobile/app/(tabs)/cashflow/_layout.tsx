import { Stack } from "expo-router";
import { palette } from "../../../src/theme/tokens";

export default function CashflowStackLayout() {
  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: palette.appBackground },
        animation: "none"
      }}
    />
  );
}
