import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import * as SecureStore from "expo-secure-store";
import {
  deleteJsonFileStorage,
  readJsonFileStorage,
  writeJsonFileStorage
} from "../../lib/storage/jsonFileStore";
import { useAuthSession } from "../../providers/AuthProvider";
import type {
  ExpensePlan,
  ExpensePlanCommunityDashboard,
  ExpensePlanDraft,
  ExpensePlanPublication,
  ExpensePlanReportReason,
  ExpensePlanStatus
} from "./expensePlanningTypes";
import {
  buildExpensePlanCreatorTag,
  buildExpensePlanDraftFromPlan,
  buildExpensePlanFromDraft,
  buildExpensePlanningSeedPlans,
  createEmptyExpensePlanDraft,
  createExpensePlanLineItem,
  duplicateExpensePlan
} from "./expensePlanningUtils";
import {
  buildExpensePlanCommunityDashboard,
  buildExpensePlanCommunitySeedPublications,
  buildExpensePlanPublicationFromPlan,
  scanExpensePlanPublicationContent
} from "./expensePlanCommunityUtils";

const PLANS_STORAGE_KEY = "nsfinance.expense_plans.v1";
const BUILDER_STORAGE_KEY = "nsfinance.expense_plan_builder.v1";
const COMMUNITY_STORAGE_KEY = "nsfinance.expense_plan_community.v1";

type ExpensePlanningContextValue = {
  plans: ExpensePlan[];
  publications: ExpensePlanPublication[];
  builderDraft: ExpensePlanDraft | null;
  selectionLineItemId: string | null;
  createNewPlanDraft: () => void;
  startEditingPlan: (planId: string) => void;
  startDuplicatePlan: (planId: string) => void;
  updateBuilderDraft: (patch: Partial<ExpensePlanDraft>) => void;
  updateBuilderLineItem: (lineItemId: string, patch: Partial<ExpensePlanDraft["lineItems"][number]>) => void;
  addBuilderLineItem: () => void;
  removeBuilderLineItem: (lineItemId: string) => void;
  setSelectionLineItemId: (lineItemId: string | null) => void;
  assignBuilderLineItemSubcategory: (lineItemId: string, subcategoryId: number) => void;
  clearBuilderDraft: () => void;
  saveBuilderDraftAs: (status: Extract<ExpensePlanStatus, "drafted" | "scheduled" | "active">) => ExpensePlan | null;
  cancelScheduledPlan: (planId: string) => void;
  completePlan: (planId: string) => void;
  sharePlan: (planId: string) => Promise<void>;
  publishPlan: (input: { planId: string; publicTitle: string; publicDescription: string; tags: string[] }) => { publication: ExpensePlanPublication | null; error: string | null };
  updatePublication: (publicationId: string, input: { publicTitle: string; publicDescription: string; tags: string[] }) => { publication: ExpensePlanPublication | null; error: string | null };
  togglePublicationLike: (publicationId: string) => void;
  usePublication: (publicationId: string) => ExpensePlan | null;
  reportPublication: (publicationId: string, reason: ExpensePlanReportReason, notes: string) => { ok: boolean; error: string | null };
  unpublishPublication: (publicationId: string) => void;
  rescanPublication: (publicationId: string) => ExpensePlanPublication | null;
  getPlanById: (planId: string) => ExpensePlan | null;
  getPublicationById: (publicationId: string) => ExpensePlanPublication | null;
  getCreatorDashboard: () => ExpensePlanCommunityDashboard;
};

const ExpensePlanningContext = createContext<ExpensePlanningContextValue | undefined>(undefined);

type ExpensePlanningProviderProps = {
  children: React.ReactNode;
};

function normalizePublicationStatus(publication: ExpensePlanPublication, now = new Date()): ExpensePlanPublication {
  const moderation = scanExpensePlanPublicationContent({
    title: publication.publicTitle,
    description: publication.publicDescription,
    tags: publication.tags
  });

  const nextStatus = publication.publicationStatus === "unpublished"
    ? "unpublished"
    : publication.publicationStatus === "removed"
      ? "removed"
      : publication.publicationStatus === "published" && moderation.publicationStatus === "pending_review"
        ? "flagged"
        : moderation.publicationStatus;

  const nextModerationStatus = publication.publicationStatus === "published" && moderation.moderationStatus === "needs_review"
    ? "flagged_after_publish"
    : moderation.moderationStatus;

  const nextEvent = nextModerationStatus === publication.moderationStatus && publication.lastRescannedAtUtc
    ? publication.moderationEvents
    : [
        {
          id: `mod-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
          triggerType: "rescan" as const,
          resultStatus: nextModerationStatus,
          summary: moderation.summary,
          matchedRules: moderation.matchedRules,
          createdAtUtc: now.toISOString()
        },
        ...publication.moderationEvents
      ];

  return {
    ...publication,
    publicationStatus: nextStatus,
    moderationStatus: nextModerationStatus,
    moderationSummary: moderation.summary,
    reportCount: publication.reports.length,
    expectedSpendTotal: Number(publication.lineItems.reduce((sum, item) => sum + item.expectedAmount, 0).toFixed(2)),
    lastModeratedAtUtc: now.toISOString(),
    lastRescannedAtUtc: now.toISOString(),
    moderationEvents: nextEvent
  };
}

export function ExpensePlanningProvider({ children }: ExpensePlanningProviderProps) {
  const { session } = useAuthSession();
  const creatorName = session?.user.displayName?.trim() || session?.user.fullName?.trim() || "You";
  const creatorId = session?.user.id ?? "local-user";
  const creatorTag = buildExpensePlanCreatorTag(creatorName, session?.user.primaryEmail);

  const [plans, setPlans] = useState<ExpensePlan[]>([]);
  const [publications, setPublications] = useState<ExpensePlanPublication[]>([]);
  const [builderDraft, setBuilderDraft] = useState<ExpensePlanDraft | null>(null);
  const [selectionLineItemId, setSelectionLineItemId] = useState<string | null>(null);
  const [hasHydrated, setHasHydrated] = useState(false);

  useEffect(() => {
    const hydrate = async () => {
      try {
        const [storedPlans, storedBuilder, storedCommunity] = await Promise.all([
          readJsonFileStorage<ExpensePlan[]>(PLANS_STORAGE_KEY),
          readJsonFileStorage<ExpensePlanDraft>(BUILDER_STORAGE_KEY),
          readJsonFileStorage<ExpensePlanPublication[]>(COMMUNITY_STORAGE_KEY)
        ]);

        if (storedPlans) {
          setPlans(storedPlans);
        }
        if (storedBuilder) {
          setBuilderDraft(storedBuilder);
        }
        if (storedCommunity) {
          setPublications(storedCommunity.map((item) => normalizePublicationStatus(item)));
        }

        if (storedPlans || storedBuilder || storedCommunity) {
          setHasHydrated(true);
          return;
        }

        const [rawPlans, rawBuilder, rawCommunity] = await Promise.all([
          SecureStore.getItemAsync(PLANS_STORAGE_KEY),
          SecureStore.getItemAsync(BUILDER_STORAGE_KEY),
          SecureStore.getItemAsync(COMMUNITY_STORAGE_KEY)
        ]);

        if (rawPlans) {
          const parsedPlans = JSON.parse(rawPlans) as ExpensePlan[];
          setPlans(parsedPlans);
          await writeJsonFileStorage(PLANS_STORAGE_KEY, parsedPlans);
          await SecureStore.deleteItemAsync(PLANS_STORAGE_KEY);
        }
        if (rawBuilder) {
          const parsedBuilder = JSON.parse(rawBuilder) as ExpensePlanDraft;
          setBuilderDraft(parsedBuilder);
          await writeJsonFileStorage(BUILDER_STORAGE_KEY, parsedBuilder);
          await SecureStore.deleteItemAsync(BUILDER_STORAGE_KEY);
        }
        if (rawCommunity) {
          const parsedCommunity = (JSON.parse(rawCommunity) as ExpensePlanPublication[]).map((item) =>
            normalizePublicationStatus(item)
          );
          setPublications(parsedCommunity);
          await writeJsonFileStorage(COMMUNITY_STORAGE_KEY, parsedCommunity);
          await SecureStore.deleteItemAsync(COMMUNITY_STORAGE_KEY);
        }
      } catch {
        // Ignore hydration issues and fall back to seeded state.
      } finally {
        setHasHydrated(true);
      }
    };

    void hydrate();
  }, []);

  useEffect(() => {
    if (!hasHydrated) {
      return;
    }

    setPlans((current) => {
      if (current.length > 0) {
        return current.map((plan) => {
          if (plan.creatorId !== "local-user") {
            return plan;
          }

          return {
            ...plan,
            creatorId,
            creatorName,
            creatorTag
          };
        });
      }

      return buildExpensePlanningSeedPlans({
        creatorId,
        creatorName,
        creatorTag
      });
    });
  }, [creatorId, creatorName, creatorTag, hasHydrated]);

  useEffect(() => {
    if (!hasHydrated || plans.length === 0) {
      return;
    }

    setPublications((current) => {
      if (current.length > 0) {
        return current.map((publication) => publication.creatorId === "local-user"
          ? {
              ...publication,
              creatorId,
              creatorName,
              creatorTag
            }
          : publication);
      }

      return buildExpensePlanCommunitySeedPublications(plans, creatorId);
    });
  }, [creatorId, creatorName, creatorTag, hasHydrated, plans]);

  useEffect(() => {
    if (!hasHydrated) {
      return;
    }

    void writeJsonFileStorage(PLANS_STORAGE_KEY, plans);
  }, [hasHydrated, plans]);

  useEffect(() => {
    if (!hasHydrated) {
      return;
    }

    void writeJsonFileStorage(COMMUNITY_STORAGE_KEY, publications);
  }, [hasHydrated, publications]);

  useEffect(() => {
    if (!hasHydrated) {
      return;
    }

    if (!builderDraft) {
      void deleteJsonFileStorage(BUILDER_STORAGE_KEY);
      return;
    }

    void writeJsonFileStorage(BUILDER_STORAGE_KEY, builderDraft);
  }, [builderDraft, hasHydrated]);

  const getPlanById = useCallback((planId: string) => plans.find((plan) => plan.id === planId) ?? null, [plans]);
  const getPublicationById = useCallback((publicationId: string) => publications.find((publication) => publication.id === publicationId) ?? null, [publications]);

  const createNewPlanDraft = useCallback(() => {
    setBuilderDraft(createEmptyExpensePlanDraft());
    setSelectionLineItemId(null);
  }, []);

  const startEditingPlan = useCallback((planId: string) => {
    const plan = plans.find((item) => item.id === planId);
    if (!plan) {
      return;
    }

    if (plan.status === "completed") {
      setBuilderDraft(duplicateExpensePlan(plan));
    } else {
      setBuilderDraft(buildExpensePlanDraftFromPlan(plan));
    }
    setSelectionLineItemId(null);
  }, [plans]);

  const startDuplicatePlan = useCallback((planId: string) => {
    const plan = plans.find((item) => item.id === planId);
    if (!plan) {
      return;
    }

    setBuilderDraft(duplicateExpensePlan(plan));
    setSelectionLineItemId(null);
  }, [plans]);

  const updateBuilderDraft = useCallback((patch: Partial<ExpensePlanDraft>) => {
    setBuilderDraft((current) => current ? { ...current, ...patch } : current);
  }, []);

  const updateBuilderLineItem = useCallback((lineItemId: string, patch: Partial<ExpensePlanDraft["lineItems"][number]>) => {
    setBuilderDraft((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        lineItems: current.lineItems.map((item) => item.id === lineItemId ? { ...item, ...patch } : item)
      };
    });
  }, []);

  const addBuilderLineItem = useCallback(() => {
    setBuilderDraft((current) => current ? { ...current, lineItems: [...current.lineItems, createExpensePlanLineItem()] } : current);
  }, []);

  const removeBuilderLineItem = useCallback((lineItemId: string) => {
    setBuilderDraft((current) => {
      if (!current) {
        return current;
      }

      const nextLineItems = current.lineItems.filter((item) => item.id !== lineItemId);
      return {
        ...current,
        lineItems: nextLineItems.length > 0 ? nextLineItems : [createExpensePlanLineItem()]
      };
    });
  }, []);

  const assignBuilderLineItemSubcategory = useCallback((lineItemId: string, subcategoryId: number) => {
    setBuilderDraft((current) => current ? {
      ...current,
      lineItems: current.lineItems.map((item) => item.id === lineItemId ? { ...item, subcategoryId } : item)
    } : current);
    setSelectionLineItemId(null);
  }, []);

  const clearBuilderDraft = useCallback(() => {
    setBuilderDraft(null);
    setSelectionLineItemId(null);
  }, []);

  const saveBuilderDraftAs = useCallback((status: Extract<ExpensePlanStatus, "drafted" | "scheduled" | "active">) => {
    if (!builderDraft) {
      return null;
    }

    const existing = builderDraft.editingPlanId ? plans.find((plan) => plan.id === builderDraft.editingPlanId) ?? null : null;
    const nextPlan = buildExpensePlanFromDraft(builderDraft, {
      creatorId: existing?.creatorId ?? creatorId,
      creatorName: existing?.creatorName ?? creatorName,
      creatorTag: existing?.creatorTag ?? creatorTag,
      status,
      existingPlanId: existing?.id ?? null,
      sharedIdentity: existing?.sharedIdentity ?? null,
      completedAtUtc: existing?.completedAtUtc ?? null
    });

    setPlans((current) => {
      const existingIndex = current.findIndex((plan) => plan.id === nextPlan.id);
      if (existingIndex < 0) {
        return [nextPlan, ...current];
      }

      const copy = [...current];
      copy[existingIndex] = {
        ...current[existingIndex],
        ...nextPlan,
        createdAtUtc: current[existingIndex].createdAtUtc,
        updatedAtUtc: new Date().toISOString()
      };
      return copy;
    });

    setBuilderDraft(null);
    return nextPlan;
  }, [builderDraft, creatorId, creatorName, creatorTag, plans]);

  const cancelScheduledPlan = useCallback((planId: string) => {
    setPlans((current) => current.map((plan) => plan.id === planId && plan.status === "scheduled"
      ? { ...plan, status: "drafted", updatedAtUtc: new Date().toISOString() }
      : plan));
  }, []);

  const completePlan = useCallback((planId: string) => {
    setPlans((current) => current.map((plan) => plan.id === planId
      ? { ...plan, status: "completed", completedAtUtc: new Date().toISOString(), updatedAtUtc: new Date().toISOString() }
      : plan));
  }, []);

  const sharePlan = useCallback(async (planId: string) => {
    setPlans((current) => current.map((item) => item.id === planId
      ? { ...item, isShared: true, sharedIdentity: item.sharedIdentity ?? `share-${item.id}`, updatedAtUtc: new Date().toISOString() }
      : item));
  }, []);

  const publishPlan = useCallback((input: { planId: string; publicTitle: string; publicDescription: string; tags: string[] }) => {
    const plan = plans.find((item) => item.id === input.planId);
    if (!plan) {
      return { publication: null, error: "Choose a plan to publish." };
    }

    const existing = publications.find((item) => item.sourcePlanId === plan.id && item.creatorId === creatorId && item.publicationStatus !== "removed");
    if (existing) {
      return { publication: null, error: "This plan already has a public version. Edit the existing publication instead." };
    }

    const publication = buildExpensePlanPublicationFromPlan({
      plan,
      creatorId,
      creatorName,
      creatorTag,
      title: input.publicTitle,
      description: input.publicDescription,
      tags: input.tags
    });

    setPlans((current) => current.map((item) => item.id === plan.id
      ? { ...item, isShared: true, sharedIdentity: publication.id, updatedAtUtc: new Date().toISOString() }
      : item));
    setPublications((current) => [publication, ...current]);
    return { publication, error: null };
  }, [creatorId, creatorName, creatorTag, plans, publications]);

  const updatePublication = useCallback((publicationId: string, input: { publicTitle: string; publicDescription: string; tags: string[] }) => {
    let nextPublication: ExpensePlanPublication | null = null;
    setPublications((current) => current.map((publication) => {
      if (publication.id !== publicationId || publication.creatorId !== creatorId) {
        return publication;
      }

      const moderation = scanExpensePlanPublicationContent({
        title: input.publicTitle,
        description: input.publicDescription,
        tags: input.tags
      });
      const updated: ExpensePlanPublication = {
        ...publication,
        publicTitle: input.publicTitle.trim(),
        publicDescription: input.publicDescription.trim(),
        tags: input.tags,
        publicationStatus: publication.publicationStatus === "unpublished"
          ? "unpublished"
          : publication.publicationStatus === "published" && moderation.publicationStatus === "pending_review"
            ? "flagged"
            : moderation.publicationStatus,
        moderationStatus: publication.publicationStatus === "published" && moderation.moderationStatus === "needs_review"
          ? "flagged_after_publish"
          : moderation.moderationStatus,
        moderationSummary: moderation.summary,
        lastModeratedAtUtc: new Date().toISOString(),
        lastRescannedAtUtc: new Date().toISOString(),
        moderationEvents: [
          {
            id: `mod-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
            triggerType: "metadata_update",
            resultStatus: publication.publicationStatus === "published" && moderation.moderationStatus === "needs_review" ? "flagged_after_publish" : moderation.moderationStatus,
            summary: moderation.summary,
            matchedRules: moderation.matchedRules,
            createdAtUtc: new Date().toISOString()
          },
          ...publication.moderationEvents
        ]
      };
      nextPublication = updated;
      return updated;
    }));

    return { publication: nextPublication, error: nextPublication ? null : "Publication not found." };
  }, [creatorId]);

  const togglePublicationLike = useCallback((publicationId: string) => {
    setPublications((current) => current.map((publication) => {
      if (publication.id !== publicationId || publication.publicationStatus !== "published") {
        return publication;
      }

      const hasLiked = publication.likedByUserIds.includes(creatorId);
      return {
        ...publication,
        likedByUserIds: hasLiked
          ? publication.likedByUserIds.filter((id) => id !== creatorId)
          : [...publication.likedByUserIds, creatorId],
        likeCount: hasLiked ? Math.max(publication.likeCount - 1, 0) : publication.likeCount + 1
      };
    }));
  }, [creatorId]);

  const usePublication = useCallback((publicationId: string) => {
    const publication = publications.find((item) => item.id === publicationId && item.publicationStatus === "published");
    if (!publication) {
      return null;
    }

    const nowUtc = new Date().toISOString();
    const plan: ExpensePlan = {
      id: `plan-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
      title: `${publication.publicTitle} copy`,
      status: "drafted",
      periodType: publication.planType,
      startDate: new Date().toISOString().slice(0, 10),
      endDate: new Date().toISOString().slice(0, 10),
      creatorId,
      creatorName,
      creatorTag,
      lineItems: publication.lineItems.map((item) => ({ ...item, id: createExpensePlanLineItem(item.subcategoryId).id })),
      isRecurring: publication.isRecurring,
      recurrenceRule: publication.isRecurring ? "Monthly" : null,
      isTemplate: false,
      isShared: false,
      sharedIdentity: null,
      sourcePlanId: null,
      importedFromPublicPlanId: publication.id,
      createdAtUtc: nowUtc,
      updatedAtUtc: nowUtc,
      completedAtUtc: null
    };

    setPlans((current) => [plan, ...current]);
    setPublications((current) => current.map((item) => item.id === publication.id ? { ...item, downloadCount: item.downloadCount + 1 } : item));
    return plan;
  }, [creatorId, creatorName, creatorTag, publications]);

  const reportPublication = useCallback((publicationId: string, reason: ExpensePlanReportReason, notes: string) => {
    let ok = false;
    let error: string | null = null;
    setPublications((current) => current.map((publication) => {
      if (publication.id !== publicationId || publication.publicationStatus !== "published") {
        return publication;
      }

      if (publication.creatorId === creatorId) {
        error = "You cannot report your own publication.";
        return publication;
      }

      const existing = publication.reports.find((report) => report.reporterUserId === creatorId && report.reason === reason && report.status === "open");
      if (existing) {
        error = "You have already submitted this report.";
        return publication;
      }

      ok = true;
      const report = {
        id: `report-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
        reporterUserId: creatorId,
        reporterName: creatorName,
        reason,
        notes: notes.trim(),
        status: "open" as const,
        createdAtUtc: new Date().toISOString()
      };
      const reportCount = publication.reports.length + 1;
      return {
        ...publication,
        reports: [report, ...publication.reports],
        reportCount,
        lastReportedAtUtc: new Date().toISOString(),
        publicationStatus: reportCount >= 2 ? "flagged" : publication.publicationStatus,
        moderationStatus: reportCount >= 2 ? "flagged_after_publish" : publication.moderationStatus,
        moderationSummary: reportCount >= 2 ? "Flagged after repeated reports." : publication.moderationSummary,
        moderationEvents: reportCount >= 2
          ? [{
              id: `mod-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
              triggerType: "report_threshold",
              resultStatus: "flagged_after_publish",
              summary: "Flagged after repeated reports.",
              matchedRules: ["report_threshold"],
              createdAtUtc: new Date().toISOString()
            }, ...publication.moderationEvents]
          : publication.moderationEvents
      };
    }));
    return { ok, error };
  }, [creatorId, creatorName]);

  const unpublishPublication = useCallback((publicationId: string) => {
    setPublications((current) => current.map((publication) => publication.id === publicationId && publication.creatorId === creatorId
      ? { ...publication, publicationStatus: "unpublished", publishedAtUtc: publication.publishedAtUtc, lastRescannedAtUtc: new Date().toISOString() }
      : publication));
  }, [creatorId]);

  const rescanPublication = useCallback((publicationId: string) => {
    let nextPublication: ExpensePlanPublication | null = null;
    setPublications((current) => current.map((publication) => {
      if (publication.id !== publicationId || publication.creatorId !== creatorId) {
        return publication;
      }

      const updated = normalizePublicationStatus(publication);
      nextPublication = updated;
      return updated;
    }));
    return nextPublication;
  }, [creatorId]);

  const getCreatorDashboard = useCallback(() => buildExpensePlanCommunityDashboard(publications, creatorId), [creatorId, publications]);

  const value = useMemo<ExpensePlanningContextValue>(() => ({
    plans,
    publications,
    builderDraft,
    selectionLineItemId,
    createNewPlanDraft,
    startEditingPlan,
    startDuplicatePlan,
    updateBuilderDraft,
    updateBuilderLineItem,
    addBuilderLineItem,
    removeBuilderLineItem,
    setSelectionLineItemId,
    assignBuilderLineItemSubcategory,
    clearBuilderDraft,
    saveBuilderDraftAs,
    cancelScheduledPlan,
    completePlan,
    sharePlan,
    publishPlan,
    updatePublication,
    togglePublicationLike,
    usePublication,
    reportPublication,
    unpublishPublication,
    rescanPublication,
    getPlanById,
    getPublicationById,
    getCreatorDashboard
  }), [
    plans,
    publications,
    builderDraft,
    selectionLineItemId,
    createNewPlanDraft,
    startEditingPlan,
    startDuplicatePlan,
    updateBuilderDraft,
    updateBuilderLineItem,
    addBuilderLineItem,
    removeBuilderLineItem,
    assignBuilderLineItemSubcategory,
    clearBuilderDraft,
    saveBuilderDraftAs,
    cancelScheduledPlan,
    completePlan,
    sharePlan,
    publishPlan,
    updatePublication,
    togglePublicationLike,
    usePublication,
    reportPublication,
    unpublishPublication,
    rescanPublication,
    getPlanById,
    getPublicationById,
    getCreatorDashboard
  ]);

  return <ExpensePlanningContext.Provider value={value}>{children}</ExpensePlanningContext.Provider>;
}

export function useExpensePlanning() {
  const context = useContext(ExpensePlanningContext);
  if (!context) {
    throw new Error("useExpensePlanning must be used within the Planning Hub provider.");
  }

  return context;
}
