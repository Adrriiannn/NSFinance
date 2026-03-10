import { PolicyDocumentScreen } from "../../src/components/legal/PolicyDocumentScreen";
import { useDataRightsPolicyQuery } from "../../src/features/policies/usePolicies";

export default function DataRightsScreen() {
  const query = useDataRightsPolicyQuery();

  return (
    <PolicyDocumentScreen
      title="Data Rights / GDPR Summary"
      policy={query.data}
      isLoading={query.isLoading}
      errorMessage={query.error?.message}
      onRetry={() => {
        void query.refetch();
      }}
    />
  );
}