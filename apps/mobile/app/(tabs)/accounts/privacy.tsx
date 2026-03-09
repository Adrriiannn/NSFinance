import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { IconButton } from "../../../src/components/ui/IconButton";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SelectField } from "../../../src/components/ui/SelectField";
import { TextField } from "../../../src/components/ui/TextField";
import {
  useConsentsQuery,
  useUpdateConsentMutation
} from "../../../src/features/policies/usePolicies";
import {
  useUpdateUserPreferencesMutation,
  useUserPreferencesQuery
} from "../../../src/features/users/useUserSettings";
import { palette, spacing, typography } from "../../../src/theme/tokens";

export default function PrivacySettingsScreen() {
  const router = useRouter();
  const preferencesQuery = useUserPreferencesQuery();
  const updatePreferencesMutation = useUpdateUserPreferencesMutation();
  const consentsQuery = useConsentsQuery();
  const updateConsentMutation = useUpdateConsentMutation();
  const [adviceTone, setAdviceTone] = useState("balanced");
  const [digestFrequency, setDigestFrequency] = useState("weekly");
  const [reminderPreference, setReminderPreference] = useState("important_only");

  useEffect(() => {
    if (!preferencesQuery.data) {
      return;
    }

    setAdviceTone(preferencesQuery.data.adviceTonePreference);
    setDigestFrequency(preferencesQuery.data.digestFrequency);
    setReminderPreference(preferencesQuery.data.reminderPreference);
  }, [preferencesQuery.data]);

  const marketingConsent =
    consentsQuery.data?.find((item) => item.consentType === "marketing_communications")?.status ??
    "denied";

  const savePreferences = async () => {
    await updatePreferencesMutation.mutateAsync({
      adviceTonePreference: adviceTone,
      digestFrequency,
      reminderPreference,
      notificationPreferencesJson: "{}",
      privacyPreferencesJson: "{}",
      essentialCategoryPreferencesJson: "{}",
      futureGoalConfigurationJson: "{}"
    });
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>Privacy & Preferences</Text>
        <View style={{ width: 42 }} />
      </View>

      {preferencesQuery.isError || consentsQuery.isError ? (
        <ErrorState
          title="Could not load privacy settings"
          message={preferencesQuery.error?.message ?? consentsQuery.error?.message ?? "Unknown error"}
          onRetry={() => {
            void preferencesQuery.refetch();
            void consentsQuery.refetch();
          }}
        />
      ) : (
        <View style={styles.form}>
          <Text style={styles.sectionTitle}>Preference Placeholders</Text>
          <TextField label="Advice tone" value={adviceTone} onChangeText={setAdviceTone} />
          <TextField label="Digest frequency" value={digestFrequency} onChangeText={setDigestFrequency} />
          <TextField
            label="Reminder preference"
            value={reminderPreference}
            onChangeText={setReminderPreference}
          />
          <PrimaryButton
            label="Save preference placeholders"
            onPress={() => void savePreferences()}
            isLoading={updatePreferencesMutation.isPending}
          />

          <Text style={styles.sectionTitle}>Communications Consent</Text>
          <SelectField
            label="Marketing communications"
            value={marketingConsent}
            options={[
              { label: "Granted", value: "granted" },
              { label: "Denied", value: "denied" },
              { label: "Revoked", value: "revoked" }
            ]}
            onChange={(value) => {
              void updateConsentMutation.mutateAsync({
                consentType: "marketing_communications",
                status: value,
                source: "mobile_settings"
              });
            }}
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
  sectionTitle: {
    marginTop: spacing[8],
    color: palette.textPrimary,
    ...typography.bodyStrong
  }
});
