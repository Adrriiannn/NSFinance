import { ReactNode } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { getFloatingFabOffset } from "../../theme/insets";
import {
  palette,
  radius,
  shadows,
  spacing,
  surfaces,
  typography
} from "../../theme/tokens";

type FloatingActionButtonProps = {
  label: string;
  icon: ReactNode;
  onPress: () => void;
  bottomOffset?: number;
};

export function FloatingActionButton({
  label,
  icon,
  onPress,
  bottomOffset = 0
}: FloatingActionButtonProps) {
  const insets = useSafeAreaInsets();
  const computedBottom = getFloatingFabOffset(insets.bottom, bottomOffset);

  return (
    <View style={[styles.wrapper, { bottom: computedBottom }]} pointerEvents="box-none">
      <Pressable
        onPress={onPress}
        style={({ pressed }) => [styles.button, pressed ? styles.pressed : null]}
      >
        {icon}
        <Text style={styles.label}>{label}</Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    position: "absolute",
    right: spacing[16]
  },
  button: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8],
    backgroundColor: surfaces.floating,
    paddingHorizontal: spacing[16],
    paddingVertical: spacing[12],
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    ...shadows.floating
  },
  label: {
    color: palette.textPrimary,
    ...typography.button,
    fontWeight: "700"
  },
  pressed: {
    transform: [{ scale: 0.97 }],
    opacity: 0.96
  }
});
