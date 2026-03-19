import { Pressable, StyleSheet } from "react-native";
import { AppText } from "../../components/ui/text/AppText";
import { HEADER_CONSTANTS, HEADER_SURFACES, HEADER_TYPOGRAPHY } from "./header.constants";
import type { HeaderActionButtonProps } from "./header.types";

export function HeaderActionButton({
  icon,
  label,
  onPress,
  accessibilityLabel,
  variant = "icon",
  style
}: HeaderActionButtonProps) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? label}
      onPress={onPress}
      style={({ pressed }) => [
        variant === "compact" ? HEADER_SURFACES.compactButton : HEADER_SURFACES.iconButton,
        styles.base,
        variant === "compact" ? styles.compact : styles.icon,
        pressed ? styles.pressed : null,
        style
      ]}
    >
      {icon}
      {label ? (
        <AppText
          preset="buttonLabel"
          style={HEADER_TYPOGRAPHY.headerButtonText}
          numberOfLines={1}
        >
          {label}
        </AppText>
      ) : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    overflow: "hidden"
  },
  icon: {
    width: HEADER_CONSTANTS.touchTarget,
    minWidth: HEADER_CONSTANTS.touchTarget
  },
  compact: {
    minWidth: HEADER_CONSTANTS.touchTarget
  },
  pressed: {
    opacity: 0.88
  }
});

