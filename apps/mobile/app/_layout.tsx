import { DarkTheme, DefaultTheme, ThemeProvider } from "@react-navigation/native";
import { Stack } from "expo-router";
import { StatusBar } from "expo-status-bar";
import { type ComponentProps, useEffect, useMemo } from "react";
import { TextInput } from "react-native";
import { GlobalFlashToast } from "../src/components/feedback/GlobalFlashToast";
import { AppProviders } from "../src/providers/AppProviders";
import { ThemeRuntimeProvider, useThemeRuntime } from "../src/theme/runtime/ThemeRuntimeProvider";

type TextInputWithDefaults = typeof TextInput & {
  defaultProps?: ComponentProps<typeof TextInput>;
};

const textInputWithDefaults = TextInput as TextInputWithDefaults;

export default function RootLayout() {
  return (
    <AppProviders>
      <ThemeRuntimeProvider>
        <RootNavigator />
      </ThemeRuntimeProvider>
    </AppProviders>
  );
}

function RootNavigator() {
  const { theme, resolvedThemeName } = useThemeRuntime();
  const caretColor = theme.colors.accent.primary;

  useEffect(() => {
    textInputWithDefaults.defaultProps = {
      ...(textInputWithDefaults.defaultProps ?? {}),
      selectionColor: caretColor,
      cursorColor: caretColor
    };
  }, [caretColor]);

  const appNavigationTheme = useMemo(
    () => ({
      ...(resolvedThemeName === "dark" ? DarkTheme : DefaultTheme),
      colors: {
        ...(resolvedThemeName === "dark" ? DarkTheme.colors : DefaultTheme.colors),
        primary: theme.colors.action.primary,
        background: theme.colors.canvas,
        card: theme.colors.canvas,
        text: theme.colors.text.primary,
        border: theme.colors.border.divider,
        notification: theme.colors.accent.primary
      }
    }),
    [resolvedThemeName, theme]
  );

  return (
    <ThemeProvider value={appNavigationTheme}>
      <StatusBar
        style={resolvedThemeName === "dark" ? "light" : "dark"}
        backgroundColor={theme.colors.canvas}
      />
      <GlobalFlashToast />
      <Stack
        screenOptions={{
          headerShown: false,
          animation: "none",
          contentStyle: { backgroundColor: theme.colors.canvas }
        }}
      >
        <Stack.Screen name="index" />
        <Stack.Screen name="oauthredirect" />
        <Stack.Screen name="(auth)" />
        <Stack.Screen name="(tabs)" />
      </Stack>
    </ThemeProvider>
  );
}
