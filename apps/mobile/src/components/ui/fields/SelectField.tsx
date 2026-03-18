import { Ionicons } from "@expo/vector-icons";
import type { ReactNode } from "react";
import { Pressable, View } from "react-native";
import type { StyleProp, ViewStyle } from "react-native";
import { palette } from "../../../theme/tokens";
import { FieldError } from "../forms/FieldError";
import { FieldHint } from "../forms/FieldHint";
import { AppText } from "../text/AppText";
import { fieldPresets } from "./field.presets";

type SelectFieldProps = {
  label?: string;
  value?: string | null;
  placeholder?: string;
  helper?: string;
  error?: string;
  leading?: ReactNode;
  disabled?: boolean;
  onPress?: () => void;
  trailing?: ReactNode;
  containerStyle?: StyleProp<ViewStyle>;
};

export function SelectField({
  label,
  value,
  placeholder = "Select",
  helper,
  error,
  leading,
  disabled = false,
  onPress,
  trailing,
  containerStyle
}: SelectFieldProps) {
  return (
    <View style={fieldPresets.wrapper}>
      {label ? <AppText preset="fieldLabel">{label}</AppText> : null}
      <Pressable
        disabled={disabled}
        onPress={onPress}
        style={({ pressed }) => [
          fieldPresets.container,
          error ? fieldPresets.containerError : null,
          containerStyle,
          disabled ? { opacity: 0.6 } : null,
          pressed ? { opacity: 0.94 } : null
        ]}
      >
        {leading}
        <AppText preset="body" tone={value ? "default" : "secondary"} style={{ flex: 1 }}>
          {value || placeholder}
        </AppText>
        {trailing ?? <Ionicons name="chevron-down" size={16} color={palette.textSecondary} />}
      </Pressable>
      {error ? <FieldError>{error}</FieldError> : helper ? <FieldHint>{helper}</FieldHint> : null}
    </View>
  );
}
