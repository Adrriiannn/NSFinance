import { Ionicons } from "@expo/vector-icons";
import { useRef, useState } from "react";
import { Pressable, TextInput, View } from "react-native";
import type { TextInputProps } from "react-native";
import { palette } from "../../theme/tokens";
import { FieldError } from "./forms/FieldError";
import { AppText } from "./text/AppText";
import { fieldPresets } from "./fields/field.presets";

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
    <View style={fieldPresets.wrapper}>
      {showLabel ? <AppText preset="fieldLabel">{label}</AppText> : null}
      <Pressable
        onPress={() => inputRef.current?.focus()}
        style={[
          fieldPresets.container,
          surfaceMode === "solid" ? { backgroundColor: "#162D48" } : null,
          forceFocused ? fieldPresets.containerFocused : null,
          error ? fieldPresets.containerError : null
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
          onFocus={(event) => {
            props.onFocus?.(event);
          }}
          onBlur={(event) => {
            if (autoHideOnBlur) {
              setVisible(false);
            }
            props.onBlur?.(event);
          }}
          placeholderTextColor={palette.textSecondary}
          style={[fieldPresets.input, style]}
        />
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={visible ? "Hide password" : "Show password"}
          onPress={() => {
            setVisible(!visible);
            inputRef.current?.focus();
          }}
          style={({ pressed }) => [
            fieldPresets.action,
            pressed ? { opacity: 0.75 } : null
          ]}
        >
          <Ionicons
            name={visible ? "eye-outline" : "eye-off-outline"}
            size={18}
            color={palette.textSecondary}
          />
        </Pressable>
      </Pressable>
      {error ? <FieldError>{error}</FieldError> : null}
    </View>
  );
}
