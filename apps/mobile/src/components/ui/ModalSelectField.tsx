import { Ionicons } from "@expo/vector-icons";
import { useMemo, useState } from "react";
import { Modal, Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { palette, spacing, typography } from "../../theme/tokens";

export type ModalSelectOption = {
  label: string;
  value: string;
};

type ModalSelectFieldProps = {
  label: string;
  value: string | null | undefined;
  options: ModalSelectOption[];
  placeholder?: string;
  onChange: (value: string) => void;
  disabled?: boolean;
};

export function ModalSelectField({
  label,
  value,
  options,
  placeholder = "Select",
  onChange,
  disabled = false
}: ModalSelectFieldProps) {
  const [isOpen, setIsOpen] = useState(false);

  const selected = useMemo(
    () => options.find((item) => item.value === value),
    [options, value]
  );

  return (
    <View style={styles.wrapper}>
      <Text style={styles.label}>{label}</Text>

      <Pressable
        disabled={disabled}
        onPress={() => setIsOpen(true)}
        style={({ pressed }) => [
          styles.fieldButton,
          disabled ? styles.fieldButtonDisabled : null,
          pressed ? styles.fieldButtonPressed : null
        ]}
      >
        <Text style={styles.fieldValue} numberOfLines={1}>
          {selected?.label ?? placeholder}
        </Text>
        <Ionicons name="chevron-down" size={16} color={palette.textSecondary} />
      </Pressable>

      <Modal visible={isOpen} transparent animationType="fade" onRequestClose={() => setIsOpen(false)}>
        <Pressable style={styles.overlay} onPress={() => setIsOpen(false)}>
          <Pressable style={styles.sheet} onPress={() => undefined}>
            <Text style={styles.sheetTitle}>{label}</Text>

            <ScrollView contentContainerStyle={styles.optionList} showsVerticalScrollIndicator={false}>
              {options.map((option) => (
                <Pressable
                  key={option.value}
                  style={({ pressed }) => [
                    styles.optionRow,
                    option.value === value ? styles.optionRowActive : null,
                    pressed ? styles.optionRowPressed : null
                  ]}
                  onPress={() => {
                    onChange(option.value);
                    setIsOpen(false);
                  }}
                >
                  <Text style={styles.optionLabel}>{option.label}</Text>
                </Pressable>
              ))}
            </ScrollView>
          </Pressable>
        </Pressable>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    gap: spacing[8]
  },
  label: {
    color: palette.textPrimary,
    ...typography.caption
  },
  fieldButton: {
    minHeight: 50,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: palette.elevatedBackground,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8]
  },
  fieldButtonPressed: {
    opacity: 0.9
  },
  fieldButtonDisabled: {
    opacity: 0.6
  },
  fieldValue: {
    flex: 1,
    color: palette.textPrimary,
    ...typography.body1
  },
  overlay: {
    flex: 1,
    backgroundColor: "rgba(4,11,23,0.74)",
    justifyContent: "flex-end"
  },
  sheet: {
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.98)",
    padding: spacing[16],
    gap: spacing[12],
    maxHeight: "80%"
  },
  sheetTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  optionList: {
    gap: spacing[8],
    paddingBottom: spacing[8]
  },
  optionRow: {
    minHeight: 46,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  optionRowActive: {
    borderColor: palette.primaryGlow
  },
  optionRowPressed: {
    opacity: 0.9
  },
  optionLabel: {
    color: palette.textPrimary,
    ...typography.body2
  }
});
