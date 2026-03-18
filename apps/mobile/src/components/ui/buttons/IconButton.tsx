import type { ReactNode } from "react";
import type { StyleProp, ViewStyle } from "react-native";
import { Button } from "./Button";

type IconButtonProps = {
  icon: ReactNode;
  onPress?: () => void;
  disabled?: boolean;
  accessibilityLabel?: string;
  style?: StyleProp<ViewStyle>;
};

export function IconButton({
  icon,
  onPress,
  disabled = false,
  accessibilityLabel,
  style
}: IconButtonProps) {
  return (
    <Button
      icon={icon}
      variant="icon"
      onPress={onPress}
      disabled={disabled}
      accessibilityLabel={accessibilityLabel}
      style={style}
    />
  );
}
