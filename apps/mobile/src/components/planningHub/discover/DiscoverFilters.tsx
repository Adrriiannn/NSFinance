import { Pressable, ScrollView, StyleSheet, Text } from "react-native";
import { palette, radius, spacing, surfaces, typography } from "../../../theme/tokens";

export type DiscoverFilterOption = {
  id: string;
  label: string;
};

type DiscoverFiltersProps = {
  options: DiscoverFilterOption[];
  selectedId: string;
  onSelect: (id: string) => void;
  emphasis?: "primary" | "secondary";
};

export function DiscoverFilters({
  options,
  selectedId,
  onSelect,
  emphasis = "primary"
}: DiscoverFiltersProps) {
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.rail}>
      {options.map((option) => {
        const selected = selectedId === option.id;
        const selectedStyles =
          emphasis === "primary"
            ? styles.primarySelected
            : styles.secondarySelected;

        return (
          <Pressable
            key={option.id}
            style={[
              styles.chip,
              emphasis === "secondary" ? styles.secondaryChip : null,
              selected ? selectedStyles : null
            ]}
            onPress={() => onSelect(option.id)}
          >
            <Text style={[styles.chipLabel, selected ? styles.selectedLabel : null]}>{option.label}</Text>
          </Pressable>
        );
      })}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  rail: {
    gap: spacing[8],
    paddingRight: spacing[16]
  },
  chip: {
    minHeight: 34,
    paddingHorizontal: spacing[12],
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    justifyContent: "center"
  },
  secondaryChip: {
    borderColor: palette.border,
    backgroundColor: surfaces.field
  },
  primarySelected: {
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(242,140,40,0.14)"
  },
  secondarySelected: {
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(242,140,40,0.12)"
  },
  chipLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "500"
  },
  selectedLabel: {
    color: palette.textPrimary
  }
});
