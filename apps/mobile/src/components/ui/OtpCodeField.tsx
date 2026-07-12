import { forwardRef, useImperativeHandle, useRef } from "react";
import { Platform, StyleSheet, Text, TextInput, View } from "react-native";
import { controls, palette, spacing, surfaces, typography } from "../../theme/tokens";

type OtpCodeFieldProps = {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  error?: string | null;
  accessibilityLabel?: string;
  autoFocus?: boolean;
};

export type OtpCodeFieldHandle = {
  focus: () => void;
};

export const OtpCodeField = forwardRef<OtpCodeFieldHandle, OtpCodeFieldProps>(function OtpCodeField(
  {
    value,
    onChange,
    disabled = false,
    error,
    accessibilityLabel = "Six-digit verification code",
    autoFocus = false
  },
  forwardedRef
) {
  const inputRef = useRef<TextInput | null>(null);
  const normalizedValue = value.replace(/\D/g, "").slice(0, 6);

  useImperativeHandle(forwardedRef, () => ({
    focus: () => inputRef.current?.focus()
  }));

  return (
    <View style={styles.wrap}>
      <View style={styles.codeRow}>
        <View
          pointerEvents="none"
          importantForAccessibility="no-hide-descendants"
          style={styles.visualRow}
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
        </View>
        <TextInput
          ref={inputRef}
          value={normalizedValue}
          editable={!disabled}
          onChangeText={(nextValue) => onChange(nextValue.replace(/\D/g, "").slice(0, 6))}
          accessibilityLabel={accessibilityLabel}
          accessibilityHint="Enter the six-digit code"
          autoFocus={autoFocus}
          autoCorrect={false}
          spellCheck={false}
          autoComplete={Platform.OS === "android" ? "sms-otp" : "one-time-code"}
          textContentType="oneTimeCode"
          importantForAutofill={Platform.OS === "android" ? "yes" : undefined}
          inputMode="numeric"
          keyboardType="number-pad"
          maxLength={12}
          caretHidden
          selectionColor="transparent"
          underlineColorAndroid="transparent"
          style={styles.hiddenInput}
        />
      </View>
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
});

const styles = StyleSheet.create({
  wrap: {
    gap: spacing[8]
  },
  codeRow: {
    width: "100%",
    maxWidth: 360,
    alignSelf: "center",
    position: "relative"
  },
  visualRow: {
    width: "100%",
    flexDirection: "row",
    gap: spacing[8]
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
    top: 0,
    right: 0,
    bottom: 0,
    left: 0,
    color: "transparent",
    backgroundColor: "transparent",
    fontSize: 1,
    opacity: 0.02
  },
  error: {
    color: palette.negative,
    fontSize: typography.helper.fontSize,
    fontFamily: typography.helper.fontFamily,
    textAlign: "center"
  }
});
