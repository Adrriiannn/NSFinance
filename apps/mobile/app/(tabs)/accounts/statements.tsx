import { useMutation } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { Modal, Platform, Pressable, ScrollView, Text, View } from "react-native";
import * as Sharing from "expo-sharing";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ModalSelectField } from "../../../src/components/ui/ModalSelectField";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { HeaderShell } from "../../../src/layout/appHeader";
import {
  useConnectedBanksQuery
} from "../../../src/features/banking/useBanking";
import {
  useCreateExportRequestMutation,
  useMyExportRequestsQuery
} from "../../../src/features/support/useSupport";
import { downloadExportRequestFile } from "../../../src/features/support/supportApi";
import { formatUnknownError } from "../../../src/lib/api/errors";
import { showFlashMessage } from "../../../src/lib/flashMessage";
import { useRuntimeBottomInsetPolicy } from "../../../src/theme/insets";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";

type DatePoint = {
  year: number;
  month: number;
  day: number | null;
};

type PeriodPresetKey = "custom" | "last_7_days" | "last_30_days" | "last_2_months" | "last_3_months" | "last_6_months" | "last_12_months" | "last_2_years";

type PickerTarget = "start" | "end";

const monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

const periodPresets: { key: PeriodPresetKey; label: string }[] = [
  { key: "last_7_days", label: "Last 7 days" },
  { key: "last_30_days", label: "Last 30 days" },
  { key: "last_2_months", label: "Last 2 months" },
  { key: "last_3_months", label: "Last 3 months" },
  { key: "last_6_months", label: "Last 6 months" },
  { key: "last_12_months", label: "Last 12 months" },
  { key: "last_2_years", label: "Last 2 years" },
  { key: "custom", label: "Custom" }
];

function dayCountForMonth(year: number, month: number) {
  return new Date(year, month + 1, 0).getDate();
}

function datePointFromDate(date: Date): DatePoint {
  return {
    year: date.getFullYear(),
    month: date.getMonth(),
    day: date.getDate()
  };
}

function formatDatePointLabel(point: DatePoint) {
  if (point.day === null) {
    return `${monthNames[point.month]} ${point.year}`;
  }

  return `${String(point.day).padStart(2, "0")} ${monthNames[point.month]} ${point.year}`;
}

function toDateOnlyString(point: DatePoint, asEndDate = false) {
  const day = point.day ?? (asEndDate ? dayCountForMonth(point.year, point.month) : 1);
  const month = String(point.month + 1).padStart(2, "0");
  const dayPart = String(day).padStart(2, "0");
  return `${point.year}-${month}-${dayPart}`;
}

function addDays(date: Date, delta: number) {
  const next = new Date(date);
  next.setDate(next.getDate() + delta);
  return next;
}

function addMonths(date: Date, delta: number) {
  const next = new Date(date);
  next.setMonth(next.getMonth() + delta);
  return next;
}

function applyPeriodPreset(preset: PeriodPresetKey): { start: DatePoint; end: DatePoint } {
  const today = new Date();
  const end = datePointFromDate(today);

  if (preset === "custom") {
    return { start: end, end };
  }

  if (preset === "last_7_days") {
    return { start: datePointFromDate(addDays(today, -6)), end };
  }

  if (preset === "last_30_days") {
    return { start: datePointFromDate(addDays(today, -29)), end };
  }

  if (preset === "last_2_months") {
    return { start: datePointFromDate(addMonths(today, -2)), end };
  }

  if (preset === "last_3_months") {
    return { start: datePointFromDate(addMonths(today, -3)), end };
  }

  if (preset === "last_6_months") {
    return { start: datePointFromDate(addMonths(today, -6)), end };
  }

  if (preset === "last_12_months") {
    return { start: datePointFromDate(addMonths(today, -12)), end };
  }

  return { start: datePointFromDate(addMonths(today, -24)), end };
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "-";
  }

  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "long",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false
  }).format(date);
}

function formatFileSize(bytes?: number | null) {
  if (!bytes || bytes <= 0) {
    return "-";
  }

  const units = ["B", "KB", "MB", "GB"];
  let size = bytes;
  let unitIndex = 0;

  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024;
    unitIndex += 1;
  }

  const precision = size >= 10 ? 1 : 2;
  return `${size.toFixed(precision)} ${units[unitIndex]}`;
}

export default function StatementsScreen() {
  const bottomInsetPolicy = useRuntimeBottomInsetPolicy();
  const connectedBanksQuery = useConnectedBanksQuery();
  const exportRequestsQuery = useMyExportRequestsQuery();
  const createExportMutation = useCreateExportRequestMutation();
  const [selectedConnectionId, setSelectedConnectionId] = useState<string>("all");
  const [selectedPreset, setSelectedPreset] = useState<PeriodPresetKey>("last_30_days");

  const defaultRange = useMemo(() => applyPeriodPreset("last_30_days"), []);
  const [startDate, setStartDate] = useState<DatePoint>(defaultRange.start);
  const [endDate, setEndDate] = useState<DatePoint>(defaultRange.end);

  const [pickerVisible, setPickerVisible] = useState(false);
  const [pickerTarget, setPickerTarget] = useState<PickerTarget>("start");
  const [pickerDraft, setPickerDraft] = useState<DatePoint>(startDate);

  const downloadMutation = useMutation({
    mutationFn: async (requestId: string) => {
      const downloadResult = await downloadExportRequestFile(requestId);
      const canShare = await Sharing.isAvailableAsync();
      if (Platform.OS !== "android" && canShare) {
        await Sharing.shareAsync(downloadResult.uri, {
          mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
          dialogTitle: "Share your NSFinance statements file"
        });
      }

      return downloadResult;
    },
    onSuccess: (downloadResult) => {
      showFlashMessage(
        Platform.OS === "android" && downloadResult.usedAndroidDownloadManager
          ? "Your file is downloading."
          : "Your file has been downloaded.",
        { tone: "success" }
      );
      void exportRequestsQuery.refetch();
    },
    onError: (error) => {
      showFlashMessage(formatUnknownError(error), { tone: "error", durationMs: 3200 });
    }
  });

  const bankOptions = useMemo(() => {
    const activeConnections = connectedBanksQuery.data?.activeConnections ?? [];
    const seen = new Set<string>();

    const dynamicOptions = activeConnections
      .filter((connection) => {
        if (!connection.id || seen.has(connection.id)) {
          return false;
        }

        seen.add(connection.id);
        return true;
      })
      .map((connection) => ({
        value: connection.id,
        label: connection.providerDisplayName || connection.provider
      }));

    return [{ value: "all", label: "All" }, ...dynamicOptions];
  }, [connectedBanksQuery.data?.activeConnections]);

  const latestExport = (exportRequestsQuery.data ?? [])[0] ?? null;
  const canDownload = latestExport?.status?.toLowerCase() === "ready";

  const openPicker = (target: PickerTarget) => {
    setPickerTarget(target);
    setPickerDraft(target === "start" ? startDate : endDate);
    setPickerVisible(true);
  };

  const savePickerDraft = () => {
    if (pickerTarget === "start") {
      setStartDate(pickerDraft);
    } else {
      setEndDate(pickerDraft);
    }

    setSelectedPreset("custom");
    setPickerVisible(false);
  };

  const selectPreset = (preset: PeriodPresetKey) => {
    setSelectedPreset(preset);
    const nextRange = applyPeriodPreset(preset);
    setStartDate(nextRange.start);
    setEndDate(nextRange.end);
  };

  const submitExport = async () => {
    const startDateValue = toDateOnlyString(startDate);
    const endDateValue = toDateOnlyString(endDate, true);
    const [normalizedStartDate, normalizedEndDate] =
      startDateValue <= endDateValue
        ? [startDateValue, endDateValue]
        : [endDateValue, startDateValue];

    const payload = {
      notes: "User generated statements export from Statements page.",
      format: "xlsx" as const,
      connectionId: selectedConnectionId === "all" ? null : selectedConnectionId,
      startDate: normalizedStartDate,
      endDate: normalizedEndDate,
      periodPreset: selectedPreset === "custom" ? null : selectedPreset
    };

    try {
      await createExportMutation.mutateAsync(payload);
      await exportRequestsQuery.refetch();
      showFlashMessage("Your file has been generated.", { tone: "success" });
    } catch (error) {
      showFlashMessage(formatUnknownError(error), { tone: "error", durationMs: 3200 });
    }
  };

  const selectedBankLabel =
    bankOptions.find((option) => option.value === selectedConnectionId)?.label ?? "All";

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset scrollable={false}>
      <HeaderShell preset="secondaryDetail" title="Statements" />

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={styles.scrollContent}>
        <GlassCard style={styles.sectionCard}>
          <Text style={styles.sectionTitle}>Export filters</Text>
          <Text style={styles.hintText}>Choose a bank, select a date range, and generate your statements file.</Text>

          <ModalSelectField
            label="Bank"
            value={selectedConnectionId}
            options={bankOptions}
            onChange={(value) => setSelectedConnectionId(value)}
            placeholder="Select bank"
          />

          <View style={styles.dateFieldsRow}>
            <Pressable
              style={({ pressed }) => [styles.dateField, pressed ? styles.pressed : null]}
              onPress={() => openPicker("start")}
            >
              <Text style={styles.dateFieldLabel}>Start date</Text>
              <Text style={styles.dateFieldValue}>{formatDatePointLabel(startDate)}</Text>
            </Pressable>

            <Pressable
              style={({ pressed }) => [styles.dateField, pressed ? styles.pressed : null]}
              onPress={() => openPicker("end")}
            >
              <Text style={styles.dateFieldLabel}>End date</Text>
              <Text style={styles.dateFieldValue}>{formatDatePointLabel(endDate)}</Text>
            </Pressable>
          </View>

          <View style={styles.presetWrap}>
            {periodPresets.map((preset) => {
              const isActive = preset.key === selectedPreset;
              return (
                <Pressable
                  key={preset.key}
                  onPress={() => selectPreset(preset.key)}
                  style={({ pressed }) => [
                    styles.presetChip,
                    isActive ? styles.presetChipActive : null,
                    pressed ? styles.pressed : null
                  ]}
                >
                  <Text style={styles.presetChipText}>{preset.label}</Text>
                </Pressable>
              );
            })}
          </View>
        </GlassCard>

        <View style={styles.actionsWrap}>
          <PrimaryButton
            label="Generate your file"
            onPress={() => {
              void submitExport();
            }}
            isLoading={createExportMutation.isPending}
          />
          <SecondaryButton
            label="Download your file"
            onPress={() => {
              if (!latestExport) {
                return;
              }

              downloadMutation.mutate(latestExport.id);
            }}
            disabled={!canDownload || downloadMutation.isPending}
          />
        </View>

        {exportRequestsQuery.isError ? (
          <ErrorState
            title="Could not load statements"
            message={formatUnknownError(exportRequestsQuery.error)}
            onRetry={() => {
              void exportRequestsQuery.refetch();
            }}
          />
        ) : null}

        <GlassCard style={styles.sectionCard}>
          <Text style={styles.sectionTitle}>Export details</Text>
          <View style={styles.metaChip}>
            <View style={styles.metaLineRow}>
              <Text style={styles.metaLabel}>Status</Text>
              <Text style={styles.metaValue}>{latestExport?.status ?? "-"}</Text>
            </View>
            <View style={styles.metaLineRow}>
              <Text style={styles.metaLabel}>Last requested</Text>
              <Text style={styles.metaValue}>{formatDateTime(latestExport?.requestedUtc)}</Text>
            </View>
            <View style={styles.metaLineRow}>
              <Text style={styles.metaLabel}>File size</Text>
              <Text style={styles.metaValue}>{formatFileSize(latestExport?.fileSizeBytes ?? null)}</Text>
            </View>
            <View style={styles.metaLineRow}>
              <Text style={styles.metaLabel}>Bank filter</Text>
              <Text style={styles.metaValue}>{selectedBankLabel}</Text>
            </View>
            <View style={styles.metaLineRow}>
              <Text style={styles.metaLabel}>Date range</Text>
              <Text style={styles.metaValue}>{`${toDateOnlyString(startDate)} to ${toDateOnlyString(endDate, true)}`}</Text>
            </View>
          </View>
        </GlassCard>
      </ScrollView>

      <Modal
        visible={pickerVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setPickerVisible(false)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setPickerVisible(false)}>
          <Pressable
            style={[
              styles.modalSheet,
              { paddingBottom: spacing[12] + bottomInsetPolicy.bottomActionInsetTight }
            ]}
            onPress={() => undefined}
          >
            <Text style={styles.modalTitle}>Select a date</Text>
            <Text style={styles.modalLiveLabel}>Current selection</Text>
            <Text style={styles.modalLiveValue}>{formatDatePointLabel(pickerDraft)}</Text>

            <Text style={styles.stepLabel}>{pickerTarget === "start" ? "Start date" : "End date"}</Text>
            <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.yearRow}>
              {Array.from({ length: 9 }, (_, index) => new Date().getFullYear() - 4 + index).map((year) => (
                <Pressable
                  key={year}
                  style={[styles.yearChip, pickerDraft.year === year ? styles.yearChipActive : null]}
                  onPress={() => setPickerDraft((current) => ({ ...current, year }))}
                >
                  <Text style={styles.yearChipText}>{year}</Text>
                </Pressable>
              ))}
            </ScrollView>

            <View style={styles.monthGrid}>
              {monthNames.map((month, index) => (
                <Pressable
                  key={month}
                  style={[styles.monthChip, pickerDraft.month === index ? styles.monthChipActive : null]}
                  onPress={() => setPickerDraft((current) => ({ ...current, month: index }))}
                >
                  <Text style={styles.monthChipText}>{month}</Text>
                </Pressable>
              ))}
            </View>

            <View style={styles.daySelectorRow}>
              <Pressable
                style={[styles.dayOption, pickerDraft.day === null ? styles.dayOptionActive : null]}
                onPress={() => setPickerDraft((current) => ({ ...current, day: null }))}
              >
                <Text style={styles.dayOptionText}>Whole month</Text>
              </Pressable>
              <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.dayRow}>
                {Array.from({ length: dayCountForMonth(pickerDraft.year, pickerDraft.month) }, (_, dayIndex) => dayIndex + 1).map((day) => (
                  <Pressable
                    key={`${pickerDraft.year}-${pickerDraft.month}-${day}`}
                    style={[styles.dayOption, styles.dayNumberOption, pickerDraft.day === day ? styles.dayOptionActive : null]}
                    onPress={() => setPickerDraft((current) => ({ ...current, day }))}
                  >
                    <Text style={styles.dayOptionText}>{String(day).padStart(2, "0")}</Text>
                  </Pressable>
                ))}
              </ScrollView>
            </View>

            <View style={styles.modalActions}>
              <Pressable style={styles.modalActionButton} onPress={() => setPickerVisible(false)}>
                <Text style={styles.modalActionText}>Cancel</Text>
              </Pressable>
              <Pressable style={styles.modalActionButtonPrimary} onPress={savePickerDraft}>
                <Text style={styles.modalActionTextPrimary}>Done</Text>
              </Pressable>
            </View>
          </Pressable>
        </Pressable>
      </Modal>
    </ScreenContainer>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {
    paddingTop: 0
  },
  scrollContent: {
    gap: spacing[12],
    paddingTop: spacing[10],
    paddingBottom: spacing[12]
  },
  sectionCard: {
    gap: spacing[12]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  hintText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  dateFieldsRow: {
    flexDirection: "row",
    gap: spacing[8]
  },
  dateField: {
    flex: 1,
    minHeight: 64,
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 6,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    justifyContent: "center",
    gap: spacing[4]
  },
  dateFieldLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  dateFieldValue: {
    color: palette.textPrimary,
    ...typography.body2
  },
  presetWrap: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  presetChip: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[10],
    minHeight: 34,
    justifyContent: "center"
  },
  presetChipActive: {
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(242,140,40,0.14)"
  },
  presetChipText: {
    color: palette.textPrimary,
    ...typography.caption
  },
  metaChip: {
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 6,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[10],
    gap: spacing[8]
  },
  metaLineRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  metaLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  metaValue: {
    color: palette.textPrimary,
    ...typography.caption
  },
  actionsWrap: {
    gap: spacing[10]
  },
  pressed: {
    opacity: 0.86
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: palette.overlay,
    justifyContent: "flex-end"
  },
  modalSheet: {
    borderTopLeftRadius: 6,
    borderTopRightRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.sheet,
    padding: spacing[16],
    gap: spacing[12],
    maxHeight: "86%"
  },
  modalTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  modalLiveLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  modalLiveValue: {
    color: palette.textPrimary,
    ...typography.body1
  },
  stepLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  yearRow: {
    gap: spacing[8]
  },
  yearChip: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8]
  },
  yearChipActive: {
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(242,140,40,0.14)"
  },
  yearChipText: {
    color: palette.textPrimary,
    ...typography.caption
  },
  monthGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  monthChip: {
    width: "23%",
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    alignItems: "center",
    justifyContent: "center",
    minHeight: 36
  },
  monthChipActive: {
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(242,140,40,0.14)"
  },
  monthChipText: {
    color: palette.textPrimary,
    ...typography.caption
  },
  daySelectorRow: {
    gap: spacing[8]
  },
  dayRow: {
    gap: spacing[8]
  },
  dayOption: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8],
    alignItems: "center",
    justifyContent: "center"
  },
  dayNumberOption: {
    width: 46
  },
  dayOptionActive: {
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(242,140,40,0.14)"
  },
  dayOptionText: {
    color: palette.textPrimary,
    ...typography.caption
  },
  modalActions: {
    flexDirection: "row",
    gap: spacing[12]
  },
  modalActionButton: {
    flex: 1,
    minHeight: 44,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: surfaces.field
  },
  modalActionButtonPrimary: {
    flex: 1,
    minHeight: 44,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(242,140,40,0.34)"
  },
  modalActionText: {
    color: palette.textPrimary,
    ...typography.body2
  },
  modalActionTextPrimary: {
    color: palette.textPrimary,
    ...typography.body2
  }
}));
