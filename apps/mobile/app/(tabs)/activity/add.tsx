import { Ionicons } from "@expo/vector-icons";
import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Pressable, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { SelectField } from "../../../src/components/ui/SelectField";
import { SkeletonBlock } from "../../../src/components/ui/SkeletonBlock";
import { TextField } from "../../../src/components/ui/TextField";
import { useAccountsQuery } from "../../../src/features/accounts/useAccounts";
import { buildConnectBankRoute } from "../../../src/features/banking/bankingLinking";
import { useConnectBankCtaLabels } from "../../../src/features/banking/connectBankCta";
import { consumePendingActivityAddTransactionSubcategorySelection } from "../../../src/features/expenseTracker/categoryPickerBridge";
import {
  flattenVisibleExpenseTaxonomy
} from "../../../src/features/expenseTracker/expenseTrackerModels";
import { useExpenseTrackerTaxonomyQuery } from "../../../src/features/expenseTracker/useExpenseTracker";
import { useCreateTransactionMutation } from "../../../src/features/transactions/useTransactions";
import { formatUnknownError } from "../../../src/lib/api/errors";
import { useFeedbackSound } from "../../../src/lib/sound/useFeedbackSound";
import { HeaderShell } from "../../../src/layout/appHeader";
import { usePlannerStore } from "../../../src/providers/PlannerProvider";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";
import type { TransactionDirection } from "../../../src/types/api";

type FormErrors = Partial<
  Record<"description" | "amount" | "bookedDate" | "category", string>
>;

const directionOptions: { label: string; value: TransactionDirection }[] = [
  { label: "Expense", value: "Expense" },
  { label: "Income", value: "Income" }
];

export default function AddTransactionScreen() {
  const router = useRouter();
  const { playSuccess } = useFeedbackSound();
  const plannerStore = usePlannerStore();
  const accountsQuery = useAccountsQuery();
  const connectBankCta = useConnectBankCtaLabels();
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const createMutation = useCreateTransactionMutation();

  const [accountId, setAccountId] = useState("");
  const [description, setDescription] = useState("");
  const [amount, setAmount] = useState("");
  const [direction, setDirection] = useState<TransactionDirection>("Expense");
  const [selectedSubcategoryId, setSelectedSubcategoryId] = useState<number | null>(null);
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

  const subcategoryLookup = useMemo(() => {
    const visibleDomains = (taxonomyQuery.data?.domains ?? []).filter(
      (domain) => domain.isUserSelectable && !domain.isSystemDomain && domain.isActive
    );
    const flattened = flattenVisibleExpenseTaxonomy(visibleDomains);
    return new Map(flattened.map((entry) => [entry.subcategory.id, entry] as const));
  }, [taxonomyQuery.data?.domains]);

  const selectedCategory = selectedSubcategoryId
    ? subcategoryLookup.get(selectedSubcategoryId) ?? null
    : null;

  useEffect(() => {
    if (!accountId && accountOptions.length > 0) {
      setAccountId(accountOptions[0].value);
    }
  }, [accountId, accountOptions]);

  useFocusEffect(
    useCallback(() => {
      const selected = consumePendingActivityAddTransactionSubcategorySelection();
      if (selected) {
        setSelectedSubcategoryId(selected);
        setErrors((current) => ({
          ...current,
          category: undefined
        }));
      }
      return undefined;
    }, [])
  );

  useEffect(() => {
    if (!selectedSubcategoryId) {
      return;
    }

    const currentTaxonomyEntry = subcategoryLookup.get(selectedSubcategoryId);
    if (!currentTaxonomyEntry) {
      setSelectedSubcategoryId(null);
    }
  }, [selectedSubcategoryId, subcategoryLookup]);

  const isLoadingOptions = accountsQuery.isLoading && !accountsQuery.data;
  const hasNoAccounts = !isLoadingOptions && (accountsQuery.data?.length ?? 0) === 0;

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

    if (!selectedSubcategoryId) {
      nextErrors.category = "Select a category.";
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const handleSubmit = async () => {
    if (!validate()) {
      return;
    }

    const selectedAccount = (accountsQuery.data ?? []).find((account) => account.id === accountId);
    const fallbackCategory = direction === "Income" ? "Salary" : "Other";
    const resolvedCategory = selectedCategory
      ? plannerStore.resolveCategory(direction, selectedCategory.subcategory.name)
      : plannerStore.resolveCategory(direction, fallbackCategory);

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
      merchant: description.trim(),
      direction
    });

    playSuccess();
    router.back();
  };

  return (
    <ScreenContainer contentStyle={styles.content}>
      <HeaderShell preset="secondaryDetail" title="Add transaction" />

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
      ) : accountsQuery.isError ? (
        <ErrorState
          title="Could not load form options"
          message={accountsQuery.error.message}
          onRetry={() => {
            void accountsQuery.refetch();
          }}
        />
      ) : hasNoAccounts ? (
        <EmptyState
          title="No connected accounts"
          message="Connect your bank first before adding transactions."
          actionLabel={connectBankCta.primaryLabel}
          onActionPress={() =>
            router.push(
              buildConnectBankRoute({ intent: "new", returnTo: "/(tabs)/activity/add" })
            )
          }
          hideOrb
          centerText
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
            onChange={(value: string) => setDirection(value as TransactionDirection)}
          />

          <View style={styles.fieldWrap}>
            <Text style={styles.fieldLabel}>Category</Text>
            <Pressable
              style={({ pressed }) => [
                styles.categoryPickerButton,
                errors.category ? styles.categoryPickerButtonError : null,
                pressed ? styles.categoryPickerButtonPressed : null
              ]}
              onPress={() =>
                router.push({
                  pathname: "/(tabs)/planning/categories",
                  params: {
                    selectionMode: "true",
                    selectionTarget: "activityAddTransaction"
                  }
                })
              }
            >
              <View style={styles.categoryPickerTextWrap}>
                <Text style={styles.categoryPickerTitle}>
                  {selectedCategory?.subcategory.name ?? "Select category"}
                </Text>
                <Text style={styles.categoryPickerMeta}>
                  {selectedCategory
                    ? `${selectedCategory.category.name} | ${selectedCategory.domain.name}`
                    : "Use the taxonomy picker"}
                </Text>
              </View>
              <Ionicons name="chevron-forward" size={18} color={palette.textSecondary} />
            </Pressable>
            {errors.category ? <Text style={styles.fieldError}>{errors.category}</Text> : null}
          </View>

          <TextField
            label="Booked date (UTC)"
            value={bookedDate}
            onChangeText={setBookedDate}
            placeholder="YYYY-MM-DD"
            error={errors.bookedDate}
          />

          <View style={styles.actions}>
            <PrimaryButton
              label="Add transaction"
              onPress={() => {
                void handleSubmit();
              }}
              isLoading={createMutation.isPending}
            />
            <SecondaryButton label="Cancel" onPress={() => router.back()} />
          </View>
        </>
      )}
    </ScreenContainer>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {
    gap: spacing[16]
  },
  actions: {
    gap: spacing[12],
    marginTop: spacing[4]
  },
  loadingWrap: {
    gap: spacing[12]
  },
  fieldWrap: {
    gap: spacing[8]
  },
  fieldLabel: {
    color: palette.textPrimary,
    ...typography.caption
  },
  fieldError: {
    color: palette.negative,
    ...typography.caption
  },
  categoryPickerButton: {
    minHeight: 56,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: palette.elevatedBackground,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8]
  },
  categoryPickerButtonPressed: {
    opacity: 0.9
  },
  categoryPickerButtonError: {
    borderColor: palette.negative
  },
  categoryPickerTextWrap: {
    flex: 1,
    gap: 2
  },
  categoryPickerTitle: {
    color: palette.textPrimary,
    ...typography.body1
  },
  categoryPickerMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  loadingField: {
    height: 54,
    borderRadius: 6,
    backgroundColor: palette.elevatedBackground
  }
}));

