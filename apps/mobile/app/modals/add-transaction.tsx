import { Ionicons } from "@expo/vector-icons";
import { Redirect, useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { EmptyState } from "../../src/components/ui/EmptyState";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { SelectField } from "../../src/components/ui/SelectField";
import { SkeletonBlock } from "../../src/components/ui/SkeletonBlock";
import { TextField } from "../../src/components/ui/TextField";
import { useAccountsQuery } from "../../src/features/accounts/useAccounts";
import { useCreateTransactionMutation } from "../../src/features/transactions/useTransactions";
import { formatUnknownError } from "../../src/lib/api/errors";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { usePlannerStore } from "../../src/providers/PlannerProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";
import type { TransactionDirection } from "../../src/types/api";

type FormErrors = Partial<
  Record<
    "description" | "amount" | "direction" | "bookedDate" | "customCategory",
    string
  >
>;

const directionOptions: { label: string; value: TransactionDirection }[] = [
  { label: "Expense", value: "Expense" },
  { label: "Income", value: "Income" }
];

function defaultCategoryForDirection(
  direction: TransactionDirection,
  categories: string[]
) {
  if (categories.length === 0) {
    return "Other";
  }

  const firstPreferred = categories.find((item) => item !== "Other");
  return firstPreferred ?? "Other";
}

export default function AddTransactionModalScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ accountId?: string }>();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const { playSuccess } = useFeedbackSound();
  const plannerStore = usePlannerStore();

  const accountsQuery = useAccountsQuery();
  const createMutation = useCreateTransactionMutation();

  const [accountId, setAccountId] = useState(params.accountId ?? "");
  const [description, setDescription] = useState("");
  const [amount, setAmount] = useState("");
  const [direction, setDirection] = useState<TransactionDirection>("Expense");
  const [category, setCategory] = useState("Groceries");
  const [customCategory, setCustomCategory] = useState("");
  const [bookedDate, setBookedDate] = useState(new Date().toISOString().slice(0, 10));
  const [errors, setErrors] = useState<FormErrors>({});

  const accountOptions = useMemo(
    () =>
      (accountsQuery.data ?? []).map((account) => ({
        label: `${account.name} (${account.currency})`,
        value: account.id
      })),
    [accountsQuery.data]
  );

  const categoryOptions = useMemo(() => {
    const options = plannerStore.categoryCatalog[direction] ?? [];
    return options.map((item) => ({ label: item, value: item }));
  }, [direction, plannerStore.categoryCatalog]);

  useEffect(() => {
    if (!accountId && accountOptions.length > 0) {
      setAccountId(accountOptions[0].value);
    }
  }, [accountId, accountOptions]);

  useEffect(() => {
    const available = plannerStore.categoryCatalog[direction] ?? [];
    const defaultCategory = defaultCategoryForDirection(direction, available);
    if (!available.includes(category)) {
      setCategory(defaultCategory);
      setCustomCategory("");
    }
  }, [category, direction, plannerStore.categoryCatalog]);

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={("/login" as never)} />;
  }

  const validate = () => {
    const nextErrors: FormErrors = {};
    const parsedAmount = Number(amount);

    if (!description.trim()) {
      nextErrors.description = "Description is required.";
    }

    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      nextErrors.amount = "Amount must be greater than zero.";
    }

    if (!/^\d{4}-\d{2}-\d{2}$/.test(bookedDate)) {
      nextErrors.bookedDate = "Date must use YYYY-MM-DD format.";
    }

    if (category === "Other" && !customCategory.trim()) {
      nextErrors.customCategory = "Enter a custom category name.";
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const isLoadingOptions = accountsQuery.isLoading && !accountsQuery.data;
  const hasNoAccounts = !isLoadingOptions && (accountsQuery.data?.length ?? 0) === 0;
  const loadError = accountsQuery.error;

  const handleSubmit = async () => {
    if (!validate()) {
      return;
    }

    const selectedAccount = (accountsQuery.data ?? []).find((account) => account.id === accountId);
    const resolvedCategory =
      category === "Other"
        ? plannerStore.resolveCategory(direction, customCategory)
        : plannerStore.resolveCategory(direction, category);

    const created = await createMutation.mutateAsync({
      accountId,
      description: description.trim(),
      amount: Number(Number(amount).toFixed(2)),
      direction,
      currency: selectedAccount?.currency ?? "EUR",
      categoryId: null,
      bookedAtUtc: new Date(`${bookedDate}T12:00:00Z`).toISOString()
    });

    plannerStore.saveAnnotation({
      transactionId: created.id,
      category: resolvedCategory,
      type: null,
      merchant: description.trim(),
      direction
    });

    playSuccess();
    router.back();
  };

  return (
    <ScreenContainer contentStyle={styles.content}>
      <View style={styles.header}>
        <View>
          <Text style={styles.title}>Add a transaction</Text>
        </View>
        <Ionicons
          name="close"
          size={26}
          color={palette.textSecondary}
          onPress={() => router.back()}
        />
      </View>

      {createMutation.isError ? (
        <ErrorState
          title="Could not save transaction"
          message={formatUnknownError(createMutation.error)}
          onRetry={handleSubmit}
          retryLabel="Try again"
        />
      ) : null}

      {isLoadingOptions ? (
        <View style={styles.loadingWrap}>
          <SkeletonBlock style={styles.loadingField} />
          <SkeletonBlock style={styles.loadingField} />
          <SkeletonBlock style={styles.loadingField} />
          <SkeletonBlock style={styles.loadingField} />
        </View>
      ) : loadError ? (
        <ErrorState
          title="Could not load form options"
          message={loadError.message}
          onRetry={() => {
            void accountsQuery.refetch();
          }}
        />
      ) : hasNoAccounts ? (
        <EmptyState
          title="No connected accounts"
          message="Connect your bank first before adding off-book transactions."
          actionLabel="Connect bank"
          onActionPress={() => router.push("/modals/add-account")}
        />
      ) : (
        <>
          <TextField
            label="Description"
            value={description}
            onChangeText={setDescription}
            placeholder="Grocery - SuperValu"
            error={errors.description}
          />

          <TextField
            label="Amount"
            value={amount}
            onChangeText={setAmount}
            placeholder="0.00"
            keyboardType="decimal-pad"
            error={errors.amount}
          />

          <SelectField
            label="Direction"
            value={direction}
            options={directionOptions}
            onChange={(value) => setDirection(value as TransactionDirection)}
            error={errors.direction}
          />

          <SelectField
            label="Category"
            value={category}
            options={categoryOptions}
            onChange={setCategory}
          />

          {category === "Other" ? (
            <TextField
              label="Custom category"
              value={customCategory}
              onChangeText={setCustomCategory}
              placeholder="Type a category name"
              error={errors.customCategory}
            />
          ) : null}

          <TextField
            label="Booked date (UTC)"
            value={bookedDate}
            onChangeText={setBookedDate}
            placeholder="YYYY-MM-DD"
            error={errors.bookedDate}
          />

          <View style={styles.actions}>
            <PrimaryButton
              label="Add the transaction"
              onPress={() => void handleSubmit()}
              isLoading={createMutation.isPending}
            />
            <SecondaryButton label="Cancel" onPress={() => router.back()} />
          </View>
        </>
      )}
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: spacing[32],
    gap: spacing[16]
  },
  header: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center"
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1
  },
  actions: {
    marginTop: spacing[8],
    gap: spacing[12]
  },
  loadingWrap: {
    gap: spacing[8]
  },
  loadingField: {
    height: 54,
    borderRadius: 14
  }
});
