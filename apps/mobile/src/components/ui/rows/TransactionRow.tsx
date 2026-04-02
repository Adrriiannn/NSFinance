import { Ionicons } from "@expo/vector-icons";
import { useEffect, useRef } from "react";
import { Animated, Pressable, Text, View } from "react-native";
import type { StyleProp, ViewStyle } from "react-native";
import type { TransactionDto } from "../../../types/api";
import { useThemeTokens } from "../../../theme/tokens";
import {
  buildTransactionDetailDate,
  buildTransactionMetaLine
} from "../../../features/transactions/activityGrouping";
import { AmountText } from "../../ui/AmountText";
import { useRowPresets } from "./row.presets";

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
  const { palette, surfaces } = useThemeTokens();
  const rowPresets = useRowPresets();
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

  const metadata = metadataOverride ?? buildTransactionMetaLine(transaction);
  const timestamp = buildTransactionDetailDate(transaction);
  const isSavingsMovement =
    transaction.displaySemantic === "savings_roundup"
    || transaction.displaySemantic === "savings_manual_move"
    || transaction.relationshipType === "savings_roundup"
    || transaction.relationshipType === "savings_manual_deposit"
    || transaction.relationshipType === "savings_manual_withdrawal";
  const relationshipBadge = resolveRelationshipBadge(transaction);

  return (
    <Animated.View style={{ opacity, transform: [{ translateY }] }}>
      <Pressable
        onPress={onPress}
        onLongPress={onLongPress}
        onPressOut={onPressOut}
        delayLongPress={delayLongPress}
        disabled={!onPress && !onLongPress}
        style={({ pressed }) => [
          rowPresets.container,
          { backgroundColor: surfaces.field },
          isSavingsMovement
            ? {
                borderColor: "rgba(90, 186, 226, 0.35)"
              }
            : null,
          rowStyle,
          pressed ? { opacity: 0.93, transform: [{ scale: 0.995 }] } : null
        ]}
      >
        <View
          style={[
            rowPresets.leadingIcon,
            {
              backgroundColor:
                isSavingsMovement
                  ? "rgba(90, 186, 226, 0.18)"
                  : transaction.direction === "Expense"
                  ? "rgba(226, 90, 90, 0.26)"
                  : "rgba(29, 186, 114, 0.22)"
            }
          ]}
        >
          <Ionicons
            name={transaction.direction === "Expense" ? "arrow-down" : "arrow-up"}
            size={16}
            color={palette.textPrimary}
          />
        </View>

        <View style={{ flex: 1 }}>
          <Text numberOfLines={1} style={rowPresets.title}>
            {transaction.description}
          </Text>
          <Text numberOfLines={1} style={rowPresets.subtitle}>
            {metadata}
          </Text>
          {relationshipBadge ? (
            <Text numberOfLines={1} style={[rowPresets.trailing, { marginTop: 2 }]}>
              {relationshipBadge}
            </Text>
          ) : null}
        </View>

        <View style={{ alignItems: "flex-end", minWidth: 88 }}>
          <AmountText amount={transaction.amount} currency={transaction.currency} appearance="transaction" />
          {showTimestamp ? <Text style={rowPresets.trailing}>{timestamp}</Text> : null}
        </View>
      </Pressable>
    </Animated.View>
  );
}

function resolveRelationshipBadge(transaction: TransactionDto): string | null {
  if (
    transaction.relationshipType === "savings_manual_deposit"
    || transaction.relationshipType === "savings_manual_withdrawal"
    || transaction.transferKind === "savings_manual_deposit"
    || transaction.transferKind === "savings_manual_withdrawal"
  ) {
    return null;
  }

  if (
    transaction.relationshipType === "savings_roundup"
    || transaction.transferKind === "savings_roundup"
  ) {
    return null;
  }

  return null;
}
