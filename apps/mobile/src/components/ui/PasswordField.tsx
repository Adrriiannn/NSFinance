import { Ionicons } from "@expo/vector-icons";
import { useRef, useState } from "react";
import { Pressable, StyleSheet, Text, TextInput, TextInputProps, View } from "react-native";
import { controls, palette, spacing, typography } from "../../theme/tokens";

type PasswordFieldProps = Omit<TextInputProps, "secureTextEntry"> & {
  label: string;
  error?: string;
  showLabel?: boolean;
  forceFocused?: boolean;
  surfaceMode?: "normal" | "solid";
  isPasswordVisible?: boolean;
  onPasswordVisibilityChange?: (isVisible: boolean) => void;
  autoHideOnBlur?: boolean;
};

export function PasswordField({
  label,
  error,
  style,
  showLabel = true,
  forceFocused = false,
  surfaceMode = "normal",
  isPasswordVisible,
  onPasswordVisibilityChange,
  autoHideOnBlur = true,
  ...props
}: PasswordFieldProps) {
  const [internalVisible, setInternalVisible] = useState(false);
  const inputRef = useRef<TextInput>(null);
  const visible = typeof isPasswordVisible === "boolean" ? isPasswordVisible : internalVisible;

  const setVisible = (nextValue: boolean) => {
    if (onPasswordVisibilityChange) {
      onPasswordVisibilityChange(nextValue);
      return;
    }

    setInternalVisible(nextValue);
  };

  return (
    <View style={[styles.wrapper, !showLabel ? styles.wrapperCompact : null]}>
      {showLabel ? <Text style={styles.label}>{label}</Text> : null}
      <Pressable
        onPress={() => inputRef.current?.focus()}
        style={[
          styles.inputWrap,
          surfaceMode === "solid" ? styles.inputWrapSolid : styles.inputWrapNormal,
          forceFocused ? styles.inputFocused : null,
          error ? styles.inputError : null
        ]}
      >
        <TextInput
          ref={inputRef}
          {...props}
          secureTextEntry={!visible}
          autoCapitalize={props.autoCapitalize ?? "none"}
          autoCorrect={props.autoCorrect ?? false}
          autoComplete={props.autoComplete ?? "off"}
          textContentType={props.textContentType ?? "none"}
          importantForAutofill={props.importantForAutofill ?? "no"}
          onFocus={props.onFocus}
          onBlur={(event) => {
            if (autoHideOnBlur) {
              setVisible(false);
            }
            props.onBlur?.(event);
          }}
          placeholderTextColor={palette.textSecondary}
          style={[
            styles.input,
            surfaceMode === "solid" ? styles.inputSolid : styles.inputNormal,
            style
          ]}
        />
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={visible ? "Hide password" : "Show password"}
          onPress={() => {
            setVisible(!visible);
            inputRef.current?.focus();
          }}
          style={({ pressed }) => [styles.eyeButton, pressed ? styles.eyeButtonPressed : null]}
        >
          <Ionicons
            name={visible ? "eye-outline" : "eye-off-outline"}
            size={18}
            color={palette.textSecondary}
          />
        </Pressable>
      </Pressable>
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
    ...typography.caption
  },
  inputWrap: {
    minHeight: controls.fieldHeight,
    borderRadius: controls.fieldRadius,
    borderWidth: 1,
    borderColor: palette.border,
    paddingLeft: spacing[16],
    paddingRight: spacing[8],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  inputWrapNormal: {
    backgroundColor: controls.controlSurfaceMuted
  },
  inputWrapSolid: {
    backgroundColor: controls.controlSurfaceStrong
  },
  input: {
    flex: 1,
    backgroundColor: "transparent",
    color: palette.textPrimary,
    ...typography.body1
  },
  inputNormal: {
    backgroundColor: controls.controlSurfaceMuted
  },
  inputSolid: {
    backgroundColor: controls.controlSurfaceStrong
  },
  eyeButton: {
    width: 34,
    height: 34,
    borderRadius: 10,
    alignItems: "center",
    justifyContent: "center"
  },
  eyeButtonPressed: {
    opacity: 0.75
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

