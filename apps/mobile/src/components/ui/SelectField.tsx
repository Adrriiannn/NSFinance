import { StyleSheet, Text, View } from "react-native";
import { palette, spacing, typography } from "../../theme/tokens";
import { Chip } from "./Chip";

export type SelectOption = {
  label: string;
  value: string;
};

type SelectFieldProps = {
  label: string;
  value: string | null | undefined;
  options: SelectOption[];
  onChange: (value: string) => void;
  error?: string;
  compact?: boolean;
};

export function SelectField({
  label,
  value,
  options,
  onChange,
  error,
  compact = false
}: SelectFieldProps) {
  return (
    <View style={styles.wrapper}>
      <Text style={styles.label}>{label}</Text>
      <View style={[styles.optionWrap, compact ? styles.optionWrapCompact : null]}>
        {options.map((option) => (
          <Chip
            key={option.value}
            label={option.label}
            selected={option.value === value}
            onPress={() => onChange(option.value)}
            compact={compact}
          />
        ))}
      </View>
      {error ? <Text style={styles.error}>{error}</Text> : null}
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
  optionWrap: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  optionWrapCompact: {
    gap: 6
  },
  error: {
    color: palette.negative,
    ...typography.caption
  }
});
