import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useMemo, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { IconButton } from "../../../src/components/ui/IconButton";
import { AnimatedCurrencyText } from "../../../src/components/ui/AnimatedCurrencyText";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { SectionHeader } from "../../../src/components/ui/SectionHeader";
import { SelectField } from "../../../src/components/ui/SelectField";
import { TextField } from "../../../src/components/ui/TextField";
import {
  getEssentialTransactions,
  getNecessitiesSummary
} from "../../../src/features/planner/necessityMetrics";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import {
  type PlannerCategory,
  type NecessityFrequency,
  type NecessityItem,
  type NecessityType,
  usePlannerStore
} from "../../../src/providers/PlannerProvider";
import { formatCurrency } from "../../../src/lib/format";
import { layout, palette, spacing, typography } from "../../../src/theme/tokens";

const frequencyOptions: { label: string; value: NecessityFrequency }[] = [
  { label: "Weekly", value: "Weekly" },
  { label: "Monthly", value: "Monthly" },
  { label: "Yearly", value: "Yearly" },
  { label: "One off", value: "OneOff" }
];

const typeOptions: { label: string; value: NecessityType }[] = [
  { label: "Essential", value: "Essential" },
  { label: "Optional", value: "Optional" }
];

const yesNoOptions = [
  { label: "Yes", value: "yes" },
  { label: "No", value: "no" }
];

export default function PlannerNecessitiesScreen() {
  const router = useRouter();
  const plannerStore = usePlannerStore();
  const transactionsQuery = useTransactionsQuery();

  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [category, setCategory] = useState<string>(plannerStore.categoryCatalog.Expense[0] ?? "Groceries");
  const [estimatedMonthlyCost, setEstimatedMonthlyCost] = useState("");
  const [frequency, setFrequency] = useState<NecessityFrequency>("Monthly");
  const [reasonNotes, setReasonNotes] = useState("");
  const [merchant, setMerchant] = useState("");
  const [type, setType] = useState<NecessityType>("Essential");
  const [recurring, setRecurring] = useState("yes");

  const expenseCategories = plannerStore.categoryCatalog.Expense;
  const essentialTransactions = useMemo(
    () => getEssentialTransactions(transactionsQuery.data ?? [], plannerStore.annotations),
    [plannerStore.annotations, transactionsQuery.data]
  );
  const summary = useMemo(
    () =>
      getNecessitiesSummary({
        necessities: plannerStore.necessities,
        essentialTransactions,
        annotations: plannerStore.annotations
      }),
    [essentialTransactions, plannerStore.annotations, plannerStore.necessities]
  );

  const totalBaseline = useMemo(
    () => summary.total,
    [summary.total]
  );
  const trackedCount = plannerStore.necessities.length + essentialTransactions.length;

  const startAdd = () => {
    setEditingId(null);
    setName("");
    setCategory(expenseCategories[0] ?? "Groceries");
    setEstimatedMonthlyCost("");
    setFrequency("Monthly");
    setReasonNotes("");
    setMerchant("");
    setType("Essential");
    setRecurring("yes");
    setIsFormOpen(true);
  };

  const startEdit = (item: NecessityItem) => {
    setEditingId(item.id);
    setName(item.name);
    setCategory(item.category);
    setEstimatedMonthlyCost(String(item.estimatedMonthlyCost));
    setFrequency(item.frequency);
    setReasonNotes(item.reasonNotes);
    setMerchant(item.merchant);
    setType(item.type);
    setRecurring(item.isRecurring ? "yes" : "no");
    setIsFormOpen(true);
  };

  const closeForm = () => {
    setIsFormOpen(false);
    setEditingId(null);
  };

  const handleSave = () => {
    const parsedCost = Number(estimatedMonthlyCost);
    if (!name.trim() || !Number.isFinite(parsedCost) || parsedCost <= 0) {
      return;
    }

    const payload = {
      name: name.trim(),
      category: (category as PlannerCategory),
      estimatedMonthlyCost: Number(parsedCost.toFixed(2)),
      frequency,
      reasonNotes: reasonNotes.trim(),
      merchant: merchant.trim(),
      type,
      isRecurring: recurring === "yes"
    };

    if (editingId) {
      plannerStore.updateNecessity(editingId, payload);
    } else {
      plannerStore.addNecessity(payload);
    }

    closeForm();
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>Necessities</Text>
        <View style={{ width: 40 }} />
      </View>

      <GlassCard style={styles.summaryCard}>
        <AnimatedCurrencyText
          value={totalBaseline}
          currency="EUR"
          style={styles.baselineValue}
          baseColor={palette.textPrimary}
        />
        <Text style={styles.baselineMeta}>Estimated monthly baseline</Text>
        <Text style={styles.summaryHint}>
          {summary.essentialsCount} essential tracked | {summary.optionalCount} optional tracked
        </Text>
        <View style={styles.addButtonWrap}>
          <PrimaryButton
            label="Add necessity"
            onPress={startAdd}
          />
        </View>
      </GlassCard>

      {isFormOpen ? (
        <>
          <SectionHeader title={editingId ? "Edit necessity" : "Add necessity"} />
          <GlassCard style={styles.formCard}>
            <TextField label="Name" value={name} onChangeText={setName} placeholder="Rent" />
            <SelectField
              label="Category"
              value={category}
              options={expenseCategories.map((item) => ({ label: item, value: item }))}
              onChange={setCategory}
            />
            <TextField
              label="Estimated monthly cost"
              value={estimatedMonthlyCost}
              onChangeText={setEstimatedMonthlyCost}
              keyboardType="decimal-pad"
              placeholder="1200.00"
            />
            <SelectField
              label="Frequency"
              value={frequency}
              options={frequencyOptions}
              onChange={(value) => setFrequency(value as NecessityFrequency)}
            />
            <SelectField
              label="Essential or optional"
              value={type}
              options={typeOptions}
              onChange={(value) => setType(value as NecessityType)}
            />
            <SelectField
              label="Recurring"
              value={recurring}
              options={yesNoOptions}
              onChange={setRecurring}
            />
            <TextField
              label="Reason/notes"
              value={reasonNotes}
              onChangeText={setReasonNotes}
              placeholder="Primary household bill"
            />
            <TextField
              label="Merchant/store"
              value={merchant}
              onChangeText={setMerchant}
              placeholder="Provider or store"
            />
            <View style={styles.formActions}>
              <SecondaryButton label="Cancel" onPress={closeForm} />
              <PrimaryButton label={editingId ? "Save changes" : "Save necessity"} onPress={handleSave} />
            </View>
          </GlassCard>
        </>
      ) : null}

      <SectionHeader title="Tracked necessities" />
      {trackedCount === 0 ? (
        <EmptyState
          title="No necessities yet"
          message="Mark expense transactions as Essential or add a manual necessity."
          actionLabel="Add necessity"
          onActionPress={startAdd}
        />
      ) : (
        <View style={styles.listWrap}>
          {plannerStore.necessities.map((item) => (
            <GlassCard key={item.id} style={styles.itemCard}>
              <View style={styles.itemHeader}>
                <View style={styles.itemTitleWrap}>
                  <Text style={styles.itemTitle}>{item.name}</Text>
                  <Text style={styles.itemMeta}>
                    {item.category} | {item.frequency} | {item.isRecurring ? "Recurring" : "One-off"}
                  </Text>
                </View>
                <Text style={[styles.typeBadge, item.type === "Essential" ? styles.badgeEssential : styles.badgeOptional]}>
                  {item.type}
                </Text>
              </View>
              <Text style={styles.itemCost}>{formatCurrency(item.estimatedMonthlyCost, "EUR")}</Text>
              {item.reasonNotes ? <Text style={styles.itemBody}>{item.reasonNotes}</Text> : null}
              {item.merchant ? <Text style={styles.itemBody}>Merchant: {item.merchant}</Text> : null}
              <View style={styles.itemActions}>
                <IconButton
                  onPress={() => startEdit(item)}
                  icon={<Ionicons name="create-outline" size={16} color={palette.textPrimary} />}
                />
                <IconButton
                  onPress={() => plannerStore.removeNecessity(item.id)}
                  icon={<Ionicons name="trash-outline" size={16} color={palette.negative} />}
                />
              </View>
            </GlassCard>
          ))}
          {essentialTransactions.map((item) => (
            <GlassCard key={item.transactionId} style={styles.itemCard}>
              <Text style={styles.itemTitle}>{item.label}</Text>
              <Text style={styles.itemMeta}>
                {item.accountName} | {item.category}
              </Text>
              <Text style={styles.itemCost}>{formatCurrency(item.amount, "EUR")}</Text>
            </GlassCard>
          ))}
        </View>
      )}
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: layout.screenTopPadding
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  headerTitle: {
    color: palette.textPrimary,
    ...typography.sectionTitle
  },
  summaryCard: {
    gap: spacing[12]
  },
  baselineValue: {
    color: palette.textPrimary,
    ...typography.title
  },
  baselineMeta: {
    color: palette.textSecondary,
    ...typography.body2
  },
  summaryHint: {
    color: palette.textSecondary,
    ...typography.caption
  },
  addButtonWrap: {
    marginTop: spacing[4],
    alignSelf: "center",
    minWidth: 190
  },
  formCard: {
    gap: spacing[12]
  },
  formActions: {
    marginTop: spacing[4],
    gap: spacing[8]
  },
  listWrap: {
    gap: spacing[12]
  },
  itemCard: {
    gap: spacing[12]
  },
  itemHeader: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  itemTitleWrap: {
    flex: 1,
    gap: spacing[4]
  },
  itemTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  itemMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  itemCost: {
    color: palette.textPrimary,
    ...typography.sectionTitle
  },
  itemBody: {
    color: palette.textSecondary,
    ...typography.body2
  },
  typeBadge: {
    borderRadius: 999,
    paddingHorizontal: spacing[8],
    paddingVertical: spacing[4],
    ...typography.caption,
    fontWeight: "700"
  },
  badgeEssential: {
    backgroundColor: "rgba(24,195,126,0.2)",
    color: palette.success
  },
  badgeOptional: {
    backgroundColor: "rgba(244,91,105,0.2)",
    color: palette.negative
  },
  itemActions: {
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: spacing[8]
  }
});

