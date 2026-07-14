import { Ionicons } from "@expo/vector-icons";
import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Animated, Easing, Pressable, RefreshControl, ScrollView, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { BalanceHeroCard } from "../../src/components/dashboard/BalanceHeroCard";
import { TransactionRow } from "../../src/components/transactions/TransactionRow";
import { GlassCard } from "../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SectionHeader } from "../../src/components/ui/SectionHeader";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { SkeletonBlock } from "../../src/components/ui/SkeletonBlock";
import { TabEmptyStateCard } from "../../src/components/ui/TabEmptyStateCard";
import { useAccountsQuery } from "../../src/features/accounts/useAccounts";
import {
  resolveAccountBalancePresentation,
  resolvePortfolioBalancePresentation
} from "../../src/features/accounts/accountBalancePresentation";
import { useConnectBankCtaLabels } from "../../src/features/banking/connectBankCta";
import { buildConnectBankRoute } from "../../src/features/banking/bankingLinking";
import { useDashboardSummaryQuery } from "../../src/features/dashboard/useDashboardSummaryQuery";
import {
  buildHomeInsights
} from "../../src/features/planner/plannerInsights";
import {
  buildActivityFocusRoute,
  logActivityFocusEvent
} from "../../src/features/transactions/activityFocusNavigation";
import { useTransactionsQuery } from "../../src/features/transactions/useTransactions";
import { useEntranceAnimation } from "../../src/hooks/useEntranceAnimation";
import { formatGreetingForDisplay, useLocalClock } from "../../src/hooks/useLocalClock";
import { useMainTabSwipeNavigation } from "../../src/components/layout/useHorizontalSiblingSwipe";
import { AdaptiveScreen } from "../../src/layout/adaptive/AdaptiveScreen";
import { HeaderShell } from "../../src/layout/appHeader";
import {
  CONTENT_FRAME_HEADER_GAP,
  getDockAwareContentBottomInset
} from "../../src/layout/contentFrame";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { usePlannerStore } from "../../src/providers/PlannerProvider";
import { useThemeRuntime } from "../../src/theme/runtime/ThemeRuntimeProvider";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../src/theme/tokens";
import type { AccountDto } from "../../src/types/api";

type HeroCardItem = {
  key: string;
  accountId: string | null;
  title: string;
  badgeLabel: string | null;
  balance: number | null;
  currency: string;
  subtitle: string;
  currencyNote?: string | null;
  providerBranding?: Pick<
    AccountDto,
    "providerId" | "providerDisplayName" | "providerIconUrl" | "providerLogoUrl"
  > | null;
};

type HeroCarouselItem = HeroCardItem & {
  renderKey: string;
  logicalIndex: number;
};

const HERO_PAGER_DOT_SIZE = 6;
const HERO_PAGER_DOT_ACTIVE_WIDTH = 16;
const HERO_PAGER_DOT_GAP = 8;
const HERO_PAGER_STEP = HERO_PAGER_DOT_SIZE + HERO_PAGER_DOT_GAP;
const HERO_PAGER_ACTIVE_EXTRA = HERO_PAGER_DOT_ACTIVE_WIDTH - HERO_PAGER_DOT_SIZE;

export default function DashboardTabScreen() {
  useThemeRuntime();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const { gestureHandlers, animatedStyle } = useMainTabSwipeNavigation("/(tabs)");
  const { session } = useAuthSession();
  const plannerStore = usePlannerStore();
  const summaryQuery = useDashboardSummaryQuery();
  const accountsQuery = useAccountsQuery();
  const connectBankCta = useConnectBankCtaLabels();
  const transactionsQuery = useTransactionsQuery();
  const { greeting, dateLabel, timeLabel } = useLocalClock();
  const fullName = session?.user.fullName?.trim() || session?.user.displayName?.trim() || "";
  const firstName = fullName.split(/\s+/).find(Boolean) ?? null;
  const greetingTitle = useMemo(
    () => formatGreetingForDisplay(greeting, firstName),
    [firstName, greeting]
  );

  const heroAnimation = useEntranceAnimation(30);
  const sectionAnimation = useEntranceAnimation(150);

  const isInitialLoading =
    (summaryQuery.isLoading && !summaryQuery.data) ||
    (accountsQuery.isLoading && !accountsQuery.data) ||
    (transactionsQuery.isLoading && !transactionsQuery.data);
  const [isManualRefreshing, setIsManualRefreshing] = useState(false);
  const refreshing = isManualRefreshing && !isInitialLoading;
  const summaryData = summaryQuery.data;
  const accounts = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data]);
  const heroScrollRef = useRef<ScrollView | null>(null);
  const heroPhysicalIndexRef = useRef(0);
  const [heroIndex, setHeroIndex] = useState(0);
  const [heroWidth, setHeroWidth] = useState(0);
  const heroDotTranslateX = useRef(new Animated.Value(0)).current;
  const heroPrevIndexRef = useRef(0);
  const transactions = transactionsQuery.data ?? [];
  const recentTransactions = [...transactions]
    .sort((left, right) => new Date(right.bookedAtUtc).getTime() - new Date(left.bookedAtUtc).getTime())
    .slice(0, 5);
  const heroTotals = useMemo(() => {
    if (accounts.length === 0) {
      return null;
    }

    const hasStructuredPortfolio = Boolean(
      summaryData && Object.prototype.hasOwnProperty.call(summaryData, "portfolioBalance")
    );
    if (hasStructuredPortfolio) {
      const portfolio = resolvePortfolioBalancePresentation(summaryData?.portfolioBalance);
      if (!portfolio || portfolio.currencyGroups.length === 0) {
        return {
          totalBalance: null,
          currency: accounts[0]?.currency ?? "EUR",
          currencyNote: "No current account balance is available."
        };
      }

      const sortedGroups = [...portfolio.currencyGroups].sort((left, right) => {
        if (right.accountCount !== left.accountCount) {
          return right.accountCount - left.accountCount;
        }

        return Math.abs(right.amount) - Math.abs(left.amount);
      });
      const primaryGroup = sortedGroups[0];
      const exclusionNote = portfolio.excludedAccountCount > 0
        ? `${portfolio.excludedAccountCount} account${portfolio.excludedAccountCount === 1 ? " is" : "s are"} unavailable.`
        : null;
      const currencyNote = portfolio.hasMultipleCurrencies
        ? `Balances stay separate by currency. Showing ${primaryGroup.currency}.`
        : exclusionNote;

      return {
        totalBalance: Number(primaryGroup.amount.toFixed(2)),
        currency: primaryGroup.currency,
        currencyNote: [currencyNote, exclusionNote]
          .filter((value, index, values) => value && values.indexOf(value) === index)
          .join(" ") || null
      };
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
  }, [accounts, summaryData]);
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
      currencyNote: heroTotals.currencyNote,
      providerBranding: null
    };

    const accountItems = accounts.map((account) => {
      const balance = resolveAccountBalancePresentation(account);
      return {
        key: account.id,
        accountId: account.id,
        title: "Account balance",
        badgeLabel: null,
        balance: balance.current,
        currency: balance.currency,
        subtitle: `${account.name} | ${account.transactionCount} transactions`,
        currencyNote: balance.freshness === "stale" ? "Balance may be out of date." : null,
        providerBranding: {
          providerId: account.providerId,
          providerDisplayName: account.providerDisplayName,
          providerIconUrl: account.providerIconUrl,
          providerLogoUrl: account.providerLogoUrl
        }
      };
    });

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
  const handleRefresh = useCallback(async () => {
    setIsManualRefreshing(true);

    try {
      await Promise.all([summaryQuery.refetch(), accountsQuery.refetch(), transactionsQuery.refetch()]);
    } finally {
      setIsManualRefreshing(false);
    }
  }, [
    accountsQuery,
    summaryQuery,
    transactionsQuery
  ]);
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

  useEffect(() => {
    const targetTranslateX = heroIndex * HERO_PAGER_STEP;
    const previousHeroIndex = heroPrevIndexRef.current;
    const shouldAnimate = Math.abs(heroIndex - previousHeroIndex) <= 1;

    if (shouldAnimate) {
      Animated.timing(heroDotTranslateX, {
        toValue: targetTranslateX,
        duration: 220,
        easing: Easing.out(Easing.cubic),
        useNativeDriver: true
      }).start();
    } else {
      heroDotTranslateX.setValue(targetTranslateX);
    }

    heroPrevIndexRef.current = heroIndex;
  }, [heroDotTranslateX, heroIndex]);

  return (
    <AdaptiveScreen contentStyle={styles.content} gestureHandlers={gestureHandlers}>
      <Animated.View style={[styles.tabStage, animatedStyle]}>
        <HeaderShell
          preset="primaryGreeting"
          includeTopInset
          title={greetingTitle}
          subtitle={`${dateLabel} | ${timeLabel}`}
        />

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
              ctaLabel={connectBankCta.primaryLabel}
              onCtaPress={() =>
                router.push(buildConnectBankRoute({ intent: "new", returnTo: "/(tabs)" }))
              }
              verticalSpacingMode="tab-aligned"
              hideOrb
              centerText
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
                          providerBranding={item.providerBranding ?? null}
                        />
                      </Pressable>
                    ))}
                  </ScrollView>
                  <View style={styles.heroPagerDots}>
                    <View
                      style={[
                        styles.heroPagerTrack,
                        {
                          width:
                            heroItems.length * HERO_PAGER_DOT_SIZE +
                            Math.max(heroItems.length - 1, 0) * HERO_PAGER_DOT_GAP +
                            HERO_PAGER_ACTIVE_EXTRA
                        }
                      ]}
                    >
                      {heroItems.map((item, index) => (
                        <View
                          key={`dot-${item.key}`}
                          style={[
                            styles.heroPagerSlot,
                            index === heroItems.length - 1 ? styles.heroPagerSlotLast : null,
                            index > heroIndex ? styles.heroPagerSlotShifted : null
                          ]}
                        >
                          <View style={[styles.heroPagerDot, index === heroIndex ? styles.heroPagerDotHidden : null]} />
                        </View>
                      ))}
                      {heroItems.length > 0 ? (
                        <Animated.View
                          pointerEvents="none"
                          style={[
                            styles.heroPagerDotActive,
                            {
                              transform: [{ translateX: heroDotTranslateX }]
                            }
                          ]}
                        />
                      ) : null}
                    </View>
                  </View>
                </View>
              </Animated.View>

              <View style={styles.quickActionRow}>
                <View style={styles.quickActionPrimary}>
                  <PrimaryButton
                    label="View activity"
                    onPress={() => router.push("/(tabs)/activity")}
                    labelStyle={styles.viewActivityLabel}
                    icon={
                      <Ionicons
                        name="list-outline"
                        size={18}
                        color={palette.textPrimary}
                      />
                    }
                  />
                </View>
                <View style={styles.quickActionSecondary}>
                  <SecondaryButton
                    label={connectBankCta.compactLabel}
                    onPress={() =>
                      router.push(buildConnectBankRoute({ intent: "new", returnTo: "/(tabs)" }))
                    }
                  />
                </View>
              </View>

              <Animated.View style={sectionAnimation}>
                <SectionHeader
                  title="Key insights"
                  actionLabel="Cashflow"
                  onActionPress={() => router.push("/(tabs)/cashflow" as never)}
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
                    onPress={() => {
                      const focusRoute = buildActivityFocusRoute(transaction.id);
                      if (!focusRoute) {
                        return;
                      }

                      logActivityFocusEvent("recent_activity_press", {
                        source: "home",
                        transactionId: transaction.id
                      });
                      router.push(focusRoute);
                    }}
                  />
                ))}
              </View>
            </>
          ) : null}
        </ScrollView>
      </Animated.View>
    </AdaptiveScreen>
  );
}

function DashboardLoading() {
  return (
    <View style={styles.loadingWrap}>
      <SkeletonBlock style={{ height: 184, borderRadius: 6 }} />
      <SkeletonBlock style={{ height: 132, borderRadius: 6 }} />
      <SkeletonBlock style={{ height: 110, borderRadius: 6 }} />
      <SkeletonBlock style={{ height: 94, borderRadius: 6 }} />
    </View>
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
    gap: spacing[16]
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
    justifyContent: "center"
  },
  heroPagerTrack: {
    position: "relative",
    flexDirection: "row",
    alignItems: "center",
    height: HERO_PAGER_DOT_SIZE
  },
  heroPagerSlot: {
    width: HERO_PAGER_DOT_SIZE,
    height: HERO_PAGER_DOT_SIZE,
    justifyContent: "center",
    alignItems: "center",
    marginRight: HERO_PAGER_DOT_GAP
  },
  heroPagerSlotLast: {
    marginRight: 0
  },
  heroPagerSlotShifted: {
    transform: [{ translateX: HERO_PAGER_ACTIVE_EXTRA }]
  },
  heroPagerDot: {
    width: HERO_PAGER_DOT_SIZE,
    height: HERO_PAGER_DOT_SIZE,
    borderRadius: HERO_PAGER_DOT_SIZE,
    backgroundColor: "rgba(242,140,40,0.35)"
  },
  heroPagerDotHidden: {
    opacity: 0
  },
  heroPagerDotActive: {
    position: "absolute",
    top: 0,
    left: 0,
    width: HERO_PAGER_DOT_ACTIVE_WIDTH,
    height: HERO_PAGER_DOT_SIZE,
    borderRadius: 6,
    backgroundColor: palette.accent
  },
  quickActionPrimary: {
    flex: 1
  },
  viewActivityLabel: {
    color: palette.textPrimary
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
}));

