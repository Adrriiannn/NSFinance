import { View } from "react-native";
import { FieldError } from "./forms/FieldError";
import { AppText } from "./text/AppText";
import { Chip } from "./chips/Chip";
import { useThemeTokens } from "../../theme/tokens";

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
  const { spacing } = useThemeTokens();

  return (
    <View style={{ gap: spacing[8] }}>
      <AppText preset="fieldLabel">{label}</AppText>
      <View style={{ flexDirection: "row", flexWrap: "wrap", gap: compact ? 6 : spacing[8] }}>
        {options.map((option) => (
          <Chip
            key={option.value}
            label={option.label}
            selected={option.value === value}
            onPress={() => onChange(option.value)}
            variant={compact ? "compact" : "filter"}
            tone={option.value === value ? "info" : "default"}
          />
        ))}
      </View>
      {error ? <FieldError>{error}</FieldError> : null}
    </View>
  );
}
