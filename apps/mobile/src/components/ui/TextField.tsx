import { AppText } from "./text/AppText";
import { TextField as BaseTextField } from "./fields/TextField";

type TextFieldProps = React.ComponentProps<typeof BaseTextField> & {
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
  containerStyle,
  showLabel = true,
  forceFocused = false,
  surfaceMode = "normal",
  leadingText,
  ...props
}: TextFieldProps) {
  return (
    <BaseTextField
      {...props}
      label={label}
      error={error}
      showLabel={showLabel}
      inputStyle={style}
      leading={leadingText ? <AppText preset="secondary">{leadingText}</AppText> : undefined}
      containerStyle={[
        surfaceMode === "solid" ? { backgroundColor: "#162D48" } : null,
        forceFocused ? { borderColor: "#7FAEFF", backgroundColor: "#162D48" } : null,
        containerStyle
      ]}
    />
  );
}
