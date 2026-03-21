import type { ReactNode } from "react";
import type { StyleProp, ViewStyle } from "react-native";
import { Button } from "./buttons/Button";

type PrimaryButtonProps = {
  label: string;
  onPress: () => void;
  icon?: ReactNode;
  isLoading?: boolean;
  disabled?: boolean;
  style?: StyleProp<ViewStyle>;
};

export function PrimaryButton({
  label,
  onPress,
  icon,
  isLoading = false,
  disabled = false,
  style
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
    />
  );
}
