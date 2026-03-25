import { AppText } from "./text/AppText";
import { TextField as BaseTextField } from "./fields/TextField";
import { palette, surfaces } from "../../theme/tokens";

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
        surfaceMode === "solid" ? { backgroundColor: surfaces.fieldStrong } : null,
        forceFocused ? { borderColor: palette.borderStrong, backgroundColor: surfaces.fieldStrong } : null,
        containerStyle
      ]}
    />
  );
}
