import type { ReactNode } from "react";
import type { StyleProp, ViewStyle } from "react-native";
import { View, StyleSheet } from "react-native";
import { borders, radius, spacing, surfaces } from "../../../theme/tokens";

type FramedSurfaceProps = {
  children: ReactNode;
  style?: StyleProp<ViewStyle>;
};

export function FramedSurface({ children, style }: FramedSurfaceProps) {
  return <View style={[styles.base, style]}>{children}</View>;
}

const styles = StyleSheet.create({
  base: {
    borderWidth: borders.width.thin,
    borderColor: borders.color.subtle,
    borderRadius: radius.medium,
    backgroundColor: surfaces.card,
    padding: spacing[16]
  }
});

