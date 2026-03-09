import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
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
import { TransactionRow } from "../../src/components/transactions/TransactionRow";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { EmptyState } from "../../src/components/ui/EmptyState";
import { FloatingActionButton } from "../../src/components/ui/FloatingActionButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { SkeletonBlock } from "../../src/components/ui/SkeletonBlock";
import {
  type ActivityFilter,
  applyActivityFilter,
  groupTransactionsByTimeBucket
} from "../../src/features/transactions/activityGrouping";
import { useTransactionsQuery } from "../../src/features/transactions/useTransactions";
import { usePlannerStore } from "../../src/providers/PlannerProvider";
import { getFloatingFabOffset } from "../../src/theme/insets";
import { layout, palette, spacing, typography } from "../../src/theme/tokens";
import type { TransactionDto } from "../../src/types/api";

const filters: ActivityFilter[] = ["All", "Income", "Expense", "Online", "In person"];

export default function ActivityTabScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ focusTransactionId?: string; focusNonce?: string }>();
  const insets = useSafeAreaInsets();
  const plannerStore = usePlannerStore();
  const [filter, setFilter] = useState<ActivityFilter>("All");
  const [filterDropdownOpen, setFilterDropdownOpen] = useState(false);
  const [highlightedTransactionId, setHighlightedTransactionId] = useState<string | null>(
    null
  );
  const sectionListRef = useRef<SectionList<TransactionDto>>(null);
  const handledFocusTransactionIdRef = useRef<string | null>(null);
  const transactionsQuery = useTransactionsQuery();
  const isInitialLoading = transactionsQuery.isLoading && !transactionsQuery.data;
  const refreshing = transactionsQuery.isRefetching && !isInitialLoading;
  const focusTransactionId =
    typeof params.focusTransactionId === "string" ? params.focusTransactionId : "";
  const focusNonce = typeof params.focusNonce === "string" ? params.focusNonce : "";
  const focusKey = focusTransactionId ? `${focusTransactionId}:${focusNonce}` : "";

  const listBottomInset = Math.max(
    spacing[12],
    getFloatingFabOffset(insets.bottom, -spacing[20])
  );

  const grouped = useMemo(() => {
    const data = applyActivityFilter(transactionsQuery.data ?? [], filter);
    return groupTransactionsByTimeBucket(data)
      .filter((section) => section.items.length > 0)
      .map((section) => ({
        title: section.title,
        data: section.items
      }));
  }, [transactionsQuery.data, filter]);

  useEffect(() => {
    if (!focusTransactionId || handledFocusTransactionIdRef.current === focusKey) {
      return;
    }

    handledFocusTransactionIdRef.current = focusKey;
    setFilter("All");
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
    <ScreenContainer
      scrollable={false}
      contentStyle={styles.container}
    >
      <View style={styles.headerWrap}>
        <View style={styles.selectorRow}>
          <Pressable
            style={styles.filterSelector}
            onPress={() => setFilterDropdownOpen((current) => !current)}
          >
            <Text style={styles.filterSelectorText}>{filter}</Text>
            <Ionicons
              name={filterDropdownOpen ? "chevron-up" : "chevron-down"}
              size={16}
              color={palette.textSecondary}
            />
          </Pressable>
          <View style={styles.selectorRightSpacer} />
        </View>

        {filterDropdownOpen ? (
          <View style={styles.filterDropdown}>
            {filters.map((item) => (
              <Pressable
                key={item}
                style={({ pressed }) => [
                  styles.filterDropdownItem,
                  filter === item ? styles.filterDropdownItemActive : null,
                  pressed ? styles.filterDropdownItemPressed : null
                ]}
                onPress={() => {
                  setFilter(item);
                  setFilterDropdownOpen(false);
                }}
              >
                <Text
                  style={[
                    styles.filterDropdownItemText,
                    filter === item ? styles.filterDropdownItemTextActive : null
                  ]}
                >
                  {item}
                </Text>
              </Pressable>
            ))}
          </View>
        ) : null}
      </View>

      <View style={styles.feedWrap}>
      <SectionList
        ref={sectionListRef}
        sections={grouped}
        keyExtractor={(item) => item.id}
        stickySectionHeadersEnabled={false}
        bounces={false}
        renderSectionHeader={({ section }) => (
          <Text style={styles.groupHeading}>{section.title}</Text>
        )}
        renderItem={({ item, index }) => (
          <SwipeableTransactionItem
            transaction={item}
            index={index}
            onPress={() =>
              router.push({
                pathname: "/modals/transaction-context",
                params: { transactionId: item.id }
              })
            }
            highlighted={item.id === highlightedTransactionId}
            onMarkEssential={() =>
              plannerStore.saveAnnotation({
                transactionId: item.id,
                type: "Essential",
                direction: item.direction
              })
            }
            onMarkOptional={() =>
              plannerStore.saveAnnotation({
                transactionId: item.id,
                type: "Optional",
                direction: item.direction
              })
            }
          />
        )}
        contentContainerStyle={[styles.listContent, { paddingBottom: listBottomInset }]}
        SectionSeparatorComponent={() => <View style={{ height: spacing[12] }} />}
        ItemSeparatorComponent={() => <View style={{ height: spacing[12] }} />}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => {
              void transactionsQuery.refetch();
            }}
            tintColor={palette.textSecondary}
          />
        }
        ListEmptyComponent={
          isInitialLoading ? (
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
          ) : (
            <EmptyState
              title="No transactions yet"
              message="Manual transactions will appear here once added."
              actionLabel="Add transaction"
              onActionPress={() => router.push("/modals/add-transaction")}
            />
          )
        }
      />
      </View>

      <FloatingActionButton
        label="Add transaction"
        onPress={() => router.push("/modals/add-transaction")}
        icon={<Ionicons name="add" size={20} color={palette.textPrimary} />}
      />
    </ScreenContainer>
  );
}

function SwipeableTransactionItem({
  transaction,
  index,
  onPress,
  highlighted = false,
  onMarkEssential,
  onMarkOptional
}: {
  transaction: TransactionDto;
  index: number;
  onPress: () => void;
  highlighted?: boolean;
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

  const panResponder = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponder: (_event, gestureState) =>
          isExpense &&
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
        },
        onPanResponderTerminate: () => {
          Animated.spring(translateX, {
            toValue: 0,
            useNativeDriver: true,
            tension: 130,
            friction: 12
          }).start();
        }
      }),
    [isExpense, onMarkEssential, onMarkOptional, showFeedback, translateX]
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
          Mark essential
        </Animated.Text>
        <Animated.Text style={[styles.swipeLeftText, { opacity: leftLabelOpacity }]}>
          Mark optional
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
      <Animated.View style={{ transform: [{ translateX }] }} {...panResponder.panHandlers}>
        <TransactionRow
          transaction={transaction}
          index={index}
          onPress={onPress}
          showTimestamp
        />
      </Animated.View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    paddingTop: layout.screenTopPadding,
    paddingBottom: 0
  },
  headerWrap: {
    gap: spacing[12],
    marginBottom: spacing[12],
    backgroundColor: "transparent",
    zIndex: 20,
    elevation: 20
  },
  selectorRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  filterSelector: {
    flex: 1,
    minHeight: 42,
    maxHeight: 42,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingHorizontal: spacing[12]
  },
  filterSelectorText: {
    color: palette.textPrimary,
    ...typography.title2
  },
  selectorRightSpacer: {
    width: 42,
    height: 42
  },
  filterDropdown: {
    gap: spacing[8],
    zIndex: 21,
    elevation: 21
  },
  filterDropdownItem: {
    minHeight: 44,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.75)",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  filterDropdownItemActive: {
    borderColor: palette.primaryGlow,
    backgroundColor: "rgba(47,107,255,0.2)"
  },
  filterDropdownItemPressed: {
    opacity: 0.88
  },
  filterDropdownItemText: {
    color: palette.textPrimary,
    ...typography.body1
  },
  filterDropdownItemTextActive: {
    fontWeight: "700"
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
