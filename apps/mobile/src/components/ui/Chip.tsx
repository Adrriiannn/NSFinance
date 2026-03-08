import { Pressable, StyleSheet, Text } from "react-native";
import { palette, radius, spacing, surfaces, typography } from "../../theme/tokens";

type ChipProps = {
  label: string;
  selected?: boolean;
  onPress?: () => void;
  compact?: boolean;
};

export function Chip({ label, selected = false, onPress, compact = false }: ChipProps) {
  return (
    <Pressable
      onPress={onPress}
      disabled={!onPress}
      style={({ pressed }) => [
        styles.chip,
        compact ? styles.chipCompact : null,
        selected ? styles.selected : null,
        pressed ? styles.pressed : null
      ]}
    >
      <Text style={[styles.label, compact ? styles.labelCompact : null, selected ? styles.selectedLabel : null]}>
        {label}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  chip: {
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: palette.border,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8],
    minHeight: 34,
    backgroundColor: surfaces.section,
    alignItems: "center",
    justifyContent: "center"
  },
  chipCompact: {
    minHeight: 30,
    paddingHorizontal: 10,
    paddingVertical: 6
  },
  selected: {
    borderColor: "rgba(127,174,255,0.62)",
    backgroundColor: "rgba(47,107,255,0.28)"
  },
  label: {
    color: palette.textSecondary,
    ...typography.caption
  },
  labelCompact: {
    fontSize: 11
  },
  selectedLabel: {
    color: palette.textPrimary
  },
  pressed: {
    opacity: 0.9
  }
});
