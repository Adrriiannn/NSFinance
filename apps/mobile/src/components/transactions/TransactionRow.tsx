import { Ionicons } from "@expo/vector-icons";
import { useEffect, useRef } from "react";
import { Animated, Pressable, StyleProp, StyleSheet, Text, View, ViewStyle } from "react-native";
import type { TransactionDto } from "../../types/api";
import { palette, radius, spacing, surfaces, typography } from "../../theme/tokens";
import { AmountText } from "../ui/AmountText";
import {
  buildTransactionDetailDate,
  buildTransactionMetaLine
} from "../../features/transactions/activityGrouping";
import { usePlannerStore } from "../../providers/PlannerProvider";

type TransactionRowProps = {
  transaction: TransactionDto;
  index?: number;
  onPress?: () => void;
  onLongPress?: () => void;
  onPressOut?: () => void;
  delayLongPress?: number;
  metadataOverride?: string;
  showTimestamp?: boolean;
  rowStyle?: StyleProp<ViewStyle>;
};

export function TransactionRow({
  transaction,
  index = 0,
  onPress,
  onLongPress,
  onPressOut,
  delayLongPress,
  metadataOverride,
  showTimestamp = false,
  rowStyle
}: TransactionRowProps) {
  const plannerStore = usePlannerStore();
  const opacity = useRef(new Animated.Value(0)).current;
  const translateY = useRef(new Animated.Value(10)).current;

  useEffect(() => {
    Animated.parallel([
      Animated.timing(opacity, {
        toValue: 1,
        duration: 240,
        delay: Math.min(index * 40, 200),
        useNativeDriver: true
      }),
      Animated.timing(translateY, {
        toValue: 0,
        duration: 240,
        delay: Math.min(index * 40, 200),
        useNativeDriver: true
      })
    ]).start();
  }, [index, opacity, translateY]);

  const plannerCategory = plannerStore.annotations[transaction.id]?.category;
  const metadata =
    metadataOverride ?? buildTransactionMetaLine(transaction, plannerCategory);
  const timestamp = buildTransactionDetailDate(transaction);

  return (
    <Animated.View style={{ opacity, transform: [{ translateY }] }}>
      <Pressable
        onPress={onPress}
        onLongPress={onLongPress}
        onPressOut={onPressOut}
        delayLongPress={delayLongPress}
        disabled={!onPress && !onLongPress}
        style={({ pressed }) => [styles.row, rowStyle, pressed ? styles.pressed : null]}
      >
        <View
          style={[
            styles.iconCircle,
            transaction.direction === "Expense" ? styles.iconExpense : styles.iconIncome
          ]}
        >
          <Ionicons
            name={transaction.direction === "Expense" ? "arrow-down" : "arrow-up"}
            size={16}
            color={palette.textPrimary}
          />
        </View>

        <View style={styles.content}>
          <Text numberOfLines={1} style={styles.description}>
            {transaction.description}
          </Text>
          <Text numberOfLines={1} style={styles.meta}>
            {metadata}
          </Text>
        </View>

        <View style={styles.rightColumn}>
          <AmountText amount={transaction.amount} currency={transaction.currency} />
          {showTimestamp ? (
            <Text style={styles.timestamp}>{timestamp}</Text>
          ) : null}
        </View>
      </Pressable>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  row: {
    borderRadius: radius.medium,
    borderColor: palette.border,
    borderWidth: 1,
    backgroundColor: surfaces.section,
    padding: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12]
  },
  pressed: {
    opacity: 0.93,
    transform: [{ scale: 0.995 }]
  },
  iconCircle: {
    width: 34,
    height: 34,
    borderRadius: 17,
    alignItems: "center",
    justifyContent: "center"
  },
  iconExpense: {
    backgroundColor: "rgba(244,91,105,0.32)"
  },
  iconIncome: {
    backgroundColor: "rgba(24,195,126,0.32)"
  },
  content: {
    flex: 1
  },
  rightColumn: {
    alignItems: "flex-end",
    justifyContent: "flex-start",
    minWidth: 88
  },
  description: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  meta: {
    marginTop: spacing[4],
    color: palette.textSecondary,
    ...typography.body2
  },
  timestamp: {
    marginTop: spacing[4],
    color: palette.textSecondary,
    ...typography.caption
  }
});
