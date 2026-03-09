import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import {
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { SelectField } from "../../src/components/ui/SelectField";
import { SkeletonBlock } from "../../src/components/ui/SkeletonBlock";
import { TextField } from "../../src/components/ui/TextField";
import { useTransactionDetailQuery } from "../../src/features/transactions/useTransactions";
import {
  type PlannerCategory,
  usePlannerStore
} from "../../src/providers/PlannerProvider";
import { palette, spacing, surfaces, typography } from "../../src/theme/tokens";

function defaultCategoryForDirection(direction: "Income" | "Expense" | undefined) {
  return direction === "Income" ? "Salary" : "Groceries";
}

export default function TransactionContextModalScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ transactionId?: string }>();
  const transactionId = params.transactionId ?? "";
  const transactionQuery = useTransactionDetailQuery(transactionId);
  const plannerStore = usePlannerStore();

  const existing = useMemo(
    () => (transactionId ? plannerStore.annotations[transactionId] : undefined),
    [plannerStore.annotations, transactionId]
  );

  const [category, setCategory] = useState<string>("Other");
  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [merchant, setMerchant] = useState("");
  const [customCategory, setCustomCategory] = useState("");
  const direction = transactionQuery.data?.direction;
  const categoryDirection = direction === "Income" ? "Income" : "Expense";
  const categoryOptions = plannerStore.categoryCatalog[categoryDirection];

  useEffect(() => {
    if (!existing) {
      setCategory(defaultCategoryForDirection(direction));
      setCustomCategory("");
      return;
    }

    const allowed = new Set<string>(categoryOptions);
    const hasDirectMatch =
      existing.category && allowed.has(existing.category);
    const nextCategory =
      hasDirectMatch
        ? existing.category ?? defaultCategoryForDirection(direction)
        : existing.category
          ? "Other"
          : defaultCategoryForDirection(direction);

    setCategory(nextCategory);
    setCustomCategory(hasDirectMatch ? "" : (existing.category ?? ""));
    setReason(existing.reason);
    setNotes(existing.notes);
    setMerchant(existing.merchant);
  }, [categoryOptions, direction, existing]);

  if (!transactionId) {
    return (
      <SafeAreaView style={styles.screen}>
        <View style={styles.sheetCard}>
          <ErrorState title="Transaction missing" message="No transaction ID was provided." />
        </View>
      </SafeAreaView>
    );
  }

  const isLoading = transactionQuery.isLoading && !transactionQuery.data;

  const saveContext = () => {
    const resolvedCategory =
      category === "Other" && customCategory.trim()
        ? plannerStore.resolveCategory(categoryDirection, customCategory)
        : plannerStore.resolveCategory(categoryDirection, category);

    plannerStore.saveAnnotation({
      transactionId,
      category: (resolvedCategory as PlannerCategory) ?? null,
      type: null,
      reason: reason.trim(),
      notes: notes.trim(),
      merchant: merchant.trim(),
      direction: categoryDirection
    });
    router.back();
  };

  return (
    <SafeAreaView style={styles.screen} edges={["top", "left", "right", "bottom"]}>
      <Pressable style={styles.backdrop} onPress={() => router.back()} />
      <KeyboardAvoidingView
        style={styles.sheetWrap}
        behavior={Platform.OS === "ios" ? "padding" : undefined}
      >
        <View style={styles.sheetCard}>
          <View style={styles.header}>
            <Text style={styles.title}>Transaction context</Text>
            <Ionicons
              name="close"
              size={22}
              color={palette.textSecondary}
              onPress={() => router.back()}
            />
          </View>

          {isLoading ? (
            <View style={styles.loadingWrap}>
              <SkeletonBlock style={styles.loadingField} />
              <SkeletonBlock style={styles.loadingField} />
              <SkeletonBlock style={styles.loadingField} />
            </View>
          ) : transactionQuery.isError ? (
            <ErrorState
              title="Could not load transaction"
              message={transactionQuery.error.message}
              onRetry={() => {
                void transactionQuery.refetch();
              }}
            />
          ) : (
            <ScrollView
              contentContainerStyle={styles.formContent}
              showsVerticalScrollIndicator={false}
              keyboardShouldPersistTaps="handled"
            >
              <View style={styles.factCard}>
                <Text style={styles.factTitle}>{transactionQuery.data?.description}</Text>
                <Text style={styles.factMeta}>
                  Keep known facts from the ledger, then add your own planning context.
                </Text>
              </View>

              <SelectField
                label={direction === "Income" ? "Income category" : "Expense category"}
                value={category}
                options={categoryOptions.map((item) => ({ label: item, value: item }))}
                onChange={setCategory}
                compact
              />
              {category === "Other" ? (
                <TextField
                  label="Custom category"
                  value={customCategory}
                  onChangeText={setCustomCategory}
                  placeholder="Type a category name"
                />
              ) : null}

              <TextField
                label="Reason"
                value={reason}
                onChangeText={setReason}
                placeholder="Why this happened"
              />

              <TextField
                label="Merchant/store"
                value={merchant}
                onChangeText={setMerchant}
                placeholder="Store or provider"
              />

              <TextField
                label="Notes"
                value={notes}
                onChangeText={setNotes}
                placeholder="Extra context for future planning"
                multiline
                numberOfLines={3}
              />

              <View style={styles.actions}>
                <SecondaryButton label="Cancel" onPress={() => router.back()} />
                <PrimaryButton label="Save context" onPress={saveContext} />
              </View>
            </ScrollView>
          )}
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: "transparent",
    justifyContent: "flex-end"
  },
  backdrop: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: "rgba(4, 11, 23, 0.72)"
  },
  sheetWrap: {
    justifyContent: "flex-end"
  },
  sheetCard: {
    maxHeight: "90%",
    backgroundColor: surfaces.sheet,
    borderTopLeftRadius: 24,
    borderTopRightRadius: 24,
    borderWidth: 1,
    borderColor: palette.border,
    paddingHorizontal: spacing[16],
    paddingTop: spacing[16],
    paddingBottom: spacing[20],
    gap: spacing[12]
  },
  header: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center"
  },
  title: {
    color: palette.textPrimary,
    ...typography.title2
  },
  formContent: {
    gap: 10,
    paddingBottom: spacing[12]
  },
  factCard: {
    borderRadius: 14,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(17,39,66,0.7)",
    padding: spacing[12],
    gap: spacing[8]
  },
  factTitle: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  factMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  actions: {
    marginTop: spacing[8],
    gap: spacing[12]
  },
  loadingWrap: {
    gap: spacing[12]
  },
  loadingField: {
    height: 56,
    borderRadius: 14
  }
});

