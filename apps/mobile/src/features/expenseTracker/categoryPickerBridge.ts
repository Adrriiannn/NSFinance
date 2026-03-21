let pendingActivityAddTransactionSubcategoryId: number | null = null;

export function setPendingActivityAddTransactionSubcategorySelection(subcategoryId: number) {
  pendingActivityAddTransactionSubcategoryId = subcategoryId;
}

export function consumePendingActivityAddTransactionSubcategorySelection() {
  const selected = pendingActivityAddTransactionSubcategoryId;
  pendingActivityAddTransactionSubcategoryId = null;
  return selected;
}
