import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import * as SecureStore from "expo-secure-store";
import {
  readJsonFileStorage,
  writeJsonFileStorage
} from "../lib/storage/jsonFileStore";

export type PlannerCategory = string;
export type CategoryDirection = "Expense" | "Income";

export const defaultExpenseCategories: PlannerCategory[] = [
  "Groceries",
  "Transport",
  "Dining",
  "Utilities",
  "Rent",
  "Mortgage",
  "Shopping",
  "Health",
  "Entertainment",
  "Subscription",
  "Insurance",
  "Education",
  "Gifts",
  "Travel",
  "Other"
];

export const defaultIncomeCategories: PlannerCategory[] = [
  "Salary",
  "Bonus",
  "Refund",
  "Gift",
  "Transfer In",
  "Freelance",
  "Benefit",
  "Other"
];

export const plannerCategories = Array.from(
  new Set([...defaultExpenseCategories, ...defaultIncomeCategories])
);

export type CategoryCatalog = Record<CategoryDirection, PlannerCategory[]>;

const defaultCustomCategories: CategoryCatalog = {
  Expense: [],
  Income: []
};

function toTitleCase(value: string) {
  return value
    .trim()
    .replace(/\s+/g, " ")
    .split(" ")
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1).toLowerCase())
    .join(" ");
}

function dedupeCaseInsensitive(list: string[]) {
  const seen = new Set<string>();
  const ordered: string[] = [];

  list.forEach((item) => {
    const key = item.toLowerCase();
    if (seen.has(key)) {
      return;
    }

    seen.add(key);
    ordered.push(item);
  });

  return ordered;
}

function buildCategoryCatalog(custom: CategoryCatalog): CategoryCatalog {
  return {
    Expense: dedupeCaseInsensitive([...defaultExpenseCategories, ...(custom.Expense ?? [])]),
    Income: dedupeCaseInsensitive([...defaultIncomeCategories, ...(custom.Income ?? [])])
  };
}

function containsCategory(list: string[], value: string) {
  return list.some((item) => item.toLowerCase() === value.toLowerCase());
}

export type NecessityFrequency = "Weekly" | "Monthly" | "Yearly" | "OneOff";
export type NecessityType = "Essential" | "Optional";

export type NecessityItem = {
  id: string;
  name: string;
  category: PlannerCategory;
  estimatedMonthlyCost: number;
  frequency: NecessityFrequency;
  reasonNotes: string;
  merchant: string;
  type: NecessityType;
  isRecurring: boolean;
  createdUtc: string;
};

export type TransactionPlannerAnnotation = {
  transactionId: string;
  category: PlannerCategory | null;
  reason: string;
  notes: string;
  merchant: string;
  type: NecessityType | null;
  updatedUtc: string;
};

type PlannerState = {
  necessities: NecessityItem[];
  annotations: Record<string, TransactionPlannerAnnotation>;
  plannerNotes: string;
  customCategories: CategoryCatalog;
};

export type SaveAnnotationInput = {
  transactionId: string;
  category?: PlannerCategory | null;
  type?: NecessityType | null;
  reason?: string;
  notes?: string;
  merchant?: string;
  direction?: CategoryDirection;
};

type PlannerContextValue = PlannerState & {
  isReady: boolean;
  categoryCatalog: CategoryCatalog;
  resolveCategory: (direction: CategoryDirection, rawCategoryName: string) => PlannerCategory;
  addNecessity: (input: Omit<NecessityItem, "id" | "createdUtc">) => void;
  updateNecessity: (id: string, input: Omit<NecessityItem, "id" | "createdUtc">) => void;
  removeNecessity: (id: string) => void;
  saveAnnotation: (annotation: SaveAnnotationInput) => void;
  setPlannerNotes: (value: string) => void;
};

const STORAGE_KEY = "nsfinance.planner.state";
const createId = () => `${Date.now()}-${Math.random().toString(16).slice(2, 10)}`;

const defaultState: PlannerState = {
  necessities: [],
  annotations: {},
  plannerNotes: "",
  customCategories: defaultCustomCategories
};

const PlannerContext = createContext<PlannerContextValue | undefined>(undefined);

type PlannerProviderProps = {
  children: React.ReactNode;
};

export function PlannerProvider({ children }: PlannerProviderProps) {
  const [state, setState] = useState<PlannerState>(defaultState);
  const [isReady, setIsReady] = useState(false);

  useEffect(() => {
    const load = async () => {
      try {
        const stored = await readJsonFileStorage<PlannerState>(STORAGE_KEY);
        if (stored) {
          setState({
            necessities: stored.necessities ?? [],
            annotations: stored.annotations ?? {},
            plannerNotes: stored.plannerNotes ?? "",
            customCategories: {
              Expense: stored.customCategories?.Expense ?? [],
              Income: stored.customCategories?.Income ?? []
            }
          });
          setIsReady(true);
          return;
        }

        const legacyRaw = await SecureStore.getItemAsync(STORAGE_KEY);
        if (!legacyRaw) {
          setIsReady(true);
          return;
        }

        const parsed = JSON.parse(legacyRaw) as PlannerState;
        setState({
          necessities: parsed.necessities ?? [],
          annotations: parsed.annotations ?? {},
          plannerNotes: parsed.plannerNotes ?? "",
          customCategories: {
            Expense: parsed.customCategories?.Expense ?? [],
            Income: parsed.customCategories?.Income ?? []
          }
        });
        await writeJsonFileStorage(STORAGE_KEY, parsed);
        await SecureStore.deleteItemAsync(STORAGE_KEY);
      } catch {
        setState(defaultState);
      } finally {
        setIsReady(true);
      }
    };

    void load();
  }, []);

  useEffect(() => {
    if (!isReady) {
      return;
    }

    void writeJsonFileStorage(STORAGE_KEY, state);
  }, [isReady, state]);

  const categoryCatalog = useMemo(
    () => buildCategoryCatalog(state.customCategories),
    [state.customCategories]
  );

  const resolveCategory = useCallback(
    (direction: CategoryDirection, rawCategoryName: string) => {
      const normalized = toTitleCase(rawCategoryName);
      if (!normalized) {
        return "Other";
      }

      const existing = categoryCatalog[direction].find(
        (item) => item.toLowerCase() === normalized.toLowerCase()
      );

      if (existing) {
        return existing;
      }

      setState((current) => {
        const merged = buildCategoryCatalog(current.customCategories);
        if (containsCategory(merged[direction], normalized)) {
          return current;
        }

        return {
          ...current,
          customCategories: {
            ...current.customCategories,
            [direction]: dedupeCaseInsensitive([
              ...current.customCategories[direction],
              normalized
            ])
          }
        };
      });

      return normalized;
    },
    [categoryCatalog]
  );

  const addNecessity = useCallback(
    (input: Omit<NecessityItem, "id" | "createdUtc">) => {
      setState((current) => ({
        ...current,
        necessities: [
          ...current.necessities,
          {
            ...input,
            id: createId(),
            createdUtc: new Date().toISOString()
          }
        ]
      }));
    },
    []
  );

  const removeNecessity = useCallback((id: string) => {
    setState((current) => ({
      ...current,
      necessities: current.necessities.filter((item) => item.id !== id)
    }));
  }, []);

  const updateNecessity = useCallback(
    (id: string, input: Omit<NecessityItem, "id" | "createdUtc">) => {
      setState((current) => ({
        ...current,
        necessities: current.necessities.map((item) =>
          item.id === id
            ? {
                ...item,
                ...input
              }
            : item
        )
      }));
    },
    []
  );

  const saveAnnotation = useCallback((annotation: SaveAnnotationInput) => {
    setState((current) => {
      const existing = current.annotations[annotation.transactionId];
      let nextCategory =
        annotation.category !== undefined
          ? annotation.category
          : existing?.category ?? null;

      const direction = annotation.direction;
      let nextCustomCategories = current.customCategories;

      if (direction && nextCategory) {
        const normalized = toTitleCase(nextCategory);
        const merged = buildCategoryCatalog(current.customCategories);
        const existingCategory = merged[direction].find(
          (item) => item.toLowerCase() === normalized.toLowerCase()
        );
        nextCategory = existingCategory ?? normalized;

        if (!existingCategory) {
          nextCustomCategories = {
            ...current.customCategories,
            [direction]: dedupeCaseInsensitive([
              ...current.customCategories[direction],
              normalized
            ])
          };
        }
      }

      return {
        ...current,
        customCategories: nextCustomCategories,
        annotations: {
          ...current.annotations,
          [annotation.transactionId]: {
            transactionId: annotation.transactionId,
            category: nextCategory,
            type:
              annotation.type !== undefined
                ? annotation.type
                : existing?.type ?? null,
            reason:
              annotation.reason !== undefined
                ? annotation.reason
                : existing?.reason ?? "",
            notes:
              annotation.notes !== undefined
                ? annotation.notes
                : existing?.notes ?? "",
            merchant:
              annotation.merchant !== undefined
                ? annotation.merchant
                : existing?.merchant ?? "",
            updatedUtc: new Date().toISOString()
          }
        }
      };
    });
  }, []);

  const setPlannerNotes = useCallback((value: string) => {
    setState((current) => ({
      ...current,
      plannerNotes: value
    }));
  }, []);

  const value = useMemo<PlannerContextValue>(
    () => ({
      isReady,
      necessities: state.necessities,
      annotations: state.annotations,
      plannerNotes: state.plannerNotes,
      customCategories: state.customCategories,
      categoryCatalog,
      resolveCategory,
      addNecessity,
      updateNecessity,
      removeNecessity,
      saveAnnotation,
      setPlannerNotes
    }),
    [
      addNecessity,
      categoryCatalog,
      isReady,
      removeNecessity,
      resolveCategory,
      saveAnnotation,
      setPlannerNotes,
      state,
      updateNecessity
    ]
  );

  return <PlannerContext.Provider value={value}>{children}</PlannerContext.Provider>;
}

export function usePlannerStore() {
  const context = useContext(PlannerContext);
  if (!context) {
    throw new Error("usePlannerStore must be used within PlannerProvider.");
  }

  return context;
}
