import type { StyleProp, TextStyle } from "react-native";
import { formatCurrency } from "../../lib/format";
import { AppText } from "./text/AppText";

type AmountTextProps = {
  amount: number;
  currency?: string;
  style?: StyleProp<TextStyle>;
};

export function AmountText({ amount, currency = "EUR", style }: AmountTextProps) {
  const preset = amount < 0 ? "negativeMoney" : "positiveMoney";

  return (
    <AppText preset={preset} style={[{ fontVariant: ["tabular-nums"] }, style]}>
      {formatCurrency(amount, currency)}
    </AppText>
  );
}
