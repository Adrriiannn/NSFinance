import { Pressable, ScrollView, StyleSheet, Text } from "react-native";
import { palette, radius, spacing, typography } from "../../../theme/tokens";

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
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: "rgba(208,225,255,0.16)",
    backgroundColor: "rgba(14,28,46,0.76)",
    justifyContent: "center"
  },
  secondaryChip: {
    borderColor: "rgba(208,225,255,0.12)",
    backgroundColor: "rgba(14,28,46,0.6)"
  },
  primarySelected: {
    borderColor: "rgba(94,161,255,0.8)",
    backgroundColor: "rgba(58,114,196,0.32)"
  },
  secondarySelected: {
    borderColor: "rgba(142,195,255,0.54)",
    backgroundColor: "rgba(43,87,150,0.24)"
  },
  chipLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "700"
  },
  selectedLabel: {
    color: palette.textPrimary
  }
});

