import { Ionicons } from "@expo/vector-icons";
import { useMemo, useState } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { ListRow } from "../../components/ui/rows/ListRow";
import { ModalSheet } from "../../components/ui/surfaces/ModalSheet";
import { palette, spacing } from "../../theme/tokens";
import { HEADER_SURFACES, HEADER_TYPOGRAPHY } from "./header.constants";
import type { HeaderDropdownSlotProps } from "./header.types";

export function HeaderDropdownSlot({
  title,
  value,
  placeholder = "Select",
  options,
  onChange,
  onPress,
  containerStyle,
  disabled = false
}: HeaderDropdownSlotProps) {
  const [isOpen, setIsOpen] = useState(false);
  const selected = useMemo(
    () => options?.find((option) => option.value === value) ?? null,
    [options, value]
  );

  const open = () => {
    if (disabled) {
      return;
    }

    if (onPress) {
      onPress();
      return;
    }

    if (options?.length) {
      setIsOpen(true);
    }
  };

  return (
    <>
      <Pressable
        disabled={disabled}
        onPress={open}
        style={({ pressed }) => [
          HEADER_SURFACES.inputSlot,
          styles.control,
          disabled ? styles.disabled : null,
          pressed ? styles.pressed : null,
          containerStyle
        ]}
      >
        <Text
          numberOfLines={1}
          style={[
            HEADER_TYPOGRAPHY.headerDropdownText,
            styles.value,
            !selected?.label && !value ? styles.placeholder : null
          ]}
        >
          {selected?.label ?? value ?? placeholder}
        </Text>
        <Ionicons name="chevron-down" size={16} color={palette.textSecondary} />
      </Pressable>

      {options?.length ? (
        <ModalSheet visible={isOpen} onClose={() => setIsOpen(false)} title={title}>
          <View style={styles.optionList}>
            {options.map((option) => (
              <ListRow
                key={option.value}
                title={option.label}
                onPress={() => {
                  onChange?.(option.value);
                  setIsOpen(false);
                }}
                trailing={
                  option.value === value ? (
                    <Text style={styles.selectedText}>Selected</Text>
                  ) : undefined
                }
              />
            ))}
          </View>
        </ModalSheet>
      ) : null}
    </>
  );
}

const styles = StyleSheet.create({
  control: {
    flex: 1
  },
  value: {
    flex: 1
  },
  placeholder: {
    color: palette.textSecondary
  },
  disabled: {
    opacity: 0.56
  },
  pressed: {
    opacity: 0.9
  },
  optionList: {
    gap: spacing[8]
  },
  selectedText: {
    ...HEADER_TYPOGRAPHY.headerButtonText,
    color: palette.primaryGlow
  }
});
