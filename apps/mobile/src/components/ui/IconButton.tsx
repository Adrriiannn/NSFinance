import type { ReactNode } from "react";
import { IconButton as BaseIconButton } from "./buttons/IconButton";

type IconButtonProps = {
  icon: ReactNode;
  onPress: () => void;
  disabled?: boolean;
  accessibilityLabel?: string;
};

export function IconButton({ icon, onPress, disabled = false, accessibilityLabel }: IconButtonProps) {
  return (
    <BaseIconButton
      icon={icon}
      onPress={onPress}
      disabled={disabled}
      accessibilityLabel={accessibilityLabel}
    />
  );
}
