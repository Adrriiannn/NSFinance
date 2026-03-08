import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useMemo } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { IconButton } from "../../../src/components/ui/IconButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { AnimatedCurrencyText } from "../../../src/components/ui/AnimatedCurrencyText";
import { TransactionRow } from "../../../src/components/transactions/TransactionRow";
import { useTransactionsQuery } from "../../../src/features/transactions/useTransactions";
import { usePlannerStore } from "../../../src/providers/PlannerProvider";
import { layout, palette, spacing, typography } from "../../../src/theme/tokens";

export default function PlannerCategoriesScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ filter?: string }>();
  const showUnassignedOnly = params.filter === "unassigned";
  const transactionsQuery = useTransactionsQuery();
  const plannerStore = usePlannerStore();

  const summary = useMemo(() => {
    const transactions = transactionsQuery.data ?? [];
    const totals = new Map<string, number>();
    const unassignedTransactions = [...transactions].filter(
      (transaction) => !plannerStore.annotations[transaction.id]?.category
    );

    transactions.forEach((transaction) => {
      const annotation = plannerStore.annotations[transaction.id];

      if (!annotation?.category || transaction.amount >= 0) {
        return;
      }

      totals.set(
        annotation.category,
        (totals.get(annotation.category) ?? 0) + Math.abs(transaction.amount)
      );
    });

    const categories = plannerStore.categoryCatalog.Expense.map((category) => ({
      category,
      spend: totals.get(category) ?? 0
    }));

    return {
      unassignedTransactions,
      categories
    };
  }, [plannerStore.annotations, plannerStore.categoryCatalog.Expense, transactionsQuery.data]);

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>{showUnassignedOnly ? "Unassigned transactions" : "Category health"}</Text>
        <View style={{ width: 40 }} />
      </View>

      {!showUnassignedOnly ? (
        <>
          {summary.unassignedTransactions.length > 0 ? (
            <Pressable
              style={({ pressed }) => [styles.summaryCard, pressed ? styles.summaryPressed : null]}
              onPress={() =>
                router.replace({
                  pathname: "/(tabs)/planner/categories",
                  params: { filter: "unassigned" }
                })
              }
            >
              <Text style={styles.summaryValue}>{summary.unassignedTransactions.length}</Text>
              <Text style={styles.summaryMeta}>Unassigned transactions to review</Text>
            </Pressable>
          ) : null}

          <View style={styles.listWrap}>
            {summary.categories.some((item) => item.spend > 0) ? (
              [...summary.categories]
                .sort((a, b) => b.spend - a.spend)
                .map((item) => (
                  <Pressable
                    key={item.category}
                    style={({ pressed }) => [styles.itemPressable, pressed ? styles.itemPressed : null]}
                    onPress={() =>
                      router.push({
                        pathname: "/(tabs)/planner/category/[category]",
                        params: { category: item.category }
                      })
                    }
                  >
                    <GlassCard style={styles.itemCard}>
                      <View style={styles.itemRow}>
                        <Text style={styles.itemTitle}>{item.category}</Text>
                        <AnimatedCurrencyText
                          value={-item.spend}
                          currency="EUR"
                          style={styles.itemValue}
                          baseColor={palette.textSecondary}
                        />
                      </View>
                    </GlassCard>
                  </Pressable>
                ))
            ) : (
              <EmptyState
                title="No category insights yet"
                message="Open transaction context on activity rows to assign categories."
              />
            )}
          </View>
        </>
      ) : summary.unassignedTransactions.length === 0 ? (
        <EmptyState
          title="All transactions are assigned"
          message="Great. There are no unassigned transactions to review right now."
        />
      ) : (
        <View style={styles.unassignedListWrap}>
          {summary.unassignedTransactions.map((transaction, index) => (
            <TransactionRow
              key={transaction.id}
              transaction={transaction}
              index={index}
              onPress={() =>
                router.push({
                  pathname: "/modals/transaction-context",
                  params: { transactionId: transaction.id }
                })
              }
            />
          ))}
        </View>
      )}
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: layout.screenTopPadding
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  headerTitle: {
    color: palette.textPrimary,
    ...typography.sectionTitle
  },
  summaryCard: {
    borderRadius: 16,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.82)",
    padding: spacing[12],
    gap: spacing[8]
  },
  summaryPressed: {
    opacity: 0.9
  },
  summaryValue: {
    color: palette.textPrimary,
    ...typography.title
  },
  summaryMeta: {
    color: palette.textSecondary,
    ...typography.body2
  },
  listWrap: {
    gap: spacing[12]
  },
  unassignedListWrap: {
    gap: spacing[12]
  },
  itemCard: {
    paddingVertical: spacing[12]
  },
  itemPressable: {
    borderRadius: 16
  },
  itemPressed: {
    opacity: 0.9
  },
  itemRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  itemTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  itemValue: {
    color: palette.textSecondary,
    ...typography.body2
  }
});
