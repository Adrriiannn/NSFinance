import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { Alert, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import { ExpenseTrackerMiniAppScreen } from "../../../../../src/components/expenseTracker/ExpenseTrackerMiniAppScreen";
import { GlassCard } from "../../../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../../../src/components/ui/PrimaryButton";
import { useExpensePlanning } from "../../../../../src/features/expenseTracker/ExpensePlanningProvider";
import { palette, radius, spacing, typography } from "../../../../../src/theme/tokens";

export default function ExpensePlanPublishScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ planId?: string; publicationId?: string }>();
  const initialPlanId = typeof params.planId === "string" ? params.planId : "";
  const publicationId = typeof params.publicationId === "string" ? params.publicationId : "";
  const { plans, getPublicationById, publishPlan, updatePublication } = useExpensePlanning();

  const editablePlans = useMemo(() => plans.filter((plan) => plan.status !== "scheduled" || true), [plans]);
  const existingPublication = publicationId ? getPublicationById(publicationId) : null;
  const [selectedPlanId, setSelectedPlanId] = useState(initialPlanId || existingPublication?.sourcePlanId || editablePlans[0]?.id || "");
  const [publicTitle, setPublicTitle] = useState(existingPublication?.publicTitle ?? "");
  const [publicDescription, setPublicDescription] = useState(existingPublication?.publicDescription ?? "");
  const [tagText, setTagText] = useState(existingPublication?.tags.join(", ") ?? "");
  const [feedback, setFeedback] = useState<string | null>(existingPublication?.moderationSummary ?? null);

  useEffect(() => {
    if (!existingPublication && !publicTitle && selectedPlanId) {
      const plan = editablePlans.find((item) => item.id === selectedPlanId);
      if (plan) {
        setPublicTitle(plan.title);
        setPublicDescription(`Shared from ${plan.title}. Adapt this plan to your own month or week and tune the amounts to fit your pace.`);
        setTagText([plan.periodType, ...(plan.isTemplate ? ["template"] : []), ...(plan.isRecurring ? ["recurring"] : [])].join(", "));
      }
    }
  }, [editablePlans, existingPublication, publicTitle, selectedPlanId]);

  const handleSubmit = () => {
    const tags = tagText.split(",").map((item) => item.trim()).filter(Boolean);
    if (existingPublication) {
      const result = updatePublication(existingPublication.id, {
        publicTitle,
        publicDescription,
        tags
      });

      if (result.error || !result.publication) {
        Alert.alert("Could not update publication", result.error ?? "Try again.");
        return;
      }

      setFeedback(result.publication.moderationSummary);
      Alert.alert("Publication updated", result.publication.publicationStatus === "published" ? "Your public plan is live." : "The update was saved and moderation status was refreshed.");
      router.replace(`/(tabs)/planner/expense-tracker/community/${result.publication.id}` as never);
      return;
    }

    const result = publishPlan({
      planId: selectedPlanId,
      publicTitle,
      publicDescription,
      tags
    });

    if (result.error || !result.publication) {
      Alert.alert("Could not publish plan", result.error ?? "Try again.");
      return;
    }

    setFeedback(result.publication.moderationSummary);
    Alert.alert(
      result.publication.publicationStatus === "published" ? "Published" : "Moderation check complete",
      result.publication.publicationStatus === "published"
        ? "Your plan is now live in the community library."
        : result.publication.moderationSummary
    );
    router.replace(`/(tabs)/planner/expense-tracker/community/${result.publication.id}` as never);
  };

  return (
    <ExpenseTrackerMiniAppScreen title={existingPublication ? "Edit publication" : "Publish plan"}>
      <GlassCard style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>{existingPublication ? "Public metadata" : "Source plan"}</Text>
        <Text style={styles.sectionCaption}>Choose the plan and shape how it will appear in the community browser.</Text>

        {!existingPublication ? (
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.planRail}>
            {editablePlans.map((plan) => (
              <Pressable key={plan.id} style={[styles.planChip, selectedPlanId === plan.id ? styles.planChipActive : null]} onPress={() => setSelectedPlanId(plan.id)}>
                <Text style={[styles.planChipLabel, selectedPlanId === plan.id ? styles.planChipLabelActive : null]}>{plan.title}</Text>
                <Text style={styles.planChipMeta}>{plan.periodType}</Text>
              </Pressable>
            ))}
          </ScrollView>
        ) : null}

        <View style={styles.inputGroup}>
          <Text style={styles.inputLabel}>Public title</Text>
          <TextInput value={publicTitle} onChangeText={setPublicTitle} placeholder="Monthly essentials blueprint" placeholderTextColor={palette.textSecondary} style={styles.textInput} />
        </View>

        <View style={styles.inputGroup}>
          <Text style={styles.inputLabel}>Public description</Text>
          <TextInput value={publicDescription} onChangeText={setPublicDescription} placeholder="What makes this plan useful?" placeholderTextColor={palette.textSecondary} multiline style={[styles.textInput, styles.textArea]} />
        </View>

        <View style={styles.inputGroup}>
          <Text style={styles.inputLabel}>Tags</Text>
          <TextInput value={tagText} onChangeText={setTagText} placeholder="monthly, household, essentials" placeholderTextColor={palette.textSecondary} style={styles.textInput} />
        </View>
      </GlassCard>

      <GlassCard style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Moderation gate</Text>
        <Text style={styles.sectionCaption}>Every publication runs through content checks before going public. Edits are rescanned later too.</Text>
        <View style={styles.moderationRow}>
          <Ionicons name="shield-checkmark-outline" size={18} color={palette.success} />
          <Text style={styles.moderationText}>Checks cover public title, description, and tags for blocked words, spammy language, and risky phrases.</Text>
        </View>
        {feedback ? <Text style={styles.feedbackText}>{feedback}</Text> : null}
      </GlassCard>

      <PrimaryButton label={existingPublication ? "Save public metadata" : "Publish plan"} onPress={handleSubmit} />
    </ExpenseTrackerMiniAppScreen>
  );
}

const styles = StyleSheet.create({
  sectionCard: {
    gap: spacing[16]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  sectionCaption: {
    color: palette.textSecondary,
    ...typography.body2
  },
  planRail: {
    gap: spacing[12],
    paddingRight: spacing[16]
  },
  planChip: {
    width: 200,
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.08)",
    backgroundColor: "rgba(255,255,255,0.04)",
    padding: spacing[12],
    gap: 4
  },
  planChipActive: {
    borderColor: palette.primary,
    backgroundColor: "rgba(76,141,255,0.16)"
  },
  planChipLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  planChipLabelActive: {
    color: palette.textPrimary
  },
  planChipMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  inputGroup: {
    gap: spacing[8]
  },
  inputLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  textInput: {
    minHeight: 52,
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.08)",
    backgroundColor: "rgba(255,255,255,0.04)",
    paddingHorizontal: spacing[12],
    color: palette.textPrimary,
    ...typography.body1
  },
  textArea: {
    minHeight: 132,
    paddingTop: spacing[12],
    textAlignVertical: "top"
  },
  moderationRow: {
    flexDirection: "row",
    gap: spacing[8],
    alignItems: "flex-start"
  },
  moderationText: {
    flex: 1,
    color: palette.textSecondary,
    ...typography.body2
  },
  feedbackText: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  }
});
