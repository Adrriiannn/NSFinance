export type ForegroundPermissionStatus = "granted" | "denied" | "undetermined";

export type ForegroundPermissionLike = {
  status: ForegroundPermissionStatus;
  canAskAgain: boolean;
};

export type NormalizedForegroundPermissionState =
  | "unknown"
  | "granted"
  | "denied_can_ask_again"
  | "denied_open_settings"
  | "unavailable";

export function normalizeForegroundPermissionState(
  permission: ForegroundPermissionLike,
  servicesEnabled: boolean
): NormalizedForegroundPermissionState {
  if (!servicesEnabled) {
    return "unavailable";
  }

  if (permission.status === "granted") {
    return "granted";
  }

  if (permission.status === "undetermined" || permission.canAskAgain) {
    return "denied_can_ask_again";
  }

  return "denied_open_settings";
}
