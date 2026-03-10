import { PolicyDocumentScreen } from "../../src/components/legal/PolicyDocumentScreen";
import { useTermsPolicyQuery } from "../../src/features/policies/usePolicies";

export default function TermsScreen() {
  const query = useTermsPolicyQuery();

  return (
    <PolicyDocumentScreen
      title="Terms of Service"
      policy={query.data}
      isLoading={query.isLoading}
      errorMessage={query.error?.message}
      onRetry={() => {
        void query.refetch();
      }}
    />
  );
}