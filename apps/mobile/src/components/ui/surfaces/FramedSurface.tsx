import { useMemo, type ReactNode } from "react";
import type { StyleProp, ViewStyle } from "react-native";
import { View, StyleSheet } from "react-native";
import { useThemeTokens } from "../../../theme/tokens";

type FramedSurfaceProps = {
  children: ReactNode;
  style?: StyleProp<ViewStyle>;
};

export function FramedSurface({ children, style }: FramedSurfaceProps) {
  const { borders, radius, spacing, surfaces } = useThemeTokens();
  const styles = useMemo(
    () =>
      StyleSheet.create({
        base: {
          borderWidth: borders.width.thin,
          borderColor: borders.color.subtle,
          borderRadius: radius.medium,
          backgroundColor: surfaces.card,
          padding: spacing[16]
        }
      }),
    [borders, radius, spacing, surfaces]
  );

  return <View style={[styles.base, style]}>{children}</View>;
}
