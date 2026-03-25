import { useLocalSearchParams, useRouter } from "expo-router";
import { Alert, Pressable, Text, TextInput, View } from "react-native";
import { PlanningHubScreen } from "../../../../src/components/planningHub/PlanningHubScreen";
import { GlassCard } from "../../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../../src/components/ui/PrimaryButton";
import { useExpensePlanning } from "../../../../src/features/expenseTracker/ExpensePlanningProvider";
import { expensePlanReportReasons } from "../../../../src/features/expenseTracker/expensePlanCommunityUtils";
import type { ExpensePlanReportReason } from "../../../../src/features/expenseTracker/expensePlanningTypes";
import { palette, radius, spacing, typography, createRuntimeStyleSheet } from "../../../../src/theme/tokens";
import { useState } from "react";

export default function ExpensePlanReportScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ publicationId?: string }>();
  const publicationId = typeof params.publicationId === "string" ? params.publicationId : "";
  const { reportPublication } = useExpensePlanning();
  const [reason, setReason] = useState<ExpensePlanReportReason>("spam");
  const [notes, setNotes] = useState("");

  const handleSubmit = () => {
    const result = reportPublication(publicationId, reason, notes);
    if (!result.ok) {
      Alert.alert("Could not submit report", result.error ?? "Try again.");
      return;
    }

    Alert.alert("Report sent", "Thanks. The plan has been flagged for review where needed.");
    router.back();
  };

  return (
    <PlanningHubScreen title="Report plan">
      <GlassCard style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Why are you reporting this?</Text>
        <View style={styles.reasonList}>
          {expensePlanReportReasons.map((option) => (
            <Pressable key={option.id} style={[styles.reasonCard, reason === option.id ? styles.reasonCardActive : null]} onPress={() => setReason(option.id)}>
              <Text style={[styles.reasonLabel, reason === option.id ? styles.reasonLabelActive : null]}>{option.label}</Text>
            </Pressable>
          ))}
        </View>
      </GlassCard>

      <GlassCard style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Optional note</Text>
        <TextInput value={notes} onChangeText={setNotes} placeholder="Tell us what stood out." placeholderTextColor={palette.textSecondary} multiline style={styles.textArea} />
      </GlassCard>

      <PrimaryButton label="Submit report" onPress={handleSubmit} />
    </PlanningHubScreen>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  sectionCard: {
    gap: spacing[16]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  reasonList: {
    gap: spacing[8]
  },
  reasonCard: {
    minHeight: 48,
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.08)",
    backgroundColor: "rgba(255,255,255,0.04)",
    paddingHorizontal: spacing[12],
    justifyContent: "center"
  },
  reasonCardActive: {
    borderColor: palette.primary,
    backgroundColor: "rgba(76,141,255,0.14)"
  },
  reasonLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
  },
  reasonLabelActive: {
    color: palette.textPrimary
  },
  textArea: {
    minHeight: 140,
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.08)",
    backgroundColor: "rgba(255,255,255,0.04)",
    paddingHorizontal: spacing[12],
    paddingTop: spacing[12],
    color: palette.textPrimary,
    ...typography.body1,
    textAlignVertical: "top"
  }
}));



