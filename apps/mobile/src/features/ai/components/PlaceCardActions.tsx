import { Ionicons } from "@expo/vector-icons";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { radius, spacing, typography, useThemeTokens } from "../../../theme/tokens";
import type { CompanionPlaceCard } from "../utils/placeCardFormatting";

type PlaceCardActionsProps = {
  place: CompanionPlaceCard;
  onDirections: (place: CompanionPlaceCard) => void;
  onShare: (place: CompanionPlaceCard) => void;
};

export function PlaceCardActions({
  place,
  onDirections,
  onShare
}: PlaceCardActionsProps) {
  const tokens = useThemeTokens();
  return (
    <View style={styles.row}>
      <ActionButton
        label="Directions"
        icon="navigate-outline"
        accessibilityLabel={`Get directions to ${place.name}`}
        backgroundColor={tokens.palette.accent}
        onPress={() => onDirections(place)}
      />
      <ActionButton
        label="Share"
        icon="share-social-outline"
        accessibilityLabel={`Share ${place.name}`}
        backgroundColor={tokens.palette.accent}
        onPress={() => onShare(place)}
      />
    </View>
  );
}

function ActionButton({
  label,
  icon,
  accessibilityLabel,
  backgroundColor,
  onPress
}: {
  label: string;
  icon: keyof typeof Ionicons.glyphMap;
  accessibilityLabel: string;
  backgroundColor: string;
  onPress: () => void;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      style={({ pressed }) => [styles.button, { backgroundColor }, pressed ? styles.pressed : null]}
      onPress={onPress}
    >
      <Ionicons name={icon} size={13} color="#FFFFFF" />
      <Text style={styles.label}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    gap: spacing[6],
    marginTop: spacing[8]
  },
  button: {
    flex: 1,
    minHeight: 32,
    borderRadius: radius.medium,
    alignItems: "center",
    justifyContent: "center",
    flexDirection: "row",
    gap: spacing[6]
  },
  label: {
    color: "#FFFFFF",
    ...typography.caption,
    fontWeight: "700"
  },
  pressed: {
    opacity: 0.82,
    transform: [{ scale: 0.985 }]
  }
});
