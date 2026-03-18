import type { StyleProp, TextStyle } from "react-native";
import { AppText } from "../text/AppText";

type FieldHintProps = {
  children: string;
  style?: StyleProp<TextStyle>;
};

export function FieldHint({ children, style }: FieldHintProps) {
  return (
    <AppText preset="helper" tone="secondary" style={style}>
      {children}
    </AppText>
  );
}
