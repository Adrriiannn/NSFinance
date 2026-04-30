import { Ionicons } from "@expo/vector-icons";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { palette, radius, spacing, typography } from "../../../theme/tokens";
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
  return (
    <View style={styles.row}>
      <ActionButton
        label="Directions"
        icon="navigate-outline"
        accessibilityLabel={`Get directions to ${place.name}`}
        onPress={() => onDirections(place)}
      />
      <ActionButton
        label="Share"
        icon="share-social-outline"
        accessibilityLabel={`Share ${place.name}`}
        onPress={() => onShare(place)}
      />
    </View>
  );
}

function ActionButton({
  label,
  icon,
  accessibilityLabel,
  onPress
}: {
  label: string;
  icon: keyof typeof Ionicons.glyphMap;
  accessibilityLabel: string;
  onPress: () => void;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      style={({ pressed }) => [styles.button, pressed ? styles.pressed : null]}
      onPress={onPress}
    >
      <Ionicons name={icon} size={16} color="#FFFFFF" />
      <Text style={styles.label}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    gap: spacing[10],
    marginTop: spacing[10]
  },
  button: {
    flex: 1,
    minHeight: 46,
    borderRadius: radius.medium,
    backgroundColor: palette.success,
    alignItems: "center",
    justifyContent: "center",
    flexDirection: "row",
    gap: spacing[6]
  },
  label: {
    color: "#FFFFFF",
    ...typography.button,
    fontWeight: "700"
  },
  pressed: {
    opacity: 0.82,
    transform: [{ scale: 0.985 }]
  }
});
