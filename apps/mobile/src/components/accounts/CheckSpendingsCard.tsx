import { Ionicons } from "@expo/vector-icons";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Modal, Pressable, ScrollView, Text, View } from "react-native";
import { formatCurrency } from "../../lib/format";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../theme/tokens";
import type { TransactionDto } from "../../types/api";

type DatePoint = {
  year: number;
  month: number;
  day: number | null;
};

type DateRangeSelection = {
  start: DatePoint;
  end: DatePoint;
};

type PickerTarget = "left" | "right";
type PickerStep = "start" | "end";

const monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const DAY_CHIP_WIDTH = 46;
const DAY_CHIP_GAP = 8;

function dayCountForMonth(year: number, month: number) {
  return new Date(year, month + 1, 0).getDate();
}

function datePointToStartDate(point: DatePoint) {
  return new Date(
    point.year,
    point.month,
    point.day ?? 1,
    0,
    0,
    0,
    0
  );
}

function datePointToEndDate(point: DatePoint) {
  const day = point.day ?? dayCountForMonth(point.year, point.month);
  return new Date(
    point.year,
    point.month,
    day,
    23,
    59,
    59,
    999
  );
}

function normalizeRange(range: DateRangeSelection) {
  const start = datePointToStartDate(range.start);
  const end = datePointToEndDate(range.end);
  if (start <= end) {
    return range;
  }

  return {
    start: range.end,
    end: range.start
  };
}

function formatDatePoint(point: DatePoint, includeYear = true) {
  const monthName = monthNames[point.month];
  const dayPrefix = point.day ? `${String(point.day).padStart(2, "0")} ` : "";
  return `${dayPrefix}${monthName}${includeYear ? ` ${point.year}` : ""}`;
}

function formatRangeLabel(range: DateRangeSelection) {
  const normalized = normalizeRange(range);
  const { start, end } = normalized;
  const sameYear = start.year === end.year;
  const sameMonth = sameYear && start.month === end.month;
  const bothWholeMonth = !start.day && !end.day;
  const bothDay = Boolean(start.day && end.day);

  if (sameMonth) {
    if (bothWholeMonth) {
      return `${monthNames[start.month]} ${start.year}`;
    }

    if (bothDay && start.day === end.day) {
      return `${String(start.day).padStart(2, "0")} ${monthNames[start.month]} ${start.year}`;
    }

    if (bothDay) {
      return `${String(start.day!).padStart(2, "0")}-${String(end.day!).padStart(2, "0")} ${monthNames[start.month]} ${start.year}`;
    }

    return `${formatDatePoint(start)} - ${formatDatePoint(end)}`;
  }

  if (sameYear && bothWholeMonth) {
    return `${monthNames[start.month]} - ${monthNames[end.month]} ${start.year}`;
  }

  if (sameYear) {
    return `${formatDatePoint(start, false)} - ${formatDatePoint(end)}`;
  }

  return `${formatDatePoint(start)} - ${formatDatePoint(end)}`;
}

function keepDateOnSingleLine(label: string) {
  return label.replace(/ /g, "\u00A0");
}

function isTodayRange(range: DateRangeSelection) {
  const now = new Date();
  return (
    range.start.year === now.getFullYear() &&
    range.start.month === now.getMonth() &&
    range.start.day === now.getDate() &&
    range.end.year === now.getFullYear() &&
    range.end.month === now.getMonth() &&
    range.end.day === now.getDate()
  );
}

function computeMovement(transactions: TransactionDto[], range: DateRangeSelection) {
  const normalized = normalizeRange(range);
  const start = datePointToStartDate(normalized.start);
  const end = datePointToEndDate(normalized.end);
  const inRange = transactions.filter((transaction) => {
    const bookedAt = new Date(transaction.bookedAtUtc);
    return bookedAt >= start && bookedAt <= end;
  });

  const spend = Math.abs(
    inRange
      .filter((transaction) => transaction.amount < 0)
      .reduce((sum, transaction) => sum + transaction.amount, 0)
  );
  const income = inRange
    .filter((transaction) => transaction.amount > 0)
    .reduce((sum, transaction) => sum + transaction.amount, 0);

  return {
    spend: Number(spend.toFixed(2)),
    income: Number(income.toFixed(2)),
    net: Number((income - spend).toFixed(2))
  };
}

function datePointFromDate(date: Date): DatePoint {
  return {
    year: date.getFullYear(),
    month: date.getMonth(),
    day: date.getDate()
  };
}

type CheckSpendingsCardProps = {
  transactions: TransactionDto[];
  currency: string;
};

export function CheckSpendingsCard({ transactions, currency }: CheckSpendingsCardProps) {
  const todayPoint = useMemo(() => datePointFromDate(new Date()), []);
  const previousMonthDate = useMemo(
    () => new Date(todayPoint.year, todayPoint.month - 1, todayPoint.day ?? 1),
    [todayPoint.day, todayPoint.month, todayPoint.year]
  );
  const defaultSingleRange = useMemo(
    () => ({ start: todayPoint, end: todayPoint }),
    [todayPoint]
  );

  const [leftRange, setLeftRange] = useState<DateRangeSelection>(defaultSingleRange);
  const [rightRange, setRightRange] = useState<DateRangeSelection>({
    start: datePointFromDate(previousMonthDate),
    end: datePointFromDate(previousMonthDate)
  });
  const [isCompareMode, setIsCompareMode] = useState(false);
  const [pickerVisible, setPickerVisible] = useState(false);
  const [pickerTarget, setPickerTarget] = useState<PickerTarget>("left");
  const [pickerStep, setPickerStep] = useState<PickerStep>("start");
  const [pickerDraft, setPickerDraft] = useState<DateRangeSelection>(defaultSingleRange);
  const daySliderRef = useRef<ScrollView | null>(null);
  const [daySliderWidth, setDaySliderWidth] = useState(0);

  const activePoint = pickerStep === "start" ? pickerDraft.start : pickerDraft.end;
  const activeMonthDayCount = useMemo(
    () => dayCountForMonth(activePoint.year, activePoint.month),
    [activePoint.month, activePoint.year]
  );
  const selectableYears = useMemo(() => {
    const currentYear = new Date().getFullYear();
    return Array.from({ length: 8 }, (_, index) => currentYear - 4 + index);
  }, []);

  const leftMetrics = useMemo(
    () => computeMovement(transactions, leftRange),
    [leftRange, transactions]
  );
  const rightMetrics = useMemo(
    () => computeMovement(transactions, rightRange),
    [rightRange, transactions]
  );
  const leftLabel = formatRangeLabel(leftRange);
  const rightLabel = formatRangeLabel(rightRange);
  const leftLabelNoWrap = keepDateOnSingleLine(leftLabel);
  const rightLabelNoWrap = keepDateOnSingleLine(rightLabel);

  const singleSummary = useMemo(() => {
    if (leftMetrics.net < 0) {
      return `You have spent ${formatCurrency(Math.abs(leftMetrics.net), currency)}${isTodayRange(leftRange) ? " today" : ""}`;
    }

    if (leftMetrics.net > 0) {
      return `You have made ${formatCurrency(leftMetrics.net, currency)}${isTodayRange(leftRange) ? " today" : ""}`;
    }

    return "No movement for the selected date.";
  }, [currency, leftMetrics.net, leftRange]);

  const compareSummary = useMemo(() => {
    const difference = Number(Math.abs(leftMetrics.spend - rightMetrics.spend).toFixed(2));
    const relationship =
      leftMetrics.spend > rightMetrics.spend
        ? "more"
        : leftMetrics.spend < rightMetrics.spend
          ? "less"
          : "same";
    return {
      difference,
      relationship
    };
  }, [leftMetrics.spend, rightMetrics.spend]);

  const spendsEqual = compareSummary.relationship === "same";
  const lowerSpendIsLeft = leftMetrics.spend < rightMetrics.spend;

  const openPicker = (target: PickerTarget) => {
    setPickerTarget(target);
    setPickerStep("start");
    setPickerDraft(target === "left" ? leftRange : rightRange);
    setPickerVisible(true);
  };

  const updateActivePoint = (next: Partial<DatePoint>) => {
    setPickerDraft((current) => {
      const updatedPoint: DatePoint = {
        ...(pickerStep === "start" ? current.start : current.end),
        ...next
      };

      const dayLimit = dayCountForMonth(updatedPoint.year, updatedPoint.month);
      if (updatedPoint.day && updatedPoint.day > dayLimit) {
        updatedPoint.day = dayLimit;
      }

      return pickerStep === "start"
        ? { ...current, start: updatedPoint }
        : { ...current, end: updatedPoint };
    });
  };

  const selectDay = (day: number | null) => {
    if (day === null) {
      const monthPoint: DatePoint = {
        year: activePoint.year,
        month: activePoint.month,
        day: null
      };
      const monthRange: DateRangeSelection = {
        start: monthPoint,
        end: monthPoint
      };

      if (pickerTarget === "left") {
        setLeftRange(monthRange);
      } else {
        setRightRange(monthRange);
      }

      setPickerDraft(monthRange);
      setPickerStep("start");
      setPickerVisible(false);
      return;
    }

    updateActivePoint({ day });
  };

  const handleNextOrDone = () => {
    if (pickerStep === "start") {
      setPickerDraft((current) => ({
        ...current,
        end: { ...current.start }
      }));
      setPickerStep("end");
      return;
    }

    const normalized = normalizeRange(pickerDraft);
    if (pickerTarget === "left") {
      setLeftRange(normalized);
    } else {
      setRightRange(normalized);
    }

    setPickerVisible(false);
  };

  const centerSelectedDay = useCallback(
    (day: number) => {
      if (!daySliderRef.current || daySliderWidth <= 0) {
        return;
      }

      const dayIndex = Math.max(0, Math.min(day - 1, activeMonthDayCount - 1));
      const contentWidth =
        activeMonthDayCount * DAY_CHIP_WIDTH + Math.max(0, activeMonthDayCount - 1) * DAY_CHIP_GAP;
      const targetCenter = dayIndex * (DAY_CHIP_WIDTH + DAY_CHIP_GAP) + DAY_CHIP_WIDTH / 2;
      const rawOffset = targetCenter - daySliderWidth / 2;
      const maxOffset = Math.max(0, contentWidth - daySliderWidth);
      const nextOffset = Math.max(0, Math.min(rawOffset, maxOffset));

      daySliderRef.current.scrollTo({ x: nextOffset, y: 0, animated: false });
    },
    [activeMonthDayCount, daySliderWidth]
  );

  useEffect(() => {
    if (!pickerVisible || activePoint.day === null) {
      return;
    }

    const timeout = setTimeout(() => {
      centerSelectedDay(activePoint.day!);
    }, 0);

    return () => clearTimeout(timeout);
  }, [activePoint.day, centerSelectedDay, pickerVisible]);

  return (
    <View style={styles.card}>
      <View style={styles.headerRow}>
        <Text style={styles.title}>Check your spendings</Text>
        <Pressable
          style={({ pressed }) => [styles.addButton, pressed ? styles.pressed : null]}
          onPress={() => setIsCompareMode((current) => !current)}
        >
          <Ionicons name={isCompareMode ? "remove" : "add"} size={20} color={palette.textPrimary} />
        </Pressable>
      </View>

      {!isCompareMode ? (
        <>
          <View style={styles.singleChipWrap}>
            <Pressable
              style={({ pressed }) => [styles.dateChip, pressed ? styles.pressed : null]}
              onPress={() => openPicker("left")}
            >
              <Text style={styles.dateChipText}>{leftLabel}</Text>
            </Pressable>
          </View>
          <Text style={styles.currentStatus}>Current status for {leftLabel}</Text>
          <Text style={[styles.singleAmount, leftMetrics.net < 0 ? styles.higherSpend : styles.lowerSpend]}>
            {formatCurrency(Math.abs(leftMetrics.net), currency)}
          </Text>
          <Text style={styles.singleSummary}>{singleSummary}</Text>
        </>
      ) : (
        <>
          <View style={styles.compareColumns}>
            <View style={styles.compareBlock}>
              <Pressable
                style={({ pressed }) => [styles.dateChip, styles.compareDateChip, pressed ? styles.pressed : null]}
                onPress={() => openPicker("left")}
              >
                <Text style={styles.dateChipText}>{leftLabel}</Text>
              </Pressable>
              <Text
                style={[
                  styles.compareAmount,
                  spendsEqual ? styles.equalSpend : lowerSpendIsLeft ? styles.lowerSpend : styles.higherSpend
                ]}
              >
                {formatCurrency(leftMetrics.spend, currency)}
              </Text>
              <Text style={styles.compareLabel}>{leftLabel}</Text>
            </View>
            <View style={styles.compareBlock}>
              <Pressable
                style={({ pressed }) => [styles.dateChip, styles.compareDateChip, pressed ? styles.pressed : null]}
                onPress={() => openPicker("right")}
              >
                <Text style={styles.dateChipText}>{rightLabel}</Text>
              </Pressable>
              <Text
                style={[
                  styles.compareAmount,
                  spendsEqual ? styles.equalSpend : !lowerSpendIsLeft ? styles.lowerSpend : styles.higherSpend
                ]}
              >
                {formatCurrency(rightMetrics.spend, currency)}
              </Text>
              <Text style={styles.compareLabel}>{rightLabel}</Text>
            </View>
          </View>
          {compareSummary.relationship === "more" ? (
            <Text style={styles.compareSummary}>
              You have spent {formatCurrency(compareSummary.difference, currency)} more on{" "}
              <Text style={styles.compareDateToken}>{leftLabelNoWrap}</Text> than on{" "}
              <Text style={styles.compareDateToken}>{rightLabelNoWrap}</Text>
            </Text>
          ) : compareSummary.relationship === "less" ? (
            <Text style={styles.compareSummary}>
              You have spent {formatCurrency(compareSummary.difference, currency)} less on{" "}
              <Text style={styles.compareDateToken}>{leftLabelNoWrap}</Text> than on{" "}
              <Text style={styles.compareDateToken}>{rightLabelNoWrap}</Text>
            </Text>
          ) : (
            <Text style={styles.compareSummary}>
              You have spent the same amount on both <Text style={styles.compareDateToken}>{leftLabelNoWrap}</Text>{" "}
              and <Text style={styles.compareDateToken}>{rightLabelNoWrap}</Text>
            </Text>
          )}
        </>
      )}

      <Modal
        visible={pickerVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setPickerVisible(false)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setPickerVisible(false)}>
          <Pressable style={styles.modalSheet} onPress={() => undefined}>
            <Text style={styles.modalTitle}>Select a date</Text>
            <Text style={styles.modalLiveLabel}>Current selection</Text>
            <Text style={styles.modalLiveValue}>{formatRangeLabel(pickerDraft)}</Text>

            <Text style={styles.stepLabel}>{pickerStep === "start" ? "Start date" : "End date"}</Text>
            <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.yearRow}>
              {selectableYears.map((year) => (
                <Pressable
                  key={year}
                  style={[styles.yearChip, activePoint.year === year ? styles.yearChipActive : null]}
                  onPress={() => updateActivePoint({ year })}
                >
                  <Text style={styles.yearChipText}>{year}</Text>
                </Pressable>
              ))}
            </ScrollView>

            <View style={styles.monthGrid}>
              {monthNames.map((month, index) => (
                <Pressable
                  key={month}
                  style={[styles.monthChip, activePoint.month === index ? styles.monthChipActive : null]}
                  onPress={() => updateActivePoint({ month: index })}
                >
                  <Text style={styles.monthChipText}>{month}</Text>
                </Pressable>
              ))}
            </View>

            <View style={styles.daySelectorRow}>
              <Pressable
                style={[styles.dayOption, activePoint.day === null ? styles.dayOptionActive : null]}
                onPress={() => selectDay(null)}
              >
                <Text style={styles.dayOptionText}>Whole month</Text>
              </Pressable>
              <ScrollView
                ref={daySliderRef}
                horizontal
                showsHorizontalScrollIndicator={false}
                contentContainerStyle={styles.dayRow}
                onLayout={(event) => setDaySliderWidth(event.nativeEvent.layout.width)}
              >
                {Array.from({ length: dayCountForMonth(activePoint.year, activePoint.month) }, (_, dayIndex) => dayIndex + 1).map((day) => (
                  <Pressable
                    key={`${activePoint.year}-${activePoint.month}-${day}`}
                    style={[styles.dayOption, styles.dayNumberOption, activePoint.day === day ? styles.dayOptionActive : null]}
                    onPress={() => selectDay(day)}
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
              <Pressable style={styles.modalActionButtonPrimary} onPress={handleNextOrDone}>
                <Text style={styles.modalActionTextPrimary}>
                  {pickerStep === "start" ? "Next" : "Done"}
                </Text>
              </Pressable>
            </View>
          </Pressable>
        </Pressable>
      </Modal>
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  card: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.card,
    padding: spacing[12],
    gap: spacing[12]
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  title: {
    color: palette.textPrimary,
    ...typography.sectionTitle
  },
  addButton: {
    width: 34,
    height: 34,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    alignItems: "center",
    justifyContent: "center"
  },
  singleChipWrap: {
    alignItems: "center"
  },
  dateChip: {
    minHeight: 38,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    backgroundColor: surfaces.fieldStrong,
    paddingHorizontal: spacing[12],
    alignItems: "center",
    justifyContent: "center"
  },
  dateChipText: {
    color: palette.textPrimary,
    ...typography.caption
  },
  currentStatus: {
    color: palette.textSecondary,
    ...typography.caption,
    textAlign: "center"
  },
  singleAmount: {
    textAlign: "center",
    ...typography.title1
  },
  singleSummary: {
    color: palette.textSecondary,
    ...typography.body2,
    textAlign: "center"
  },
  compareColumns: {
    flexDirection: "row",
    gap: spacing[12]
  },
  compareBlock: {
    flex: 1,
    alignItems: "center",
    gap: spacing[8]
  },
  compareDateChip: {
    alignSelf: "stretch"
  },
  compareAmount: {
    ...typography.title2
  },
  compareLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    textAlign: "center"
  },
  compareSummary: {
    color: palette.textSecondary,
    ...typography.body2,
    textAlign: "center"
  },
  compareDateToken: {
    color: palette.textPrimary
  },
  equalSpend: {
    color: palette.textPrimary
  },
  lowerSpend: {
    color: palette.success
  },
  higherSpend: {
    color: palette.negative
  },
  pressed: {
    opacity: 0.88
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
    width: DAY_CHIP_WIDTH
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


