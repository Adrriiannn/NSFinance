import type { TransactionDto } from "../../types/api";

export const TRANSFER_DOMAIN_ID = 920;

function isTransferCategoryId(categoryId: number | null) {
  return categoryId !== null && categoryId >= 92000 && categoryId < 93000;
}

function isTransferSubcategoryId(subcategoryId: number | null) {
  return subcategoryId !== null && subcategoryId >= 920000 && subcategoryId < 930000;
}

export function isTransferTransaction(transaction: TransactionDto) {
  return (
    transaction.taxonomyDomainId === TRANSFER_DOMAIN_ID
    || isTransferCategoryId(transaction.taxonomyCategoryId)
    || isTransferSubcategoryId(transaction.taxonomySubcategoryId)
  );
}

export function isReportableExpenseTransaction(transaction: TransactionDto) {
  return transaction.amount < 0 && !isTransferTransaction(transaction);
}

export function isReportableIncomeTransaction(transaction: TransactionDto) {
  return transaction.amount > 0 && !isTransferTransaction(transaction);
}

