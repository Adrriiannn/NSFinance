import type { StyleProp, TextStyle } from "react-native";
import { AppText } from "../text/AppText";

type FieldErrorProps = {
  children: string;
  style?: StyleProp<TextStyle>;
};

export function FieldError({ children, style }: FieldErrorProps) {
  return (
    <AppText preset="helper" tone="negative" style={style}>
      {children}
    </AppText>
  );
}
