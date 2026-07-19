import { Ionicons } from "@expo/vector-icons";
import * as DocumentPicker from "expo-document-picker";
import { useFocusEffect, useLocalSearchParams, useRouter } from "expo-router";
import { useCallback, useMemo, useState } from "react";
import { Alert, BackHandler, Pressable, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { ModalSelectField } from "../../../src/components/ui/ModalSelectField";
import { Button } from "../../../src/components/ui/buttons/Button";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SelectField } from "../../../src/components/ui/SelectField";
import { Skeleton } from "../../../src/components/ui/feedback/Skeleton";
import { TextField } from "../../../src/components/ui/fields/TextField";
import { useAccountDetailQuery } from "../../../src/features/accounts/useAccounts";
import { isProviderProjectedAccount } from "../../../src/features/accounts/accountProvenance";
import {
  hasStatementImportMappingErrors,
  suggestStatementImportMapping,
  toStatementImportMappingRequest,
  validateStatementImportMapping,
  type StatementImportMappingDraft,
  type StatementImportMappingErrors
} from "../../../src/features/statementImports/statementImportModels";
import {
  getStatementImportBatch,
  getStatementImportRows,
  type StatementDocumentAsset
} from "../../../src/features/statementImports/statementImportsApi";
import {
  useInspectStatementMutation,
  usePreviewStatementMutation,
  useReviewStatementImportMutation,
  useStatementImportLifecycleMutations
} from "../../../src/features/statementImports/useStatementImports";
import { formatUnknownError } from "../../../src/lib/api/errors";
import { getLocaleLocationProfile } from "../../../src/lib/device/deviceLocationProfile";
import { showFlashMessage } from "../../../src/lib/flashMessage";
import { formatCurrency } from "../../../src/lib/format";
import { HeaderActionButton, HeaderShell } from "../../../src/layout/appHeader";
import {
  createRuntimeStyleSheet,
  palette,
  spacing,
  surfaces,
  typography
} from "../../../src/theme/tokens";
import type {
  StatementCsvInspectionDto,
  StatementImportBatchDto,
  StatementImportReviewDisposition,
  StatementImportRowDto,
  StatementImportRowsRequest
} from "../../../src/types/api";

type ImportStage = "select" | "mapping" | "review" | "complete";
type RowFilter = "all" | "pending" | "invalid" | "exact";

const amountModeOptions = [
  { label: "One amount column", value: "signed" },
  { label: "Money in / out", value: "debit_credit" }
];

const amountSignOptions = [
  { label: "Expenses are negative", value: "as_is" },
  { label: "Expenses are positive", value: "invert" }
];

const dateKindOptions = [
  { label: "Date only", value: "date" },
  { label: "Date and time", value: "instant" }
];

const rowFilterOptions: { label: string; value: RowFilter }[] = [
  { label: "All rows", value: "all" },
  { label: "Needs review", value: "pending" },
  { label: "Invalid", value: "invalid" },
  { label: "Exact duplicates", value: "exact" }
];

function getRowsRequest(filter: RowFilter, cursor?: string | null): StatementImportRowsRequest {
  const request: StatementImportRowsRequest = { pageSize: 100, cursor };
  if (filter === "pending") {
    request.reviewDisposition = "pending";
  } else if (filter === "invalid") {
    request.validationStatus = "invalid";
  } else if (filter === "exact") {
    request.duplicateClassification = "exact";
  }
  return request;
}

function mergeRows(current: StatementImportRowDto[], incoming: StatementImportRowDto[]) {
  const byId = new Map(current.map((row) => [row.id, row] as const));
  incoming.forEach((row) => byId.set(row.id, row));
  return [...byId.values()].sort((left, right) => left.rowNumber - right.rowNumber);
}

function isCsvFile(asset: DocumentPicker.DocumentPickerAsset) {
  return asset.name.toLowerCase().endsWith(".csv");
}

function friendlyValidationMessage(code: string | null) {
  switch (code) {
    case "duplicate_within_source":
      return "Repeated in this file";
    case "statement_import_row_amount_invalid":
      return "Amount could not be read";
    case "statement_import_row_date_invalid":
      return "Date could not be read";
    case "statement_import_row_description_required":
      return "Description is missing";
    case "statement_import_row_currency_mismatch":
      return "Currency does not match the account";
    default:
      return "This row could not be imported";
  }
}

function rowDateLabel(row: StatementImportRowDto) {
  if (row.effectiveDate) {
    return row.effectiveDate;
  }
  if (!row.effectiveAtUtc) {
    return "Date unavailable";
  }

  const parsed = new Date(row.effectiveAtUtc);
  return Number.isNaN(parsed.getTime())
    ? "Date unavailable"
    : new Intl.DateTimeFormat("en-IE", {
        day: "2-digit",
        month: "short",
        year: "numeric"
      }).format(parsed);
}

export default function ImportStatementScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ accountId?: string }>();
  const accountId = typeof params.accountId === "string" ? params.accountId : "";
  const accountQuery = useAccountDetailQuery(accountId);
  const inspectMutation = useInspectStatementMutation();
  const previewMutation = usePreviewStatementMutation();
  const reviewMutation = useReviewStatementImportMutation();
  const {
    commitMutation,
    discardMutation,
    undoMutation,
    refreshFinanceData
  } = useStatementImportLifecycleMutations();
  const localeProfile = useMemo(() => getLocaleLocationProfile(), []);

  const [stage, setStage] = useState<ImportStage>("select");
  const [asset, setAsset] = useState<StatementDocumentAsset | null>(null);
  const [inspection, setInspection] = useState<StatementCsvInspectionDto | null>(null);
  const [mapping, setMapping] = useState<StatementImportMappingDraft | null>(null);
  const [mappingErrors, setMappingErrors] = useState<StatementImportMappingErrors>({});
  const [batch, setBatch] = useState<StatementImportBatchDto | null>(null);
  const [rows, setRows] = useState<StatementImportRowDto[]>([]);
  const [rowFilter, setRowFilter] = useState<RowFilter>("all");
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [totalFilteredRows, setTotalFilteredRows] = useState(0);
  const [pendingRowCount, setPendingRowCount] = useState(0);
  const [reviewingRowId, setReviewingRowId] = useState<string | null>(null);
  const [isLoadingRows, setIsLoadingRows] = useState(false);
  const [screenError, setScreenError] = useState<string | null>(null);

  const account = accountQuery.data;
  const isConnectedAccount = isProviderProjectedAccount(account);
  const anyMutationPending =
    inspectMutation.isPending ||
    previewMutation.isPending ||
    reviewMutation.isPending ||
    commitMutation.isPending ||
    discardMutation.isPending ||
    undoMutation.isPending;

  const columnOptions = useMemo(
    () =>
      (inspection?.columns ?? []).map((column) => ({
        label: `${column.name} (column ${column.index + 1})`,
        value: String(column.index)
      })),
    [inspection?.columns]
  );
  const optionalColumnOptions = useMemo(
    () => [{ label: "Not included", value: "" }, ...columnOptions],
    [columnOptions]
  );

  const loadRows = useCallback(async (filter: RowFilter, cursor?: string | null) => {
    if (!batch) {
      return;
    }

    setIsLoadingRows(true);
    setScreenError(null);
    try {
      const page = await getStatementImportRows(batch.id, getRowsRequest(filter, cursor));
      setRows((current) => (cursor ? mergeRows(current, page.items) : page.items));
      setNextCursor(page.nextCursor);
      setTotalFilteredRows(page.totalMatchingRows);
      setRowFilter(filter);
    } catch (error) {
      setScreenError(formatUnknownError(error));
    } finally {
      setIsLoadingRows(false);
    }
  }, [batch]);

  const discardCurrentImport = useCallback(async () => {
    if (!batch) {
      router.back();
      return;
    }

    try {
      await discardMutation.mutateAsync({ batchId: batch.id, revision: batch.revision });
      showFlashMessage("Import discarded.");
      router.replace(`/(tabs)/accounts/${accountId}` as never);
    } catch (error) {
      setScreenError(formatUnknownError(error));
    }
  }, [accountId, batch, discardMutation, router]);

  const confirmDiscard = useCallback(() => {
    Alert.alert(
      "Discard this import?",
      "The staged rows and review decisions will be removed.",
      [
        { text: "Keep reviewing", style: "cancel" },
        {
          text: "Discard",
          style: "destructive",
          onPress: () => void discardCurrentImport()
        }
      ]
    );
  }, [discardCurrentImport]);

  const handleBack = useCallback(() => {
    if (stage === "review" && batch) {
      confirmDiscard();
      return;
    }
    if (stage === "complete") {
      router.replace(`/(tabs)/accounts/${accountId}` as never);
      return;
    }
    if (stage === "mapping") {
      setStage("select");
      setScreenError(null);
      return;
    }
    router.back();
  }, [accountId, batch, confirmDiscard, router, stage]);

  useFocusEffect(
    useCallback(() => {
      const subscription = BackHandler.addEventListener("hardwareBackPress", () => {
        handleBack();
        return true;
      });
      return () => subscription.remove();
    }, [handleBack])
  );

  const chooseFile = async () => {
    setScreenError(null);
    const result = await DocumentPicker.getDocumentAsync({
      type: "*/*",
      copyToCacheDirectory: true,
      multiple: false
    });
    if (result.canceled) {
      return;
    }

    const selected = result.assets[0];
    if (!selected || !isCsvFile(selected)) {
      setScreenError("Choose a CSV file exported by your bank or financial provider.");
      return;
    }

    const documentAsset: StatementDocumentAsset = {
      uri: selected.uri,
      name: selected.name,
      mimeType: selected.mimeType
    };

    try {
      const inspected = await inspectMutation.mutateAsync({ asset: documentAsset });
      const locale = localeProfile.localeTag ?? "en-IE";
      const timeZoneId = localeProfile.timezone ?? "Europe/Dublin";
      setAsset(documentAsset);
      setInspection(inspected);
      setMapping(suggestStatementImportMapping(inspected, locale, timeZoneId));
      setMappingErrors({});
      setStage("mapping");
    } catch (error) {
      setScreenError(formatUnknownError(error));
    }
  };

  const previewImport = async () => {
    if (!asset || !inspection || !mapping) {
      return;
    }

    const errors = validateStatementImportMapping(mapping);
    setMappingErrors(errors);
    if (hasStatementImportMappingErrors(errors)) {
      return;
    }

    setScreenError(null);
    try {
      const preview = await previewMutation.mutateAsync({
        asset,
        mapping: toStatementImportMappingRequest(accountId, inspection.delimiter, mapping)
      });
      const initialFilter: RowFilter =
        preview.batch.likelyDuplicateRowCount > 0 ? "pending" : "all";
      setBatch(preview.batch);
      setPendingRowCount(preview.batch.likelyDuplicateRowCount);
      setStage("review");

      if (initialFilter === "all") {
        setRows(preview.rows.items);
        setNextCursor(preview.rows.nextCursor);
        setTotalFilteredRows(preview.rows.totalMatchingRows);
        setRowFilter("all");
      } else {
        const page = await getStatementImportRows(
          preview.batch.id,
          getRowsRequest("pending")
        );
        setRows(page.items);
        setNextCursor(page.nextCursor);
        setTotalFilteredRows(page.totalMatchingRows);
        setRowFilter("pending");
      }
    } catch (error) {
      setScreenError(formatUnknownError(error));
    }
  };

  const refreshReviewState = async (filter: RowFilter) => {
    if (!batch) {
      return;
    }

    const [freshBatch, page, pendingPage] = await Promise.all([
      getStatementImportBatch(batch.id),
      getStatementImportRows(batch.id, getRowsRequest(filter)),
      getStatementImportRows(batch.id, getRowsRequest("pending"))
    ]);
    setBatch(freshBatch);
    setRows(page.items);
    setNextCursor(page.nextCursor);
    setTotalFilteredRows(page.totalMatchingRows);
    setPendingRowCount(pendingPage.totalMatchingRows);
  };

  const reviewRow = async (
    row: StatementImportRowDto,
    disposition: Exclude<StatementImportReviewDisposition, "pending">
  ) => {
    if (!batch || row.reviewDisposition === disposition) {
      return;
    }

    setReviewingRowId(row.id);
    setScreenError(null);
    try {
      const result = await reviewMutation.mutateAsync({
        batchId: batch.id,
        request: {
          expectedRevision: batch.revision,
          decisions: [{ rowId: row.id, reviewDisposition: disposition }]
        }
      });
      setBatch((current) =>
        current
          ? {
              ...current,
              revision: result.revision,
              includedRowCount: result.includedRowCount,
              updatedUtc: result.updatedUtc
            }
          : current
      );
      setPendingRowCount(result.pendingRowCount);

      if (rowFilter === "pending") {
        setRows((current) => current.filter((item) => item.id !== row.id));
        setTotalFilteredRows((current) => Math.max(0, current - 1));
        if (result.pendingRowCount === 0) {
          await loadRows("all");
        }
      } else {
        setRows((current) =>
          current.map((item) =>
            item.id === row.id ? { ...item, reviewDisposition: disposition } : item
          )
        );
      }
    } catch (error) {
      setScreenError(formatUnknownError(error));
      try {
        await refreshReviewState(rowFilter);
      } catch {
        // The original actionable error remains visible.
      }
    } finally {
      setReviewingRowId(null);
    }
  };

  const commitImport = async () => {
    if (!batch || pendingRowCount > 0 || batch.includedRowCount <= 0) {
      return;
    }

    try {
      const result = await commitMutation.mutateAsync({
        batchId: batch.id,
        revision: batch.revision
      });
      setBatch((current) =>
        current
          ? {
              ...current,
              status: result.status,
              revision: result.revision,
              includedRowCount: result.includedRowCount,
              committedRowCount: result.committedRowCount,
              updatedUtc: result.updatedUtc,
              committedUtc: result.committedUtc,
              undoneUtc: result.undoneUtc,
              wasReplay: result.wasReplay
            }
          : current
      );
      await refreshFinanceData(accountId);
      setStage("complete");
      showFlashMessage("Statement imported.", { tone: "success" });
    } catch (error) {
      setScreenError(formatUnknownError(error));
    }
  };

  const confirmCommit = () => {
    if (!batch) {
      return;
    }
    Alert.alert(
      `Import ${batch.includedRowCount} transactions?`,
      "Activity will update immediately. Your live bank balance will not change.",
      [
        { text: "Cancel", style: "cancel" },
        { text: "Import", onPress: () => void commitImport() }
      ]
    );
  };

  const undoImport = async () => {
    if (!batch || batch.status !== "committed") {
      return;
    }

    try {
      const result = await undoMutation.mutateAsync({
        batchId: batch.id,
        revision: batch.revision
      });
      setBatch((current) =>
        current
          ? {
              ...current,
              status: result.status,
              revision: result.revision,
              committedRowCount: result.committedRowCount,
              updatedUtc: result.updatedUtc,
              committedUtc: result.committedUtc,
              undoneUtc: result.undoneUtc
            }
          : current
      );
      await refreshFinanceData(accountId);
      showFlashMessage("Import undone.");
    } catch (error) {
      setScreenError(formatUnknownError(error));
    }
  };

  const confirmUndo = () => {
    Alert.alert(
      "Undo this import?",
      "The imported transactions will be removed if they have not changed since import.",
      [
        { text: "Keep import", style: "cancel" },
        { text: "Undo", style: "destructive", onPress: () => void undoImport() }
      ]
    );
  };

  if (!accountId) {
    return (
      <ScreenContainer>
        <HeaderShell preset="secondaryDetail" title="Import statement" />
        <ErrorState title="Account not found" message="Choose a connected account first." />
      </ScreenContainer>
    );
  }

  return (
    <ScreenContainer contentStyle={styles.content}>
      <HeaderShell
        preset="secondaryDetail"
        title="Import statement"
        leadingAction={
          <HeaderActionButton
            icon={<Ionicons name="arrow-back" size={20} color={palette.textPrimary} />}
            accessibilityLabel="Go back"
            onPress={handleBack}
          />
        }
      />

      {accountQuery.isLoading && !account ? (
        <View style={styles.loadingWrap}>
          <Skeleton style={styles.loadingBlock} />
          <Skeleton style={styles.loadingBlock} />
        </View>
      ) : accountQuery.isError ? (
        <ErrorState
          title="Could not load account"
          message={accountQuery.error.message}
          onRetry={() => void accountQuery.refetch()}
        />
      ) : !account || !isConnectedAccount ? (
        <ErrorState
          title="Connected account required"
          message="Legacy accounts are read-only and cannot accept a statement import."
        />
      ) : (
        <>
          <View style={styles.accountContext}>
            <Text style={styles.contextLabel}>Destination</Text>
            <Text style={styles.contextValue}>{account.name}</Text>
            <Text style={styles.contextMeta}>{account.currency} connected account</Text>
          </View>

          {screenError ? (
            <ErrorState title="Import needs attention" message={screenError} />
          ) : null}

          {stage === "select" ? (
            <View style={styles.stageBody}>
              <View style={styles.filePickerIcon}>
                <Ionicons name="document-text-outline" size={28} color={palette.accent} />
              </View>
              <View style={styles.centerCopy}>
                <Text style={styles.stageTitle}>Choose a CSV statement</Text>
                <Text style={styles.stageText}>
                  Nothing is added until you review and confirm it. Your live bank balance remains
                  authoritative.
                </Text>
              </View>
              <Button
                label="Choose CSV file"
                icon={<Ionicons name="folder-open-outline" size={18} color="#FFFFFF" />}
                onPress={() => void chooseFile()}
                isLoading={inspectMutation.isPending}
              />
            </View>
          ) : null}

          {stage === "mapping" && inspection && mapping ? (
            <View style={styles.stageBody}>
              <View style={styles.fileSummary}>
                <Ionicons name="document-outline" size={20} color={palette.accent} />
                <View style={styles.fileSummaryCopy}>
                  <Text style={styles.fileName} numberOfLines={1}>{inspection.fileName}</Text>
                  <Text style={styles.contextMeta}>
                    {inspection.dataRowCount} rows | {inspection.columns.length} columns
                  </Text>
                </View>
              </View>

              <Text style={styles.sectionTitle}>Match columns</Text>
              <ModalSelectField
                label="Transaction date"
                value={mapping.dateColumn}
                options={columnOptions}
                placeholder="Select date column"
                error={mappingErrors.dateColumn}
                onChange={(value) => {
                  setMapping({ ...mapping, dateColumn: value });
                  setMappingErrors((current) => ({ ...current, dateColumn: undefined, columns: undefined }));
                }}
              />
              <ModalSelectField
                label="Description"
                value={mapping.descriptionColumn}
                options={columnOptions}
                placeholder="Select description column"
                error={mappingErrors.descriptionColumn}
                onChange={(value) => {
                  setMapping({ ...mapping, descriptionColumn: value });
                  setMappingErrors((current) => ({ ...current, descriptionColumn: undefined, columns: undefined }));
                }}
              />
              <SelectField
                label="Amount layout"
                value={mapping.amountMode}
                options={amountModeOptions}
                compact
                onChange={(value) =>
                  setMapping({ ...mapping, amountMode: value as StatementImportMappingDraft["amountMode"] })
                }
              />
              {mapping.amountMode === "signed" ? (
                <ModalSelectField
                  label="Amount"
                  value={mapping.amountColumn}
                  options={columnOptions}
                  placeholder="Select amount column"
                  error={mappingErrors.amountColumn}
                  onChange={(value) => {
                    setMapping({ ...mapping, amountColumn: value });
                    setMappingErrors((current) => ({ ...current, amountColumn: undefined }));
                  }}
                />
              ) : (
                <>
                  <ModalSelectField
                    label="Money out"
                    value={mapping.debitColumn}
                    options={columnOptions}
                    placeholder="Select money-out column"
                    error={mappingErrors.debitColumn}
                    onChange={(value) => {
                      setMapping({ ...mapping, debitColumn: value });
                      setMappingErrors((current) => ({ ...current, debitColumn: undefined, columns: undefined }));
                    }}
                  />
                  <ModalSelectField
                    label="Money in"
                    value={mapping.creditColumn}
                    options={columnOptions}
                    placeholder="Select money-in column"
                    error={mappingErrors.creditColumn}
                    onChange={(value) => {
                      setMapping({ ...mapping, creditColumn: value });
                      setMappingErrors((current) => ({ ...current, creditColumn: undefined, columns: undefined }));
                    }}
                  />
                </>
              )}
              {mappingErrors.columns ? (
                <Text style={styles.fieldError}>{mappingErrors.columns}</Text>
              ) : null}
              <ModalSelectField
                label="Currency"
                value={mapping.currencyColumn}
                options={optionalColumnOptions}
                onChange={(value) => setMapping({ ...mapping, currencyColumn: value })}
              />
              <ModalSelectField
                label="Reference"
                value={mapping.referenceColumn}
                options={optionalColumnOptions}
                onChange={(value) => setMapping({ ...mapping, referenceColumn: value })}
              />

              <Text style={styles.sectionTitle}>Interpret values</Text>
              <SelectField
                label="Date precision"
                value={mapping.dateValueKind}
                options={dateKindOptions}
                compact
                onChange={(value) =>
                  setMapping({ ...mapping, dateValueKind: value as StatementImportMappingDraft["dateValueKind"] })
                }
              />
              <TextField
                label="Date format"
                value={mapping.dateFormat}
                onChangeText={(value) => {
                  setMapping({ ...mapping, dateFormat: value });
                  setMappingErrors((current) => ({ ...current, dateFormat: undefined }));
                }}
                placeholder="dd/MM/yyyy"
                autoCapitalize="none"
                autoCorrect={false}
                helper="Matched to the selected column's sample values."
                error={mappingErrors.dateFormat}
              />
              <SelectField
                label="Amount signs"
                value={mapping.amountSign}
                options={amountSignOptions}
                compact
                onChange={(value) =>
                  setMapping({ ...mapping, amountSign: value as StatementImportMappingDraft["amountSign"] })
                }
              />
              <TextField
                label="Locale"
                value={mapping.locale}
                onChangeText={(value) => {
                  setMapping({ ...mapping, locale: value });
                  setMappingErrors((current) => ({ ...current, locale: undefined }));
                }}
                placeholder="en-IE"
                autoCapitalize="none"
                autoCorrect={false}
                error={mappingErrors.locale}
              />
              <TextField
                label="Time zone"
                value={mapping.timeZoneId}
                onChangeText={(value) => {
                  setMapping({ ...mapping, timeZoneId: value });
                  setMappingErrors((current) => ({ ...current, timeZoneId: undefined }));
                }}
                placeholder="Europe/Dublin"
                autoCapitalize="none"
                autoCorrect={false}
                error={mappingErrors.timeZoneId}
              />
              <View style={styles.actions}>
                <Button
                  label="Review transactions"
                  onPress={() => void previewImport()}
                  isLoading={previewMutation.isPending}
                />
                <Button variant="secondary"
                  label="Choose a different file"
                  onPress={() => {
                    setStage("select");
                    setScreenError(null);
                  }}
                  disabled={anyMutationPending}
                />
              </View>
            </View>
          ) : null}

          {stage === "review" && batch ? (
            <View style={styles.stageBody}>
              <View style={styles.summaryGrid}>
                <SummaryMetric label="Ready" value={batch.includedRowCount} />
                <SummaryMetric label="Review" value={pendingRowCount} />
                <SummaryMetric label="Invalid" value={batch.invalidRowCount} />
                <SummaryMetric label="Duplicates" value={batch.exactDuplicateRowCount} />
              </View>

              {pendingRowCount > 0 ? (
                <View style={styles.noticeRow}>
                  <Ionicons name="alert-circle-outline" size={18} color={palette.accent} />
                  <Text style={styles.noticeText}>
                    Decide whether to include each possible duplicate before importing.
                  </Text>
                </View>
              ) : null}

              <SelectField
                label="Rows"
                value={rowFilter}
                options={rowFilterOptions}
                compact
                onChange={(value) => void loadRows(value as RowFilter)}
              />
              <Text style={styles.resultCount}>
                Showing {rows.length} of {totalFilteredRows}
              </Text>

              {isLoadingRows && rows.length === 0 ? (
                <View style={styles.loadingWrap}>
                  <Skeleton style={styles.rowSkeleton} />
                  <Skeleton style={styles.rowSkeleton} />
                </View>
              ) : rows.length > 0 ? (
                <View style={styles.rowsList}>
                  {rows.map((row) => (
                    <StatementRow
                      key={row.id}
                      row={row}
                      accountCurrency={batch.accountCurrency}
                      disabled={reviewingRowId === row.id || reviewMutation.isPending}
                      onDecision={(disposition) => void reviewRow(row, disposition)}
                    />
                  ))}
                </View>
              ) : (
                <Text style={styles.emptyFilterText}>No rows match this view.</Text>
              )}

              {nextCursor ? (
                <Button variant="secondary"
                  label={isLoadingRows ? "Loading rows..." : "Load more rows"}
                  onPress={() => void loadRows(rowFilter, nextCursor)}
                  disabled={isLoadingRows}
                />
              ) : null}

              <View style={styles.actions}>
                <Button
                  label={`Import ${batch.includedRowCount} transactions`}
                  onPress={confirmCommit}
                  isLoading={commitMutation.isPending}
                  disabled={pendingRowCount > 0 || batch.includedRowCount <= 0}
                />
                <Button variant="secondary"
                  label={discardMutation.isPending ? "Discarding..." : "Discard import"}
                  onPress={confirmDiscard}
                  disabled={anyMutationPending}
                />
              </View>
            </View>
          ) : null}

          {stage === "complete" && batch ? (
            <View style={styles.completionBody}>
              <View style={styles.completionIcon}>
                <Ionicons
                  name={batch.status === "undone" ? "arrow-undo-outline" : "checkmark"}
                  size={30}
                  color={batch.status === "undone" ? palette.textSecondary : palette.success}
                />
              </View>
              <View style={styles.centerCopy}>
                <Text style={styles.stageTitle}>
                  {batch.status === "undone" ? "Import undone" : "Statement imported"}
                </Text>
                <Text style={styles.stageText}>
                  {batch.status === "undone"
                    ? "The imported transactions were removed from this account."
                    : `${batch.committedRowCount} transactions are now in ${account.name}.`}
                </Text>
              </View>
              <View style={styles.actions}>
                <Button
                  label="View account"
                  onPress={() => router.replace(`/(tabs)/accounts/${accountId}` as never)}
                />
                {batch.status === "committed" ? (
                  <Button variant="secondary"
                    label={undoMutation.isPending ? "Undoing..." : "Undo import"}
                    onPress={confirmUndo}
                    disabled={undoMutation.isPending}
                  />
                ) : null}
              </View>
            </View>
          ) : null}
        </>
      )}
    </ScreenContainer>
  );
}

function SummaryMetric({ label, value }: { label: string; value: number }) {
  return (
    <View style={styles.summaryMetric}>
      <Text style={styles.summaryValue}>{value}</Text>
      <Text style={styles.summaryLabel}>{label}</Text>
    </View>
  );
}

function StatementRow({
  row,
  accountCurrency,
  disabled,
  onDecision
}: {
  row: StatementImportRowDto;
  accountCurrency: string;
  disabled: boolean;
  onDecision: (disposition: "included" | "excluded") => void;
}) {
  const isInvalid = row.validationStatus === "invalid";
  const isExactDuplicate = row.duplicateClassification === "exact";
  const isLikelyDuplicate = row.duplicateClassification === "likely";
  const isReviewable = !isInvalid && !isExactDuplicate && !row.committedTransactionId;
  const statusLabel = isInvalid
    ? friendlyValidationMessage(row.validationCode)
    : isExactDuplicate
      ? "Exact duplicate | skipped"
      : isLikelyDuplicate
        ? "Possible duplicate"
        : row.reviewDisposition === "excluded"
          ? "Skipped"
          : "Ready to import";

  return (
    <View style={[styles.statementRow, isLikelyDuplicate ? styles.statementRowAttention : null]}>
      <View style={styles.statementRowTop}>
        <View style={styles.statementRowCopy}>
          <Text style={styles.statementDescription} numberOfLines={2}>
            {row.description || "Description unavailable"}
          </Text>
          <Text style={styles.statementMeta}>
            Row {row.rowNumber} | {rowDateLabel(row)}
          </Text>
        </View>
        <Text style={styles.statementAmount}>
          {row.amount === null
            ? "-"
            : formatCurrency(row.amount, row.currency || accountCurrency)}
        </Text>
      </View>
      <Text
        style={[
          styles.rowStatus,
          isInvalid ? styles.rowStatusInvalid : null,
          isLikelyDuplicate ? styles.rowStatusAttention : null
        ]}
      >
        {statusLabel}
      </Text>
      {isReviewable ? (
        <View style={styles.rowDecisions}>
          <DecisionButton
            label="Include"
            icon="checkmark"
            selected={row.reviewDisposition === "included"}
            disabled={disabled}
            onPress={() => onDecision("included")}
          />
          <DecisionButton
            label="Skip"
            icon="close"
            selected={row.reviewDisposition === "excluded"}
            disabled={disabled}
            onPress={() => onDecision("excluded")}
          />
        </View>
      ) : null}
    </View>
  );
}

function DecisionButton({
  label,
  icon,
  selected,
  disabled,
  onPress
}: {
  label: string;
  icon: "checkmark" | "close";
  selected: boolean;
  disabled: boolean;
  onPress: () => void;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected, disabled }}
      accessibilityLabel={`${label} statement row`}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.decisionButton,
        selected ? styles.decisionButtonSelected : null,
        disabled ? styles.disabled : null,
        pressed ? styles.pressed : null
      ]}
    >
      <Ionicons
        name={icon}
        size={16}
        color={selected ? "#FFFFFF" : palette.textSecondary}
      />
      <Text style={[styles.decisionText, selected ? styles.decisionTextSelected : null]}>
        {label}
      </Text>
    </Pressable>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {
    gap: spacing[20]
  },
  accountContext: {
    gap: spacing[2]
  },
  contextLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  contextValue: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  contextMeta: {
    color: palette.textSecondary,
    ...typography.body2
  },
  stageBody: {
    gap: spacing[16]
  },
  completionBody: {
    minHeight: 360,
    justifyContent: "center",
    gap: spacing[20]
  },
  filePickerIcon: {
    alignSelf: "center",
    width: 64,
    height: 64,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    alignItems: "center",
    justifyContent: "center"
  },
  centerCopy: {
    alignItems: "center",
    gap: spacing[8]
  },
  stageTitle: {
    color: palette.textPrimary,
    textAlign: "center",
    ...typography.title2
  },
  stageText: {
    color: palette.textSecondary,
    textAlign: "center",
    ...typography.body2
  },
  fileSummary: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12],
    padding: spacing[12],
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field
  },
  fileSummaryCopy: {
    flex: 1,
    minWidth: 0,
    gap: spacing[2]
  },
  fileName: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    marginTop: spacing[4]
  },
  fieldError: {
    color: palette.negative,
    ...typography.caption
  },
  actions: {
    gap: spacing[12],
    marginTop: spacing[4]
  },
  summaryGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  summaryMetric: {
    width: "48.7%",
    minHeight: 68,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    padding: spacing[12],
    justifyContent: "center",
    gap: spacing[2]
  },
  summaryValue: {
    color: palette.textPrimary,
    ...typography.title2,
    fontVariant: ["tabular-nums"]
  },
  summaryLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  noticeRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: spacing[8],
    paddingVertical: spacing[4]
  },
  noticeText: {
    flex: 1,
    color: palette.textSecondary,
    ...typography.body2
  },
  resultCount: {
    color: palette.textSecondary,
    ...typography.caption
  },
  rowsList: {
    gap: spacing[10]
  },
  statementRow: {
    gap: spacing[8],
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    padding: spacing[12]
  },
  statementRowAttention: {
    borderColor: palette.primaryGlow
  },
  statementRowTop: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: spacing[12]
  },
  statementRowCopy: {
    flex: 1,
    minWidth: 0,
    gap: spacing[2]
  },
  statementDescription: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  statementMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  statementAmount: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontVariant: ["tabular-nums"]
  },
  rowStatus: {
    color: palette.textSecondary,
    ...typography.caption
  },
  rowStatusInvalid: {
    color: palette.negative
  },
  rowStatusAttention: {
    color: palette.primaryGlow
  },
  rowDecisions: {
    flexDirection: "row",
    gap: spacing[8]
  },
  decisionButton: {
    flex: 1,
    minHeight: 40,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.fieldStrong,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[6]
  },
  decisionButtonSelected: {
    borderColor: palette.accent,
    backgroundColor: palette.accent
  },
  decisionText: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "600"
  },
  decisionTextSelected: {
    color: "#FFFFFF"
  },
  emptyFilterText: {
    color: palette.textSecondary,
    textAlign: "center",
    paddingVertical: spacing[20],
    ...typography.body2
  },
  completionIcon: {
    alignSelf: "center",
    width: 68,
    height: 68,
    borderRadius: 34,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    alignItems: "center",
    justifyContent: "center"
  },
  loadingWrap: {
    gap: spacing[12]
  },
  loadingBlock: {
    height: 92,
    borderRadius: 6
  },
  rowSkeleton: {
    height: 128,
    borderRadius: 6
  },
  disabled: {
    opacity: 0.55
  },
  pressed: {
    opacity: 0.88
  }
}));
