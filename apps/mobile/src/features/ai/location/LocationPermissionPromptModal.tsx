import { Pressable, Text, View } from "react-native";
import { SystemModal } from "../../../components/ui/surfaces/SystemModal";
import {
  createRuntimeStyleSheet,
  palette,
  radius,
  spacing,
  surfaces,
  typography
} from "../../../theme/tokens";

export type LocationPromptAction = {
  label: string;
  onPress: () => void;
  variant?: "primary" | "secondary";
  disabled?: boolean;
};

type LocationPermissionPromptModalProps = {
  visible: boolean;
  title: string;
  message: string;
  actions: LocationPromptAction[];
  onRequestClose: () => void;
};

export function LocationPermissionPromptModal({
  visible,
  title,
  message,
  actions,
  onRequestClose
}: LocationPermissionPromptModalProps) {
  return (
    <SystemModal
      visible={visible}
      transparent
      animationType="fade"
      onRequestClose={onRequestClose}
    >
      <Pressable style={styles.overlay} onPress={onRequestClose}>
        <Pressable style={styles.sheet} onPress={() => undefined}>
          <Text style={styles.title}>{title}</Text>
          <Text style={styles.message}>{message}</Text>
          <View style={styles.actions}>
            {actions.map((action) => {
              const isPrimary = action.variant === "primary";
              return (
                <Pressable
                  key={action.label}
                  onPress={action.onPress}
                  disabled={action.disabled}
                  style={({ pressed }) => [
                    styles.actionButton,
                    isPrimary ? styles.primaryActionButton : styles.secondaryActionButton,
                    action.disabled ? styles.disabledActionButton : null,
                    pressed ? styles.pressedActionButton : null
                  ]}
                >
                  <Text
                    style={[
                      styles.actionButtonText,
                      isPrimary ? styles.primaryActionText : styles.secondaryActionText
                    ]}
                  >
                    {action.label}
                  </Text>
                </Pressable>
              );
            })}
          </View>
        </Pressable>
      </Pressable>
    </SystemModal>
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
  actions: {
    gap: spacing[8]
  },
  actionButton: {
    minHeight: 44,
    borderRadius: radius.small,
    borderWidth: 1,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
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
