import type {
  ExpensePlan,
  ExpensePlanCommunityDashboard,
  ExpensePlanModerationStatus,
  ExpensePlanPublication,
  ExpensePlanPublicationModerationEvent,
  ExpensePlanPublicationSort,
  ExpensePlanReportReason
} from "./expensePlanningTypes";

const blockedTerms = [
  "get rich quick",
  "guaranteed profit",
  "guaranteed returns",
  "loan shark",
  "ponzi",
  "scam people",
  "evade tax",
  "don't pay taxes"
];

const reviewTerms = [
  "borrow to invest",
  "max out credit cards",
  "ignore bills",
  "skip insurance",
  "payday loan forever",
  "casino strategy",
  "betting recovery"
];

function nowIso() {
  return new Date().toISOString();
}

function round(value: number) {
  return Number(value.toFixed(2));
}

function normalizeTags(tags: string[]) {
  return tags
    .map((tag) => tag.trim())
    .filter(Boolean)
    .filter((tag, index, arr) => arr.findIndex((candidate) => candidate.toLowerCase() === tag.toLowerCase()) === index)
    .slice(0, 12);
}

function buildTrendingScore(publication: ExpensePlanPublication, now = new Date()) {
  const anchor = publication.publishedAtUtc ?? publication.createdAtUtc;
  const ageDays = Math.max((now.getTime() - new Date(anchor).getTime()) / (24 * 60 * 60 * 1000), 1);
  const recencyBoost = Math.max(14 - ageDays, 0);
  const reportPenalty = publication.reportCount * 2 + (publication.publicationStatus === "flagged" ? 6 : 0);
  return round((publication.likeCount * 3) + (publication.downloadCount * 4) + recencyBoost - reportPenalty);
}

export function scanExpensePlanPublicationContent(input: {
  title: string;
  description: string;
  tags: string[];
}) {
  const normalized = `${input.title}\n${input.description}\n${input.tags.join(" ")}`.toLowerCase();
  const blockedMatches = blockedTerms.filter((term) => normalized.includes(term));
  if (blockedMatches.length > 0) {
    return {
      moderationStatus: "blocked" as ExpensePlanModerationStatus,
      publicationStatus: "blocked" as const,
      summary: `Blocked by moderation rules: ${blockedMatches.join(", ")}.`,
      matchedRules: blockedMatches
    };
  }

  const reviewMatches = reviewTerms.filter((term) => normalized.includes(term));
  const hasLink = /https?:\/\/|www\./i.test(normalized);
  const hasSpamCaps = /\b[A-Z]{8,}\b/.test(input.title) || /(.)\1{5,}/.test(input.title);
  if (hasLink) {
    reviewMatches.push("external_links");
  }
  if (hasSpamCaps) {
    reviewMatches.push("spammy_formatting");
  }

  if (reviewMatches.length > 0) {
    return {
      moderationStatus: "needs_review" as ExpensePlanModerationStatus,
      publicationStatus: "pending_review" as const,
      summary: `Needs moderation review: ${Array.from(new Set(reviewMatches)).join(", ")}.`,
      matchedRules: Array.from(new Set(reviewMatches))
    };
  }

  return {
    moderationStatus: "approved" as ExpensePlanModerationStatus,
    publicationStatus: "published" as const,
    summary: "Approved for publication.",
    matchedRules: []
  };
}

export function buildExpensePlanPublicationFromPlan(input: {
  plan: ExpensePlan;
  creatorId: string;
  creatorName: string;
  creatorTag: string;
  title: string;
  description: string;
  tags: string[];
}) {
  const moderation = scanExpensePlanPublicationContent({
    title: input.title,
    description: input.description,
    tags: input.tags
  });
  const createdAtUtc = nowIso();
  const moderationEvent: ExpensePlanPublicationModerationEvent = {
    id: `mod-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
    triggerType: "pre_publish",
    resultStatus: moderation.moderationStatus,
    summary: moderation.summary,
    matchedRules: moderation.matchedRules,
    createdAtUtc
  };

  const publication: ExpensePlanPublication = {
    id: `pub-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
    sourcePlanId: input.plan.id,
    creatorId: input.creatorId,
    creatorName: input.creatorName,
    creatorTag: input.creatorTag,
    publicTitle: input.title.trim(),
    publicDescription: input.description.trim(),
    tags: normalizeTags(input.tags),
    publicationStatus: moderation.publicationStatus,
    moderationStatus: moderation.moderationStatus,
    moderationSummary: moderation.summary,
    planType: input.plan.periodType,
    isTemplate: input.plan.isTemplate,
    isRecurring: input.plan.isRecurring,
    expectedSpendTotal: round(input.plan.lineItems.reduce((sum, item) => sum + item.expectedAmount, 0)),
    likeCount: 0,
    downloadCount: 0,
    reportCount: 0,
    likedByUserIds: [],
    createdAtUtc,
    publishedAtUtc: moderation.publicationStatus === "published" ? createdAtUtc : null,
    lastModeratedAtUtc: createdAtUtc,
    lastRescannedAtUtc: createdAtUtc,
    lastReportedAtUtc: null,
    lineItems: input.plan.lineItems.map((item) => ({ ...item })),
    moderationEvents: [moderationEvent],
    reports: []
  };

  return publication;
}

function makeSeedPublication(plan: ExpensePlan, overrides: Partial<ExpensePlanPublication>): ExpensePlanPublication {
  return {
    id: overrides.id ?? `seed-pub-${plan.id}`,
    sourcePlanId: plan.id,
    creatorId: overrides.creatorId ?? plan.creatorId,
    creatorName: overrides.creatorName ?? plan.creatorName,
    creatorTag: overrides.creatorTag ?? plan.creatorTag,
    publicTitle: overrides.publicTitle ?? plan.title,
    publicDescription: overrides.publicDescription ?? "A community plan shared to help others start with a strong structure.",
    tags: overrides.tags ?? [plan.periodType, plan.isTemplate ? "template" : "plan"],
    publicationStatus: overrides.publicationStatus ?? "published",
    moderationStatus: overrides.moderationStatus ?? "approved",
    moderationSummary: overrides.moderationSummary ?? "Approved for publication.",
    planType: overrides.planType ?? plan.periodType,
    isTemplate: overrides.isTemplate ?? plan.isTemplate,
    isRecurring: overrides.isRecurring ?? plan.isRecurring,
    expectedSpendTotal: overrides.expectedSpendTotal ?? round(plan.lineItems.reduce((sum, item) => sum + item.expectedAmount, 0)),
    likeCount: overrides.likeCount ?? 0,
    downloadCount: overrides.downloadCount ?? 0,
    reportCount: overrides.reportCount ?? 0,
    likedByUserIds: overrides.likedByUserIds ?? [],
    createdAtUtc: overrides.createdAtUtc ?? nowIso(),
    publishedAtUtc: overrides.publishedAtUtc ?? nowIso(),
    lastModeratedAtUtc: overrides.lastModeratedAtUtc ?? nowIso(),
    lastRescannedAtUtc: overrides.lastRescannedAtUtc ?? nowIso(),
    lastReportedAtUtc: overrides.lastReportedAtUtc ?? null,
    lineItems: overrides.lineItems ?? plan.lineItems.map((item) => ({ ...item })),
    moderationEvents: overrides.moderationEvents ?? [],
    reports: overrides.reports ?? []
  };
}

export function buildExpensePlanCommunitySeedPublications(plans: ExpensePlan[], currentUserId: string) {
  const basePlans = plans.slice(0, 4);
  const seedNow = new Date();
  return [
    makeSeedPublication(basePlans[0] ?? plans[0], {
      id: "publication-household-runway",
      creatorId: "creator-aisling",
      creatorName: "Aisling Byrne",
      creatorTag: "@aisling_byrne",
      publicTitle: "Monthly household runway",
      publicDescription: "A grounded monthly essentials plan for rent, utilities, groceries, and recurring basics.",
      tags: ["monthly", "household", "essentials"],
      likeCount: 184,
      downloadCount: 129,
      createdAtUtc: new Date(seedNow.getTime() - 4 * 24 * 60 * 60 * 1000).toISOString(),
      publishedAtUtc: new Date(seedNow.getTime() - 3 * 24 * 60 * 60 * 1000).toISOString()
    }),
    makeSeedPublication(basePlans[1] ?? plans[0], {
      id: "publication-commute-week",
      creatorId: currentUserId,
      creatorName: "You",
      creatorTag: "@you",
      publicTitle: "Commute and quick meals",
      publicDescription: "Weekly transport and grab-and-go food planning for office-heavy weeks.",
      tags: ["weekly", "commute", "food"],
      likeCount: 61,
      downloadCount: 43,
      createdAtUtc: new Date(seedNow.getTime() - 2 * 24 * 60 * 60 * 1000).toISOString(),
      publishedAtUtc: new Date(seedNow.getTime() - 36 * 60 * 60 * 1000).toISOString()
    }),
    makeSeedPublication(basePlans[2] ?? plans[0], {
      id: "publication-school-term",
      creatorId: "creator-noah",
      creatorName: "Noah Kelly",
      creatorTag: "@noah_kelly",
      publicTitle: "School term setup",
      publicDescription: "A monthly family setup plan covering lunches, uniforms, school supplies, and after-school costs.",
      tags: ["monthly", "family", "school"],
      likeCount: 98,
      downloadCount: 87,
      createdAtUtc: new Date(seedNow.getTime() - 8 * 24 * 60 * 60 * 1000).toISOString(),
      publishedAtUtc: new Date(seedNow.getTime() - 7 * 24 * 60 * 60 * 1000).toISOString()
    }),
    makeSeedPublication(basePlans[3] ?? plans[0], {
      id: "publication-essentials-template",
      creatorId: "creator-claire",
      creatorName: "Claire O'Shea",
      creatorTag: "@claire_oshea",
      publicTitle: "Essentials reset template",
      publicDescription: "A clean template for monthly reset planning with groceries, bills, and a small buffer category.",
      tags: ["template", "monthly", "reset"],
      isTemplate: true,
      likeCount: 142,
      downloadCount: 203,
      createdAtUtc: new Date(seedNow.getTime() - 11 * 24 * 60 * 60 * 1000).toISOString(),
      publishedAtUtc: new Date(seedNow.getTime() - 10 * 24 * 60 * 60 * 1000).toISOString()
    })
  ].map((publication) => ({
    ...publication,
    reportCount: publication.reports.length,
    expectedSpendTotal: round(publication.lineItems.reduce((sum, item) => sum + item.expectedAmount, 0))
  }));
}

export function searchAndSortExpensePlanPublications(input: {
  publications: ExpensePlanPublication[];
  search: string;
  sort: ExpensePlanPublicationSort;
  planType: "all" | ExpensePlan["periodType"];
  creatorFilter: string;
  templatesOnly: boolean;
}) {
  const now = new Date();
  const search = input.search.trim().toLowerCase();
  const creatorFilter = input.creatorFilter.trim().toLowerCase();

  const filtered = input.publications.filter((publication) => {
    if (publication.publicationStatus !== "published") {
      return false;
    }
    if (input.templatesOnly && !publication.isTemplate) {
      return false;
    }
    if (input.planType !== "all" && publication.planType !== input.planType) {
      return false;
    }
    if (creatorFilter && !`${publication.creatorName} ${publication.creatorTag}`.toLowerCase().includes(creatorFilter)) {
      return false;
    }
    if (!search) {
      return true;
    }

    return [
      publication.publicTitle,
      publication.publicDescription,
      publication.creatorName,
      publication.creatorTag,
      publication.planType,
      publication.tags.join(" ")
    ].join(" ").toLowerCase().includes(search);
  });

  const withScore = filtered.map((publication) => ({
    ...publication,
    expectedSpendTotal: round(publication.lineItems.reduce((sum, item) => sum + item.expectedAmount, 0))
  }));

  return withScore.sort((left, right) => {
    if (input.sort === "most_liked") {
      return right.likeCount - left.likeCount || new Date(right.publishedAtUtc ?? right.createdAtUtc).getTime() - new Date(left.publishedAtUtc ?? left.createdAtUtc).getTime();
    }
    if (input.sort === "most_downloaded") {
      return right.downloadCount - left.downloadCount || new Date(right.publishedAtUtc ?? right.createdAtUtc).getTime() - new Date(left.publishedAtUtc ?? left.createdAtUtc).getTime();
    }
    if (input.sort === "recently_added") {
      return new Date(right.publishedAtUtc ?? right.createdAtUtc).getTime() - new Date(left.publishedAtUtc ?? left.createdAtUtc).getTime();
    }
    if (input.sort === "newest") {
      return new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime();
    }

    return buildTrendingScore(right, now) - buildTrendingScore(left, now);
  });
}

export function buildExpensePlanCommunityDashboard(publications: ExpensePlanPublication[], creatorId: string): ExpensePlanCommunityDashboard {
  const mine = publications.filter((publication) => publication.creatorId === creatorId);
  return {
    publishedCount: mine.filter((publication) => publication.publicationStatus === "published").length,
    pendingReviewCount: mine.filter((publication) => publication.publicationStatus === "pending_review").length,
    flaggedCount: mine.filter((publication) => publication.publicationStatus === "flagged").length,
    totalLikes: mine.reduce((sum, publication) => sum + publication.likeCount, 0),
    totalDownloads: mine.reduce((sum, publication) => sum + publication.downloadCount, 0),
    totalReports: mine.reduce((sum, publication) => sum + publication.reportCount, 0),
    plans: [...mine].sort((left, right) => new Date(right.publishedAtUtc ?? right.createdAtUtc).getTime() - new Date(left.publishedAtUtc ?? left.createdAtUtc).getTime())
  };
}

export function buildPublicationSections(publications: ExpensePlanPublication[]) {
  const visible = publications.filter((publication) => publication.publicationStatus === "published");
  const trending = searchAndSortExpensePlanPublications({
    publications: visible,
    search: "",
    sort: "trending",
    planType: "all",
    creatorFilter: "",
    templatesOnly: false
  });
  const popular = searchAndSortExpensePlanPublications({
    publications: visible,
    search: "",
    sort: "most_liked",
    planType: "all",
    creatorFilter: "",
    templatesOnly: false
  });
  const recent = searchAndSortExpensePlanPublications({
    publications: visible,
    search: "",
    sort: "recently_added",
    planType: "all",
    creatorFilter: "",
    templatesOnly: false
  });

  return {
    featured: trending.slice(0, 2),
    popularThisWeek: popular.slice(0, 5),
    recentlyAdded: recent.slice(0, 5)
  };
}

export const expensePlanReportReasons: Array<{ id: ExpensePlanReportReason; label: string }> = [
  { id: "spam", label: "Spam" },
  { id: "abusive_offensive", label: "Abusive / offensive" },
  { id: "misleading", label: "Misleading" },
  { id: "inappropriate", label: "Inappropriate" },
  { id: "duplicate", label: "Duplicate" },
  { id: "dangerous_financial_advice", label: "Dangerous financial advice" },
  { id: "other", label: "Other" }
];
