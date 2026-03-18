import type { ReactNode } from "react";
import type { StyleProp, ViewStyle } from "react-native";
import { Card } from "./cards/Card";

type GlassCardProps = {
  children: ReactNode;
  style?: StyleProp<ViewStyle>;
  onPress?: () => void;
};

export function GlassCard({ children, style, onPress }: GlassCardProps) {
  return (
    <Card onPress={onPress} style={style} variant="default">
      {children}
    </Card>
  );
}
