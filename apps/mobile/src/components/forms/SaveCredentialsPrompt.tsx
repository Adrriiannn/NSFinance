import { Modal, StyleSheet, Text, View } from "react-native";
import { PrimaryButton } from "../ui/PrimaryButton";
import { SecondaryButton } from "../ui/SecondaryButton";
import { palette, spacing, typography } from "../../theme/tokens";

type SaveCredentialsPromptProps = {
  visible: boolean;
  onConfirm: () => void;
  onDecline: () => void;
};

export function SaveCredentialsPrompt({
  visible,
  onConfirm,
  onDecline
}: SaveCredentialsPromptProps) {
  return (
    <Modal visible={visible} transparent animationType="fade">
      <View style={styles.overlay}>
        <View style={styles.card}>
          <Text style={styles.title}>Save login details?</Text>
          <Text style={styles.body}>
            We can securely remember your credentials on this device so next time you only complete the captcha and sign in.
          </Text>
          <View style={styles.actions}>
            <PrimaryButton label="Save credentials" onPress={onConfirm} />
            <SecondaryButton label="Not now" onPress={onDecline} />
          </View>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  overlay: {
    flex: 1,
    backgroundColor: "rgba(4,11,23,0.76)",
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[20]
  },
  card: {
    width: "100%",
    borderRadius: 18,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.98)",
    padding: spacing[16],
    gap: spacing[12]
  },
  title: {
    color: palette.textPrimary,
    ...typography.title2
  },
  body: {
    color: palette.textSecondary,
    ...typography.body2
  },
  actions: {
    gap: spacing[12]
  }
});
