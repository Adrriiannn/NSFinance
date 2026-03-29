import type { TransactionDto } from "../../types/api";
import type { NecessityItem, TransactionPlannerAnnotation } from "../../providers/PlannerProvider";
import { isReportableExpenseTransaction } from "../transactions/transferClassification";

export type EssentialTransactionItem = {
  transactionId: string;
  label: string;
  accountName: string;
  amount: number;
  category: string;
  bookedAtUtc: string;
};

export function getEssentialTransactions(
  transactions: TransactionDto[],
  annotations: Record<string, TransactionPlannerAnnotation>
): EssentialTransactionItem[] {
  return transactions
    .filter(
      (transaction) =>
        isReportableExpenseTransaction(transaction) &&
        annotations[transaction.id]?.type === "Essential"
    )
    .map((transaction) => {
      const annotation = annotations[transaction.id];
      return {
        transactionId: transaction.id,
        label: annotation?.merchant || transaction.description,
        accountName: transaction.accountName,
        amount: Math.abs(transaction.amount),
        category: annotation?.category ?? transaction.categoryName ?? "Uncategorized",
        bookedAtUtc: transaction.bookedAtUtc
      };
    })
    .sort(
      (left, right) =>
        new Date(right.bookedAtUtc).getTime() - new Date(left.bookedAtUtc).getTime()
    );
}

export function getNecessitiesSummary(input: {
  necessities: NecessityItem[];
  essentialTransactions: EssentialTransactionItem[];
  annotations: Record<string, TransactionPlannerAnnotation>;
}) {
  const manualEssentialCount = input.necessities.filter(
    (item) => item.type === "Essential"
  ).length;
  const manualOptionalCount = input.necessities.filter(
    (item) => item.type === "Optional"
  ).length;
  const manualEssentialTotal = input.necessities
    .filter((item) => item.type === "Essential")
    .reduce((sum, item) => sum + item.estimatedMonthlyCost, 0);
  const essentialTransactionTotal = input.essentialTransactions.reduce(
    (sum, item) => sum + item.amount,
    0
  );
  const essentialTransactionCount = input.essentialTransactions.length;
  const optionalTransactionCount = Object.values(input.annotations).filter(
    (item) => item.type === "Optional"
  ).length;

  return {
    essentialsCount: manualEssentialCount + essentialTransactionCount,
    optionalCount: manualOptionalCount + optionalTransactionCount,
    total: Number((manualEssentialTotal + essentialTransactionTotal).toFixed(2))
  };
}
