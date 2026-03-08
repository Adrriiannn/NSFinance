import { ReactNode } from "react";
import { Pressable, StyleSheet } from "react-native";
import { palette, radius, surfaces } from "../../theme/tokens";

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
    width: 42,
    height: 42,
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.section,
    alignItems: "center",
    justifyContent: "center"
  },
  pressed: {
    transform: [{ scale: 0.95 }],
    opacity: 0.9
  }
});
