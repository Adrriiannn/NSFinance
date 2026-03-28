import { useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { AmountText } from "../../../src/components/ui/AmountText";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ModalSelectField } from "../../../src/components/ui/ModalSelectField";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { SkeletonBlock } from "../../../src/components/ui/SkeletonBlock";
import { TextField } from "../../../src/components/ui/TextField";
import { useExpenseTrackerTaxonomyQuery } from "../../../src/features/expenseTracker/useExpenseTracker";
import {
  useTransactionDetailQuery,
  useUpdateTransactionMetadataMutation
} from "../../../src/features/transactions/useTransactions";
import { formatUnknownError } from "../../../src/lib/api/errors";
import { formatDate, formatTime } from "../../../src/lib/format";
import { HeaderShell } from "../../../src/layout/appHeader";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";

type FormErrors = Partial<Record<"category" | "reason" | "notes", string>>;

const reasonMaxLength = 140;
const notesMaxLength = 1200;

export default function PlannerTransactionDetailScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id?: string }>();
  const transactionId = params.id ?? "";
  const transactionQuery = useTransactionDetailQuery(transactionId);
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const updateMetadataMutation = useUpdateTransactionMetadataMutation();

  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);
  const [selectedSubcategoryId, setSelectedSubcategoryId] = useState<string | null>(null);
  const [errors, setErrors] = useState<FormErrors>({});

  const visibleDomains = useMemo(
    () =>
      (taxonomyQuery.data?.domains ?? []).filter(
        (domain) => domain.isUserSelectable && !domain.isSystemDomain && domain.isActive
      ),
    [taxonomyQuery.data?.domains]
  );

  const categoriesById = useMemo(() => {
    const map = new Map<
      number,
      {
        id: number;
        name: string;
        domainName: string;
        subcategories: { id: number; name: string }[];
      }
    >();

    visibleDomains.forEach((domain) => {
      domain.categories
        .filter((category) => category.isUserSelectable && category.isActive)
        .forEach((category) => {
          map.set(category.id, {
            id: category.id,
            name: category.name,
            domainName: domain.name,
            subcategories: category.subcategories
              .filter((subcategory) => subcategory.isUserSelectable && subcategory.isActive)
              .map((subcategory) => ({
                id: subcategory.id,
                name: subcategory.name
              }))
          });
        });
    });

    return map;
  }, [visibleDomains]);

  const categoryOptions = useMemo(
    () =>
      [...categoriesById.values()]
        .sort((left, right) => left.name.localeCompare(right.name))
        .map((category) => ({
          value: String(category.id),
          label: `${category.name} | ${category.domainName}`
        })),
    [categoriesById]
  );

  const selectedCategory = selectedCategoryId ? categoriesById.get(Number(selectedCategoryId)) ?? null : null;

  const subcategoryOptions = useMemo(
    () =>
      (selectedCategory?.subcategories ?? [])
        .sort((left, right) => left.name.localeCompare(right.name))
        .map((subcategory) => ({
          value: String(subcategory.id),
          label: subcategory.name
        })),
    [selectedCategory]
  );

  useEffect(() => {
    if (!transactionQuery.data) {
      return;
    }

    setReason(transactionQuery.data.reason ?? "");
    setNotes(transactionQuery.data.notes ?? "");
    setSelectedCategoryId(
      transactionQuery.data.taxonomyCategoryId ? String(transactionQuery.data.taxonomyCategoryId) : null
    );
    setSelectedSubcategoryId(
      transactionQuery.data.taxonomySubcategoryId ? String(transactionQuery.data.taxonomySubcategoryId) : null
    );
    setErrors({});
  }, [transactionQuery.data]);

  useEffect(() => {
    if (!selectedCategoryId || !selectedSubcategoryId) {
      return;
    }

    const validSubcategoryIds = new Set(
      (categoriesById.get(Number(selectedCategoryId))?.subcategories ?? []).map((item) => String(item.id))
    );

    if (!validSubcategoryIds.has(selectedSubcategoryId)) {
      setSelectedSubcategoryId(null);
    }
  }, [categoriesById, selectedCategoryId, selectedSubcategoryId]);

  const validate = () => {
    const nextErrors: FormErrors = {};

    if (!selectedCategoryId) {
      nextErrors.category = "Category is required.";
    }

    if (reason.trim().length > reasonMaxLength) {
      nextErrors.reason = `Reason must be ${reasonMaxLength} characters or fewer.`;
    }

    if (notes.trim().length > notesMaxLength) {
      nextErrors.notes = `Notes must be ${notesMaxLength} characters or fewer.`;
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const handleSave = async () => {
    if (!transactionQuery.data || !validate()) {
      return;
    }

    await updateMetadataMutation.mutateAsync({
      transactionId: transactionQuery.data.id,
      payload: {
        reason: reason.trim() ? reason.trim() : null,
        notes: notes.trim() ? notes.trim() : null,
        taxonomyCategoryId: Number(selectedCategoryId),
        taxonomySubcategoryId: selectedSubcategoryId ? Number(selectedSubcategoryId) : null
      }
    });
  };

  const categoryLabel =
    transactionQuery.data?.taxonomyCategoryName ??
    transactionQuery.data?.categoryName ??
    "Uncategorized";
  const subcategoryLabel = transactionQuery.data?.taxonomySubcategoryName ?? "Not set";

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <HeaderShell preset="secondaryDetail" title="Transaction detail" />

      {!transactionId ? (
        <ErrorState title="Transaction not found" message="No transaction was selected." />
      ) : transactionQuery.isLoading && !transactionQuery.data ? (
        <View style={styles.loadingWrap}>
          <SkeletonBlock style={{ height: 148, borderRadius: 6 }} />
          <SkeletonBlock style={{ height: 196, borderRadius: 6 }} />
          <SkeletonBlock style={{ height: 248, borderRadius: 6 }} />
        </View>
      ) : transactionQuery.isError ? (
        <ErrorState
          title="Could not load transaction"
          message={transactionQuery.error.message}
          onRetry={() => {
            void transactionQuery.refetch();
          }}
        />
      ) : transactionQuery.data ? (
        <>
          <GlassCard style={styles.heroCard}>
            <Text style={styles.transactionName}>{transactionQuery.data.description}</Text>
            <AmountText
              amount={transactionQuery.data.amount}
              currency={transactionQuery.data.currency}
              appearance="transaction"
              style={styles.transactionAmount}
            />
            <Text style={styles.transactionSubtitle}>
              {transactionQuery.data.accountName} | {formatDate(transactionQuery.data.bookedAtUtc)}
            </Text>
          </GlassCard>

          <GlassCard style={styles.detailCard}>
            <DetailLine label="Account" value={transactionQuery.data.accountName} />
            <DetailLine label="Date" value={formatDate(transactionQuery.data.bookedAtUtc)} />
            <DetailLine label="Time" value={formatTime(transactionQuery.data.bookedAtUtc)} />
            <DetailLine label="Category" value={categoryLabel} />
            <DetailLine label="Subcategory" value={subcategoryLabel} />
          </GlassCard>

          <GlassCard style={styles.editCard}>
            <TextField
              label="Reason"
              value={reason}
              onChangeText={setReason}
              placeholder="Optional reason"
              maxLength={reasonMaxLength}
              error={errors.reason}
              helper={`${reason.trim().length}/${reasonMaxLength}`}
            />

            <TextField
              label="Notes"
              value={notes}
              onChangeText={setNotes}
              placeholder="Optional notes"
              multiline
              numberOfLines={4}
              maxLength={notesMaxLength}
              error={errors.notes}
              helper={`${notes.trim().length}/${notesMaxLength}`}
              style={styles.notesInput}
            />

            <ModalSelectField
              label="Category"
              value={selectedCategoryId}
              options={categoryOptions}
              placeholder={categoryOptions.length > 0 ? "Select a category" : "Loading categories..."}
              onChange={(value) => {
                setSelectedCategoryId(value);
                setErrors((current) => ({ ...current, category: undefined }));
              }}
              disabled={categoryOptions.length === 0}
            />
            {errors.category ? <Text style={styles.fieldError}>{errors.category}</Text> : null}

            <ModalSelectField
              label="Subcategory"
              value={selectedSubcategoryId}
              options={subcategoryOptions}
              placeholder={selectedCategoryId ? "Select a subcategory (recommended)" : "Select category first"}
              onChange={setSelectedSubcategoryId}
              disabled={!selectedCategoryId || subcategoryOptions.length === 0}
            />
            <Text style={styles.subcategoryHint}>
              Subcategory is recommended for more accurate tracking.
            </Text>
          </GlassCard>

          {updateMetadataMutation.isError ? (
            <Text style={styles.mutationError}>{formatUnknownError(updateMetadataMutation.error)}</Text>
          ) : null}

          <View style={styles.actions}>
            <PrimaryButton
              label="Save metadata"
              onPress={() => {
                void handleSave();
              }}
              isLoading={updateMetadataMutation.isPending}
            />
            <SecondaryButton label="Back" onPress={() => router.back()} />
          </View>
        </>
      ) : null}
    </ScreenContainer>
  );
}

function DetailLine({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.detailLine}>
      <Text style={styles.detailLabel}>{label}</Text>
      <Text style={styles.detailValue}>{value}</Text>
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {},
  heroCard: {
    gap: spacing[8]
  },
  transactionName: {
    color: palette.textPrimary,
    ...typography.title2
  },
  transactionAmount: {
    color: palette.textPrimary,
    ...typography.displayL,
    fontVariant: ["tabular-nums"]
  },
  transactionSubtitle: {
    color: palette.textSecondary,
    ...typography.body2
  },
  detailCard: {
    gap: spacing[8]
  },
  editCard: {
    gap: spacing[12]
  },
  detailLine: {
    gap: spacing[4]
  },
  detailLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  detailValue: {
    color: palette.textPrimary,
    ...typography.body1
  },
  notesInput: {
    minHeight: 88,
    textAlignVertical: "top"
  },
  fieldError: {
    color: palette.negative,
    ...typography.caption
  },
  subcategoryHint: {
    color: palette.textSecondary,
    ...typography.caption
  },
  actions: {
    gap: spacing[12]
  },
  mutationError: {
    color: palette.negative,
    ...typography.caption
  },
  loadingWrap: {
    gap: spacing[12]
  }
}));
