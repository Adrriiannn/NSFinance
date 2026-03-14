import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import {
  buildExpenseTrackerPeriodRange,
  type ExpenseTrackerPeriodMode
} from "./expenseTrackerAnalytics";

type ExpenseTrackerPeriodContextValue = {
  mode: ExpenseTrackerPeriodMode;
  setMode: (mode: ExpenseTrackerPeriodMode) => void;
  period: ReturnType<typeof buildExpenseTrackerPeriodRange>;
};

const ExpenseTrackerPeriodContext = createContext<ExpenseTrackerPeriodContextValue | null>(null);

export function ExpenseTrackerPeriodProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<ExpenseTrackerPeriodMode>("monthly");
  const value = useMemo(
    () => ({
      mode,
      setMode,
      period: buildExpenseTrackerPeriodRange(mode)
    }),
    [mode]
  );

  return (
    <ExpenseTrackerPeriodContext.Provider value={value}>
      {children}
    </ExpenseTrackerPeriodContext.Provider>
  );
}

export function useExpenseTrackerPeriod() {
  const context = useContext(ExpenseTrackerPeriodContext);
  if (!context) {
    throw new Error("useExpenseTrackerPeriod must be used inside ExpenseTrackerPeriodProvider");
  }

  return context;
}
