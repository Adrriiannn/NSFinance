import { useState } from "react";
import { StyleSheet, Text, TextInput, TextInputProps, View } from "react-native";
import { controls, palette, spacing, typography } from "../../theme/tokens";

type TextFieldProps = TextInputProps & {
  label: string;
  error?: string;
  showLabel?: boolean;
  forceFocused?: boolean;
  surfaceMode?: "normal" | "solid";
  leadingText?: string;
};

export function TextField({
  label,
  error,
  style,
  showLabel = true,
  forceFocused = false,
  surfaceMode = "normal",
  leadingText,
  ...props
}: TextFieldProps) {
  const [isFocused, setIsFocused] = useState(false);

  return (
    <View style={[styles.wrapper, !showLabel ? styles.wrapperCompact : null]}>
      {showLabel ? <Text style={styles.label}>{label}</Text> : null}
      <View style={styles.inputWrap}>
        {leadingText ? <Text style={styles.leadingText}>{leadingText}</Text> : null}
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
            surfaceMode === "solid" ? styles.inputSolid : styles.inputNormal,
            isFocused || forceFocused ? styles.inputFocused : null,
            error ? styles.inputError : null,
            leadingText ? styles.inputWithLeadingText : null,
            style
          ]}
        />
      </View>
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    gap: spacing[8]
  },
  wrapperCompact: {
    gap: 0
  },
  label: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  inputWrap: {
    position: "relative",
    justifyContent: "center"
  },
  input: {
    minHeight: controls.fieldHeight,
    borderRadius: controls.fieldRadius,
    borderWidth: 1,
    borderColor: palette.border,
    paddingHorizontal: spacing[16],
    backgroundColor: controls.controlSurfaceMuted,
    color: palette.textPrimary,
    ...typography.body1
  },
  inputWithLeadingText: {
    paddingLeft: spacing[40]
  },
  leadingText: {
    position: "absolute",
    left: spacing[16],
    color: palette.textSecondary,
    ...typography.body1,
    zIndex: 1
  },
  inputNormal: {
    backgroundColor: controls.controlSurfaceMuted
  },
  inputSolid: {
    backgroundColor: controls.controlSurfaceStrong
  },
  inputFocused: {
    borderColor: palette.primaryGlow,
    backgroundColor: controls.controlSurfaceStrong,
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
