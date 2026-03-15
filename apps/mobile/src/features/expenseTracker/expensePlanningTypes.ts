export type ExpensePlanStatus = "active" | "drafted" | "scheduled" | "completed";
export type ExpensePlanPeriodType = "weekly" | "monthly" | "custom";
export type ExpenseAnalyticsMode = "actual" | "planned" | "variance";
export type ExpensePlanPublicationStatus =
  | "draft_publication"
  | "pending_review"
  | "published"
  | "blocked"
  | "unpublished"
  | "flagged"
  | "removed";
export type ExpensePlanModerationStatus = "approved" | "blocked" | "needs_review" | "flagged_after_publish";
export type ExpensePlanPublicationSort = "trending" | "most_liked" | "most_downloaded" | "recently_added" | "newest";
export type ExpensePlanReportReason =
  | "spam"
  | "abusive_offensive"
  | "misleading"
  | "inappropriate"
  | "duplicate"
  | "dangerous_financial_advice"
  | "other";

export type ExpensePlanLineItem = {
  id: string;
  subcategoryId: number | null;
  expectedAmount: number;
  notes: string;
};

export type ExpensePlan = {
  id: string;
  title: string;
  status: ExpensePlanStatus;
  periodType: ExpensePlanPeriodType;
  startDate: string;
  endDate: string;
  creatorId: string;
  creatorName: string;
  creatorTag: string;
  lineItems: ExpensePlanLineItem[];
  isRecurring: boolean;
  recurrenceRule: string | null;
  isTemplate: boolean;
  isShared: boolean;
  sharedIdentity: string | null;
  sourcePlanId: string | null;
  importedFromPublicPlanId: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  completedAtUtc: string | null;
};

export type ExpensePlanDraft = {
  editingPlanId: string | null;
  title: string;
  periodType: ExpensePlanPeriodType;
  startDate: string;
  endDate: string;
  lineItems: ExpensePlanLineItem[];
  isRecurring: boolean;
  recurrenceRule: string | null;
  isTemplate: boolean;
  isShared: boolean;
  sourcePlanId: string | null;
};

export type ExpensePlanStatusMeta = {
  label: string;
  color: string;
  tint: string;
  icon: string;
};

export type ExpensePlanTaxonomyNode = {
  domainId: number;
  domainName: string;
  categoryId: number;
  categoryName: string;
  subcategoryId: number;
  subcategoryName: string;
};

export type ExpensePlanComputedLineItem = {
  id: string;
  subcategoryId: number | null;
  subcategoryName: string;
  categoryId: number | null;
  categoryName: string;
  domainId: number | null;
  domainName: string;
  expectedAmount: number;
  actualAmount: number;
  varianceAmount: number;
  entryCount: number;
};

export type ExpensePlanUnexpectedCategory = {
  subcategoryId: number | null;
  subcategoryName: string;
  categoryId: number | null;
  categoryName: string;
  domainId: number | null;
  domainName: string;
  totalAmount: number;
  entryCount: number;
  entryIds: string[];
};

export type ExpensePlanComputed = {
  expectedTotal: number;
  actualTotal: number;
  remainingAmount: number;
  varianceAmount: number;
  progressRatio: number;
  paceLabel: "on_track" | "ahead" | "over_pace";
  lineItems: ExpensePlanComputedLineItem[];
  unexpectedCategories: ExpensePlanUnexpectedCategory[];
  transactionCount: number;
};

export type ExpensePlanCategoryMetric = {
  key: string;
  categoryId: number | null;
  categoryName: string;
  domainId: number | null;
  domainName: string;
  amount: number;
  percentage: number;
  transactionCount: number;
  subcategories: Array<{
    subcategoryId: number | null;
    subcategoryName: string;
    amount: number;
    percentage: number;
    transactionCount: number;
  }>;
  entryIds: string[];
};

export type ExpensePlanPublicationModerationEvent = {
  id: string;
  triggerType: "pre_publish" | "metadata_update" | "rescan" | "report_threshold";
  resultStatus: ExpensePlanModerationStatus;
  summary: string;
  matchedRules: string[];
  createdAtUtc: string;
};

export type ExpensePlanPublicationReport = {
  id: string;
  reporterUserId: string;
  reporterName: string;
  reason: ExpensePlanReportReason;
  notes: string;
  status: "open" | "reviewed" | "dismissed";
  createdAtUtc: string;
};

export type ExpensePlanPublication = {
  id: string;
  sourcePlanId: string;
  creatorId: string;
  creatorName: string;
  creatorTag: string;
  publicTitle: string;
  publicDescription: string;
  tags: string[];
  publicationStatus: ExpensePlanPublicationStatus;
  moderationStatus: ExpensePlanModerationStatus;
  moderationSummary: string;
  planType: ExpensePlanPeriodType;
  isTemplate: boolean;
  isRecurring: boolean;
  expectedSpendTotal: number;
  likeCount: number;
  downloadCount: number;
  reportCount: number;
  likedByUserIds: string[];
  createdAtUtc: string;
  publishedAtUtc: string | null;
  lastModeratedAtUtc: string | null;
  lastRescannedAtUtc: string | null;
  lastReportedAtUtc: string | null;
  lineItems: ExpensePlanLineItem[];
  moderationEvents: ExpensePlanPublicationModerationEvent[];
  reports: ExpensePlanPublicationReport[];
};

export type ExpensePlanCommunityDashboard = {
  publishedCount: number;
  pendingReviewCount: number;
  flaggedCount: number;
  totalLikes: number;
  totalDownloads: number;
  totalReports: number;
  plans: ExpensePlanPublication[];
};
