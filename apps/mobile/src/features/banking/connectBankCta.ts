import { useMemo } from "react";
import { useConnectedBanksQuery } from "./useBanking";

function getLinkedBankCount(data: ReturnType<typeof useConnectedBanksQuery>["data"]) {
  if (!data) {
    return 0;
  }

  return data.activeConnections.length + data.attentionConnections.length;
}

export function getConnectBankLabel(linkedBankCount: number) {
  return linkedBankCount > 0 ? "Connect another bank account" : "Connect a bank account";
}

export function getCompactConnectBankLabel(linkedBankCount: number) {
  return linkedBankCount > 0 ? "Connect another bank" : "Connect a bank";
}

export function useConnectBankCtaLabels() {
  const connectedBanksQuery = useConnectedBanksQuery();

  return useMemo(() => {
    const linkedBankCount = getLinkedBankCount(connectedBanksQuery.data);
    return {
      linkedBankCount,
      primaryLabel: getConnectBankLabel(linkedBankCount),
      compactLabel: getCompactConnectBankLabel(linkedBankCount)
    };
  }, [connectedBanksQuery.data]);
}

