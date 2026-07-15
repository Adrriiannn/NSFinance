import { Ionicons } from "@expo/vector-icons";
import { useEffect, useRef } from "react";
import { Animated, Pressable, Text, View } from "react-native";
import type { StyleProp, ViewStyle } from "react-native";
import type { TransactionDto } from "../../../types/api";
import { useThemeTokens } from "../../../theme/tokens";
import {
  buildTransactionDetailDate,
  resolveTransactionDisplayLabel
} from "../../../features/transactions/activityGrouping";
import {
  resolveTransactionLeadingVisual,
  shouldRenderSemanticHelperLine
} from "../../../features/transactions/activityPresentation";
import { resolveCanonicalTransactionSemantic } from "../../../features/transactions/semanticResolver";
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
  const { surfaces } = useThemeTokens();
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

  const semantic = resolveCanonicalTransactionSemantic(transaction);
  const labelResolution = resolveTransactionDisplayLabel(transaction, semantic.subtitle);
  const metadata = metadataOverride ?? labelResolution.displayLabel;
  const timestamp = buildTransactionDetailDate(transaction);
  const shouldShowRelationshipBadge = shouldRenderSemanticHelperLine({
    metadataOverride,
    hasCanonicalLabel: labelResolution.hasCanonicalLabel,
    primaryLabel: metadata,
    semanticBadge: semantic.badgeText,
    semanticFamily: semantic.family
  });
  const relationshipBadge = shouldShowRelationshipBadge ? semantic.badgeText : null;
  const savingsStyle = semantic.styleKind === "savings_transfer";
  const leadingVisual = resolveTransactionLeadingVisual(transaction, semantic);

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
          savingsStyle
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
              backgroundColor: leadingVisual.backgroundColor
            }
          ]}
        >
          <Ionicons
            name={leadingVisual.iconName}
            size={16}
            color={leadingVisual.iconColor}
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
          <AmountText
            amount={transaction.amount}
            currency={transaction.currency}
            appearance={transaction.analyticsTreatment === "balance_only" ? "neutral" : "transaction"}
          />
          {showTimestamp ? <Text style={rowPresets.trailing}>{timestamp}</Text> : null}
        </View>
      </Pressable>
    </Animated.View>
  );
}
