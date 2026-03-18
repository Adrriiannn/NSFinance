import { Button } from "./buttons/Button";

type SecondaryButtonProps = {
  label: string;
  onPress: () => void;
  disabled?: boolean;
};

export function SecondaryButton({
  label,
  onPress,
  disabled = false
}: SecondaryButtonProps) {
  return <Button label={label} onPress={onPress} disabled={disabled} variant="secondary" />;
}
