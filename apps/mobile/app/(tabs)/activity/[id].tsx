import { useFocusEffect, useLocalSearchParams, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Pressable, Text, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { TransactionRow } from "../../../src/components/transactions/TransactionRow";
import { AmountText } from "../../../src/components/ui/AmountText";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { SkeletonBlock } from "../../../src/components/ui/SkeletonBlock";
import { TextField } from "../../../src/components/ui/TextField";
import { consumePendingTransactionDetailCategorySelection } from "../../../src/features/expenseTracker/categoryPickerBridge";
import { useExpenseTrackerTaxonomyQuery } from "../../../src/features/expenseTracker/useExpenseTracker";
import {
  useTransactionDetailQuery,
  useUpdateTransactionMetadataMutation
} from "../../../src/features/transactions/useTransactions";
import {
  getTransferPolicyEvaluation,
  getTransferPolicyWarning,
  TRANSFER_DOMAIN_ID
} from "../../../src/features/transactions/transferClassification";
import { buildTransactionMetaLine } from "../../../src/features/transactions/activityGrouping";
import { formatUnknownError } from "../../../src/lib/api/errors";
import { formatDate, formatTime } from "../../../src/lib/format";
import { HeaderShell } from "../../../src/layout/appHeader";
import type { TransactionDto } from "../../../src/types/api";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";

type FormErrors = Partial<Record<"category" | "reason" | "notes", string>>;

const reasonMaxLength = 140;
const notesMaxLength = 1200;

export default function PlannerTransactionDetailScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id?: string }>();
  const transactionId = params.id ?? "";
  const transactionQuery = useTransactionDetailQuery(transactionId);
  const linkedTransactionId = transactionQuery.data?.linkedTransferTransactionId ?? null;
  const linkedTransactionQuery = useTransactionDetailQuery(linkedTransactionId ?? "");
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const updateMetadataMutation = useUpdateTransactionMetadataMutation();

  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);
  const [selectedSubcategoryId, setSelectedSubcategoryId] = useState<string | null>(null);
  const [errors, setErrors] = useState<FormErrors>({});
  const hydratedServerSignatureRef = useRef<string | null>(null);

  const visibleDomains = useMemo(
    () =>
      (taxonomyQuery.data?.domains ?? []).filter(
        (domain) =>
          domain.isActive &&
          !domain.isSystemDomain &&
          domain.isUserSelectable
      ),
    [taxonomyQuery.data?.domains]
  );

  const categoriesById = useMemo(() => {
    const map = new Map<
      number,
      {
        id: number;
        name: string;
        domainId: number;
        domainName: string;
        subcategories: { id: number; name: string }[];
      }
    >();

    visibleDomains.forEach((domain) => {
      domain.categories
        .filter(
          (category) =>
            category.isActive &&
            category.isUserSelectable
        )
        .forEach((category) => {
          map.set(category.id, {
            id: category.id,
            name: category.name,
            domainId: domain.id,
            domainName: domain.name,
            subcategories: category.subcategories
              .filter(
                (subcategory) =>
                  subcategory.isActive &&
                  subcategory.isUserSelectable
              )
              .map((subcategory) => ({
                id: subcategory.id,
                name: subcategory.name
              }))
          });
        });
    });

    return map;
  }, [visibleDomains]);

  const selectedCategory = selectedCategoryId ? categoriesById.get(Number(selectedCategoryId)) ?? null : null;
  const selectedSubcategory = useMemo(
    () =>
      selectedSubcategoryId && selectedCategory
        ? selectedCategory.subcategories.find((subcategory) => subcategory.id === Number(selectedSubcategoryId)) ?? null
        : null,
    [selectedCategory, selectedSubcategoryId]
  );

  const serverDraftSignature = useMemo(() => {
    const transaction = transactionQuery.data;
    if (!transaction) {
      return null;
    }

    return JSON.stringify({
      id: transaction.id,
      reason: transaction.reason ?? "",
      notes: transaction.notes ?? "",
      taxonomyCategoryId: transaction.taxonomyCategoryId ?? null,
      taxonomySubcategoryId: transaction.taxonomySubcategoryId ?? null,
      metadataUpdatedUtc: transaction.metadataUpdatedUtc ?? null
    });
  }, [transactionQuery.data]);

  useEffect(() => {
    if (!transactionQuery.data || !serverDraftSignature) {
      return;
    }

    if (hydratedServerSignatureRef.current === serverDraftSignature) {
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
    hydratedServerSignatureRef.current = serverDraftSignature;
  }, [serverDraftSignature, transactionQuery.data]);

  useEffect(() => {
    if (!selectedCategoryId) {
      return;
    }

    if (!categoriesById.has(Number(selectedCategoryId))) {
      setSelectedCategoryId(null);
      setSelectedSubcategoryId(null);
    }
  }, [categoriesById, selectedCategoryId]);

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

  useFocusEffect(
    useCallback(() => {
      const picked = consumePendingTransactionDetailCategorySelection();
      if (!picked) {
        return undefined;
      }

      console.info("[Transaction Details]", {
        event: "category_selection_consumed",
        transactionId,
        categoryId: picked.categoryId,
        subcategoryId: picked.subcategoryId
      });
      setSelectedCategoryId(String(picked.categoryId));
      setSelectedSubcategoryId(picked.subcategoryId ? String(picked.subcategoryId) : null);
      setErrors((current) => ({
        ...current,
        category: undefined
      }));
      return undefined;
    }, [transactionId])
  );

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
    console.info("[Transaction Details]", {
      event: "metadata_saved",
      transactionId: transactionQuery.data.id,
      categoryId: selectedCategoryId ? Number(selectedCategoryId) : null,
      subcategoryId: selectedSubcategoryId ? Number(selectedSubcategoryId) : null
    });
  };

  const categoryLabel = selectedCategory?.name
    ?? transactionQuery.data?.taxonomyCategoryName
    ?? transactionQuery.data?.categoryName
    ?? "Uncategorized";
  const subcategoryLabel = selectedSubcategory?.name
    ?? transactionQuery.data?.taxonomySubcategoryName
    ?? "Not set";
  const draftDomainId =
    selectedCategory?.domainId
    ?? transactionQuery.data?.taxonomyDomainId
    ?? null;
  const draftCategoryId =
    selectedCategory?.id
    ?? transactionQuery.data?.taxonomyCategoryId
    ?? null;
  const draftSubcategoryId =
    selectedSubcategory?.id
    ?? transactionQuery.data?.taxonomySubcategoryId
    ?? null;
  const hasVerifiedLinkedTransfer =
    transactionQuery.data?.transferKind === "linked_internal_transfer"
    && Boolean(transactionQuery.data?.linkedTransferTransactionId);
  const draftTransferKind =
    draftDomainId === TRANSFER_DOMAIN_ID
      ? (
          hasVerifiedLinkedTransfer
            ? "linked_internal_transfer"
            : "manual_transfer"
        )
      : null;
  const draftLinkedTransferTransactionId =
    draftTransferKind === "linked_internal_transfer"
      ? transactionQuery.data?.linkedTransferTransactionId ?? null
      : null;
  const transferPolicyEvaluation = getTransferPolicyEvaluation({
    amount: transactionQuery.data?.amount ?? 0,
    taxonomyDomainId: draftDomainId,
    taxonomyCategoryId: draftCategoryId,
    taxonomySubcategoryId: draftSubcategoryId,
    transferKind: draftTransferKind,
    linkedTransferTransactionId: draftLinkedTransferTransactionId,
    transferPolicyKind: transactionQuery.data?.transferPolicyKind ?? null,
    reportingBucket: transactionQuery.data?.reportingBucket ?? null,
    isGloballyNeutralized: transactionQuery.data?.isGloballyNeutralized ?? null
  });
  const transferTotalsHint = getTransferPolicyWarning(transferPolicyEvaluation);
  const showLinkedTransactionSection = Boolean(linkedTransactionId);
  const linkedTransactionMetadata = linkedTransactionQuery.data
    ? buildLinkedTransactionMetadata(linkedTransactionQuery.data)
    : null;

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <HeaderShell preset="secondaryDetail" title="Transaction details" />

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

          {showLinkedTransactionSection ? (
            <GlassCard style={styles.linkedTransactionCard}>
              <Text style={styles.linkedTransactionTitle}>Linked transaction</Text>
              {linkedTransactionQuery.isLoading && !linkedTransactionQuery.data ? (
                <SkeletonBlock style={styles.linkedTransactionSkeleton} />
              ) : linkedTransactionQuery.data ? (
                <TransactionRow
                  transaction={linkedTransactionQuery.data}
                  metadataOverride={linkedTransactionMetadata ?? undefined}
                  showTimestamp
                  onPress={() => {
                    if (!linkedTransactionId || linkedTransactionId === transactionId) {
                      return;
                    }

                    router.push(`/(tabs)/activity/${linkedTransactionId}` as never);
                  }}
                />
              ) : linkedTransactionQuery.isError ? (
                <Text style={styles.linkedTransactionError}>
                  Could not load the linked transaction right now.
                </Text>
              ) : null}
            </GlassCard>
          ) : null}

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
                      selectionTarget: "transactionDetailCategory",
                      selectionReturnTransactionId: transactionId
                    }
                  })
                }
                disabled={categoriesById.size === 0}
              >
                <View style={styles.categoryPickerTextWrap}>
                  <Text style={styles.categoryPickerTitle}>
                    {selectedSubcategory?.name ?? selectedCategory?.name ?? "Select category"}
                  </Text>
                  <Text style={styles.categoryPickerMeta}>
                    {selectedSubcategory
                      ? `${selectedCategory?.name ?? "Category"} | ${selectedCategory?.domainName ?? "Taxonomy"}`
                      : selectedCategory
                        ? `${selectedCategory.name} | ${selectedCategory.domainName}`
                        : "Use the taxonomy picker"}
                  </Text>
                </View>
                <Ionicons name="chevron-forward" size={18} color={palette.textSecondary} />
              </Pressable>
            </View>
            {errors.category ? <Text style={styles.fieldError}>{errors.category}</Text> : null}
            {transferTotalsHint ? <Text style={styles.transferHint}>{transferTotalsHint}</Text> : null}
          </GlassCard>

          {updateMetadataMutation.isError ? (
            <Text style={styles.mutationError}>{formatUnknownError(updateMetadataMutation.error)}</Text>
          ) : null}

          <View style={styles.actions}>
            <PrimaryButton
              label="Save changes"
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

function buildLinkedTransactionMetadata(transaction: TransactionDto): string {
  const categoryLabel = buildTransactionMetaLine(transaction);
  return `${transaction.accountName} | ${categoryLabel}`;
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
  linkedTransactionCard: {
    gap: spacing[10]
  },
  linkedTransactionTitle: {
    color: palette.textPrimary,
    ...typography.body2
  },
  linkedTransactionSkeleton: {
    height: 76,
    borderRadius: 6
  },
  linkedTransactionError: {
    color: palette.textSecondary,
    ...typography.caption
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
  fieldWrap: {
    gap: spacing[8]
  },
  fieldLabel: {
    color: palette.textPrimary,
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
  fieldError: {
    color: palette.negative,
    ...typography.caption
  },
  transferHint: {
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
