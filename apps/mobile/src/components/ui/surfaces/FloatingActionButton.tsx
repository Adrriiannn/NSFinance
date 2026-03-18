import type { ReactNode } from "react";
import { Pressable, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { getFloatingFabOffset } from "../../../theme/insets";
import { palette, spacing } from "../../../theme/tokens";
import { AppText } from "../text/AppText";
import { surfacePresets } from "./surface.presets";

type FloatingActionButtonProps = {
  icon: ReactNode;
  label?: string;
  onPress: () => void;
  bottomOffset?: number;
};

export function FloatingActionButton({
  icon,
  label,
  onPress,
  bottomOffset = 0
}: FloatingActionButtonProps) {
  const insets = useSafeAreaInsets();
  const computedBottom = getFloatingFabOffset(insets.bottom, bottomOffset);

  return (
    <View style={{ position: "absolute", right: spacing[16], bottom: computedBottom }} pointerEvents="box-none">
      <Pressable
        onPress={onPress}
        style={({ pressed }) => [
          surfacePresets.fab,
          label
            ? { flexDirection: "row", alignItems: "center", gap: spacing[8], paddingHorizontal: spacing[16] }
            : surfacePresets.fabCompact,
          pressed ? { opacity: 0.96, transform: [{ scale: 0.97 }] } : null
        ]}
      >
        {icon}
        {label ? (
          <AppText preset="buttonLabel" style={{ color: palette.textPrimary }}>
            {label}
          </AppText>
        ) : null}
      </Pressable>
    </View>
  );
}
