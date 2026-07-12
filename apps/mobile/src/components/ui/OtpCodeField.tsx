import { useRef } from "react";
import { Pressable, StyleSheet, Text, TextInput, View } from "react-native";
import { controls, palette, spacing, surfaces, typography } from "../../theme/tokens";

type OtpCodeFieldProps = {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  error?: string | null;
  accessibilityLabel?: string;
};

export function OtpCodeField({
  value,
  onChange,
  disabled = false,
  error,
  accessibilityLabel = "Six-digit verification code"
}: OtpCodeFieldProps) {
  const inputRef = useRef<TextInput | null>(null);
  const normalizedValue = value.replace(/\D/g, "").slice(0, 6);

  return (
    <View style={styles.wrap}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={accessibilityLabel}
        accessibilityHint="Opens the numeric keyboard"
        disabled={disabled}
        onPress={() => inputRef.current?.focus()}
        style={styles.codeRow}
      >
        {Array.from({ length: 6 }, (_, index) => {
          const character = normalizedValue[index] ?? "";
          const isActive = index === normalizedValue.length && normalizedValue.length < 6;
          return (
            <View
              key={index}
              style={[
                styles.cell,
                isActive ? styles.cellActive : null,
                error ? styles.cellError : null,
                disabled ? styles.cellDisabled : null
              ]}
            >
              <Text style={styles.character}>{character}</Text>
            </View>
          );
        })}
        <TextInput
          ref={inputRef}
          value={normalizedValue}
          editable={!disabled}
          onChangeText={(nextValue) => onChange(nextValue.replace(/\D/g, "").slice(0, 6))}
          keyboardType="number-pad"
          textContentType="oneTimeCode"
          autoComplete="sms-otp"
          maxLength={6}
          caretHidden
          style={styles.hiddenInput}
        />
      </Pressable>
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    gap: spacing[8]
  },
  codeRow: {
    width: "100%",
    maxWidth: 360,
    alignSelf: "center",
    flexDirection: "row",
    gap: spacing[8],
    position: "relative"
  },
  cell: {
    flex: 1,
    minWidth: 0,
    maxWidth: 52,
    aspectRatio: 0.86,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    borderRadius: controls.fieldRadius,
    backgroundColor: surfaces.fieldStrong,
    alignItems: "center",
    justifyContent: "center"
  },
  cellActive: {
    borderColor: palette.primary,
    borderWidth: 2
  },
  cellError: {
    borderColor: palette.negative
  },
  cellDisabled: {
    opacity: 0.55
  },
  character: {
    color: palette.textPrimary,
    fontSize: typography.title.fontSize,
    fontFamily: typography.title.fontFamily
  },
  hiddenInput: {
    position: "absolute",
    width: 1,
    height: 1,
    opacity: 0
  },
  error: {
    color: palette.negative,
    fontSize: typography.helper.fontSize,
    fontFamily: typography.helper.fontFamily,
    textAlign: "center"
  }
});
