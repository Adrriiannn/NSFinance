import { useFocusEffect, useLocalSearchParams, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Animated,
  PanResponder,
  Pressable,
  RefreshControl,
  SectionList,
  StyleSheet,
  Text,
  View
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { TransactionRow } from "../../../src/components/transactions/TransactionRow";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { useMainTabSwipeNavigation } from "../../../src/components/layout/useHorizontalSiblingSwipe";
import { SkeletonBlock } from "../../../src/components/ui/SkeletonBlock";
import { TabEmptyStateCard } from "../../../src/components/ui/TabEmptyStateCard";
import { ActivitySearchBar } from "../../../src/features/activity/components/ActivitySearchBar";
import {
  consumeActivitySearchSnapshot,
  setActivitySearchSnapshot
} from "../../../src/features/activity/search/activitySearch.bridge";
import { useActivitySearch } from "../../../src/features/activity/search/activitySearch.hooks";
import type { ActivityTaxonomySearchEntry } from "../../../src/features/activity/search/activitySearch.types";
import { AdaptiveScreen } from "../../../src/layout/adaptive/AdaptiveScreen";
import { HeaderShell } from "../../../src/layout/appHeader";
import { HEADER_CONSTANTS } from "../../../src/layout/header/header.constants";
import {
  CONTENT_FRAME_HEADER_GAP,
  getDockAwareContentBottomInset
} from "../../../src/layout/contentFrame";
import {
  groupTransactionsByTimeBucket
} from "../../../src/features/transactions/activityGrouping";
import { consumePendingActivitySearchCategorySelection } from "../../../src/features/expenseTracker/categoryPickerBridge";
import { flattenVisibleExpenseTaxonomy } from "../../../src/features/expenseTracker/expenseTrackerModels";
import { useExpenseTrackerTaxonomyQuery } from "../../../src/features/expenseTracker/useExpenseTracker";
import { useAccountsQuery } from "../../../src/features/accounts/useAccounts";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { useUserProfileQuery } from "../../../src/features/users/useUserSettings";
import { usePlannerStore } from "../../../src/providers/PlannerProvider";
import { palette, spacing, typography } from "../../../src/theme/tokens";
import type { TransactionDto } from "../../../src/types/api";

const transactionSwipeHoldDelayMs = 1000;

export default function ActivityTabScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const pageSwipeBlockedRef = useRef(false);
  const { gestureHandlers, animatedStyle } = useMainTabSwipeNavigation("/(tabs)/activity", {
    isBlockedRef: pageSwipeBlockedRef
  });
  const params = useLocalSearchParams<{ focusTransactionId?: string; focusNonce?: string }>();
  const plannerStore = usePlannerStore();
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const accountsQuery = useAccountsQuery();
  const profileQuery = useUserProfileQuery();
  const [highlightedTransactionId, setHighlightedTransactionId] = useState<string | null>(
    null
  );
  const initialSearchSnapshotRef = useRef(consumeActivitySearchSnapshot());
  const sectionListRef = useRef<SectionList<TransactionDto>>(null);
  const handledFocusTransactionIdRef = useRef<string | null>(null);
  const transactionsQuery = useTransactionsQuery();
  const isInitialLoading = transactionsQuery.isLoading && !transactionsQuery.data;
  const [isManualRefreshing, setIsManualRefreshing] = useState(false);
  const refreshing = isManualRefreshing && !isInitialLoading;
  const focusTransactionId =
    typeof params.focusTransactionId === "string" ? params.focusTransactionId : "";
  const focusNonce = typeof params.focusNonce === "string" ? params.focusNonce : "";
  const focusKey = focusTransactionId ? `${focusTransactionId}:${focusNonce}` : "";
  const searchBackdropTop =
    insets.top + HEADER_CONSTANTS.rowHeight + HEADER_CONSTANTS.secondRowHeight + 10;
  const taxonomyEntries = useMemo<ActivityTaxonomySearchEntry[]>(() => {
    const visibleDomains = (taxonomyQuery.data?.domains ?? []).filter(
      (domain) => domain.isUserSelectable && !domain.isSystemDomain && domain.isActive
    );
    return flattenVisibleExpenseTaxonomy(visibleDomains).map((entry) => ({
      domainId: entry.domain.id,
      domainName: entry.domain.name,
      categoryId: entry.category.id,
      categoryName: entry.category.name,
      subcategoryId: entry.subcategory.id,
      subcategoryName: entry.subcategory.name
    }));
  }, [taxonomyQuery.data?.domains]);
  const search = useActivitySearch({
    transactions: transactionsQuery.data ?? [],
    annotations: plannerStore.annotations,
    preferredCurrency: profileQuery.data?.preferredCurrency,
    accountCurrencies: (accountsQuery.data ?? []).map((account) => account.currency),
    taxonomyEntries,
    initialSnapshot: initialSearchSnapshotRef.current,
    onRequestCategoryPicker: (snapshot) => {
      setActivitySearchSnapshot(snapshot);
      router.push({
        pathname: "/(tabs)/planning/categories",
        params: {
          selectionMode: "true",
          selectionTarget: "activitySearchCategoryFilter"
        }
      });
    }
  });

  const handleRefresh = useCallback(async () => {
    setIsManualRefreshing(true);
    try {
      await transactionsQuery.refetch();
    } finally {
      setIsManualRefreshing(false);
    }
  }, [transactionsQuery]);

  const grouped = useMemo(() => {
    return groupTransactionsByTimeBucket(search.filteredTransactions)
      .filter((section) => section.items.length > 0)
      .map((section) => ({
        title: section.title,
        data: section.items
      }));
  }, [search.filteredTransactions]);
  const hasAnyTransactions = (transactionsQuery.data?.length ?? 0) > 0;
  const hasActiveSearch =
    search.tokens.length > 0 || search.rawSearchText.trim().length > 0;
  const showNoSearchResultsState =
    !isInitialLoading &&
    !transactionsQuery.isError &&
    grouped.length === 0 &&
    hasAnyTransactions &&
    hasActiveSearch;
  const showNoActivityState =
    !isInitialLoading && !transactionsQuery.isError && grouped.length === 0 && !showNoSearchResultsState;

  useFocusEffect(
    useCallback(() => {
      const restoredSnapshot = consumeActivitySearchSnapshot();
      if (restoredSnapshot) {
        search.setSnapshot(restoredSnapshot);
      }

      const pickedCategorySelection = consumePendingActivitySearchCategorySelection();
      if (pickedCategorySelection) {
        search.applyPickedCategory(pickedCategorySelection);
      }

      return undefined;
    }, [search])
  );

  useEffect(() => {
    if (!focusTransactionId || handledFocusTransactionIdRef.current === focusKey) {
      return;
    }

    handledFocusTransactionIdRef.current = focusKey;
    setHighlightedTransactionId(focusTransactionId);
  }, [focusKey, focusTransactionId]);

  useEffect(() => {
    if (!focusTransactionId || grouped.length === 0) {
      return;
    }

    let targetSectionIndex = -1;
    let targetItemIndex = -1;

    grouped.forEach((section, sectionIndex) => {
      if (targetSectionIndex >= 0) {
        return;
      }

      const itemIndex = section.data.findIndex((item) => item.id === focusTransactionId);
      if (itemIndex >= 0) {
        targetSectionIndex = sectionIndex;
        targetItemIndex = itemIndex;
      }
    });

    if (targetSectionIndex < 0 || targetItemIndex < 0) {
      return;
    }

    requestAnimationFrame(() => {
      sectionListRef.current?.scrollToLocation({
        sectionIndex: targetSectionIndex,
        itemIndex: targetItemIndex,
        viewPosition: 0.34,
        animated: true
      });
    });

    const highlightTimer = setTimeout(() => {
      setHighlightedTransactionId((current) =>
        current === focusTransactionId ? null : current
      );
    }, 1800);

    return () => {
      clearTimeout(highlightTimer);
    };
  }, [focusTransactionId, grouped]);

  return (
    <AdaptiveScreen contentStyle={styles.container} gestureHandlers={gestureHandlers}>
      <Animated.View style={[styles.tabStage, animatedStyle]}>
        <HeaderShell
          preset="primaryTwoRowSearch"
          includeTopInset
          title="Activity"
          secondRow={
            <ActivitySearchBar
              tokens={search.tokens}
              rawSearchText={search.rawSearchText}
              activeTokenId={search.activeTokenId}
              activeTokenType={search.activeTokenType}
              activeTokenDraft={search.activeTokenDraft}
              dropdownOpen={search.dropdownOpen}
              dropdownMode={search.dropdownMode}
              filterOptions={search.filterOptionAvailability}
              merchantSuggestions={search.merchantSuggestions}
              dateSuggestions={search.dateSuggestionResult.suggestions}
              currencies={search.availableCurrencies}
              selectedCurrency={search.activeCurrencyCode}
              onFocusSearch={search.focusSearch}
              onSetRawSearchText={search.setRawSearchText}
              onSetActiveDraft={search.setActiveTokenDraft}
              onConfirmActiveDraft={search.confirmTokenDraft}
              onSelectFilter={search.selectFilterOption}
              onSelectMerchantSuggestion={search.selectMerchantSuggestion}
              onSelectDateSuggestion={search.selectDateSuggestion}
              onSelectCurrency={search.selectCurrency}
              onEditToken={search.beginEditingToken}
              onRemoveToken={search.removeToken}
              onOpenCategoryPicker={() => {
                const snapshot = search.getSnapshot();
                setActivitySearchSnapshot(snapshot);
                router.push({
                  pathname: "/(tabs)/planning/categories",
                  params: {
                    selectionMode: "true",
                    selectionTarget: "activitySearchCategoryFilter"
                  }
                });
              }}
              onClearSearch={search.clearSearch}
            />
          }
        />

        {search.dropdownOpen && search.dropdownMode !== "hidden" ? (
          <Pressable
            onPress={search.dismissDropdown}
            style={[styles.searchBackdrop, { top: searchBackdropTop }]}
          />
        ) : null}

        <View style={styles.feedWrap}>
          {isInitialLoading ? (
            <View style={styles.loadingList}>
              <SkeletonBlock style={styles.loadingRow} />
              <SkeletonBlock style={styles.loadingRow} />
              <SkeletonBlock style={styles.loadingRow} />
              <SkeletonBlock style={styles.loadingRow} />
            </View>
          ) : transactionsQuery.isError ? (
            <ErrorState
              title="Could not load activity"
              message={transactionsQuery.error.message}
              onRetry={() => {
                void transactionsQuery.refetch();
              }}
            />
          ) : showNoSearchResultsState ? (
            <TabEmptyStateCard
              title="No transactions match your current search."
              subtitle="Please try a different search or contact us if you are missing transactions."
              ctaLabel="Contact us"
              onCtaPress={() => router.push("/(tabs)/accounts/support")}
              verticalSpacingMode="tab-aligned"
              hideOrb
              centerText
            />
          ) : showNoActivityState ? (
            <TabEmptyStateCard
              title="No activity yet"
              subtitle="Connect your bank to start tracking spending activity."
              ctaLabel="Connect bank"
              onCtaPress={() => router.push("/(tabs)/accounts/connect-bank?intent=new")}
              verticalSpacingMode="tab-aligned"
            />
          ) : (
            <SectionList
              ref={sectionListRef}
              sections={grouped}
              keyExtractor={(item) => item.id}
              stickySectionHeadersEnabled={false}
              bounces={false}
              onScrollBeginDrag={search.dismissDropdown}
              renderSectionHeader={({ section }) => (
                <Text style={styles.groupHeading}>{section.title}</Text>
              )}
              renderItem={({ item, index }) => (
                <SwipeableTransactionItem
                  transaction={item}
                  index={index}
                  onPress={() =>
                    router.push(`/(tabs)/activity/${item.id}` as never)
                  }
                  highlighted={item.id === highlightedTransactionId}
                  onSwipeArmChange={(isArmed) => {
                    pageSwipeBlockedRef.current = isArmed;
                  }}
                  onMarkEssential={() =>
                    plannerStore.saveAnnotation({
                      transactionId: item.id,
                      type: null,
                      reason: "Flagged for review",
                      direction: item.direction
                    })
                  }
                  onMarkOptional={() =>
                    plannerStore.saveAnnotation({
                      transactionId: item.id,
                      type: null,
                      reason: "Skipped from focus",
                      direction: item.direction
                    })
                  }
                />
              )}
              contentContainerStyle={[
                styles.listContent,
                {
                  paddingTop: CONTENT_FRAME_HEADER_GAP,
                  paddingBottom: getDockAwareContentBottomInset(insets.bottom)
                }
              ]}
              SectionSeparatorComponent={() => <View style={{ height: spacing[12] }} />}
              ItemSeparatorComponent={() => <View style={{ height: spacing[12] }} />}
              showsVerticalScrollIndicator={false}
              refreshControl={
                <RefreshControl
                  refreshing={refreshing}
                  onRefresh={() => {
                    void handleRefresh();
                  }}
                  tintColor={palette.textSecondary}
                />
              }
            />
          )}
        </View>
      </Animated.View>
    </AdaptiveScreen>
  );
}

function SwipeableTransactionItem({
  transaction,
  index,
  onPress,
  highlighted = false,
  onSwipeArmChange,
  onMarkEssential,
  onMarkOptional
}: {
  transaction: TransactionDto;
  index: number;
  onPress: () => void;
  highlighted?: boolean;
  onSwipeArmChange?: (isArmed: boolean) => void;
  onMarkEssential: () => void;
  onMarkOptional: () => void;
}) {
  const translateX = useMemo(() => new Animated.Value(0), []);
  const feedbackOpacity = useRef(new Animated.Value(0)).current;
  const [feedbackTone, setFeedbackTone] = useState<
    "essential" | "optional" | "focus" | null
  >(
    null
  );
  const [isSwipeArmed, setIsSwipeArmed] = useState(false);
  const transactionSwipeArmedRef = useRef(false);
  const armExpiryTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const isExpense = transaction.direction === "Expense";

  const rightLabelOpacity = useMemo(
    () =>
      translateX.interpolate({
        inputRange: [0, 42, 72],
        outputRange: [0, 0, 1],
        extrapolate: "clamp"
      }),
    [translateX]
  );
  const leftLabelOpacity = useMemo(
    () =>
      translateX.interpolate({
        inputRange: [-72, -42, 0],
        outputRange: [1, 0, 0],
        extrapolate: "clamp"
      }),
    [translateX]
  );

  const disarmTransactionSwipe = useCallback(() => {
    if (armExpiryTimeoutRef.current) {
      clearTimeout(armExpiryTimeoutRef.current);
      armExpiryTimeoutRef.current = null;
    }

    transactionSwipeArmedRef.current = false;
    onSwipeArmChange?.(false);
    setIsSwipeArmed(false);
  }, [onSwipeArmChange]);

  const armTransactionSwipe = useCallback(() => {
    if (!isExpense) {
      return;
    }

    if (armExpiryTimeoutRef.current) {
      clearTimeout(armExpiryTimeoutRef.current);
    }

    transactionSwipeArmedRef.current = true;
    onSwipeArmChange?.(true);
    setIsSwipeArmed(true);
    armExpiryTimeoutRef.current = setTimeout(() => {
      transactionSwipeArmedRef.current = false;
      onSwipeArmChange?.(false);
      setIsSwipeArmed(false);
      armExpiryTimeoutRef.current = null;
    }, 2500);
  }, [isExpense, onSwipeArmChange]);

  const showFeedback = useCallback(
    (tone: "essential" | "optional" | "focus") => {
      setFeedbackTone(tone);
      feedbackOpacity.setValue(0);
      Animated.sequence([
        Animated.timing(feedbackOpacity, {
          toValue: 1,
          duration: 140,
          useNativeDriver: true
        }),
        Animated.delay(520),
        Animated.timing(feedbackOpacity, {
          toValue: 0,
          duration: 220,
          useNativeDriver: true
        })
      ]).start(() => setFeedbackTone(null));
    },
    [feedbackOpacity]
  );

  useEffect(() => {
    if (!highlighted) {
      return;
    }

    showFeedback("focus");
  }, [highlighted, showFeedback]);

  useEffect(() => () => {
    if (armExpiryTimeoutRef.current) {
      clearTimeout(armExpiryTimeoutRef.current);
      armExpiryTimeoutRef.current = null;
    }

    disarmTransactionSwipe();
  }, [disarmTransactionSwipe]);

  const panResponder = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponder: (_event, gestureState) =>
          isExpense &&
          transactionSwipeArmedRef.current &&
          Math.abs(gestureState.dx) > 14 &&
          Math.abs(gestureState.dx) > Math.abs(gestureState.dy),
        onPanResponderMove: (_event, gestureState) => {
          translateX.setValue(Math.max(-92, Math.min(92, gestureState.dx)));
        },
        onPanResponderRelease: (_event, gestureState) => {
          if (gestureState.dx > 64) {
            onMarkEssential();
            showFeedback("essential");
          } else if (gestureState.dx < -64) {
            onMarkOptional();
            showFeedback("optional");
          }

          Animated.spring(translateX, {
            toValue: 0,
            useNativeDriver: true,
            tension: 130,
            friction: 12
          }).start();
          disarmTransactionSwipe();
        },
        onPanResponderTerminate: () => {
          Animated.spring(translateX, {
            toValue: 0,
            useNativeDriver: true,
            tension: 130,
            friction: 12
          }).start();
          disarmTransactionSwipe();
        }
      }),
    [disarmTransactionSwipe, isExpense, onMarkEssential, onMarkOptional, showFeedback, translateX]
  );

  if (!isExpense) {
    return (
      <TransactionRow
        transaction={transaction}
        index={index}
        onPress={onPress}
        showTimestamp
      />
    );
  }

  return (
    <View style={styles.swipeWrap}>
      <View style={styles.swipeBackdrop}>
        <Animated.Text style={[styles.swipeRightText, { opacity: rightLabelOpacity }]}>
          Flag spend
        </Animated.Text>
        <Animated.Text style={[styles.swipeLeftText, { opacity: leftLabelOpacity }]}>
          Skip focus
        </Animated.Text>
      </View>
      {feedbackTone ? (
        <Animated.View
          pointerEvents="none"
          style={[
            styles.feedbackOverlay,
            feedbackTone === "essential" ? styles.feedbackEssential : styles.feedbackOptional,
            feedbackTone === "focus" ? styles.feedbackFocus : null,
            { opacity: feedbackOpacity }
          ]}
        />
      ) : null}
      <Animated.View
        style={[
          styles.transactionSwipeWrap,
          isSwipeArmed ? styles.transactionSwipeWrapArmed : null,
          { transform: [{ translateX }] }
        ]}
        {...panResponder.panHandlers}
      >
        <TransactionRow
          transaction={transaction}
          index={index}
          onPress={() => {
            if (isSwipeArmed) {
              disarmTransactionSwipe();
              return;
            }

            onPress();
          }}
          onLongPress={armTransactionSwipe}
          delayLongPress={transactionSwipeHoldDelayMs}
          rowStyle={isSwipeArmed ? styles.transactionRowArmed : undefined}
          showTimestamp
        />
      </Animated.View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1
  },
  tabStage: {
    flex: 1
  },
  searchBackdrop: {
    ...StyleSheet.absoluteFillObject,
    zIndex: 44
  },
  feedWrap: {
    flex: 1
  },
  groupHeading: {
    color: palette.textSecondary,
    ...typography.caption,
    marginBottom: spacing[8]
  },
  listContent: {
    paddingTop: 0
  },
  loadingList: {
    gap: spacing[12]
  },
  loadingRow: {
    height: 78,
    borderRadius: 18
  },
  swipeWrap: {
    position: "relative"
  },
  transactionSwipeWrap: {
    borderRadius: 16
  },
  transactionSwipeWrapArmed: {
    shadowColor: "#6FD7FF",
    shadowOpacity: 0.24,
    shadowRadius: 18,
    shadowOffset: { width: 0, height: 0 },
    elevation: 6
  },
  transactionRowArmed: {
    borderColor: "rgba(111,215,255,0.58)",
    backgroundColor: "rgba(24,48,76,0.96)"
  },
  swipeBackdrop: {
    ...StyleSheet.absoluteFillObject,
    borderRadius: 16,
    borderWidth: 1,
    borderColor: "rgba(220,232,255,0.1)",
    backgroundColor: "rgba(17,39,66,0.6)",
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  feedbackOverlay: {
    ...StyleSheet.absoluteFillObject,
    borderRadius: 16
  },
  feedbackEssential: {
    backgroundColor: "rgba(28,197,131,0.18)"
  },
  feedbackOptional: {
    backgroundColor: "rgba(244,104,119,0.14)"
  },
  feedbackFocus: {
    backgroundColor: "rgba(111,215,255,0.18)"
  },
  swipeRightText: {
    color: palette.success,
    ...typography.caption
  },
  swipeLeftText: {
    color: palette.negative,
    ...typography.caption
  }
});





