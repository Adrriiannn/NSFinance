import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { Animated, RefreshControl, ScrollView, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { BalanceHeroCard } from "../../src/components/dashboard/BalanceHeroCard";
import { TransactionRow } from "../../src/components/transactions/TransactionRow";
import { EmptyState } from "../../src/components/ui/EmptyState";
import { GlassCard } from "../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { SectionHeader } from "../../src/components/ui/SectionHeader";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { SkeletonBlock } from "../../src/components/ui/SkeletonBlock";
import { useDashboardSummaryQuery } from "../../src/features/dashboard/useDashboardSummaryQuery";
import {
  buildHomeInsights
} from "../../src/features/planner/plannerInsights";
import { useTransactionsQuery } from "../../src/features/transactions/useTransactions";
import { useEntranceAnimation } from "../../src/hooks/useEntranceAnimation";
import { useLocalClock } from "../../src/hooks/useLocalClock";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { usePlannerStore } from "../../src/providers/PlannerProvider";
import { getFloatingTabBarContentInset } from "../../src/theme/insets";
import { layout, palette, spacing, typography } from "../../src/theme/tokens";

export default function DashboardTabScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const { session } = useAuthSession();
  const plannerStore = usePlannerStore();
  const summaryQuery = useDashboardSummaryQuery();
  const transactionsQuery = useTransactionsQuery();
  const { greeting, dateLabel, timeLabel } = useLocalClock();
  const fullName = session?.user.displayName?.trim() || "";
  const firstName = fullName.split(/\s+/).find(Boolean) || "there";

  const heroAnimation = useEntranceAnimation(30);
  const sectionAnimation = useEntranceAnimation(150);

  const isInitialLoading = summaryQuery.isLoading && !summaryQuery.data;
  const refreshing = summaryQuery.isRefetching && !isInitialLoading;
  const data = summaryQuery.data;
  const suggestions = buildHomeInsights({
    dashboard: data,
    transactions: transactionsQuery.data ?? [],
    necessities: plannerStore.necessities,
    annotations: plannerStore.annotations
  }).slice(0, 2);
  const listBottomInset = Math.max(
    spacing[8],
    getFloatingTabBarContentInset(insets.bottom, spacing[8])
  );
  const handleRefresh = () => {
    void Promise.all([summaryQuery.refetch(), transactionsQuery.refetch()]);
  };

  return (
    <ScreenContainer
      scrollable={false}
      contentStyle={styles.content}
    >
      <ScrollView
        contentContainerStyle={[styles.scrollContent, { paddingBottom: listBottomInset }]}
        showsVerticalScrollIndicator={false}
        bounces={false}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={handleRefresh}
            tintColor={palette.textSecondary}
          />
        }
      >
        <View style={styles.headerRow}>
          <View>
            <Text style={styles.greeting}>
              {greeting}, {firstName}
            </Text>
            <Text style={styles.subGreeting}>
              {dateLabel} | {timeLabel}
            </Text>
          </View>
        </View>

        {isInitialLoading ? (
          <DashboardLoading />
        ) : summaryQuery.isError ? (
          <ErrorState
            title="Unable to load home"
            message={summaryQuery.error.message}
            onRetry={() => {
              void summaryQuery.refetch();
            }}
          />
        ) : data && data.accountCount === 0 ? (
          <EmptyState
            title="No accounts yet"
            message="Create your first account to start tracking balances and spending."
            actionLabel="Add account"
            onActionPress={() => router.push("/modals/add-account")}
          />
        ) : data ? (
          <>
            <Animated.View style={heroAnimation}>
              <BalanceHeroCard
                totalBalance={data.totalBalance}
                accountCount={data.accountCount}
                transactionCount={data.transactionCount}
              />
            </Animated.View>

            <View style={styles.quickActionRow}>
              <View style={styles.quickActionPrimary}>
                <PrimaryButton
                  label="Add transaction"
                  onPress={() => router.push("/modals/add-transaction")}
                  icon={
                    <Ionicons
                      name="swap-horizontal-outline"
                      size={18}
                      color={palette.textPrimary}
                    />
                  }
                />
              </View>
              <View style={styles.quickActionSecondary}>
                <SecondaryButton
                  label="Add account"
                  onPress={() => router.push("/modals/add-account")}
                />
              </View>
            </View>

            <Animated.View style={sectionAnimation}>
              <SectionHeader
                title="Key insights"
                actionLabel="Planner"
                onActionPress={() => router.push("/(tabs)/planner" as never)}
              />
              <View style={styles.suggestionsWrap}>
                {suggestions.length > 0 ? (
                  suggestions.map((item) => (
                    <GlassCard key={item.id} style={styles.suggestionCard}>
                      <Text style={styles.suggestionTitle}>{item.title}</Text>
                      <Text style={styles.suggestionBody}>{item.message}</Text>
                    </GlassCard>
                  ))
                ) : (
                  <GlassCard style={styles.suggestionCard}>
                    <Text style={styles.suggestionBody}>
                      More guidance will appear as spending patterns become clearer.
                    </Text>
                  </GlassCard>
                )}
              </View>
            </Animated.View>

            <SectionHeader
              title="Recent activity"
              actionLabel="View all"
              onActionPress={() => router.push("/(tabs)/activity")}
            />

            <View style={styles.transactionsWrap}>
              {data.recentTransactions.slice(0, 5).map((transaction, index) => (
                <TransactionRow
                  key={transaction.id}
                  transaction={transaction}
                  index={index}
                  onPress={() =>
                    router.push({
                      pathname: "/(tabs)/activity",
                      params: {
                        focusTransactionId: transaction.id,
                        focusNonce: Date.now().toString()
                      }
                    })
                  }
                />
              ))}
            </View>
          </>
        ) : null}
      </ScrollView>
    </ScreenContainer>
  );
}

function DashboardLoading() {
  return (
    <View style={styles.loadingWrap}>
      <SkeletonBlock style={{ height: 184, borderRadius: 28 }} />
      <SkeletonBlock style={{ height: 132, borderRadius: 20 }} />
      <SkeletonBlock style={{ height: 110, borderRadius: 18 }} />
      <SkeletonBlock style={{ height: 94, borderRadius: 18 }} />
    </View>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: layout.screenTopPadding,
    paddingBottom: 0
  },
  scrollContent: {
    gap: spacing[16]
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  greeting: {
    color: palette.textPrimary,
    ...typography.title2
  },
  subGreeting: {
    marginTop: spacing[4],
    color: palette.textSecondary,
    ...typography.body2
  },
  quickActionRow: {
    flexDirection: "row",
    gap: spacing[12]
  },
  quickActionPrimary: {
    flex: 1
  },
  quickActionSecondary: {
    minWidth: 132
  },
  suggestionsWrap: {
    gap: spacing[12]
  },
  suggestionCard: {
    gap: spacing[8]
  },
  suggestionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  suggestionBody: {
    color: palette.textSecondary,
    ...typography.body2
  },
  transactionsWrap: {
    gap: spacing[12]
  },
  loadingWrap: {
    gap: spacing[16]
  }
});
