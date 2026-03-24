import { DarkTheme, ThemeProvider } from "@react-navigation/native";
import { Stack } from "expo-router";
import { StatusBar } from "expo-status-bar";
import { GlobalFlashToast } from "../src/components/feedback/GlobalFlashToast";
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
        <GlobalFlashToast />
        <Stack
          screenOptions={{
            headerShown: false,
            animation: "none",
            contentStyle: { backgroundColor: palette.appBackground }
          }}
        >
          <Stack.Screen name="index" />
          <Stack.Screen name="oauthredirect" />
          <Stack.Screen name="(auth)" />
          <Stack.Screen name="(tabs)" />
        </Stack>
      </ThemeProvider>
    </AppProviders>
  );
}
