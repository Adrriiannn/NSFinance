import { PolicyDocumentScreen } from "../../src/components/legal/PolicyDocumentScreen";
import { usePrivacyPolicyQuery } from "../../src/features/policies/usePolicies";

export default function PrivacyScreen() {
  const query = usePrivacyPolicyQuery();

  return (
    <PolicyDocumentScreen
      title="Privacy Policy"
      policy={query.data}
      isLoading={query.isLoading}
      errorMessage={query.error?.message}
      onRetry={() => {
        void query.refetch();
      }}
    />
  );
}