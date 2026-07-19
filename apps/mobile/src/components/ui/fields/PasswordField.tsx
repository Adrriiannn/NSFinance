import { Ionicons } from "@expo/vector-icons";
import { useRef, useState } from "react";
import { Pressable, TextInput, View } from "react-native";
import type { StyleProp, TextInputProps, ViewStyle } from "react-native";
import { useThemeTokens } from "../../../theme/tokens";
import { FieldError } from "../forms/FieldError";
import { AppText } from "../text/AppText";
import { useFieldPresets } from "./field.presets";

type PasswordFieldProps = Omit<TextInputProps, "secureTextEntry"> & {
  label: string;
  error?: string;
  showLabel?: boolean;
  dense?: boolean;
  containerStyle?: StyleProp<ViewStyle>;
  forceFocused?: boolean;
  isPasswordVisible?: boolean;
  onPasswordVisibilityChange?: (isVisible: boolean) => void;
  autoHideOnBlur?: boolean;
};

export function PasswordField({
  label,
  error,
  style,
  showLabel = true,
  dense = false,
  containerStyle,
  forceFocused = false,
  isPasswordVisible,
  onPasswordVisibilityChange,
  autoHideOnBlur = true,
  ...props
}: PasswordFieldProps) {
  const fieldPresets = useFieldPresets();
  const { palette } = useThemeTokens();
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
          dense ? fieldPresets.containerDense : null,
          forceFocused ? fieldPresets.containerFocused : null,
          error ? fieldPresets.containerError : null,
          containerStyle
        ]}
      >
        <TextInput
          ref={inputRef}
          {...props}
          secureTextEntry={!visible}
          allowFontScaling={props.allowFontScaling ?? false}
          maxFontSizeMultiplier={props.maxFontSizeMultiplier ?? 1}
          selectionColor={props.selectionColor ?? palette.accent}
          cursorColor={props.cursorColor ?? palette.accent}
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
          onPress={(event) => {
            event.stopPropagation();
            setVisible(!visible);
          }}
          onPressIn={(event) => {
            event.stopPropagation();
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
