import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { Alert, Pressable, StyleSheet, Text, View } from "react-native";
import { ExpenseTrackerMiniAppScreen } from "../../../../../src/components/expenseTracker/ExpenseTrackerMiniAppScreen";
import { EmptyState } from "../../../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../../../src/components/ui/PrimaryButton";
import { useExpensePlanning } from "../../../../../src/features/expenseTracker/ExpensePlanningProvider";
import { useExpenseTrackerEntriesQuery, useExpenseTrackerTaxonomyQuery } from "../../../../../src/features/expenseTracker/useExpenseTracker";
import {
  buildExpensePlanComputed,
  buildExpensePlanTaxonomyLookup,
  formatExpensePlanPeriod,
  getExpensePlanStatusMeta
} from "../../../../../src/features/expenseTracker/expensePlanningUtils";
import { getExpenseTrackerSubcategoryVisual } from "../../../../../src/features/expenseTracker/expenseTrackerModels";
import { palette, spacing, typography } from "../../../../../src/theme/tokens";

function formatAmount(amount: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency
  }).format(amount);
}

export default function ExpensePlanDetailScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ planId?: string }>();
  const planId = typeof params.planId === "string" ? params.planId : "";
  const { getPlanById, startEditingPlan, startDuplicatePlan, cancelScheduledPlan, completePlan } = useExpensePlanning();
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();

  const plan = getPlanById(planId);
  const currency = entriesQuery.data?.[0]?.currency ?? "EUR";
  const taxonomyLookup = buildExpensePlanTaxonomyLookup(taxonomyQuery.data?.domains ?? []);

  if (!plan) {
    return (
      <ExpenseTrackerMiniAppScreen title="Plan detail">
        <EmptyState title="Plan not found" message="This plan could not be loaded." />
      </ExpenseTrackerMiniAppScreen>
    );
  }

  const computed = buildExpensePlanComputed(plan, entriesQuery.data ?? [], taxonomyLookup);
  const statusMeta = getExpensePlanStatusMeta(plan.status);

  const handleEdit = () => {
    startEditingPlan(plan.id);
    router.push("/(tabs)/planner/expense-tracker/plan-builder" as never);
  };

  return (
    <ExpenseTrackerMiniAppScreen title="Plan detail">
      <GlassCard style={styles.heroCard}>
        <View style={styles.heroHeaderRow}>
          <View style={styles.heroTextWrap}>
            <Text style={styles.planTitle}>{plan.title}</Text>
            <Text style={styles.planMeta}>{formatExpensePlanPeriod(plan.startDate, plan.endDate)} • {plan.creatorTag}</Text>
          </View>
          <View style={[styles.statusPill, { backgroundColor: statusMeta.tint, borderColor: `${statusMeta.color}55` }]}>
            <Text style={[styles.statusPillLabel, { color: statusMeta.color }]}>{statusMeta.label}</Text>
          </View>
        </View>

        <View style={styles.metadataRow}>
          {plan.isRecurring ? <Text style={styles.metadataChip}>Recurring {plan.recurrenceRule ?? ""}</Text> : null}
          {plan.isTemplate ? <Text style={styles.metadataChip}>Template</Text> : null}
          {plan.isShared ? <Text style={styles.metadataChip}>Shared</Text> : null}
          <Text style={styles.metadataChip}>Creator {plan.creatorTag}</Text>
        </View>

        <View style={styles.metricsGrid}>
          <View style={styles.metricBlock}>
            <Text style={styles.metricLabel}>Expected total</Text>
            <Text style={styles.metricValue}>{formatAmount(computed.expectedTotal, currency)}</Text>
          </View>
          <View style={styles.metricBlock}>
            <Text style={styles.metricLabel}>Actual total</Text>
            <Text style={styles.metricValue}>{formatAmount(computed.actualTotal, currency)}</Text>
          </View>
          <View style={styles.metricBlock}>
            <Text style={styles.metricLabel}>Difference</Text>
            <Text style={[styles.metricValue, computed.varianceAmount > 0 ? styles.metricNegative : styles.metricPositive]}>
              {computed.varianceAmount >= 0 ? "+" : "-"}{formatAmount(Math.abs(computed.varianceAmount), currency)}
            </Text>
          </View>
          <View style={styles.metricBlock}>
            <Text style={styles.metricLabel}>Transactions</Text>
            <Text style={styles.metricValue}>{computed.transactionCount}</Text>
          </View>
        </View>
      </GlassCard>

      <GlassCard style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Category breakdown</Text>
        <View style={styles.lineItemList}>
          {computed.lineItems.map((lineItem) => {
            const visuals = getExpenseTrackerSubcategoryVisual({
              domainId: lineItem.domainId,
              categoryId: lineItem.categoryId,
              subcategoryId: lineItem.subcategoryId,
              subcategoryName: lineItem.subcategoryName
            });
            return (
              <View key={lineItem.id} style={styles.lineItemRow}>
                <View style={[styles.lineItemIconWrap, { backgroundColor: `${visuals.color}22` }]}>
                  <Ionicons name={visuals.icon as keyof typeof Ionicons.glyphMap} size={18} color={visuals.color} />
                </View>
                <View style={styles.lineItemTextWrap}>
                  <Text style={styles.lineItemTitle}>{lineItem.subcategoryName}</Text>
                  <Text style={styles.lineItemMeta}>{lineItem.categoryName} • {lineItem.domainName}</Text>
                </View>
                <View style={styles.lineItemAmounts}>
                  <Text style={styles.lineItemAmount}>Planned {formatAmount(lineItem.expectedAmount, currency)}</Text>
                  <Text style={[styles.lineItemVariance, lineItem.varianceAmount > 0 ? styles.metricNegative : styles.metricPositive]}>
                    Actual {formatAmount(lineItem.actualAmount, currency)}
                  </Text>
                </View>
              </View>
            );
          })}

          {plan.status === "active" && computed.unexpectedCategories.length > 0 ? (
            <>
              <Text style={styles.unexpectedSpacerLabel}>Unexpected categories</Text>
              {computed.unexpectedCategories.map((item) => {
                const visuals = getExpenseTrackerSubcategoryVisual({
                  domainId: item.domainId,
                  categoryId: item.categoryId,
                  subcategoryId: item.subcategoryId,
                  subcategoryName: item.subcategoryName
                });

                return (
                  <View key={`${item.subcategoryId ?? item.subcategoryName}`} style={styles.lineItemRow}>
                    <View style={[styles.lineItemIconWrap, { backgroundColor: `${visuals.color}22` }]}>
                      <Ionicons name={visuals.icon as keyof typeof Ionicons.glyphMap} size={18} color={visuals.color} />
                    </View>
                    <View style={styles.lineItemTextWrap}>
                      <Text style={styles.lineItemTitle}>{item.subcategoryName}</Text>
                      <Text style={styles.lineItemMeta}>{item.categoryName} • {item.domainName}</Text>
                    </View>
                    <View style={styles.lineItemAmounts}>
                      <Text style={styles.lineItemAmount}>Planned {formatAmount(0, currency)}</Text>
                      <Text style={[styles.lineItemVariance, styles.metricNegative]}>
                        Actual {formatAmount(item.totalAmount, currency)}
                      </Text>
                    </View>
                  </View>
                );
              })}
            </>
          ) : null}
        </View>
      </GlassCard>

      <GlassCard style={styles.actionCard}>
        <Text style={styles.sectionTitle}>Actions</Text>
        <Text style={styles.sectionCaption}>Actions change based on the lifecycle state of the plan.</Text>
        {plan.status === "active" ? (
          <View style={styles.actionButtons}>
            <PrimaryButton label="Edit plan" onPress={handleEdit} />
            <PrimaryButton label="Share plan" onPress={() => router.push({ pathname: "/(tabs)/planner/expense-tracker/community/publish", params: { planId: plan.id } } as never)} />
            <PrimaryButton
              label="Complete plan"
              onPress={() => {
                completePlan(plan.id);
                Alert.alert("Plan completed", "This plan is now locked and view-only.");
              }}
            />
          </View>
        ) : null}
        {plan.status === "scheduled" ? (
          <View style={styles.actionButtons}>
            <PrimaryButton label="Edit plan" onPress={handleEdit} />
            <PrimaryButton label="Reschedule plan" onPress={handleEdit} />
            <PrimaryButton
              label="Cancel plan"
              onPress={() => {
                cancelScheduledPlan(plan.id);
                router.replace("/(tabs)/planner/expense-tracker/overview" as never);
              }}
            />
          </View>
        ) : null}
        {plan.status === "drafted" ? (
          <View style={styles.actionButtons}>
            <PrimaryButton label="Edit draft" onPress={handleEdit} />
            <Text style={styles.lockedNote}>This draft is not active yet, so nothing has started tracking against live spend.</Text>
          </View>
        ) : null}
        {plan.status === "completed" ? (
          <View style={styles.actionButtons}>
            <PrimaryButton
              label="Reuse as new plan"
              onPress={() => {
                startDuplicatePlan(plan.id);
                router.push("/(tabs)/planner/expense-tracker/plan-builder" as never);
              }}
            />
            <PrimaryButton label="Share plan" onPress={() => router.push({ pathname: "/(tabs)/planner/expense-tracker/community/publish", params: { planId: plan.id } } as never)} />
            <Text style={styles.lockedNote}>Completed plans stay locked so their original planned and actual values remain preserved.</Text>
          </View>
        ) : null}
      </GlassCard>
    </ExpenseTrackerMiniAppScreen>
  );
}

const styles = StyleSheet.create({
  heroCard: {
    gap: spacing[16]
  },
  heroHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: spacing[12]
  },
  heroTextWrap: {
    flex: 1,
    gap: 4
  },
  planTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  planMeta: {
    color: palette.textSecondary,
    ...typography.body2
  },
  statusPill: {
    minHeight: 30,
    paddingHorizontal: spacing[12],
    borderRadius: 999,
    borderWidth: 1,
    alignItems: "center",
    justifyContent: "center"
  },
  statusPillLabel: {
    ...typography.caption,
    fontWeight: "700"
  },
  metadataRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  metadataChip: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "700"
  },
  metricsGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[12]
  },
  metricBlock: {
    width: "47%",
    gap: 4
  },
  metricLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  metricValue: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  metricPositive: {
    color: palette.success
  },
  metricNegative: {
    color: palette.negative
  },
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
  lineItemList: {
    gap: spacing[12]
  },
  lineItemRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12]
  },
  lineItemIconWrap: {
    width: 42,
    height: 42,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center"
  },
  lineItemTextWrap: {
    flex: 1,
    gap: 2
  },
  lineItemTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  lineItemMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  lineItemAmounts: {
    alignItems: "flex-end",
    gap: 2
  },
  lineItemAmount: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  lineItemVariance: {
    ...typography.caption,
    fontWeight: "700"
  },
  unexpectedSpacerLabel: {
    marginTop: spacing[8],
    color: palette.textSecondary,
    ...typography.caption
  },
  actionCard: {
    gap: spacing[16]
  },
  actionButtons: {
    gap: spacing[12]
  },
  lockedNote: {
    color: palette.textSecondary,
    ...typography.body2
  }
});

