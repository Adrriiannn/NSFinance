import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { Alert, Pressable, Text, View } from "react-native";
import { PlanningHubScreen } from "../../../src/components/planningHub/PlanningHubScreen";
import { PlanningHubSegmentedControl } from "../../../src/components/planningHub/PlanningHubSegmentedControl";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { TextField } from "../../../src/components/ui/TextField";
import { useExpensePlanning } from "../../../src/features/expenseTracker/ExpensePlanningProvider";
import { buildExpensePlanTaxonomyLookup } from "../../../src/features/expenseTracker/expensePlanningUtils";
import { useExpenseTrackerTaxonomyQuery } from "../../../src/features/expenseTracker/useExpenseTracker";
import { useUserProfileQuery } from "../../../src/features/users/useUserSettings";
import { palette, radius, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";

const periodOptions = [
  { label: "Weekly", value: "weekly" },
  { label: "Monthly", value: "monthly" },
  { label: "Custom", value: "custom" }
] as const;

function parseAmount(value: string) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function getCurrencySymbol(currencyCode: string) {
  try {
    return (
      new Intl.NumberFormat("en-IE", {
        style: "currency",
        currency: currencyCode,
        maximumFractionDigits: 0
      })
        .formatToParts(0)
        .find((part) => part.type === "currency")
        ?.value ?? currencyCode
    );
  } catch {
    return currencyCode;
  }
}

export default function ExpensePlanBuilderScreen() {
  const router = useRouter();
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const profileQuery = useUserProfileQuery();
  const {
    builderDraft,
    createNewPlanDraft,
    updateBuilderDraft,
    updateBuilderLineItem,
    addBuilderLineItem,
    removeBuilderLineItem,
    setSelectionLineItemId,
    saveBuilderDraftAs,
    clearBuilderDraft
  } = useExpensePlanning();
  const [amountInputs, setAmountInputs] = useState<Record<string, string>>({});

  const taxonomyLookup = useMemo(
    () => buildExpensePlanTaxonomyLookup(taxonomyQuery.data?.domains ?? []),
    [taxonomyQuery.data?.domains]
  );
  const preferredCurrency = profileQuery.data?.preferredCurrency ?? "EUR";
  const currencySymbol = useMemo(() => getCurrencySymbol(preferredCurrency), [preferredCurrency]);

  useEffect(() => {
    if (!builderDraft) {
      createNewPlanDraft();
    }
  }, [builderDraft, createNewPlanDraft]);

  useEffect(() => {
    if (!builderDraft) {
      return;
    }

    setAmountInputs(
      builderDraft.lineItems.reduce<Record<string, string>>((accumulator, item) => {
        accumulator[item.id] = item.expectedAmount > 0 ? item.expectedAmount.toFixed(2).replace(/\.00$/, "") : "";
        return accumulator;
      }, {})
    );
  }, [builderDraft?.lineItems]);

  if (!builderDraft) {
    return (
      <PlanningHubScreen title="Plan builder">
        <View />
      </PlanningHubScreen>
    );
  }

  const canSave =
    builderDraft.title.trim().length > 0 &&
    builderDraft.lineItems.some((item) => item.subcategoryId && item.expectedAmount > 0) &&
    builderDraft.lineItems.every((item) => !item.subcategoryId || item.expectedAmount > 0);

  const handleSave = (mode: "drafted" | "scheduled" | "active") => {
    if (!canSave) {
      Alert.alert("Finish the plan", "Add a title and at least one category with an amount before saving.");
      return;
    }

    const nextPlan = saveBuilderDraftAs(mode);
    if (!nextPlan) {
      return;
    }

    router.replace(`/(tabs)/planning/${nextPlan.id}` as never);
  };

  return (
    <PlanningHubScreen title="Plan builder">
      <View style={styles.pageContent}>
        <GlassCard style={styles.sectionCard}>
          <TextField
            label="Plan title"
            value={builderDraft.title}
            onChangeText={(value) => updateBuilderDraft({ title: value })}
            placeholder="Monthly household runway"
          />

          <PlanningHubSegmentedControl
            label="Period type"
            value={builderDraft.periodType}
            options={[...periodOptions]}
            onChange={(value) => updateBuilderDraft({ periodType: value })}
          />

          <View style={styles.rowFields}>
            <View style={styles.rowFieldItem}>
              <TextField
                label="Start date"
                value={builderDraft.startDate}
                onChangeText={(value) => updateBuilderDraft({ startDate: value })}
                placeholder="YYYY-MM-DD"
              />
            </View>
            <View style={styles.rowFieldItem}>
              <TextField
                label="End date"
                value={builderDraft.endDate}
                onChangeText={(value) => updateBuilderDraft({ endDate: value })}
                placeholder="YYYY-MM-DD"
              />
            </View>
          </View>

          <View style={styles.metaRow}>
            <Pressable
              style={[styles.toggleChip, builderDraft.isRecurring ? styles.toggleChipActive : null]}
              onPress={() =>
                updateBuilderDraft({
                  isRecurring: !builderDraft.isRecurring,
                  recurrenceRule: !builderDraft.isRecurring ? "Monthly" : null
                })
              }
            >
              <Text style={styles.toggleChipLabel}>Recurring</Text>
            </Pressable>
            <Pressable
              style={[styles.toggleChip, builderDraft.isTemplate ? styles.toggleChipActive : null]}
              onPress={() => updateBuilderDraft({ isTemplate: !builderDraft.isTemplate })}
            >
              <Text style={styles.toggleChipLabel}>Template</Text>
            </Pressable>
            <Pressable
              style={[styles.toggleChip, builderDraft.isShared ? styles.toggleChipActive : null]}
              onPress={() => updateBuilderDraft({ isShared: !builderDraft.isShared })}
            >
              <Text style={styles.toggleChipLabel}>Shared</Text>
            </Pressable>
          </View>

          {builderDraft.isRecurring ? (
            <TextField
              label="Recurrence"
              value={builderDraft.recurrenceRule ?? ""}
              onChangeText={(value) => updateBuilderDraft({ recurrenceRule: value })}
              placeholder="Monthly"
            />
          ) : null}
        </GlassCard>

        <View style={styles.sectionWrap}>
          <View style={styles.sectionHeaderRow}>
            <View>
              <Text style={styles.sectionTitle}>Plan your expenses</Text>
            </View>
          </View>

          <View style={styles.lineItemList}>
            {builderDraft.lineItems.map((lineItem, index) => {
              const taxonomy = lineItem.subcategoryId ? taxonomyLookup.get(lineItem.subcategoryId) : null;
              return (
                <GlassCard key={lineItem.id} style={styles.lineItemCard}>
                  <View style={styles.lineItemHeaderRow}>
                    <Text style={styles.lineItemIndex}>Line {index + 1}</Text>
                    <Pressable style={styles.removeButton} onPress={() => removeBuilderLineItem(lineItem.id)}>
                      <Ionicons name="trash-outline" size={16} color={palette.textSecondary} />
                    </Pressable>
                  </View>

                  <Pressable
                    style={styles.selectCategoryButton}
                    onPress={() => {
                      setSelectionLineItemId(lineItem.id);
                      router.push({
                        pathname: "/(tabs)/planning/categories",
                        params: { selectionMode: "true", lineItemId: lineItem.id }
                      });
                    }}
                  >
                    <View style={styles.selectCategoryIconWrap}>
                      <Ionicons name="grid-outline" size={18} color={palette.textPrimary} />
                    </View>
                    <View style={styles.selectCategoryTextWrap}>
                      <Text style={styles.selectCategoryLabel}>{taxonomy?.subcategoryName ?? "Select category"}</Text>
                      <Text style={styles.selectCategoryMeta}>
                        {taxonomy ? `${taxonomy.categoryName} • ${taxonomy.domainName}` : "Canonical taxonomy selection only"}
                      </Text>
                    </View>
                    <Ionicons name="chevron-forward" size={18} color={palette.textSecondary} />
                  </Pressable>

                  <View style={styles.rowFields}>
                    <View style={styles.rowFieldItem}>
                      <TextField
                        label="Expected amount"
                        value={amountInputs[lineItem.id] ?? ""}
                        onChangeText={(value) => {
                          setAmountInputs((current) => ({ ...current, [lineItem.id]: value }));
                          updateBuilderLineItem(lineItem.id, { expectedAmount: parseAmount(value) });
                        }}
                        keyboardType="decimal-pad"
                        placeholder="0.00"
                        leadingText={currencySymbol}
                      />
                    </View>
                    <View style={styles.rowFieldItem}>
                      <TextField
                        label="Notes"
                        value={lineItem.notes}
                        onChangeText={(value) => updateBuilderLineItem(lineItem.id, { notes: value })}
                        placeholder="Optional"
                      />
                    </View>
                  </View>
                </GlassCard>
              );
            })}
          </View>

          <Pressable style={styles.addLineTextAction} onPress={addBuilderLineItem}>
            <Text style={styles.addLineTextLabel}>+ Add a new line</Text>
          </Pressable>
        </View>

        <View style={styles.actionSection}>
          <View style={styles.actionButtonsRow}>
            <Pressable style={[styles.actionButton, styles.activateButton]} onPress={() => handleSave("active")}>
              <Text style={styles.actionButtonLabel}>Activate plan</Text>
            </Pressable>
            <Pressable
              style={[styles.actionButton, styles.scheduleButton]}
              onPress={() => handleSave("scheduled")}
            >
              <Text style={styles.actionButtonLabel}>Schedule plan</Text>
            </Pressable>
            <Pressable
              style={[styles.actionButton, styles.saveDraftButton]}
              onPress={() => handleSave("drafted")}
            >
              <Text style={styles.actionButtonLabel}>Save draft</Text>
            </Pressable>
          </View>
          <Pressable
            style={styles.cancelBuilderButton}
            onPress={() => {
              clearBuilderDraft();
              router.back();
            }}
          >
            <Text style={styles.cancelBuilderLabel}>Cancel</Text>
          </Pressable>
        </View>
      </View>
    </PlanningHubScreen>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  pageContent: {
    width: "100%",
    gap: spacing[20],
    overflow: "hidden"
  },
  sectionWrap: {
    gap: spacing[12]
  },
  sectionCard: {
    gap: spacing[16]
  },
  rowFields: {
    flexDirection: "row",
    gap: spacing[12]
  },
  rowFieldItem: {
    flex: 1
  },
  metaRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  toggleChip: {
    minHeight: 38,
    paddingHorizontal: spacing[16],
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    alignItems: "center",
    justifyContent: "center"
  },
  toggleChipActive: {
    backgroundColor: "rgba(242,140,40,0.24)",
    borderColor: "rgba(242,140,40,0.36)"
  },
  toggleChipLabel: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "600"
  },
  sectionHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12]
  },
  sectionTitle: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "600",
    letterSpacing: 1.1,
    textTransform: "uppercase"
  },
  addLineTextAction: {
    alignSelf: "center",
    paddingVertical: spacing[4]
  },
  addLineTextLabel: {
    color: palette.primaryGlow,
    ...typography.caption,
    fontWeight: "600",
    textShadowColor: "rgba(242,140,40,0.28)",
    textShadowOffset: { width: 0, height: 0 },
    textShadowRadius: 8
  },
  lineItemList: {
    gap: spacing[12]
  },
  lineItemCard: {
    gap: spacing[12]
  },
  lineItemHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center"
  },
  lineItemIndex: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "600"
  },
  removeButton: {
    width: 32,
    height: 32,
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: surfaces.field
  },
  selectCategoryButton: {
    minHeight: 62,
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[16],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12]
  },
  selectCategoryIconWrap: {
    width: 38,
    height: 38,
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(242,140,40,0.24)"
  },
  selectCategoryTextWrap: {
    flex: 1,
    gap: 2
  },
  selectCategoryLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
  },
  selectCategoryMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  actionSection: {
    gap: spacing[12],
    marginTop: spacing[12]
  },
  actionButtonsRow: {
    flexDirection: "row",
    gap: spacing[8]
  },
  actionButton: {
    flex: 1,
    minHeight: 40,
    borderRadius: radius.medium,
    borderWidth: 1,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[8]
  },
  activateButton: {
    backgroundColor: "rgba(53, 162, 107, 0.2)",
    borderColor: "rgba(92, 211, 144, 0.38)"
  },
  scheduleButton: {
    backgroundColor: "rgba(217, 181, 59, 0.2)",
    borderColor: "rgba(239, 212, 102, 0.38)"
  },
  saveDraftButton: {
    backgroundColor: "rgba(214, 124, 57, 0.2)",
    borderColor: "rgba(255, 166, 98, 0.38)"
  },
  actionButtonLabel: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "600"
  },
  cancelBuilderButton: {
    minHeight: 42,
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    alignItems: "center",
    justifyContent: "center"
  },
  cancelBuilderLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
  }
}));



