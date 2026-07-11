import { DarkTheme, DefaultTheme, ThemeProvider } from "@react-navigation/native";
import { useFonts } from "expo-font";
import { Stack } from "expo-router";
import { StatusBar } from "expo-status-bar";
import { type ComponentProps, useEffect, useMemo } from "react";
import { Text, TextInput } from "react-native";
import { GlobalFlashToast } from "../src/components/feedback/GlobalFlashToast";
import { GlobalEnrichmentProgressDial } from "../src/components/feedback/GlobalEnrichmentProgressDial";
import { AppProviders } from "../src/providers/AppProviders";
import { ThemeRuntimeProvider, useThemeRuntime } from "../src/theme/runtime/ThemeRuntimeProvider";

type TextInputWithDefaults = typeof TextInput & {
  defaultProps?: ComponentProps<typeof TextInput>;
};

type TextWithDefaults = typeof Text & {
  defaultProps?: ComponentProps<typeof Text>;
};

const textInputWithDefaults = TextInput as TextInputWithDefaults;
const textWithDefaults = Text as TextWithDefaults;

export default function RootLayout() {
  const [fontsLoaded] = useFonts({
    "Inter-Regular": require("./assets/fonts/Inter-Regular.ttf"),
    "Inter-Medium": require("./assets/fonts/Inter-Medium.ttf"),
    "Inter-SemiBold": require("./assets/fonts/Inter-SemiBold.ttf")
  });

  if (!fontsLoaded) {
    return null;
  }

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
    textWithDefaults.defaultProps = {
      ...(textWithDefaults.defaultProps ?? {}),
      allowFontScaling: false,
      maxFontSizeMultiplier: 1
    };

    textInputWithDefaults.defaultProps = {
      ...(textInputWithDefaults.defaultProps ?? {}),
      allowFontScaling: false,
      maxFontSizeMultiplier: 1,
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
      <GlobalEnrichmentProgressDial />
      <Stack
        screenOptions={{
          headerShown: false,
          animation: "none",
          contentStyle: { backgroundColor: theme.colors.canvas }
        }}
      >
        <Stack.Screen name="index" />
        <Stack.Screen name="(auth)" />
        <Stack.Screen name="(tabs)" />
      </Stack>
    </ThemeProvider>
  );
}


