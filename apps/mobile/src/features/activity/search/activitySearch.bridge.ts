import type { ActivitySearchSnapshot } from "./activitySearch.types";

let activitySearchSnapshot: ActivitySearchSnapshot | null = null;

export function setActivitySearchSnapshot(snapshot: ActivitySearchSnapshot) {
  activitySearchSnapshot = snapshot;
}

export function consumeActivitySearchSnapshot() {
  const snapshot = activitySearchSnapshot;
  activitySearchSnapshot = null;
  return snapshot;
}

export function peekActivitySearchSnapshot() {
  return activitySearchSnapshot;
}

