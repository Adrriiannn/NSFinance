let isActivityFeedInteracting = false;

const listeners = new Set<(isInteracting: boolean) => void>();

export function setActivityFeedInteracting(next: boolean) {
  if (isActivityFeedInteracting === next) {
    return;
  }

  isActivityFeedInteracting = next;
  listeners.forEach((listener) => listener(next));
}

export function getActivityFeedInteracting() {
  return isActivityFeedInteracting;
}

export function subscribeToActivityFeedInteraction(listener: (isInteracting: boolean) => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
