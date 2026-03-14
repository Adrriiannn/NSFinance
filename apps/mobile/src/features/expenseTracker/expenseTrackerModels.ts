export type ExpenseTrackerQuickRange = "all" | "today" | "week" | "month";
export type ExpenseTrackerSortOrder = "newest" | "oldest" | "highest" | "lowest";

export const expenseTrackerCategoryOptions = [
  { label: "Groceries", value: "Groceries", icon: "basket-outline", color: "#68D7A9" },
  { label: "Bills", value: "Bills", icon: "receipt-outline", color: "#7CB6FF" },
  { label: "Eating Out", value: "Eating Out", icon: "restaurant-outline", color: "#FFB86C" },
  { label: "Transport", value: "Transport", icon: "car-outline", color: "#89A8FF" },
  { label: "Shopping", value: "Shopping", icon: "bag-handle-outline", color: "#FF8FA3" },
  { label: "Entertainment", value: "Entertainment", icon: "film-outline", color: "#9B8CFF" },
  { label: "Health", value: "Health", icon: "medkit-outline", color: "#66D6D2" },
  { label: "Gifts", value: "Gifts", icon: "gift-outline", color: "#FF9B7E" },
  { label: "Travel", value: "Travel", icon: "airplane-outline", color: "#7CD4FF" },
  { label: "Education", value: "Education", icon: "school-outline", color: "#C8A2FF" },
  { label: "Subscriptions", value: "Subscriptions", icon: "repeat-outline", color: "#F6C75F" },
  { label: "Other", value: "Other", icon: "ellipse-outline", color: "#9AAAC7" }
] as const;

export const expenseTrackerPaymentSourceOptions = [
  { label: "Cash", value: "Cash", icon: "cash-outline" },
  { label: "AIB", value: "AIB", icon: "card-outline" },
  { label: "BOI", value: "BOI", icon: "card-outline" },
  { label: "Revolut", value: "Revolut", icon: "phone-portrait-outline" },
  { label: "Credit Card", value: "Credit Card", icon: "card-outline" },
  { label: "Savings", value: "Savings", icon: "wallet-outline" },
  { label: "Other", value: "Other", icon: "layers-outline" }
] as const;

export const expenseTrackerStatusOptions = [
  { label: "Completed", value: "completed" },
  { label: "Planned", value: "planned" }
] as const;

export const expenseTrackerQuickRangeOptions = [
  { label: "All", value: "all" },
  { label: "Today", value: "today" },
  { label: "This week", value: "week" },
  { label: "This month", value: "month" }
] as const;

export const expenseTrackerSortOptions = [
  { label: "Newest", value: "newest" },
  { label: "Oldest", value: "oldest" },
  { label: "Highest", value: "highest" },
  { label: "Lowest", value: "lowest" }
] as const;
