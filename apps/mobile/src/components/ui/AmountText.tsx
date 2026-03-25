import type { StyleProp, TextStyle } from "react-native";
import { formatCurrency } from "../../lib/format";
import { useThemeTokens } from "../../theme/tokens";
import { AppText } from "./text/AppText";

type AmountTextProps = {
  amount: number;
  currency?: string;
  style?: StyleProp<TextStyle>;
  appearance?: "default" | "transaction";
};

export function AmountText({
  amount,
  currency = "EUR",
  style,
  appearance = "default"
}: AmountTextProps) {
  const { palette } = useThemeTokens();
  const preset = amount < 0 ? "negativeMoney" : amount > 0 ? "positiveMoney" : "moneyValue";
  const transactionToneStyle =
    appearance === "transaction"
      ? amount < 0
        ? { color: palette.moneyNegative }
        : amount > 0
          ? { color: palette.moneyPositive }
          : null
      : null;

  return (
    <AppText preset={preset} style={[{ fontVariant: ["tabular-nums"] }, transactionToneStyle, style]}>
      {formatCurrency(amount, currency)}
    </AppText>
  );
}
