import { PolicyDocumentScreen } from "../../src/components/legal/PolicyDocumentScreen";
import { useAiDisclosurePolicyQuery } from "../../src/features/policies/usePolicies";

export default function AiDisclosureScreen() {
  const query = useAiDisclosurePolicyQuery();

  return (
    <PolicyDocumentScreen
      title="AI Disclosure"
      policy={query.data}
      isLoading={query.isLoading}
      errorMessage={query.error?.message}
      onRetry={() => {
        void query.refetch();
      }}
    />
  );
}