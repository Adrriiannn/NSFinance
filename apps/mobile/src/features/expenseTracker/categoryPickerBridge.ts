let pendingActivityAddTransactionSubcategoryId: number | null = null;

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

let pendingActivitySearchCategorySelection: ActivitySearchCategorySelection | null = null;

export function setPendingActivityAddTransactionSubcategorySelection(subcategoryId: number) {
  pendingActivityAddTransactionSubcategoryId = subcategoryId;
}

export function consumePendingActivityAddTransactionSubcategorySelection() {
  const selected = pendingActivityAddTransactionSubcategoryId;
  pendingActivityAddTransactionSubcategoryId = null;
  return selected;
}

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
