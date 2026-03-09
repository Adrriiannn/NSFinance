import { DarkTheme, ThemeProvider } from "@react-navigation/native";
import { Stack } from "expo-router";
import { StatusBar } from "expo-status-bar";
import { AppProviders } from "../src/providers/AppProviders";
import { palette } from "../src/theme/tokens";

const appNavigationTheme = {
  ...DarkTheme,
  colors: {
    ...DarkTheme.colors,
    primary: palette.primary,
    background: palette.appBackground,
    card: palette.appBackground,
    text: palette.textPrimary,
    border: "rgba(220,232,255,0.16)",
    notification: palette.accent
  }
};

export default function RootLayout() {
  return (
    <AppProviders>
      <ThemeProvider value={appNavigationTheme}>
        <StatusBar style="light" backgroundColor={palette.appBackground} />
        <Stack
          screenOptions={{
            headerShown: false,
            animation: "none",
            contentStyle: { backgroundColor: palette.appBackground }
          }}
        >
          <Stack.Screen name="index" />
          <Stack.Screen name="(auth)" />
          <Stack.Screen name="(tabs)" />
          <Stack.Screen
            name="modals/add-account"
            options={{
              presentation: "card",
              animation: "slide_from_bottom",
              contentStyle: { backgroundColor: palette.appBackground }
            }}
          />
          <Stack.Screen
            name="modals/send-money"
            options={{
              presentation: "card",
              animation: "slide_from_bottom",
              contentStyle: { backgroundColor: palette.appBackground }
            }}
          />
          <Stack.Screen
            name="modals/move-money"
            options={{
              presentation: "card",
              animation: "slide_from_bottom",
              contentStyle: { backgroundColor: palette.appBackground }
            }}
          />
          <Stack.Screen
            name="modals/add-transaction"
            options={{
              presentation: "card",
              animation: "slide_from_bottom",
              contentStyle: { backgroundColor: palette.appBackground }
            }}
          />
          <Stack.Screen
            name="modals/transaction-context"
            options={{
              presentation: "transparentModal",
              animation: "fade",
              contentStyle: { backgroundColor: "transparent" }
            }}
          />
        </Stack>
      </ThemeProvider>
    </AppProviders>
  );
}
