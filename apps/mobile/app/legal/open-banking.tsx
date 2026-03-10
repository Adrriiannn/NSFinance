import { PolicyDocumentScreen } from "../../src/components/legal/PolicyDocumentScreen";
import { useOpenBankingDisclosurePolicyQuery } from "../../src/features/policies/usePolicies";

export default function OpenBankingDisclosureScreen() {
  const query = useOpenBankingDisclosurePolicyQuery();

  return (
    <PolicyDocumentScreen
      title="Open Banking Disclosure"
      policy={query.data}
      isLoading={query.isLoading}
      errorMessage={query.error?.message}
      onRetry={() => {
        void query.refetch();
      }}
    />
  );
}