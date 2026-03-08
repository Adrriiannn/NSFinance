import { useState } from "react";
import { StyleSheet, Text, TextInput, TextInputProps, View } from "react-native";
import { palette, radius, spacing, surfaces, typography } from "../../theme/tokens";

type TextFieldProps = TextInputProps & {
  label: string;
  error?: string;
};

export function TextField({ label, error, style, ...props }: TextFieldProps) {
  const [isFocused, setIsFocused] = useState(false);

  return (
    <View style={styles.wrapper}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        {...props}
        onFocus={(event) => {
          setIsFocused(true);
          props.onFocus?.(event);
        }}
        onBlur={(event) => {
          setIsFocused(false);
          props.onBlur?.(event);
        }}
        placeholderTextColor={palette.textSecondary}
        style={[
          styles.input,
          isFocused ? styles.inputFocused : null,
          error ? styles.inputError : null,
          style
        ]}
      />
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
  input: {
    minHeight: 50,
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.section,
    paddingHorizontal: spacing[12],
    color: palette.textPrimary,
    ...typography.body1
  },
  inputFocused: {
    borderColor: palette.primaryGlow,
    shadowColor: palette.primaryGlow,
    shadowOpacity: 0.18,
    shadowRadius: 8,
    shadowOffset: { width: 0, height: 2 },
    elevation: 2
  },
  inputError: {
    borderColor: palette.negative
  },
  error: {
    color: palette.negative,
    ...typography.caption
  }
});
