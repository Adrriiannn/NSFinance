import { MaterialCommunityIcons } from "@expo/vector-icons";
import { Pressable, StyleSheet } from "react-native";
import { palette, shadows } from "../../theme/tokens";
import type { PlanningHubDockButtonProps } from "./adaptive.types";

export function PlanningHubDockButton({
  size,
  onPress,
  accessibilityLabel = "Open Planning Hub"
}: PlanningHubDockButtonProps) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      onPress={onPress}
      style={({ pressed }) => [
        styles.button,
        {
          width: size,
          height: size,
          borderRadius: size / 2
        },
        pressed ? styles.buttonPressed : null
      ]}
    >
      <MaterialCommunityIcons
        name="notebook-edit-outline"
        size={Math.round(size * 0.38)}
        color={palette.textPrimary}
      />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(47,107,255,0.98)",
    borderWidth: 1,
    borderColor: "rgba(173,204,255,0.72)",
    ...shadows.glow
  },
  buttonPressed: {
    opacity: 0.92,
    transform: [{ scale: 0.97 }]
  }
});
