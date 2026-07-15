let pendingTransactionDetailCategorySelection: TransactionDetailCategorySelection | null = null;

export type ActivitySearchCategorySelection = {
  scope: "domain" | "category" | "subcategory";
  domainId: number | null;
  domainName: string;
  categoryId: number | null;
  categoryName: string;
  subcategoryId: number | null;
  subcategoryName: string;
  excludedCategoryIds: number[];
  excludedSubcategoryIds: number[];
};

export type TransactionDetailCategorySelection = {
  domainId: number;
  domainName: string;
  categoryId: number;
  categoryName: string;
  subcategoryId: number | null;
  subcategoryName: string;
};

let pendingActivitySearchCategorySelection: ActivitySearchCategorySelection | null = null;

export function setPendingActivitySearchCategorySelection(
  selection: ActivitySearchCategorySelection
) {
  pendingActivitySearchCategorySelection = selection;
}

export function consumePendingActivitySearchCategorySelection() {
  const selected = pendingActivitySearchCategorySelection;
  pendingActivitySearchCategorySelection = null;
  return selected;
}

export function setPendingTransactionDetailCategorySelection(
  selection: TransactionDetailCategorySelection
) {
  pendingTransactionDetailCategorySelection = selection;
}

export function consumePendingTransactionDetailCategorySelection() {
  const selected = pendingTransactionDetailCategorySelection;
  pendingTransactionDetailCategorySelection = null;
  return selected;
}
