import { View } from "react-native";
import { AuthProvider, useAuthSession } from "./AuthProvider";
import { PlannerProvider } from "./PlannerProvider";
import { QueryProvider } from "./QueryProvider";

type AppProvidersProps = {
  children: React.ReactNode;
};

function InteractionCapture({ children }: AppProvidersProps) {
  const { notifyUserInteraction } = useAuthSession();

  return (
    <View
      style={{ flex: 1 }}
      onStartShouldSetResponderCapture={() => {
        notifyUserInteraction();
        return false;
      }}
      onMoveShouldSetResponderCapture={() => {
        notifyUserInteraction();
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
        <PlannerProvider>
          <InteractionCapture>{children}</InteractionCapture>
        </PlannerProvider>
      </AuthProvider>
    </QueryProvider>
  );
}
