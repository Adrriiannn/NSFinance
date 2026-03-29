import { useMemo } from "react";
import { StyleSheet, View } from "react-native";
import { useThemeRuntime } from "../../../theme/runtime/ThemeRuntimeProvider";

export function AppBackgroundLayer() {
  const { theme } = useThemeRuntime();
  const isDark = theme.isDark;
  const backgroundStyles = useMemo(
    () => ({
      neutralHaze: {
        backgroundColor: isDark ? "transparent" : "rgba(255,255,255,0.2)"
      },
      vignette: {
        borderColor: isDark ? "rgba(0,0,0,0.2)" : "rgba(17,17,17,0.06)"
      }
    }),
    [isDark]
  );

  return (
    <View pointerEvents="none" style={StyleSheet.absoluteFill}>
      <View style={[styles.neutralHaze, backgroundStyles.neutralHaze]} />
      <View style={[styles.vignette, backgroundStyles.vignette]} />
    </View>
  );
}

const styles = StyleSheet.create({
  neutralHaze: {
    position: "absolute",
    top: 90,
    right: -80,
    width: 260,
    height: 260,
    borderRadius: 130,
    backgroundColor: "rgba(255,255,255,0.015)"
  },
  vignette: {
    ...StyleSheet.absoluteFillObject,
    borderColor: "rgba(0,0,0,0.2)",
    borderWidth: 1
  }
});
