import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Animated, Pressable, RefreshControl, ScrollView, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { BalanceHeroCard } from "../../src/components/dashboard/BalanceHeroCard";
import { TransactionRow } from "../../src/components/transactions/TransactionRow";
import { GlassCard } from "../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { SectionHeader } from "../../src/components/ui/SectionHeader";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { SkeletonBlock } from "../../src/components/ui/SkeletonBlock";
import { TabEmptyStateCard } from "../../src/components/ui/TabEmptyStateCard";
import { useAccountsQuery } from "../../src/features/accounts/useAccounts";
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

type HeroCardItem = {
  key: string;
  accountId: string | null;
  title: string;
  badgeLabel: string;
  balance: number;
  currency: string;
  subtitle: string;
  currencyNote?: string | null;
};

type HeroCarouselItem = HeroCardItem & {
  renderKey: string;
  logicalIndex: number;
};

export default function DashboardTabScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const { session } = useAuthSession();
  const plannerStore = usePlannerStore();
  const summaryQuery = useDashboardSummaryQuery();
  const accountsQuery = useAccountsQuery();
  const transactionsQuery = useTransactionsQuery();
  const { greeting, dateLabel, timeLabel } = useLocalClock();
  const fullName = session?.user.fullName?.trim() || session?.user.displayName?.trim() || "";
  const firstName = fullName.split(/\s+/).find(Boolean) || "there";

  const heroAnimation = useEntranceAnimation(30);
  const sectionAnimation = useEntranceAnimation(150);

  const isInitialLoading =
    (summaryQuery.isLoading && !summaryQuery.data) ||
    (accountsQuery.isLoading && !accountsQuery.data) ||
    (transactionsQuery.isLoading && !transactionsQuery.data);
  const refreshing =
    (summaryQuery.isRefetching || accountsQuery.isRefetching || transactionsQuery.isRefetching) &&
    !isInitialLoading;
  const summaryData = summaryQuery.data;
  const accounts = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data]);
  const heroScrollRef = useRef<ScrollView | null>(null);
  const heroPhysicalIndexRef = useRef(0);
  const [heroIndex, setHeroIndex] = useState(0);
  const [heroWidth, setHeroWidth] = useState(0);
  const transactions = transactionsQuery.data ?? [];
  const recentTransactions = [...transactions]
    .sort((left, right) => new Date(right.bookedAtUtc).getTime() - new Date(left.bookedAtUtc).getTime())
    .slice(0, 5);
  const heroTotals = useMemo(() => {
    if (accounts.length === 0) {
      return null;
    }

    const grouped = new Map<string, { total: number; accountCount: number }>();
    accounts.forEach((account) => {
      const group = grouped.get(account.currency) ?? { total: 0, accountCount: 0 };
      group.total += account.currentBalance;
      group.accountCount += 1;
      grouped.set(account.currency, group);
    });

    if (grouped.size === 1) {
      const [currency, value] = Array.from(grouped.entries())[0];
      return {
        totalBalance: Number(value.total.toFixed(2)),
        currency,
        currencyNote: null as string | null
      };
    }

    const [primaryCurrency, primaryGroup] = Array.from(grouped.entries()).sort((left, right) => {
      if (right[1].accountCount !== left[1].accountCount) {
        return right[1].accountCount - left[1].accountCount;
      }

      return Math.abs(right[1].total) - Math.abs(left[1].total);
    })[0];

    return {
      totalBalance: Number(primaryGroup.total.toFixed(2)),
      currency: primaryCurrency,
      currencyNote: `Mixed currencies detected. Showing ${primaryGroup.accountCount} ${primaryCurrency} account${primaryGroup.accountCount === 1 ? "" : "s"} only.`
    };
  }, [accounts]);
  const heroItems = useMemo<HeroCardItem[]>(() => {
    if (!heroTotals) {
      return [];
    }

    const totalItem: HeroCardItem = {
      key: "total",
      accountId: null,
      title: "Total balance",
      badgeLabel: "All accounts",
      balance: heroTotals.totalBalance,
      currency: heroTotals.currency,
      subtitle: `${accounts.length} accounts | ${transactions.length} transactions`,
      currencyNote: heroTotals.currencyNote
    };

    const accountItems = accounts.map((account) => ({
      key: account.id,
      accountId: account.id,
      title: "Account balance",
      badgeLabel: "Account",
      balance: account.currentBalance,
      currency: account.currency,
      subtitle: `${account.name} | ${account.transactionCount} transactions`,
      currencyNote: null
    }));

    return [totalItem, ...accountItems];
  }, [accounts, heroTotals, transactions.length]);
  const carouselItems = useMemo<HeroCarouselItem[]>(() => {
    if (heroItems.length === 0) {
      return [];
    }

    if (heroItems.length === 1) {
      return [{ ...heroItems[0], logicalIndex: 0, renderKey: `${heroItems[0].key}-single` }];
    }

    const first = heroItems[0];
    const last = heroItems[heroItems.length - 1];

    return [
      { ...last, logicalIndex: heroItems.length - 1, renderKey: `${last.key}-loop-head` },
      ...heroItems.map((item, logicalIndex) => ({
        ...item,
        logicalIndex,
        renderKey: `${item.key}-core-${logicalIndex}`
      })),
      { ...first, logicalIndex: 0, renderKey: `${first.key}-loop-tail` }
    ];
  }, [heroItems]);
  const getInitialPhysicalIndex = useCallback(
    () => (heroItems.length > 1 ? 1 : 0),
    [heroItems.length]
  );
  const suggestions = buildHomeInsights({
    dashboard: summaryData,
    transactions,
    annotations: plannerStore.annotations
  }).slice(0, 2);
  const listBottomInset = Math.max(
    spacing[8],
    getFloatingTabBarContentInset(insets.bottom, spacing[8])
  );
  const handleRefresh = () => {
    void Promise.all([summaryQuery.refetch(), accountsQuery.refetch(), transactionsQuery.refetch()]);
  };
  const loadError = summaryQuery.error ?? accountsQuery.error ?? transactionsQuery.error;
  const handleHeroPress = (item: HeroCardItem) => {
    if (!item.accountId) {
      router.push("/(tabs)/accounts");
      return;
    }

    router.push({
      pathname: "/(tabs)/accounts",
      params: {
        selectedAccountId: item.accountId,
        focusNonce: Date.now().toString()
      }
    });
  };

  useFocusEffect(
    useCallback(() => {
      setHeroIndex(0);
      const initialPhysicalIndex = getInitialPhysicalIndex();
      heroPhysicalIndexRef.current = initialPhysicalIndex;
      requestAnimationFrame(() => {
        heroScrollRef.current?.scrollTo({
          x: heroWidth > 0 ? heroWidth * initialPhysicalIndex : 0,
          y: 0,
          animated: false
        });
      });
      return undefined;
    }, [getInitialPhysicalIndex, heroWidth])
  );

  useEffect(() => {
    if (heroWidth <= 0 || heroItems.length === 0) {
      return;
    }

    const initialPhysicalIndex = getInitialPhysicalIndex();
    heroPhysicalIndexRef.current = initialPhysicalIndex;
    requestAnimationFrame(() => {
      heroScrollRef.current?.scrollTo({
        x: heroWidth * initialPhysicalIndex,
        y: 0,
        animated: false
      });
    });
  }, [getInitialPhysicalIndex, heroItems.length, heroWidth]);

  return (
    <ScreenContainer
      scrollable={false}
      contentStyle={styles.content}
    >
      <View style={styles.headerTopBar}>
        <View style={styles.headerRow}>
          <View>
            <Text style={styles.greeting}>
              {greeting}, {firstName}
            </Text>
            <Text style={styles.subGreeting}>
              {dateLabel} | {timeLabel}
            </Text>
          </View>
          <View style={styles.headerRightActions}>
            <Pressable
              style={styles.companionButton}
              onPress={() => router.push("/companion" as never)}
            >
              <MaterialCommunityIcons name="robot-happy-outline" size={20} color="#4FE3D5" />
            </Pressable>
            <View style={styles.headerRightSpacer} />
          </View>
        </View>
      </View>

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
        {isInitialLoading ? (
          <DashboardLoading />
        ) : loadError ? (
          <ErrorState
            title="Unable to load home"
            message={loadError.message}
            onRetry={() => {
              void handleRefresh();
            }}
          />
        ) : accounts.length === 0 ? (
          <TabEmptyStateCard
            title="No connected accounts"
            subtitle="Connect your bank to start tracking balances and spending."
            ctaLabel="Connect bank"
            onCtaPress={() => router.push("/modals/add-account")}
            verticalSpacingMode="tab-aligned"
          />
        ) : heroTotals ? (
          <>
            <Animated.View style={heroAnimation}>
              <View
                style={styles.heroPagerWrap}
                onLayout={(event) => setHeroWidth(event.nativeEvent.layout.width)}
              >
                <ScrollView
                  ref={heroScrollRef}
                  horizontal
                  pagingEnabled
                  showsHorizontalScrollIndicator={false}
                  bounces={false}
                  scrollEnabled={carouselItems.length > 1}
                  onMomentumScrollEnd={(event) => {
                    const width = event.nativeEvent.layoutMeasurement.width;
                    if (width <= 0) {
                      return;
                    }

                    const logicalCount = heroItems.length;
                    if (logicalCount === 0) {
                      setHeroIndex(0);
                      return;
                    }

                    let physicalIndex = Math.round(event.nativeEvent.contentOffset.x / width);
                    let logicalIndex = physicalIndex;

                    if (logicalCount > 1) {
                      if (physicalIndex === 0) {
                        physicalIndex = logicalCount;
                        logicalIndex = logicalCount - 1;
                        requestAnimationFrame(() => {
                          heroScrollRef.current?.scrollTo({
                            x: width * physicalIndex,
                            y: 0,
                            animated: false
                          });
                        });
                      } else if (physicalIndex === logicalCount + 1) {
                        physicalIndex = 1;
                        logicalIndex = 0;
                        requestAnimationFrame(() => {
                          heroScrollRef.current?.scrollTo({
                            x: width * physicalIndex,
                            y: 0,
                            animated: false
                          });
                        });
                      } else {
                        logicalIndex = physicalIndex - 1;
                      }
                    } else {
                      logicalIndex = 0;
                    }

                    heroPhysicalIndexRef.current = physicalIndex;
                    setHeroIndex(logicalIndex);
                  }}
                >
                  {carouselItems.map((item) => (
                    <Pressable
                      key={item.renderKey}
                      style={[styles.heroPage, heroWidth > 0 ? { width: heroWidth } : null]}
                      onPress={() => handleHeroPress(item)}
                    >
                      <BalanceHeroCard
                        totalBalance={item.balance}
                        currency={item.currency}
                        title={item.title}
                        badgeLabel={item.badgeLabel}
                        subtitleOverride={item.subtitle}
                        currencyNote={item.currencyNote}
                      />
                    </Pressable>
                  ))}
                </ScrollView>
                <View style={styles.heroPagerDots}>
                  {heroItems.map((item, index) => (
                    <View
                      key={`dot-${item.key}`}
                      style={[styles.heroPagerDot, index === heroIndex ? styles.heroPagerDotActive : null]}
                    />
                  ))}
                </View>
              </View>
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
                  label="Connect bank"
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
              {recentTransactions.map((transaction, index) => (
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
  headerTopBar: {
    marginBottom: spacing[16],
    backgroundColor: "transparent",
    zIndex: 20,
    elevation: 20
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between"
  },
  headerRightSpacer: {
    width: 42,
    height: 42
  },
  headerRightActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  companionButton: {
    width: 42,
    height: 42,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    alignItems: "center",
    justifyContent: "center"
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
  heroPagerWrap: {
    gap: spacing[8]
  },
  heroPage: {
    width: "100%"
  },
  heroPagerDots: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8]
  },
  heroPagerDot: {
    width: 6,
    height: 6,
    borderRadius: 3,
    backgroundColor: "rgba(220,232,255,0.35)"
  },
  heroPagerDotActive: {
    width: 16,
    borderRadius: 999,
    backgroundColor: palette.accent
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

