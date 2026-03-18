import { MaterialCommunityIcons } from "@expo/vector-icons";
import { Pressable, StyleSheet, View } from "react-native";
import { palette } from "../../theme/tokens";
import { surfacePresets } from "../../components/ui/surfaces/surface.presets";
import { useAdaptiveShell } from "./adaptive.hooks";
import type { FloatingAssistantDockProps } from "./adaptive.types";

export function FloatingAssistantDock({
  onPress,
  accessibilityLabel = "Open NS Companion"
}: FloatingAssistantDockProps) {
  const { metrics, shellFrame } = useAdaptiveShell();
  const rightOffset = shellFrame?.x ?? metrics.floatingAssistantRightMargin;

  return (
    <View
      pointerEvents="box-none"
      style={[
        styles.wrapper,
        {
          right: rightOffset,
          bottom: metrics.floatingAssistantBottomOffset
        }
      ]}
    >
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={accessibilityLabel}
        onPress={onPress}
        style={({ pressed }) => [
          surfacePresets.fab,
          styles.button,
          {
            width: metrics.floatingAssistantSize,
            height: metrics.floatingAssistantSize,
            borderRadius: metrics.floatingAssistantSize / 2
          },
          pressed ? styles.buttonPressed : null
        ]}
      >
        <MaterialCommunityIcons
          name="robot-happy-outline"
          size={22}
          color={palette.accent}
        />
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    position: "absolute"
  },
  button: {
    paddingHorizontal: 0,
    alignItems: "center",
    justifyContent: "center"
  },
  buttonPressed: {
    opacity: 0.94,
    transform: [{ scale: 0.97 }]
  }
});
