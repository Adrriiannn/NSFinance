import { Ionicons } from "@expo/vector-icons";
import { Pressable } from "react-native";
import { TextField } from "../../components/ui/fields/TextField";
import { palette, createRuntimeStyleSheet } from "../../theme/tokens";
import { HEADER_CONSTANTS } from "./header.constants";
import type { HeaderSearchSlotProps } from "./header.types";

export function HeaderSearchSlot({
  containerStyle,
  onClear,
  value,
  ...props
}: HeaderSearchSlotProps) {
  return (
    <TextField
      {...props}
      value={value}
      showLabel={false}
      dense
      placeholderTextColor={palette.textSecondary}
      containerStyle={[
        styles.container,
        containerStyle
      ]}
      inputStyle={styles.input}
      leading={<Ionicons name="search-outline" size={18} color={palette.textSecondary} />}
      trailing={
        value && onClear ? (
          <Pressable onPress={onClear} style={styles.clearButton}>
            <Ionicons name="close" size={16} color={palette.textSecondary} />
          </Pressable>
        ) : undefined
      }
    />
  );
}

const styles = createRuntimeStyleSheet(() => ({
  container: {
    minHeight: HEADER_CONSTANTS.searchHeight,
    borderRadius: HEADER_CONSTANTS.searchRadius,
    paddingHorizontal: 12
  },
  input: {
    paddingVertical: 8
  },
  clearButton: {
    width: 24,
    height: 24,
    alignItems: "center",
    justifyContent: "center"
  }
}));



