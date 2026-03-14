import { ReactNode } from "react";
import { Pressable, StyleSheet } from "react-native";
import { controls, palette } from "../../theme/tokens";

type IconButtonProps = {
  icon: ReactNode;
  onPress: () => void;
};

export function IconButton({ icon, onPress }: IconButtonProps) {
  return (
    <Pressable
      onPress={onPress}
      style={({ pressed }) => [styles.button, pressed ? styles.pressed : null]}
    >
      {icon}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    width: controls.iconButtonSize,
    height: controls.iconButtonSize,
    borderRadius: controls.compactRadius,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: controls.controlSurfaceStrong,
    alignItems: "center",
    justifyContent: "center"
  },
  pressed: {
    transform: [{ scale: controls.pressedScale }],
    opacity: 0.92
  }
});
