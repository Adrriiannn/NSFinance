import { useMemo } from "react";
import { useConnectedBanksQuery } from "./useBanking";
import type { BankConnectionStatus } from "../../types/api";

const nonLinkedConnectionStatuses = new Set<BankConnectionStatus>([
  "disconnect_pending",
  "disconnect_failed",
  "revoked",
  "failed"
]);

function shouldCountAsLinkedConnection(status: BankConnectionStatus) {
  return !nonLinkedConnectionStatuses.has(status);
}

function getLinkedBankCount(data: ReturnType<typeof useConnectedBanksQuery>["data"]) {
  if (!data) {
    return 0;
  }

  const activeLinkedCount = data.activeConnections.filter((connection) =>
    shouldCountAsLinkedConnection(connection.status)
  ).length;
  const attentionLinkedCount = data.attentionConnections.filter((connection) =>
    shouldCountAsLinkedConnection(connection.status)
  ).length;

  return activeLinkedCount + attentionLinkedCount;
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
