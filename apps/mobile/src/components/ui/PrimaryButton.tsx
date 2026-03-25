import type { ReactNode } from "react";
import type { StyleProp, TextStyle, ViewStyle } from "react-native";
import { Button } from "./buttons/Button";

type PrimaryButtonProps = {
  label: string;
  onPress: () => void;
  icon?: ReactNode;
  isLoading?: boolean;
  disabled?: boolean;
  style?: StyleProp<ViewStyle>;
  labelStyle?: StyleProp<TextStyle>;
};

export function PrimaryButton({
  label,
  onPress,
  icon,
  isLoading = false,
  disabled = false,
  style,
  labelStyle
}: PrimaryButtonProps) {
  return (
    <Button
      label={label}
      onPress={onPress}
      icon={icon}
      isLoading={isLoading}
      disabled={disabled}
      variant="primary"
      style={style}
      labelStyle={labelStyle}
    />
  );
}
