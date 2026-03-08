import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useMemo } from "react";
import { StyleSheet, Text, View } from "react-native";
import { TransactionRow } from "../../../../src/components/transactions/TransactionRow";
import { EmptyState } from "../../../../src/components/ui/EmptyState";
import { IconButton } from "../../../../src/components/ui/IconButton";
import { ScreenContainer } from "../../../../src/components/ui/ScreenContainer";
import { useTransactionsQuery } from "../../../../src/features/transactions/useTransactions";
import { formatCurrency, formatDate } from "../../../../src/lib/format";
import { usePlannerStore } from "../../../../src/providers/PlannerProvider";
import { layout, palette, spacing, typography } from "../../../../src/theme/tokens";

export default function PlannerCategoryDetailScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ category?: string }>();
  const category = params.category ?? "Category";
  const transactionsQuery = useTransactionsQuery();
  const plannerStore = usePlannerStore();

  const transactions = useMemo(() => {
    const list = transactionsQuery.data ?? [];
    return list.filter((transaction) => {
      const annotationCategory = plannerStore.annotations[transaction.id]?.category;
      return annotationCategory?.toLowerCase() === category.toLowerCase();
    });
  }, [category, plannerStore.annotations, transactionsQuery.data]);

  const totalSpend = useMemo(
    () =>
      Math.abs(
        transactions
          .filter((transaction) => transaction.amount < 0)
          .reduce((sum, transaction) => sum + transaction.amount, 0)
      ),
    [transactions]
  );

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>{category}</Text>
        <View style={{ width: 40 }} />
      </View>

      <Text style={styles.subHeader}>
        {transactions.length} transaction{transactions.length === 1 ? "" : "s"} |{" "}
        {formatCurrency(totalSpend, "EUR")}
      </Text>

      {transactions.length === 0 ? (
        <EmptyState
          title="No transactions in this category"
          message="Transactions assigned to this category will show up here."
        />
      ) : (
        <View style={styles.listWrap}>
          {transactions.map((transaction, index) => (
            <TransactionRow
              key={transaction.id}
              transaction={transaction}
              index={index}
              metadataOverride={`${transaction.accountName} | ${formatDate(transaction.bookedAtUtc)}`}
              onPress={() =>
                router.push({
                  pathname: "/(tabs)/planner/transaction/[id]",
                  params: { id: transaction.id }
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
  subHeader: {
    color: palette.textSecondary,
    ...typography.body2
  },
  listWrap: {
    gap: spacing[12]
  }
});
