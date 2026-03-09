import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { IconButton } from "../../../src/components/ui/IconButton";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { TextField } from "../../../src/components/ui/TextField";
import {
  useUpdateUserProfileMutation,
  useUserProfileQuery
} from "../../../src/features/users/useUserSettings";
import { palette, spacing, typography } from "../../../src/theme/tokens";

export default function ProfileSettingsScreen() {
  const router = useRouter();
  const profileQuery = useUserProfileQuery();
  const updateMutation = useUpdateUserProfileMutation();
  const [displayName, setDisplayName] = useState("");
  const [timezone, setTimezone] = useState("UTC");
  const [locale, setLocale] = useState("en-US");
  const [preferredCurrency, setPreferredCurrency] = useState("EUR");
  const [onboardingStatus, setOnboardingStatus] = useState("completed");
  const [biometricUnlockEnabled, setBiometricUnlockEnabled] = useState(false);

  useEffect(() => {
    if (!profileQuery.data) {
      return;
    }

    setDisplayName(profileQuery.data.displayName);
    setTimezone(profileQuery.data.timezone);
    setLocale(profileQuery.data.locale);
    setPreferredCurrency(profileQuery.data.preferredCurrency);
    setOnboardingStatus(profileQuery.data.onboardingStatus);
    setBiometricUnlockEnabled(profileQuery.data.biometricUnlockEnabled);
  }, [profileQuery.data]);

  const handleSave = async () => {
    await updateMutation.mutateAsync({
      displayName: displayName.trim(),
      timezone: timezone.trim(),
      locale: locale.trim(),
      preferredCurrency: preferredCurrency.trim().toUpperCase(),
      onboardingStatus: onboardingStatus.trim(),
      biometricUnlockEnabled
    });
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>Profile</Text>
        <View style={{ width: 42 }} />
      </View>

      {profileQuery.isError ? (
        <ErrorState
          title="Could not load profile"
          message={profileQuery.error.message}
          onRetry={() => {
            void profileQuery.refetch();
          }}
        />
      ) : (
        <View style={styles.form}>
          <TextField label="Display name" value={displayName} onChangeText={setDisplayName} />
          <TextField label="Timezone" value={timezone} onChangeText={setTimezone} />
          <TextField label="Locale" value={locale} onChangeText={setLocale} />
          <TextField
            label="Preferred currency"
            value={preferredCurrency}
            onChangeText={setPreferredCurrency}
            autoCapitalize="characters"
          />
          <TextField
            label="Onboarding status"
            value={onboardingStatus}
            onChangeText={setOnboardingStatus}
          />

          <View style={styles.row}>
            <Text style={styles.rowLabel}>
              Biometric app unlock: {biometricUnlockEnabled ? "enabled" : "disabled"}
            </Text>
            <SecondaryButton
              label={biometricUnlockEnabled ? "Disable" : "Enable"}
              onPress={() => setBiometricUnlockEnabled((current) => !current)}
            />
          </View>

          <PrimaryButton
            label="Save profile"
            onPress={() => void handleSave()}
            isLoading={updateMutation.isPending}
            disabled={!displayName.trim()}
          />
        </View>
      )}
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
  row: {
    gap: spacing[8]
  },
  rowLabel: {
    color: palette.textSecondary,
    ...typography.body2
  }
});
