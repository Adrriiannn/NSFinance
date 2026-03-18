import { Button } from "./buttons/Button";

type TertiaryButtonProps = {
  label: string;
  onPress: () => void;
  disabled?: boolean;
};

export function TertiaryButton({ label, onPress, disabled = false }: TertiaryButtonProps) {
  return <Button label={label} onPress={onPress} disabled={disabled} variant="ghost" />;
}
