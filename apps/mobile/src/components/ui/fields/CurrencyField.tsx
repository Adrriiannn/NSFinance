import type { TextInputProps } from "react-native";
import { AppText } from "../text/AppText";
import { TextField } from "./TextField";

type CurrencyFieldProps = TextInputProps & {
  label?: string;
  currencySymbol?: string;
  helper?: string;
  error?: string;
};

export function CurrencyField({
  currencySymbol = "EUR",
  ...props
}: CurrencyFieldProps) {
  return (
    <TextField
      {...props}
      leading={<AppText preset="secondary">{currencySymbol}</AppText>}
      keyboardType={props.keyboardType ?? "decimal-pad"}
    />
  );
}
