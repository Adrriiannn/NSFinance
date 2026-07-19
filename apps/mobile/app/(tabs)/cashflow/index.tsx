import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Animated, Pressable, RefreshControl, ScrollView, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { SpendTrendGraph } from "../../../src/components/planner/SpendTrendGraph";
import { AnimatedCurrencyText } from "../../../src/components/ui/AnimatedCurrencyText";
import { Card } from "../../../src/components/ui/cards/Card";
import { useMainTabSwipeNavigation } from "../../../src/components/layout/useHorizontalSiblingSwipe";
import { Skeleton } from "../../../src/components/ui/feedback/Skeleton";
import { TabEmptyStateCard } from "../../../src/components/ui/TabEmptyStateCard";
import { SystemModal } from "../../../src/components/ui/surfaces/SystemModal";
import { AdaptiveScreen } from "../../../src/layout/adaptive/AdaptiveScreen";
import { HeaderShell } from "../../../src/layout/appHeader";
import {
  CONTENT_FRAME_HEADER_GAP,
  getDockAwareContentBottomInset
} from "../../../src/layout/contentFrame";
import { useDashboardSummaryQuery } from "../../../src/features/dashboard/useDashboardSummaryQuery";
import { buildConnectBankRoute } from "../../../src/features/banking/bankingLinking";
import { useConnectBankCtaLabels } from "../../../src/features/banking/connectBankCta";
import {
  buildPlannerGraphModel,
  type PlannerComparisonPeriod,
  buildRecurringPaymentForecast
} from "../../../src/features/planner/forecasting";
import { buildPlannerSuggestions } from "../../../src/features/planner/plannerInsights";
import { useRecurringPaymentsQuery } from "../../../src/features/banking/useBanking";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { useThemeRuntime } from "../../../src/theme/runtime/ThemeRuntimeProvider";
import { layout, palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";
import type { BankRecurringPaymentsDto } from "../../../src/types/api";

function formatCountdown(daysUntilDue: number) {
  if (daysUntilDue <= 0) {
    return "today";
  }

  return `in ${daysUntilDue} day${daysUntilDue === 1 ? "" : "s"}`;
}

const MONTH_NAMES = [
  "January",
  "February",
  "March",
  "April",
  "May",
  "June",
  "July",
  "August",
  "September",
  "October",
  "November",
  "December"
];
const MONTH_ROW_HEIGHT = 44;
const MONTH_WHEEL_VISIBLE_ROWS = 5;
// Keep the wheel effectively infinite via recentering after each scroll end,
// without rendering thousands of rows up front.
const MONTH_VIRTUAL_RADIUS = 120;
const MONTH_VIRTUAL_COUNT = MONTH_VIRTUAL_RADIUS * 2 + 1;
const MONTH_VIRTUAL_CENTER_INDEX = MONTH_VIRTUAL_RADIUS;
const YEAR_CHIP_WIDTH = 82;
const YEAR_CHIP_GAP = 8;

function isFutureMonth(year: number, month: number, now: Date) {
  return year > now.getFullYear() || (year === now.getFullYear() && month > now.getMonth());
}

function splitAbsoluteMonth(absoluteMonth: number) {
  const year = Math.floor(absoluteMonth / 12);
  const month = ((absoluteMonth % 12) + 12) % 12;
  return { year, month };
}

type ProviderRecurringPayment = {
  id: string;
  label: string;
  amount: number | null;
  currency: string | null;
  nextPaymentDateUtc: string | null;
  source: "direct_debit" | "standing_order";
};

function normalizeProviderRecurringPayments(data: BankRecurringPaymentsDto | undefined): ProviderRecurringPayment[] {
  if (!data) {
    return [];
  }

  const directDebits = data.directDebits.map((entry) => ({
    id: `dd-${entry.id}`,
    label: entry.merchantName || entry.reference || entry.accountDisplayName || "Direct debit",
    amount: entry.nextPaymentAmount,
    currency: entry.nextPaymentCurrency,
    nextPaymentDateUtc: entry.nextPaymentDateUtc,
    source: "direct_debit" as const
  }));

  const standingOrders = data.standingOrders.map((entry) => ({
    id: `so-${entry.id}`,
    label: entry.payeeName || entry.reference || entry.accountDisplayName || "Standing order",
    amount: entry.nextPaymentAmount,
    currency: entry.nextPaymentCurrency,
    nextPaymentDateUtc: entry.nextPaymentDateUtc,
    source: "standing_order" as const
  }));

  return [...directDebits, ...standingOrders].sort((left, right) => {
    const leftStamp = left.nextPaymentDateUtc ? Date.parse(left.nextPaymentDateUtc) : Number.MAX_SAFE_INTEGER;
    const rightStamp = right.nextPaymentDateUtc ? Date.parse(right.nextPaymentDateUtc) : Number.MAX_SAFE_INTEGER;
    return leftStamp - rightStamp;
  });
}

function computeDaysUntilDue(nextPaymentDateUtc: string | null, now: Date) {
  if (!nextPaymentDateUtc) {
    return 0;
  }

  const dueDate = new Date(nextPaymentDateUtc);
  if (Number.isNaN(dueDate.getTime())) {
    return 0;
  }

  const diffMs = dueDate.getTime() - now.getTime();
  return Math.max(0, Math.ceil(diffMs / (24 * 60 * 60 * 1000)));
}

export default function CashflowScreen() {
  useThemeRuntime();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const { gestureHandlers, animatedStyle } = useMainTabSwipeNavigation("/(tabs)/cashflow");
  const dashboardQuery = useDashboardSummaryQuery();
  const transactionsQuery = useTransactionsQuery();
  const recurringPaymentsQuery = useRecurringPaymentsQuery();
  const connectBankCta = useConnectBankCtaLabels();
  const [clockNow, setClockNow] = useState(() => new Date());
  const [currentPeriod, setCurrentPeriod] = useState<PlannerComparisonPeriod>(() => ({
    year: new Date().getFullYear(),
    month: new Date().getMonth()
  }));
  const [previousPeriod, setPreviousPeriod] = useState<PlannerComparisonPeriod>(() => {
    const previous = new Date(new Date().getFullYear(), new Date().getMonth() - 1, 1);
    return {
      year: previous.getFullYear(),
      month: previous.getMonth()
    };
  });
  const [pickerTarget, setPickerTarget] = useState<"current" | "previous" | null>(null);
  const [pickerYear, setPickerYear] = useState(() => new Date().getFullYear());
  const [pickerMonth, setPickerMonth] = useState(() => new Date().getMonth());
  const [isManualRefreshing, setIsManualRefreshing] = useState(false);
  const [yearRailWidth, setYearRailWidth] = useState(0);
  const yearWheelRef = useRef<ScrollView | null>(null);
  const monthWheelRef = useRef<ScrollView | null>(null);
  const monthWheelAnchorAbsoluteRef = useRef(new Date().getFullYear() * 12 + new Date().getMonth());

  useEffect(() => {
    const interval = setInterval(() => {
      setClockNow(new Date());
    }, 60_000);

    return () => clearInterval(interval);
  }, []);

  const isLoading =
    (dashboardQuery.isLoading && !dashboardQuery.data) ||
    (transactionsQuery.isLoading && !transactionsQuery.data);
  const refreshing = isManualRefreshing && !isLoading;
  const error = dashboardQuery.error ?? transactionsQuery.error;
  const transactions = useMemo(() => transactionsQuery.data ?? [], [transactionsQuery.data]);
  const hasCashflowData = (dashboardQuery.data?.accountCount ?? 0) > 0 && transactions.length > 0;
  const handleRefresh = useCallback(async () => {
    setIsManualRefreshing(true);
    try {
      await Promise.all([dashboardQuery.refetch(), transactionsQuery.refetch()]);
    } finally {
      setIsManualRefreshing(false);
    }
  }, [dashboardQuery, transactionsQuery]);

  const yearOptions = useMemo(() => {
    const currentYear = clockNow.getFullYear();
    const minYear = Math.min(currentYear - 20, pickerYear - 8);
    const maxYear = Math.max(currentYear + 6, pickerYear + 8);
    return Array.from({ length: maxYear - minYear + 1 }, (_, index) => minYear + index);
  }, [clockNow, pickerYear]);
  const monthWheelIndices = useMemo(
    () => Array.from({ length: MONTH_VIRTUAL_COUNT }, (_, index) => index),
    []
  );
  const pickerOpen = pickerTarget !== null;
  const pickerAbsoluteMonth = pickerYear * 12 + pickerMonth;

  const graphModel = useMemo(
    () =>
      buildPlannerGraphModel(transactions, {
        currentPeriod,
        previousPeriod,
        now: clockNow
      }),
    [clockNow, currentPeriod, previousPeriod, transactions]
  );
  const recurringForecast = useMemo(
    () => buildRecurringPaymentForecast(transactions, clockNow),
    [clockNow, transactions]
  );
  const providerRecurringPayments = useMemo(
    () => normalizeProviderRecurringPayments(recurringPaymentsQuery.data),
    [recurringPaymentsQuery.data]
  );
  const providerUpcoming = useMemo(() => {
    return providerRecurringPayments
      .map((entry) => ({
        ...entry,
        daysUntilDue: computeDaysUntilDue(entry.nextPaymentDateUtc, clockNow)
      }))
      .slice(0, 3);
  }, [clockNow, providerRecurringPayments]);
  const suggestions = useMemo(
    () =>
      buildPlannerSuggestions({
        dashboard: dashboardQuery.data,
        transactions,
        annotations: {}
      }),
    [dashboardQuery.data, transactions]
  );
  const currentDisplayAmount = -graphModel.currentSpend;
  const previousDisplayAmount = -graphModel.previousSpend;
  const currentAmountVerb = currentDisplayAmount > 0 ? "made" : "spent";
  const previousAmountVerb = previousDisplayAmount > 0 ? "made" : "spent";
  const isCurrentPeriodThisMonth =
    currentPeriod.year === clockNow.getFullYear() && currentPeriod.month === clockNow.getMonth();
  const summaryAmountValue = Math.abs(currentDisplayAmount);
  const summaryAmountVerb = currentDisplayAmount >= 0 ? "earned" : "spent";
  const summaryComparisonBase =
    graphModel.previousSpend > 0 ? graphModel.previousSpend : graphModel.currentSpend;
  const summaryComparisonPercent =
    summaryComparisonBase > 0
      ? Math.abs(((graphModel.currentSpend - graphModel.previousSpend) / summaryComparisonBase) * 100)
      : 0;
  const summaryComparisonDirection =
    graphModel.currentSpend <= graphModel.previousSpend ? "less" : "more";
  const summaryAmountLabel = new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency: graphModel.displayCurrency
  }).format(summaryAmountValue);
  const summaryLeadText = isCurrentPeriodThisMonth
    ? "So far this month you've"
    : `In ${graphModel.currentMonthLabel}, you've`;

  const openPicker = (target: "current" | "previous") => {
    const source = target === "current" ? currentPeriod : previousPeriod;
    const sourceAbsoluteMonth = source.year * 12 + source.month;
    monthWheelAnchorAbsoluteRef.current = sourceAbsoluteMonth;
    setPickerTarget(target);
    setPickerYear(source.year);
    setPickerMonth(source.month);
  };

  const closePicker = () => {
    setPickerTarget(null);
  };

  const applyPicker = () => {
    if (!pickerTarget || isFutureMonth(pickerYear, pickerMonth, clockNow)) {
      return;
    }

    const selection: PlannerComparisonPeriod = {
      year: pickerYear,
      month: pickerMonth
    };

    if (pickerTarget === "current") {
      setCurrentPeriod(selection);
    } else {
      setPreviousPeriod(selection);
    }

    setPickerTarget(null);
  };

  const onMonthWheelEnd = (offsetY: number) => {
    const virtualIndex = Math.round(offsetY / MONTH_ROW_HEIGHT);
    const deltaFromCenter = virtualIndex - MONTH_VIRTUAL_CENTER_INDEX;
    const nextAbsoluteMonth = monthWheelAnchorAbsoluteRef.current + deltaFromCenter;
    const { year, month } = splitAbsoluteMonth(nextAbsoluteMonth);

    monthWheelAnchorAbsoluteRef.current = nextAbsoluteMonth;
    setPickerYear(year);
    setPickerMonth(month);

    requestAnimationFrame(() => {
      monthWheelRef.current?.scrollTo({
        y: MONTH_VIRTUAL_CENTER_INDEX * MONTH_ROW_HEIGHT,
        animated: false
      });
    });
  };

  useEffect(() => {
    if (!pickerOpen) {
      return;
    }

    requestAnimationFrame(() => {
      monthWheelRef.current?.scrollTo({
        y: MONTH_VIRTUAL_CENTER_INDEX * MONTH_ROW_HEIGHT,
        animated: false
      });
    });
  }, [pickerOpen, pickerYear, pickerMonth]);

  useEffect(() => {
    if (!pickerOpen || yearRailWidth <= 0) {
      return;
    }

    const selectedYearIndex = yearOptions.indexOf(pickerYear);
    if (selectedYearIndex < 0) {
      return;
    }

    const contentWidth =
      yearOptions.length * YEAR_CHIP_WIDTH + (yearOptions.length - 1) * YEAR_CHIP_GAP;
    const targetOffset =
      selectedYearIndex * (YEAR_CHIP_WIDTH + YEAR_CHIP_GAP) -
      (yearRailWidth - YEAR_CHIP_WIDTH) / 2;
    const clampedOffset = Math.max(0, Math.min(targetOffset, Math.max(0, contentWidth - yearRailWidth)));

    requestAnimationFrame(() => {
      yearWheelRef.current?.scrollTo({
        x: clampedOffset,
        animated: true
      });
    });
  }, [pickerOpen, pickerYear, yearOptions, yearRailWidth]);

  return (
    <AdaptiveScreen contentStyle={styles.content} gestureHandlers={gestureHandlers}>
      <Animated.View style={[styles.tabStage, animatedStyle]}>
        <HeaderShell preset="primaryDefault" includeTopInset title="Cashflow" />

      <ScrollView
        contentContainerStyle={[
          styles.scrollContent,
          {
            paddingTop: CONTENT_FRAME_HEADER_GAP,
            paddingBottom: getDockAwareContentBottomInset(insets.bottom)
          }
        ]}
        showsVerticalScrollIndicator={false}
        bounces={false}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => {
              void handleRefresh();
            }}
            tintColor={palette.textSecondary}
          />
        }
      >
        {isLoading ? (
          <View style={styles.loadingWrap}>
            <Skeleton style={styles.loadingHero} />
            <Skeleton style={styles.loadingCard} />
            <Skeleton style={styles.loadingCard} />
          </View>
        ) : error ? (
          <ErrorState
            title="Could not load cashflow"
            message={error.message}
            onRetry={() => {
              void Promise.all([dashboardQuery.refetch(), transactionsQuery.refetch()]);
            }}
          />
        ) : !hasCashflowData ? (
          <TabEmptyStateCard
            title="No cashflow data yet"
            subtitle="Connect your bank to start building cashflow insights from your spending data."
            ctaLabel={connectBankCta.primaryLabel}
            onCtaPress={() =>
              router.push(buildConnectBankRoute({ intent: "new", returnTo: "/(tabs)/cashflow" }))
            }
            verticalSpacingMode="tab-aligned"
            hideOrb
            centerText
          />
        ) : (
          <>
            <Card style={styles.graphCard}>
              <View style={styles.graphTitleRow}>
                <Pressable
                  style={styles.graphMonthDropdown}
                  onPress={() => openPicker("current")}
                >
                  <Text style={[styles.graphTitle, styles.currentMonthLabel]}>
                    {graphModel.currentMonthLabel}
                  </Text>
                  <MaterialCommunityIcons name="chevron-down" size={18} color={palette.success} />
                </Pressable>
                <Text style={styles.graphTitlePlain}>vs</Text>
                <Pressable
                  style={styles.graphMonthDropdown}
                  onPress={() => openPicker("previous")}
                >
                  <Text style={[styles.graphTitle, styles.previousMonthLabel]}>
                    {graphModel.previousMonthLabel}
                  </Text>
                  <MaterialCommunityIcons name="chevron-down" size={18} color={palette.negative} />
                </Pressable>
              </View>
              <SpendTrendGraph
                primarySeries={graphModel.currentSeries}
                secondarySeries={graphModel.previousSeries}
                xCheckpoints={graphModel.xCheckpoints}
                yCheckpoints={graphModel.yCheckpoints}
                currency={graphModel.displayCurrency}
                monthDate={new Date(currentPeriod.year, currentPeriod.month, 1)}
                maxValue={Math.max(...graphModel.currentSeries, ...graphModel.previousSeries, 1)}
                height={186}
              />
              <View style={styles.graphValuesRow}>
                <View style={[styles.graphValueBlock, styles.graphValueBlockLeft]}>
                  <AnimatedCurrencyText
                    value={currentDisplayAmount}
                    currency={graphModel.displayCurrency}
                    style={[styles.graphValue, styles.currentMonthLabel]}
                    baseColor={palette.success}
                  />
                  <Text style={styles.graphValueLabel}>
                    {currentAmountVerb} in {graphModel.currentMonthLabel}
                  </Text>
                </View>
                <View style={[styles.graphValueBlock, styles.graphValueBlockRight]}>
                  <AnimatedCurrencyText
                    value={previousDisplayAmount}
                    currency={graphModel.displayCurrency}
                    style={[styles.graphValue, styles.graphValueRightAlign, styles.previousMonthLabel]}
                    baseColor={palette.negative}
                  />
                  <Text style={[styles.graphValueLabel, styles.graphValueLabelRightAlign]}>
                    {previousAmountVerb} in {graphModel.previousMonthLabel}
                  </Text>
                </View>
              </View>
              <View style={styles.graphSummaryWrap}>
                <Text style={styles.graphMeta}>
                  {`${summaryLeadText} ${summaryAmountVerb} ${summaryAmountLabel}.`}
                </Text>
                <Text style={styles.graphMeta}>
                  That is {summaryComparisonPercent.toFixed(0)}% {summaryComparisonDirection} than the same
                  period last month.
                </Text>
              </View>
            </Card>

            <Card style={styles.insightCard}>
              <Text style={styles.insightTitle}>{graphModel.bucketTitle}</Text>
              <Text style={styles.insightBody}>{graphModel.bucketMessage}</Text>
            </Card>

            <Pressable
              style={({ pressed }) => [styles.upcomingPaymentsCard, pressed ? styles.cardPressed : null]}
              onPress={() => router.push("/(tabs)/cashflow/upcoming-payments")}
            >
              <Text style={styles.upcomingTitle}>Upcoming payments</Text>
              {providerUpcoming.length > 0 ? (
                providerUpcoming.map((payment) => (
                  <View key={payment.id} style={styles.upcomingRow}>
                    <Text style={styles.upcomingLabel}>
                      {payment.label}{" "}
                      <Text style={styles.upcomingSource}>
                        ({payment.source === "direct_debit" ? "direct debit" : "standing order"})
                      </Text>
                    </Text>
                    <Text style={styles.upcomingMeta}>
                      {payment.amount !== null && payment.currency
                        ? new Intl.NumberFormat("en-GB", {
                            style: "currency",
                            currency: payment.currency
                          }).format(payment.amount)
                        : "Amount pending"}{" "}
                      {payment.nextPaymentDateUtc ? formatCountdown(payment.daysUntilDue) : "date pending"}
                    </Text>
                  </View>
                ))
              ) : recurringForecast.next7Days.length > 0 ? (
                recurringForecast.next7Days.slice(0, 3).map((payment) => (
                  <View key={payment.id} style={styles.upcomingRow}>
                    <Text style={styles.upcomingLabel}>{payment.label}</Text>
                    <Text style={styles.upcomingMeta}>
                      {new Intl.NumberFormat("en-GB", {
                        style: "currency",
                        currency: payment.currency
                      }).format(payment.amount)}{" "}
                      {formatCountdown(payment.daysUntilDue)}
                    </Text>
                  </View>
                ))
              ) : (
                <Text style={styles.upcomingEmpty}>
                  No provider recurring payments available yet. Detected recurring transactions will appear as history grows.
                </Text>
              )}
            </Pressable>

            <Text style={styles.suggestionsTitle}>Suggestions</Text>
            <View style={styles.suggestionsWrap}>
              {suggestions.slice(0, 3).map((suggestion) => (
                <Card key={suggestion.id} style={styles.suggestionCard}>
                  <Text style={styles.suggestionTitle}>{suggestion.title}</Text>
                  <Text style={styles.suggestionMessage}>{suggestion.message}</Text>
                </Card>
              ))}
              {suggestions.length === 0 ? (
                <Card style={styles.suggestionCard}>
                  <Text style={styles.suggestionMessage}>
                    Suggestions will appear when more cashflow context is available.
                  </Text>
                </Card>
              ) : null}
            </View>
          </>
        )}
      </ScrollView>

      <SystemModal
        visible={pickerOpen}
        transparent
        animationType="fade"
        onRequestClose={closePicker}
      >
        <Pressable style={styles.pickerOverlay} onPress={closePicker}>
          <Pressable style={styles.pickerSheet} onPress={() => undefined}>
            <Text style={styles.pickerTitle}>Select month</Text>
            <Text style={styles.pickerSubtitle}>
              {pickerTarget === "current" ? "Current period" : "Comparison period"}
            </Text>

            <ScrollView
              ref={yearWheelRef}
              horizontal
              showsHorizontalScrollIndicator={false}
              onLayout={(event) => setYearRailWidth(event.nativeEvent.layout.width)}
              contentContainerStyle={styles.yearRow}
            >
              {yearOptions.map((year) => {
                const disabled = year > clockNow.getFullYear();
                const selected = year === pickerYear;
                return (
                  <Pressable
                    key={`year-${year}`}
                    onPress={() => {
                      if (disabled) {
                        return;
                      }

                      const safeMonth = isFutureMonth(year, pickerMonth, clockNow)
                        ? clockNow.getMonth()
                        : pickerMonth;
                      monthWheelAnchorAbsoluteRef.current = year * 12 + safeMonth;
                      setPickerYear(year);
                      setPickerMonth(safeMonth);
                      requestAnimationFrame(() => {
                        monthWheelRef.current?.scrollTo({
                          y: MONTH_VIRTUAL_CENTER_INDEX * MONTH_ROW_HEIGHT,
                          animated: false
                        });
                      });
                    }}
                    style={[
                      styles.yearChip,
                      selected ? styles.yearChipActive : null,
                      disabled ? styles.yearChipDisabled : null
                    ]}
                  >
                    <Text
                      style={[
                        styles.yearChipText,
                        selected ? styles.yearChipTextActive : null,
                        disabled ? styles.yearChipTextDisabled : null
                      ]}
                    >
                      {year}
                    </Text>
                  </Pressable>
                );
              })}
            </ScrollView>

            <View style={styles.monthWheelWrap}>
              <View style={styles.monthWheelHighlight} pointerEvents="none" />
              <ScrollView
                ref={monthWheelRef}
                showsVerticalScrollIndicator={false}
                snapToInterval={MONTH_ROW_HEIGHT}
                decelerationRate="fast"
                contentContainerStyle={styles.monthWheelContent}
                onMomentumScrollEnd={(event) => onMonthWheelEnd(event.nativeEvent.contentOffset.y)}
                onScrollEndDrag={(event) => onMonthWheelEnd(event.nativeEvent.contentOffset.y)}
              >
                {monthWheelIndices.map((virtualIndex) => {
                  const absoluteMonth =
                    monthWheelAnchorAbsoluteRef.current +
                    (virtualIndex - MONTH_VIRTUAL_CENTER_INDEX);
                  const { year: monthYear, month: monthIndex } = splitAbsoluteMonth(absoluteMonth);
                  const monthName = MONTH_NAMES[monthIndex];
                  const disabled = isFutureMonth(monthYear, monthIndex, clockNow);
                  const selected = absoluteMonth === pickerAbsoluteMonth;
                  return (
                    <Pressable
                      key={`month-${virtualIndex}`}
                      onPress={() => {
                        if (!disabled) {
                          monthWheelAnchorAbsoluteRef.current = absoluteMonth;
                          setPickerYear(monthYear);
                          setPickerMonth(monthIndex);
                          requestAnimationFrame(() => {
                            monthWheelRef.current?.scrollTo({
                              y: MONTH_VIRTUAL_CENTER_INDEX * MONTH_ROW_HEIGHT,
                              animated: false
                            });
                          });
                        }
                      }}
                      style={styles.monthWheelRow}
                    >
                      <Text
                        style={[
                          styles.monthWheelText,
                          selected ? styles.monthWheelTextActive : null,
                          disabled ? styles.monthWheelTextDisabled : null
                        ]}
                      >
                        {monthName}
                      </Text>
                    </Pressable>
                  );
                })}
              </ScrollView>
            </View>

            <View style={styles.pickerActionRow}>
              <Pressable style={styles.pickerSecondaryButton} onPress={closePicker}>
                <Text style={styles.pickerSecondaryText}>Cancel</Text>
              </Pressable>
              <Pressable
                style={[
                  styles.pickerPrimaryButton,
                  isFutureMonth(pickerYear, pickerMonth, clockNow) ? styles.pickerPrimaryButtonDisabled : null
                ]}
                onPress={applyPicker}
                disabled={isFutureMonth(pickerYear, pickerMonth, clockNow)}
              >
                <Text style={styles.pickerPrimaryText}>Apply</Text>
              </Pressable>
            </View>
          </Pressable>
        </Pressable>
      </SystemModal>
      </Animated.View>
    </AdaptiveScreen>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {
    flex: 1
  },
  tabStage: {
    flex: 1
  },
  scrollContent: {
    gap: layout.sectionGap
  },
  graphCard: {
    gap: spacing[12]
  },
  graphTitleRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8]
  },
  graphMonthDropdown: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[4]
  },
  graphTitle: {
    ...typography.bodyStrong
  },
  graphTitlePlain: {
    color: palette.textPrimary
  },
  currentMonthLabel: {
    color: palette.success
  },
  previousMonthLabel: {
    color: palette.negative
  },
  graphValuesRow: {
    flexDirection: "row",
    gap: spacing[12]
  },
  graphValueBlock: {
    flex: 1,
    gap: spacing[4]
  },
  graphValueBlockLeft: {
    alignItems: "flex-start"
  },
  graphValueBlockRight: {
    alignItems: "flex-end"
  },
  graphValue: {
    ...typography.bodyStrong,
    fontVariant: ["tabular-nums"]
  },
  graphValueRightAlign: {
    textAlign: "right"
  },
  graphValueLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  graphValueLabelRightAlign: {
    textAlign: "right"
  },
  graphMeta: {
    color: palette.textSecondary,
    ...typography.body2
  },
  graphSummaryWrap: {
    gap: spacing[4]
  },
  insightCard: {
    gap: spacing[8]
  },
  insightTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  insightBody: {
    color: palette.textSecondary,
    ...typography.body2
  },
  upcomingPaymentsCard: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.card,
    padding: spacing[12],
    gap: spacing[8]
  },
  upcomingTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  upcomingRow: {
    gap: spacing[4]
  },
  upcomingLabel: {
    color: palette.textPrimary,
    ...typography.body2
  },
  upcomingSource: {
    color: palette.textSecondary,
    ...typography.caption
  },
  upcomingMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  upcomingEmpty: {
    color: palette.textSecondary,
    ...typography.caption
  },
  suggestionsTitle: {
    color: palette.textPrimary,
    ...typography.sectionTitle
  },
  suggestionsWrap: {
    gap: spacing[12]
  },
  suggestionCard: {
    gap: spacing[8]
  },
  suggestionTitle: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  suggestionMessage: {
    color: palette.textSecondary,
    ...typography.body2
  },
  cardPressed: {
    opacity: 0.9,
    transform: [{ scale: 0.995 }]
  },
  loadingWrap: {
    gap: spacing[12]
  },
  loadingHero: {
    height: 210,
    borderRadius: 6
  },
  loadingCard: {
    height: 108,
    borderRadius: 6
  },
  pickerOverlay: {
    flex: 1,
    backgroundColor: palette.overlay,
    justifyContent: "center",
    paddingHorizontal: spacing[16]
  },
  pickerSheet: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.sheet,
    padding: spacing[16],
    gap: spacing[12]
  },
  pickerTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  pickerSubtitle: {
    color: palette.textSecondary,
    ...typography.caption
  },
  yearRow: {
    gap: YEAR_CHIP_GAP
  },
  yearChip: {
    width: YEAR_CHIP_WIDTH,
    minHeight: 36,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  yearChipActive: {
    borderColor: palette.primaryGlow,
    backgroundColor: "rgba(242,140,40,0.22)"
  },
  yearChipDisabled: {
    borderColor: "rgba(242,140,40,0.16)",
    backgroundColor: surfaces.muted
  },
  yearChipText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  yearChipTextActive: {
    color: palette.textPrimary,
    fontWeight: "600"
  },
  yearChipTextDisabled: {
    color: "rgba(242,140,40,0.38)"
  },
  monthWheelWrap: {
    height: MONTH_ROW_HEIGHT * MONTH_WHEEL_VISIBLE_ROWS,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    overflow: "hidden"
  },
  monthWheelHighlight: {
    position: "absolute",
    left: 0,
    right: 0,
    top: MONTH_ROW_HEIGHT * 2,
    height: MONTH_ROW_HEIGHT,
    borderTopWidth: 1,
    borderBottomWidth: 1,
    borderColor: "rgba(255,190,122,0.24)",
    backgroundColor: "rgba(255,190,122,0.08)"
  },
  monthWheelContent: {
    paddingVertical: MONTH_ROW_HEIGHT * 2
  },
  monthWheelRow: {
    height: MONTH_ROW_HEIGHT,
    alignItems: "center",
    justifyContent: "center"
  },
  monthWheelText: {
    color: palette.textSecondary,
    ...typography.body2
  },
  monthWheelTextActive: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  monthWheelTextDisabled: {
    color: "rgba(242,140,40,0.34)"
  },
  pickerActionRow: {
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: spacing[8]
  },
  pickerSecondaryButton: {
    minWidth: 88,
    minHeight: 38,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  pickerSecondaryText: {
    color: palette.textSecondary,
    ...typography.body2
  },
  pickerPrimaryButton: {
    minWidth: 88,
    minHeight: 38,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: "rgba(255,190,122,0.32)",
    backgroundColor: "rgba(242,140,40,0.34)",
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  pickerPrimaryButtonDisabled: {
    opacity: 0.45
  },
  pickerPrimaryText: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  }
}));



