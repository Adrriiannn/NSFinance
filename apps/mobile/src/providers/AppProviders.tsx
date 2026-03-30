import { View } from "react-native";
import { useRef } from "react";
import { BankingAutoSyncController } from "../features/banking/BankingAutoSyncController";
import { ExpensePlanningProvider } from "../features/expenseTracker/ExpensePlanningProvider";
import { AuthProvider, useAuthSession } from "./AuthProvider";
import { PlannerProvider } from "./PlannerProvider";
import { QueryProvider } from "./QueryProvider";

type AppProvidersProps = {
  children: React.ReactNode;
};

function InteractionCapture({ children }: AppProvidersProps) {
  const { notifyUserInteraction } = useAuthSession();
  const lastInteractionNotifiedAtRef = useRef(0);

  const notifyUserInteractionThrottled = () => {
    const now = Date.now();
    if (now - lastInteractionNotifiedAtRef.current < 1_500) {
      return;
    }

    lastInteractionNotifiedAtRef.current = now;
    notifyUserInteraction();
  };

  return (
    <View
      style={{ flex: 1 }}
      onStartShouldSetResponderCapture={() => {
        notifyUserInteractionThrottled();
        return false;
      }}
      onMoveShouldSetResponderCapture={() => {
        notifyUserInteractionThrottled();
        return false;
      }}
    >
      {children}
    </View>
  );
}

export function AppProviders({ children }: AppProvidersProps) {
  return (
    <QueryProvider>
      <AuthProvider>
        <BankingAutoSyncController />
        <ExpensePlanningProvider>
          <PlannerProvider>
            <InteractionCapture>{children}</InteractionCapture>
          </PlannerProvider>
        </ExpensePlanningProvider>
      </AuthProvider>
    </QueryProvider>
  );
}
