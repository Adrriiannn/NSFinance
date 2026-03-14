import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useCallback, useMemo, useState } from "react";
import {
  Alert,
  RefreshControl,
  SectionList,
  StyleSheet,
  Text,
  View
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ExpenseTrackerEntryCard } from "../../../../src/components/expenseTracker/ExpenseTrackerEntryCard";
import { ErrorState } from "../../../../src/components/feedback/ErrorState";
import { FloatingActionButton } from "../../../../src/components/ui/FloatingActionButton";
import { GlassCard } from "../../../../src/components/ui/GlassCard";
import { Chip } from "../../../../src/components/ui/Chip";
import { EmptyState } from "../../../../src/components/ui/EmptyState";
import { ScreenContainer } from "../../../../src/components/ui/ScreenContainer";
import { TextField } from "../../../../src/components/ui/TextField";
import {
  expenseTrackerCategoryOptions,
  expenseTrackerPaymentSourceOptions,
  expenseTrackerQuickRangeOptions,
  expenseTrackerSortOptions
} from "../../../../src/features/expenseTracker/expenseTrackerModels";
import {
  buildExpenseTrackerSummary,
  filterExpenseTrackerEntries,
  groupExpenseTrackerEntries
} from "../../../../src/features/expenseTracker/expenseTrackerUtils";
import {
  useCreateExpenseTrackerEntryMutation,
  useDeleteExpenseTrackerEntryMutation,
  useExpenseTrackerEntriesQuery,
  useUpdateExpenseTrackerEntryMutation
} from "../../../../src/features/expenseTracker/useExpenseTracker";
import { showFlashMessage } from "../../../../src/lib/flashMessage";
import { getFloatingFabOffset } from "../../../../src/theme/insets";
import { layout, palette, spacing, typography } from "../../../../src/theme/tokens";
import type {
  CreateExpenseTrackerEntryRequest,
  ExpenseTrackerEntryDto,
  ExpenseTrackerEntryStatus,
  UpdateExpenseTrackerEntryRequest
} from "../../../../src/types/api";

function formatAmount(amount: number) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency: "EUR"
  }).format(amount);
}

export default function ExpenseTrackerScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const deleteMutation = useDeleteExpenseTrackerEntryMutation();
  const updateMutation = useUpdateExpenseTrackerEntryMutation();
  const createMutation = useCreateExpenseTrackerEntryMutation();
  const [isManualRefreshing, setIsManualRefreshing] = useState(false);
  const [search, setSearch] = useState("");
  const [quickRange, setQuickRange] = useState<(typeof expenseTrackerQuickRangeOptions)[number]["value"]>("all");
  const [categoryFilter, setCategoryFilter] = useState<string | null>(null);
  const [paymentSourceFilter, setPaymentSourceFilter] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<ExpenseTrackerEntryStatus | "all">("all");
  const [sortOrder, setSortOrder] = useState<(typeof expenseTrackerSortOptions)[number]["value"]>("newest");

  const entries = entriesQuery.data ?? [];
  const summary = useMemo(() => buildExpenseTrackerSummary(entries), [entries]);
  const filteredEntries = useMemo(
    () =>
      filterExpenseTrackerEntries(entries, {
        search,
        quickRange,
        category: categoryFilter,
        paymentSource: paymentSourceFilter,
        status: statusFilter,
        sortOrder
      }),
    [categoryFilter, entries, paymentSourceFilter, quickRange, search, sortOrder, statusFilter]
  );
  const groupedEntries = useMemo(
    () => groupExpenseTrackerEntries(filteredEntries),
    [filteredEntries]
  );

  const listBottomInset = Math.max(
    spacing[16],
    getFloatingFabOffset(insets.bottom, -spacing[20])
  );

  const handleRefresh = useCallback(async () => {
    setIsManualRefreshing(true);
    try {
      await entriesQuery.refetch();
    } finally {
      setIsManualRefreshing(false);
    }
  }, [entriesQuery]);

  const handleToggleStatus = useCallback(
    async (entry: ExpenseTrackerEntryDto) => {
      const nextStatus: ExpenseTrackerEntryStatus = entry.status === "planned" ? "completed" : "planned";
      const payload: UpdateExpenseTrackerEntryRequest = {
        title: entry.title,
        amount: entry.amount,
        currency: entry.currency,
        category: entry.category,
        paymentSource: entry.paymentSource,
        occurredAtUtc: entry.occurredAtUtc,
        notes: entry.notes,
        tags: entry.tags,
        status: nextStatus,
        isRecurring: entry.isRecurring,
        merchant: entry.merchant
      };

      await updateMutation.mutateAsync({ entryId: entry.id, payload });
      showFlashMessage(
        nextStatus === "completed" ? "Expense marked completed." : "Expense marked planned.",
        { tone: "success" }
      );
    },
    [updateMutation]
  );

  const handleDuplicate = useCallback(
    async (entry: ExpenseTrackerEntryDto) => {
      const payload: CreateExpenseTrackerEntryRequest = {
        title: `${entry.title} copy`,
        amount: entry.amount,
        currency: entry.currency,
        category: entry.category,
        paymentSource: entry.paymentSource,
        occurredAtUtc: new Date().toISOString(),
        notes: entry.notes,
        tags: entry.tags,
        status: entry.status,
        isRecurring: entry.isRecurring,
        merchant: entry.merchant
      };

      await createMutation.mutateAsync(payload);
      showFlashMessage("Expense duplicated.", { tone: "success" });
    },
    [createMutation]
  );

  const handleDelete = useCallback(
    (entry: ExpenseTrackerEntryDto) => {
      Alert.alert(
        "Delete entry?",
        `Remove ${entry.title} from your expense tracker?`,
        [
          { text: "Cancel", style: "cancel" },
          {
            text: "Delete",
            style: "destructive",
            onPress: () => {
              void deleteMutation.mutateAsync(entry.id).then(() => {
                showFlashMessage("Expense removed.", { tone: "success" });
              });
            }
          }
        ]
      );
    },
    [deleteMutation]
  );

  return (
    <ScreenContainer scrollable={false} contentStyle={styles.content} withBottomTabOffset>
      <View style={styles.headerRow}>
        <View>
          <Text style={styles.title}>Expense Tracker</Text>
          <Text style={styles.subtitle}>A manual spending journal for plans, purchases, and daily entries.</Text>
        </View>
      </View>

      {entriesQuery.isError ? (
        <ErrorState
          title="Could not load expense tracker"
          message={entriesQuery.error.message}
          onRetry={() => {
            void entriesQuery.refetch();
          }}
        />
      ) : null}

      <SectionList
        sections={groupedEntries}
        keyExtractor={(item) => item.id}
        stickySectionHeadersEnabled={false}
        showsVerticalScrollIndicator={false}
        contentContainerStyle={[styles.listContent, { paddingBottom: listBottomInset }]}
        ListHeaderComponent={
          <View style={styles.listHeader}>
            <View style={styles.summaryGrid}>
              <GlassCard style={styles.summaryCard}>
                <Text style={styles.summaryLabel}>Today</Text>
                <Text style={styles.summaryValue}>{formatAmount(summary.todayTotal)}</Text>
              </GlassCard>
              <GlassCard style={styles.summaryCard}>
                <Text style={styles.summaryLabel}>This week</Text>
                <Text style={styles.summaryValue}>{formatAmount(summary.weekTotal)}</Text>
              </GlassCard>
              <GlassCard style={styles.summaryCard}>
                <Text style={styles.summaryLabel}>This month</Text>
                <Text style={styles.summaryValue}>{formatAmount(summary.monthTotal)}</Text>
              </GlassCard>
            </View>

            <GlassCard style={styles.summaryStrip}>
              <View>
                <Text style={styles.summaryStripLabel}>Completed total</Text>
                <Text style={styles.summaryStripValue}>{formatAmount(summary.completedTotal)}</Text>
              </View>
              <View>
                <Text style={styles.summaryStripLabel}>Planned total</Text>
                <Text style={styles.summaryStripValue}>{formatAmount(summary.plannedTotal)}</Text>
              </View>
              <View>
                <Text style={styles.summaryStripLabel}>Entries</Text>
                <Text style={styles.summaryStripValue}>{summary.entryCount}</Text>
              </View>
            </GlassCard>

            <TextField
              label="Search"
              value={search}
              onChangeText={setSearch}
              placeholder="Search title, note, merchant, or tag"
            />

            <View style={styles.filterGroup}>
              <Text style={styles.filterLabel}>Quick range</Text>
              <View style={styles.chipWrap}>
                {expenseTrackerQuickRangeOptions.map((option) => (
                  <Chip
                    key={option.value}
                    label={option.label}
                    selected={quickRange === option.value}
                    onPress={() => setQuickRange(option.value)}
                    compact
                  />
                ))}
              </View>
            </View>

            <View style={styles.filterGroup}>
              <Text style={styles.filterLabel}>Category</Text>
              <View style={styles.chipWrap}>
                <Chip label="All" selected={!categoryFilter} onPress={() => setCategoryFilter(null)} compact />
                {expenseTrackerCategoryOptions.slice(0, 6).map((option) => (
                  <Chip
                    key={option.value}
                    label={option.label}
                    selected={categoryFilter === option.value}
                    onPress={() => setCategoryFilter(option.value)}
                    compact
                  />
                ))}
              </View>
            </View>

            <View style={styles.filterGroup}>
              <Text style={styles.filterLabel}>Status</Text>
              <View style={styles.chipWrap}>
                <Chip label="All" selected={statusFilter === "all"} onPress={() => setStatusFilter("all")} compact />
                <Chip label="Completed" selected={statusFilter === "completed"} onPress={() => setStatusFilter("completed")} compact />
                <Chip label="Planned" selected={statusFilter === "planned"} onPress={() => setStatusFilter("planned")} compact />
              </View>
            </View>

            <View style={styles.filterGroup}>
              <Text style={styles.filterLabel}>Payment source</Text>
              <View style={styles.chipWrap}>
                <Chip label="All" selected={!paymentSourceFilter} onPress={() => setPaymentSourceFilter(null)} compact />
                {expenseTrackerPaymentSourceOptions.slice(0, 5).map((option) => (
                  <Chip
                    key={option.value}
                    label={option.label}
                    selected={paymentSourceFilter === option.value}
                    onPress={() => setPaymentSourceFilter(option.value)}
                    compact
                  />
                ))}
              </View>
            </View>

            <View style={styles.filterGroup}>
              <Text style={styles.filterLabel}>Sort</Text>
              <View style={styles.chipWrap}>
                {expenseTrackerSortOptions.map((option) => (
                  <Chip
                    key={option.value}
                    label={option.label}
                    selected={sortOrder === option.value}
                    onPress={() => setSortOrder(option.value)}
                    compact
                  />
                ))}
              </View>
            </View>

            {!entriesQuery.isLoading && filteredEntries.length === 0 ? (
              <EmptyState
                title={entries.length === 0 ? "Start your spending journal" : "No entries match these filters"}
                message={
                  entries.length === 0
                    ? "Track groceries, bills, subscriptions, and planned purchases in one calm place."
                    : "Try another search or clear a few filters to see more manual entries."
                }
                actionLabel={entries.length === 0 ? "Add your first expense" : "Create an entry"}
                onActionPress={() => router.push("/(tabs)/planner/expense-tracker/entry" as never)}
              />
            ) : null}
          </View>
        }
        renderSectionHeader={({ section }) => (
          <View style={styles.sectionHeader}>
            <Text style={styles.sectionTitle}>{section.title}</Text>
            <Text style={styles.sectionTotal}>{formatAmount(section.total)}</Text>
          </View>
        )}
        renderItem={({ item }) => (
          <ExpenseTrackerEntryCard
            entry={item}
            onPress={() =>
              router.push({
                pathname: "/(tabs)/planner/expense-tracker/entry",
                params: { entryId: item.id }
              })
            }
            onToggleStatus={() => {
              void handleToggleStatus(item);
            }}
            onDuplicate={() => {
              void handleDuplicate(item);
            }}
            onDelete={() => handleDelete(item)}
          />
        )}
        ItemSeparatorComponent={() => <View style={{ height: spacing[12] }} />}
        SectionSeparatorComponent={() => <View style={{ height: spacing[12] }} />}
        refreshControl={
          <RefreshControl
            refreshing={isManualRefreshing}
            onRefresh={() => {
              void handleRefresh();
            }}
            tintColor={palette.textSecondary}
          />
        }
      />

      <FloatingActionButton
        label="Add expense"
        onPress={() => router.push("/(tabs)/planner/expense-tracker/entry" as never)}
        icon={<Ionicons name="add" size={20} color={palette.textPrimary} />}
      />
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: layout.screenTopPadding,
    paddingBottom: 0
  },
  headerRow: {
    marginBottom: spacing[16],
    paddingRight: spacing[40]
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1
  },
  subtitle: {
    marginTop: spacing[4],
    color: palette.textSecondary,
    ...typography.body2
  },
  listContent: {
    paddingBottom: spacing[24]
  },
  listHeader: {
    gap: spacing[16],
    paddingBottom: spacing[16]
  },
  summaryGrid: {
    flexDirection: "row",
    gap: spacing[12]
  },
  summaryCard: {
    flex: 1,
    gap: spacing[8]
  },
  summaryLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  summaryValue: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  summaryStrip: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  summaryStripLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  summaryStripValue: {
    marginTop: spacing[4],
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  filterGroup: {
    gap: spacing[8]
  },
  filterLabel: {
    color: palette.textPrimary,
    ...typography.caption
  },
  chipWrap: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  sectionHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingBottom: spacing[8]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  sectionTotal: {
    color: palette.textSecondary,
    ...typography.caption
  }
});
