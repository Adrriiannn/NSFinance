import { Pressable, StyleSheet, Text, View } from "react-native";
import { palette, radius, spacing, typography } from "../../theme/tokens";

type SegmentedOption<T extends string> = {
  label: string;
  value: T;
};

type ExpenseTrackerSegmentedControlProps<T extends string> = {
  label?: string;
  value: T;
  options: SegmentedOption<T>[];
  onChange: (value: T) => void;
};

export function ExpenseTrackerSegmentedControl<T extends string>({
  label,
  value,
  options,
  onChange
}: ExpenseTrackerSegmentedControlProps<T>) {
  return (
    <View style={styles.wrapper}>
      {label ? <Text style={styles.label}>{label}</Text> : null}
      <View style={styles.segmentedRow}>
        {options.map((option) => {
          const selected = option.value === value;
          return (
            <Pressable
              key={option.value}
              onPress={() => onChange(option.value)}
              style={({ pressed }) => [
                styles.segment,
                selected ? styles.segmentSelected : null,
                pressed ? styles.segmentPressed : null
              ]}
            >
              <Text style={[styles.segmentLabel, selected ? styles.segmentLabelSelected : null]}>
                {option.label}
              </Text>
            </Pressable>
          );
        })}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    gap: spacing[8]
  },
  label: {
    color: palette.textPrimary,
    ...typography.caption
  },
  segmentedRow: {
    flexDirection: "row",
    gap: spacing[8],
    padding: 4,
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.88)"
  },
  segment: {
    flex: 1,
    minHeight: 42,
    borderRadius: radius.small,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  segmentSelected: {
    backgroundColor: "rgba(47,107,255,0.34)",
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.5)"
  },
  segmentPressed: {
    opacity: 0.92
  },
  segmentLabel: {
    color: palette.textSecondary,
    ...typography.body2,
    fontWeight: "600"
  },
  segmentLabelSelected: {
    color: palette.textPrimary
  }
});
