import { forwardRef, useRef, useState, type ReactNode } from "react";
import type { StyleProp, TextInputProps, TextStyle, ViewStyle } from "react-native";
import { Pressable, TextInput, View } from "react-native";
import { FieldError } from "../forms/FieldError";
import { FieldHint } from "../forms/FieldHint";
import { AppText } from "../text/AppText";
import { useFieldPresets } from "./field.presets";
import { useThemeTokens } from "../../../theme/tokens";

type SharedTextFieldProps = TextInputProps & {
  label?: string;
  helper?: string;
  error?: string;
  dense?: boolean;
  leading?: ReactNode;
  trailing?: ReactNode;
  containerStyle?: StyleProp<ViewStyle>;
  inputStyle?: StyleProp<TextStyle>;
  showLabel?: boolean;
  forceFocused?: boolean;
};

export const TextField = forwardRef<TextInput, SharedTextFieldProps>(function TextField(
  {
    label,
    helper,
    error,
    dense = false,
    leading,
    trailing,
    containerStyle,
    inputStyle,
    showLabel = true,
    forceFocused = false,
    multiline,
    onFocus,
    onBlur,
    ...props
  },
  ref
) {
  const fieldPresets = useFieldPresets();
  const { palette } = useThemeTokens();
  const [focused, setFocused] = useState(false);
  const inputRef = useRef<TextInput | null>(null);

  const assignRef = (node: TextInput | null) => {
    inputRef.current = node;

    if (!ref) {
      return;
    }

    if (typeof ref === "function") {
      ref(node);
      return;
    }

    ref.current = node;
  };

  return (
    <View style={[fieldPresets.wrapper, !showLabel ? fieldPresets.wrapperCompact : null]}>
      {showLabel && label ? <AppText preset="fieldLabel">{label}</AppText> : null}
      <Pressable
        onPress={() => inputRef.current?.focus()}
        style={[
          fieldPresets.container,
          dense ? fieldPresets.containerDense : null,
          focused || forceFocused ? fieldPresets.containerFocused : null,
          error ? fieldPresets.containerError : null,
          containerStyle
        ]}
      >
        {leading}
        <TextInput
          {...props}
          ref={assignRef}
          multiline={multiline}
          allowFontScaling={props.allowFontScaling ?? false}
          maxFontSizeMultiplier={props.maxFontSizeMultiplier ?? 1}
          selectionColor={props.selectionColor ?? palette.accent}
          cursorColor={props.cursorColor ?? palette.accent}
          onFocus={(event) => {
            setFocused(true);
            onFocus?.(event);
          }}
          onBlur={(event) => {
            setFocused(false);
            onBlur?.(event);
          }}
          placeholderTextColor={palette.textSecondary}
          style={[
            fieldPresets.input,
            multiline ? fieldPresets.multilineInput : null,
            inputStyle
          ]}
        />
        {trailing}
      </Pressable>
      {error ? <FieldError>{error}</FieldError> : helper ? <FieldHint>{helper}</FieldHint> : null}
    </View>
  );
});
