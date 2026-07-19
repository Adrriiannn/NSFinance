import { Ionicons } from "@expo/vector-icons";
import { useFocusEffect, useLocalSearchParams, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Animated,
  Easing,
  InteractionManager,
  PanResponder,
  Pressable,
  RefreshControl,
  SectionList,
  StyleSheet,
  Text,
  View
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { TransactionRow } from "../../../src/components/ui/rows/TransactionRow";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { useMainTabSwipeNavigation } from "../../../src/components/layout/useHorizontalSiblingSwipe";
import { Skeleton } from "../../../src/components/ui/feedback/Skeleton";
import { TabEmptyStateCard } from "../../../src/components/ui/TabEmptyStateCard";
import { ActivitySearchBar } from "../../../src/features/activity/components/ActivitySearchBar";
import {
  consumeActivitySearchSnapshot,
  setActivitySearchSnapshot
} from "../../../src/features/activity/search/activitySearch.bridge";
import { setActivityFeedInteracting } from "../../../src/features/activity/activityFeedRuntime";
import { useActivitySearch } from "../../../src/features/activity/search/activitySearch.hooks";
import type {
  ActivityAccountSuggestion,
  ActivityTaxonomySearchEntry
} from "../../../src/features/activity/search/activitySearch.types";
import { AdaptiveScreen } from "../../../src/layout/adaptive/AdaptiveScreen";
import { HeaderActionButton, HeaderShell } from "../../../src/layout/appHeader";
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
import { buildConnectBankRoute } from "../../../src/features/banking/bankingLinking";
import { getGlobalSyncFeedbackMessage } from "../../../src/features/banking/bankingSyncFeedback";
import { useConnectBankCtaLabels } from "../../../src/features/banking/connectBankCta";
import { useGlobalBankSyncMutation } from "../../../src/features/banking/useBanking";
import { logActivityFocusEvent } from "../../../src/features/transactions/activityFocusNavigation";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { useTransactionPagesInfiniteQuery } from "../../../src/features/transactions/useTransactionPageQuery";
import { showFlashMessage } from "../../../src/lib/flashMessage";
import { useUserProfileQuery } from "../../../src/features/users/useUserSettings";
import { usePlannerStore } from "../../../src/providers/PlannerProvider";
import { useThemeRuntime } from "../../../src/theme/runtime/ThemeRuntimeProvider";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";
import type { TransactionDto } from "../../../src/types/api";

const transactionSwipeHoldDelayMs = 1000;

type PendingFocusRequest = {
  key: string;
  transactionId: string;
};

export default function ActivityTabScreen() {
  useThemeRuntime();
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
  const connectBankCta = useConnectBankCtaLabels();
  const profileQuery = useUserProfileQuery();
  const globalSyncMutation = useGlobalBankSyncMutation();
  const [highlightedTransactionId, setHighlightedTransactionId] = useState<string | null>(
    null
  );
  const [pendingFocusRequest, setPendingFocusRequest] = useState<PendingFocusRequest | null>(
    null
  );
  const initialSearchSnapshotRef = useRef(consumeActivitySearchSnapshot());
  const sectionListRef = useRef<SectionList<TransactionDto>>(null);
  const handledFocusKeyRef = useRef<string | null>(null);
  const syncSpinValue = useRef(new Animated.Value(0)).current;
  const syncSpinDelayTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const syncSpinLoopRef = useRef<Animated.CompositeAnimation | null>(null);
  const transactionsQuery = useTransactionsQuery();
  const transactionPages = useTransactionPagesInfiniteQuery();
  const pagedTransactions = useMemo(
    () => transactionPages.data?.pages.flatMap((page) => page.items) ?? [],
    [transactionPages.data]
  );
  const [isManualRefreshing, setIsManualRefreshing] = useState(false);
  const [showSyncSpin, setShowSyncSpin] = useState(false);
  const [isListInteracting, setIsListInteracting] = useState(false);
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
  const linkedAccountSuggestions = useMemo<ActivityAccountSuggestion[]>(
    () =>
      (accountsQuery.data ?? []).map((account) => ({
        id: account.id,
        name: account.name,
        type: account.type,
        currentBalance: account.currentBalance,
        currency: account.currency,
        transactionCount: account.transactionCount,
        providerId: account.providerId,
        providerDisplayName: account.providerDisplayName,
        providerIconUrl: account.providerIconUrl,
        providerLogoUrl: account.providerLogoUrl
      })),
    [accountsQuery.data]
  );
  const search = useActivitySearch({
    transactions: transactionsQuery.data ?? [],
    annotations: plannerStore.annotations,
    preferredCurrency: profileQuery.data?.preferredCurrency,
    accountCurrencies: (accountsQuery.data ?? []).map((account) => account.currency),
    accountSuggestions: linkedAccountSuggestions,
    taxonomyEntries,
    initialSnapshot: initialSearchSnapshotRef.current,
    onRequestCategoryPicker: (snapshot) => {
      setActivitySearchSnapshot(snapshot);
      router.push({
        pathname: "/(tabs)/activity/categories" as never,
        params: {
          selectionMode: "true",
          selectionTarget: "activitySearchCategoryFilter"
        }
      });
    }
  });
  const setSearchSnapshot = search.setSnapshot;
  const applyPickedCategory = search.applyPickedCategory;
  const dismissSearchDropdown = search.dismissDropdown;

  // Browsing renders from the canonical cursor-paged contract; the unbounded
  // legacy list only backs an active search until server-side search exists.
  const hasActiveSearch =
    search.tokens.length > 0 || search.rawSearchText.trim().length > 0;
  const isInitialLoading = hasActiveSearch
    ? transactionsQuery.isLoading && !transactionsQuery.data
    : transactionPages.isLoading && !transactionPages.data;
  const refreshing = isManualRefreshing && !isInitialLoading;
  const activeSourceIsError = hasActiveSearch
    ? transactionsQuery.isError
    : transactionPages.isError;

  const handleRefresh = useCallback(async () => {
    setIsManualRefreshing(true);
    try {
      if (hasActiveSearch) {
        await transactionsQuery.refetch();
      } else {
        await transactionPages.refetch();
      }
    } finally {
      setIsManualRefreshing(false);
    }
  }, [hasActiveSearch, transactionsQuery, transactionPages]);

  const groupedCandidate = useMemo(() => {
    const source = hasActiveSearch ? search.filteredTransactions : pagedTransactions;
    return groupTransactionsByTimeBucket(source)
      .filter((section) => section.items.length > 0)
      .map((section) => ({
        title: section.title,
        data: section.items
      }));
  }, [hasActiveSearch, search.filteredTransactions, pagedTransactions]);
  const [grouped, setGrouped] = useState(groupedCandidate);
  const pendingGroupedRef = useRef(groupedCandidate);

  const commitPendingGrouped = useCallback(() => {
    setIsListInteracting(false);
    setActivityFeedInteracting(false);
    if (pendingGroupedRef.current !== grouped) {
      setGrouped(pendingGroupedRef.current);
    }
  }, [grouped]);

  const beginListInteraction = useCallback(() => {
    setIsListInteracting(true);
    setActivityFeedInteracting(true);
  }, []);

  useEffect(() => {
    pendingGroupedRef.current = groupedCandidate;
    if (!isListInteracting) {
      setGrouped(groupedCandidate);
    }
  }, [groupedCandidate, isListInteracting]);

  useEffect(() => {
    return () => {
      setActivityFeedInteracting(false);
    };
  }, []);
  const hasAnyTransactions = hasActiveSearch
    ? (transactionsQuery.data?.length ?? 0) > 0
    : pagedTransactions.length > 0;
  const showNoSearchResultsState =
    !isInitialLoading &&
    !activeSourceIsError &&
    grouped.length === 0 &&
    hasAnyTransactions &&
    hasActiveSearch;
  const showNoActivityState =
    !isInitialLoading && !activeSourceIsError && grouped.length === 0 && !showNoSearchResultsState;

  useFocusEffect(
    useCallback(() => {
      const restoredSnapshot = consumeActivitySearchSnapshot();
      if (restoredSnapshot) {
        setSearchSnapshot(restoredSnapshot);
      }

      const pickedCategorySelection = consumePendingActivitySearchCategorySelection();
      if (pickedCategorySelection) {
        applyPickedCategory(pickedCategorySelection);
      }

      return undefined;
    }, [applyPickedCategory, setSearchSnapshot])
  );

  useEffect(() => {
    if (!showSyncSpin) {
      syncSpinLoopRef.current?.stop();
      syncSpinLoopRef.current = null;
      syncSpinValue.stopAnimation();
      syncSpinValue.setValue(0);
      return;
    }

    const loop = Animated.loop(
      Animated.timing(syncSpinValue, {
        toValue: 1,
        duration: 740,
        easing: Easing.linear,
        useNativeDriver: true
      })
    );
    syncSpinLoopRef.current = loop;
    loop.start();

    return () => {
      loop.stop();
      syncSpinLoopRef.current = null;
      syncSpinValue.stopAnimation();
      syncSpinValue.setValue(0);
    };
  }, [showSyncSpin, syncSpinValue]);

  useEffect(() => {
    return () => {
      if (syncSpinDelayTimerRef.current) {
        clearTimeout(syncSpinDelayTimerRef.current);
        syncSpinDelayTimerRef.current = null;
      }
    };
  }, []);

  const handleGlobalSyncPress = useCallback(async () => {
    if (globalSyncMutation.isPending) {
      return;
    }

    if (syncSpinDelayTimerRef.current) {
      clearTimeout(syncSpinDelayTimerRef.current);
    }

    syncSpinDelayTimerRef.current = setTimeout(() => {
      setShowSyncSpin(true);
    }, 220);

    try {
      const result = await globalSyncMutation.mutateAsync({
        trigger: "manual",
        source: "activity_header"
      });
      const feedback = getGlobalSyncFeedbackMessage(result);
      showFlashMessage(feedback.message, { tone: feedback.tone });
    } catch (error) {
      console.info("[Banking Sync]", {
        event: "manual_sync_request_failed",
        source: "activity_header",
        message: error instanceof Error ? error.message : "unknown_sync_error"
      });
      showFlashMessage("Sync failed. Please try again later.", { tone: "error" });
    } finally {
      if (syncSpinDelayTimerRef.current) {
        clearTimeout(syncSpinDelayTimerRef.current);
        syncSpinDelayTimerRef.current = null;
      }
      setShowSyncSpin(false);
    }
  }, [globalSyncMutation]);

  const syncIconRotate = syncSpinValue.interpolate({
    inputRange: [0, 1],
    outputRange: ["0deg", "360deg"]
  });

  useFocusEffect(
    useCallback(() => {
      return () => {
        dismissSearchDropdown();
      };
    }, [dismissSearchDropdown])
  );

  useEffect(() => {
    if (!focusTransactionId || !focusKey || handledFocusKeyRef.current === focusKey) {
      return;
    }

    handledFocusKeyRef.current = focusKey;
    setPendingFocusRequest({
      key: focusKey,
      transactionId: focusTransactionId
    });
    setHighlightedTransactionId(focusTransactionId);
    logActivityFocusEvent("focus_target_received", {
      focusKey,
      transactionId: focusTransactionId
    });
  }, [focusKey, focusTransactionId]);

  useEffect(() => {
    if (!pendingFocusRequest) {
      return;
    }

    if (activeSourceIsError) {
      logActivityFocusEvent("focus_target_skipped", {
        reason: "transactions_query_error",
        focusKey: pendingFocusRequest.key,
        transactionId: pendingFocusRequest.transactionId
      });
      setPendingFocusRequest(null);
      setHighlightedTransactionId(null);
      return;
    }

    if (isInitialLoading) {
      return;
    }

    if (grouped.length === 0) {
      logActivityFocusEvent("focus_target_skipped", {
        reason: "activity_feed_empty",
        focusKey: pendingFocusRequest.key,
        transactionId: pendingFocusRequest.transactionId
      });
      setPendingFocusRequest(null);
      setHighlightedTransactionId(null);
      return;
    }

    let targetSectionIndex = -1;
    let targetItemIndex = -1;

    grouped.forEach((section, sectionIndex) => {
      if (targetSectionIndex >= 0) {
        return;
      }

      const itemIndex = section.data.findIndex((item) => item.id === pendingFocusRequest.transactionId);
      if (itemIndex >= 0) {
        targetSectionIndex = sectionIndex;
        targetItemIndex = itemIndex;
      }
    });

    if (targetSectionIndex < 0 || targetItemIndex < 0) {
      logActivityFocusEvent("focus_target_skipped", {
        reason: hasActiveSearch ? "target_not_found_in_filtered_activity_feed" : "target_not_found_in_activity_feed",
        focusKey: pendingFocusRequest.key,
        transactionId: pendingFocusRequest.transactionId,
        hasActiveSearch,
        groupedSectionCount: grouped.length
      });
      setPendingFocusRequest(null);
      setHighlightedTransactionId(null);
      return;
    }

    logActivityFocusEvent("focus_target_scroll_attempt", {
      focusKey: pendingFocusRequest.key,
      transactionId: pendingFocusRequest.transactionId,
      sectionIndex: targetSectionIndex,
      itemIndex: targetItemIndex
    });

    InteractionManager.runAfterInteractions(() => {
      requestAnimationFrame(() => {
        try {
          sectionListRef.current?.scrollToLocation({
            sectionIndex: targetSectionIndex,
            itemIndex: targetItemIndex,
            viewPosition: 0.34,
            animated: true
          });
          logActivityFocusEvent("focus_target_scroll_success", {
            focusKey: pendingFocusRequest.key,
            transactionId: pendingFocusRequest.transactionId,
            sectionIndex: targetSectionIndex,
            itemIndex: targetItemIndex
          });
        } catch (error) {
          logActivityFocusEvent("focus_target_scroll_failed", {
            focusKey: pendingFocusRequest.key,
            transactionId: pendingFocusRequest.transactionId,
            message: error instanceof Error ? error.message : "unknown_scroll_error"
          });
          setHighlightedTransactionId(null);
        }
      });
    });
    setPendingFocusRequest(null);

    setTimeout(() => {
      setHighlightedTransactionId((current) =>
        current === pendingFocusRequest.transactionId ? null : current
      );
    }, 1800);
  }, [grouped, hasActiveSearch, isInitialLoading, pendingFocusRequest, activeSourceIsError]);

  return (
    <AdaptiveScreen contentStyle={styles.container} gestureHandlers={gestureHandlers}>
      <Animated.View style={[styles.tabStage, animatedStyle]}>
        <HeaderShell
          preset="primaryTwoRowSearch"
          includeTopInset
          title="Activity"
          trailingAction={
            <HeaderActionButton
              icon={
                <Animated.View
                  style={showSyncSpin ? { transform: [{ rotate: syncIconRotate }] } : undefined}
                >
                  <Ionicons name="refresh-outline" size={18} color={palette.textPrimary} />
                </Animated.View>
              }
              accessibilityLabel="Sync all linked banks"
              onPress={() => {
                void handleGlobalSyncPress();
              }}
            />
          }
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
              accountSuggestions={search.accountSuggestions}
              dateSuggestions={search.dateSuggestionResult.suggestions}
              currencies={search.availableCurrencies}
              selectedCurrency={search.activeCurrencyCode}
              onFocusSearch={search.focusSearch}
              onOpenFilters={search.toggleFilters}
              onSetRawSearchText={search.setRawSearchText}
              onSetActiveDraft={search.setActiveTokenDraft}
              onConfirmActiveDraft={search.confirmTokenDraft}
              onSelectFilter={search.selectFilterOption}
              onSelectMerchantSuggestion={search.selectMerchantSuggestion}
              onSelectAccountSuggestion={search.selectAccountSuggestion}
              onSelectDateSuggestion={search.selectDateSuggestion}
              onSelectCurrency={search.selectCurrency}
              onEditToken={search.beginEditingToken}
              onRemoveToken={search.removeToken}
              onOpenCategoryPicker={() => {
                const snapshot = search.getSnapshot();
                setActivitySearchSnapshot(snapshot);
                router.push({
                  pathname: "/(tabs)/activity/categories" as never,
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
              <Skeleton style={styles.loadingRow} />
              <Skeleton style={styles.loadingRow} />
              <Skeleton style={styles.loadingRow} />
              <Skeleton style={styles.loadingRow} />
            </View>
          ) : activeSourceIsError ? (
            <ErrorState
              title="Could not load activity"
              message={
                (hasActiveSearch ? transactionsQuery.error?.message : transactionPages.error?.message)
                ?? "Something went wrong."
              }
              onRetry={() => {
                if (hasActiveSearch) {
                  void transactionsQuery.refetch();
                } else {
                  void transactionPages.refetch();
                }
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
              ctaLabel={connectBankCta.primaryLabel}
              onCtaPress={() =>
                router.push(buildConnectBankRoute({ intent: "new", returnTo: "/(tabs)/activity" }))
              }
              verticalSpacingMode="tab-aligned"
              hideOrb
              centerText
            />
          ) : (
            <SectionList
              ref={sectionListRef}
              sections={grouped}
              keyExtractor={(item) => item.id}
              stickySectionHeadersEnabled={false}
              bounces={false}
              onScrollBeginDrag={() => {
                beginListInteraction();
                search.dismissDropdown();
              }}
              onMomentumScrollBegin={beginListInteraction}
              onScrollEndDrag={(event) => {
                const velocityY = event.nativeEvent.velocity?.y ?? 0;
                if (Math.abs(velocityY) < 0.15) {
                  commitPendingGrouped();
                }
              }}
              onMomentumScrollEnd={commitPendingGrouped}
              onEndReachedThreshold={0.6}
              onEndReached={() => {
                if (
                  !hasActiveSearch
                  && transactionPages.hasNextPage
                  && !transactionPages.isFetchingNextPage
                ) {
                  void transactionPages.fetchNextPage();
                }
              }}
              ListFooterComponent={
                !hasActiveSearch && transactionPages.isFetchingNextPage ? (
                  <View style={styles.pageFooterLoading}>
                    <Skeleton style={styles.loadingRow} />
                    <Skeleton style={styles.loadingRow} />
                  </View>
                ) : null
              }
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
                      direction: item.direction === "Adjustment" ? undefined : item.direction
                    })
                  }
                  onMarkOptional={() =>
                    plannerStore.saveAnnotation({
                      transactionId: item.id,
                      type: null,
                      reason: "Skipped from focus",
                      direction: item.direction === "Adjustment" ? undefined : item.direction
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

const styles = createRuntimeStyleSheet(() => ({
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
    borderRadius: 6
  },
  pageFooterLoading: {
    gap: spacing[8],
    paddingTop: spacing[8],
    paddingBottom: spacing[16]
  },
  swipeWrap: {
    position: "relative"
  },
  transactionSwipeWrap: {
    borderRadius: 6
  },
  transactionSwipeWrapArmed: {
    shadowColor: "#FFBE7A",
    shadowOpacity: 0.24,
    shadowRadius: 18,
    shadowOffset: { width: 0, height: 0 },
    elevation: 6
  },
  transactionRowArmed: {
    borderColor: "rgba(255,190,122,0.58)",
    backgroundColor: surfaces.fieldStrong
  },
  swipeBackdrop: {
    ...StyleSheet.absoluteFillObject,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.2)",
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  feedbackOverlay: {
    ...StyleSheet.absoluteFillObject,
    borderRadius: 6
  },
  feedbackEssential: {
    backgroundColor: "rgba(28,197,131,0.18)"
  },
  feedbackOptional: {
    backgroundColor: "rgba(244,104,119,0.14)"
  },
  feedbackFocus: {
    backgroundColor: "rgba(255,190,122,0.18)"
  },
  swipeRightText: {
    color: palette.success,
    ...typography.caption
  },
  swipeLeftText: {
    color: palette.negative,
    ...typography.caption
  }
}));






