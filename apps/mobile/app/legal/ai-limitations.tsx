import { PolicyDocumentScreen } from "../../src/components/legal/PolicyDocumentScreen";
import { useAiLimitationsPolicyQuery } from "../../src/features/policies/usePolicies";

export default function AiLimitationsScreen() {
  const query = useAiLimitationsPolicyQuery();

  return (
    <PolicyDocumentScreen
      title="AI Limitations"
      policy={query.data}
      isLoading={query.isLoading}
      errorMessage={query.error?.message}
      onRetry={() => {
        void query.refetch();
      }}
    />
  );
}