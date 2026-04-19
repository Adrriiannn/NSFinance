import { Modal, Pressable, Text, TextInput, View } from "react-native";
import {
  createRuntimeStyleSheet,
  palette,
  radius,
  spacing,
  surfaces,
  typography
} from "../../../theme/tokens";

type LocationTypedAreaModalProps = {
  visible: boolean;
  value: string;
  onChangeValue: (value: string) => void;
  onCancel: () => void;
  onConfirm: () => void;
};

export function LocationTypedAreaModal({
  visible,
  value,
  onChangeValue,
  onCancel,
  onConfirm
}: LocationTypedAreaModalProps) {
  return (
    <Modal
      visible={visible}
      transparent
      animationType="fade"
      onRequestClose={onCancel}
    >
      <Pressable style={styles.overlay} onPress={onCancel}>
        <Pressable style={styles.sheet} onPress={() => undefined}>
          <Text style={styles.title}>Enter an area</Text>
          <Text style={styles.message}>
            Add a suburb, city centre, postcode, or landmark so we can search nearby places
            without GPS.
          </Text>
          <TextInput
            value={value}
            onChangeText={onChangeValue}
            placeholder="Example: Dublin city centre"
            placeholderTextColor={palette.textSecondary}
            selectionColor={palette.accent}
            cursorColor={palette.accent}
            style={styles.input}
            autoFocus
          />
          <View style={styles.actions}>
            <Pressable
              onPress={onCancel}
              style={({ pressed }) => [
                styles.actionButton,
                styles.secondaryActionButton,
                pressed ? styles.pressedActionButton : null
              ]}
            >
              <Text style={[styles.actionButtonText, styles.secondaryActionText]}>Cancel</Text>
            </Pressable>
            <Pressable
              onPress={onConfirm}
              disabled={!value.trim()}
              style={({ pressed }) => [
                styles.actionButton,
                styles.primaryActionButton,
                !value.trim() ? styles.disabledActionButton : null,
                pressed ? styles.pressedActionButton : null
              ]}
            >
              <Text style={[styles.actionButtonText, styles.primaryActionText]}>
                Use this area
              </Text>
            </Pressable>
          </View>
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  overlay: {
    flex: 1,
    backgroundColor: palette.overlay,
    justifyContent: "flex-end"
  },
  sheet: {
    borderTopLeftRadius: radius.hero,
    borderTopRightRadius: radius.hero,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.sheet,
    paddingHorizontal: spacing[16],
    paddingVertical: spacing[16],
    gap: spacing[12]
  },
  title: {
    color: palette.textPrimary,
    ...typography.title2,
    fontWeight: "700"
  },
  message: {
    color: palette.textSecondary,
    ...typography.body2
  },
  input: {
    minHeight: 44,
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    color: palette.textPrimary,
    paddingHorizontal: spacing[12],
    ...typography.body1
  },
  actions: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8]
  },
  actionButton: {
    flex: 1,
    minHeight: 44,
    borderRadius: radius.small,
    borderWidth: 1,
    alignItems: "center",
    justifyContent: "center"
  },
  primaryActionButton: {
    borderColor: "rgba(242,140,40,0.42)",
    backgroundColor: "rgba(242,140,40,0.2)"
  },
  secondaryActionButton: {
    borderColor: "rgba(242,140,40,0.2)",
    backgroundColor: surfaces.fieldStrong
  },
  actionButtonText: {
    ...typography.body2,
    fontWeight: "600"
  },
  primaryActionText: {
    color: palette.textPrimary
  },
  secondaryActionText: {
    color: palette.textSecondary
  },
  disabledActionButton: {
    opacity: 0.5
  },
  pressedActionButton: {
    opacity: 0.86
  }
}));
