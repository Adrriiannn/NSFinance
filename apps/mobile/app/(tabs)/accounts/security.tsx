import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { IconButton } from "../../../src/components/ui/IconButton";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { TextField } from "../../../src/components/ui/TextField";
import { useChangePasswordMutation } from "../../../src/features/auth/useAuthMutations";
import { formatUnknownError } from "../../../src/lib/api/errors";
import { palette, spacing, typography } from "../../../src/theme/tokens";

export default function SecuritySettingsScreen() {
  const router = useRouter();
  const changePasswordMutation = useChangePasswordMutation();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [message, setMessage] = useState<string | null>(null);

  const handleChangePassword = async () => {
    const response = await changePasswordMutation.mutateAsync({
      currentPassword,
      newPassword
    });
    setMessage(response.message);
    setCurrentPassword("");
    setNewPassword("");
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>Security</Text>
        <View style={{ width: 42 }} />
      </View>

      {changePasswordMutation.isError ? (
        <ErrorState
          title="Password update failed"
          message={formatUnknownError(changePasswordMutation.error)}
          onRetry={handleChangePassword}
          retryLabel="Try again"
        />
      ) : null}

      <View style={styles.form}>
        <TextField
          label="Current password"
          value={currentPassword}
          onChangeText={setCurrentPassword}
          secureTextEntry
        />
        <TextField
          label="New password"
          value={newPassword}
          onChangeText={setNewPassword}
          secureTextEntry
          placeholder="At least 10 characters"
        />
      </View>

      {message ? <Text style={styles.message}>{message}</Text> : null}

      <View style={styles.actions}>
        <PrimaryButton
          label="Change password"
          onPress={() => void handleChangePassword()}
          isLoading={changePasswordMutation.isPending}
          disabled={!currentPassword || !newPassword}
        />
        <SecondaryButton
          label="Manage sessions/devices"
          onPress={() => router.push("/(tabs)/accounts/sessions")}
        />
      </View>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: spacing[16],
    gap: spacing[16]
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  headerTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  form: {
    gap: spacing[12]
  },
  message: {
    color: palette.textSecondary,
    ...typography.body2
  },
  actions: {
    gap: spacing[12]
  }
});
