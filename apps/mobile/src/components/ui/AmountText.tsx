import { StyleProp, StyleSheet, Text, TextStyle } from "react-native";
import { formatCurrency } from "../../lib/format";
import { palette, typography } from "../../theme/tokens";

type AmountTextProps = {
  amount: number;
  currency?: string;
  style?: StyleProp<TextStyle>;
};

export function AmountText({ amount, currency = "EUR", style }: AmountTextProps) {
  const color = amount < 0 ? palette.negative : palette.success;

  return (
    <Text style={[styles.text, { color }, style]}>
      {formatCurrency(amount, currency)}
    </Text>
  );
}

const styles = StyleSheet.create({
  text: {
    ...typography.body1,
    fontWeight: "700",
    fontVariant: ["tabular-nums"]
  }
});
