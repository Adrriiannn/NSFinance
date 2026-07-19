// Classifies which routes are top-level surfaces where Android Back should
// exit the app (after secure exit preparation) instead of navigating back.
//
// Only the visible primary tabs are exit surfaces. Hidden tab groups such as
// companion are opened by in-app actions on top of a launching tab, so Back
// must return there; treating them as exit surfaces closes the task and, for
// non-remembered sessions, deliberately drops the in-memory session.

const TOP_LEVEL_EXIT_TAB_SEGMENTS = new Set(["index", "accounts", "activity", "cashflow"]);

export function isTopLevelExitRoute(segments: readonly string[]): boolean {
  if (segments[0] !== "(tabs)") {
    return false;
  }

  // Home renders as the bare "(tabs)" group index.
  if (segments.length === 1) {
    return true;
  }

  if (segments.length !== 2 || typeof segments[1] !== "string") {
    return false;
  }

  return TOP_LEVEL_EXIT_TAB_SEGMENTS.has(segments[1]);
}
