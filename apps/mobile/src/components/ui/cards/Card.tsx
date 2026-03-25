import type { ReactNode } from "react";
import type { StyleProp, ViewStyle } from "react-native";
import { Pressable } from "react-native";
import { useCardPresetStyles, type CardVariant } from "./card.presets";

type CardProps = {
  children: ReactNode;
  variant?: CardVariant;
  onPress?: () => void;
  style?: StyleProp<ViewStyle>;
};

export function Card({
  children,
  variant = "default",
  onPress,
  style
}: CardProps) {
  const { cardPresets, cardStateStyles } = useCardPresetStyles();

  return (
    <Pressable
      onPress={onPress}
      disabled={!onPress}
      style={({ pressed }) => [
        cardPresets[variant],
        style,
        onPress && pressed ? cardStateStyles.pressed : null
      ]}
    >
      {children}
    </Pressable>
  );
}
