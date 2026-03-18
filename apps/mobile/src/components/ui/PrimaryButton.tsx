import type { ReactNode } from "react";
import { Button } from "./buttons/Button";

type PrimaryButtonProps = {
  label: string;
  onPress: () => void;
  icon?: ReactNode;
  isLoading?: boolean;
  disabled?: boolean;
};

export function PrimaryButton({
  label,
  onPress,
  icon,
  isLoading = false,
  disabled = false
}: PrimaryButtonProps) {
  return (
    <Button
      label={label}
      onPress={onPress}
      icon={icon}
      isLoading={isLoading}
      disabled={disabled}
      variant="primary"
    />
  );
}
