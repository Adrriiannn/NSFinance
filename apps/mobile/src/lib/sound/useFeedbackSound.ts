import { useCallback } from "react";

// Intentional no-op placeholder for future optional sound integration.
export function useFeedbackSound() {
  const playSuccess = useCallback(() => {
    // Sound hooks can be wired here without changing screen-level mutation flows.
  }, []);

  return { playSuccess };
}
