import type { StyleProp, ViewStyle } from "react-native";
import { Button } from "./buttons/Button";

type SecondaryButtonProps = {
  label: string;
  onPress: () => void;
  disabled?: boolean;
  style?: StyleProp<ViewStyle>;
};

export function SecondaryButton({
  label,
  onPress,
  disabled = false,
  style
}: SecondaryButtonProps) {
  return <Button label={label} onPress={onPress} disabled={disabled} variant="secondary" style={style} />;
}
