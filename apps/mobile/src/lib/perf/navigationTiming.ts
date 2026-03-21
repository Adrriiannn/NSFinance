type NavigationProbe = {
  id: string;
  source: string;
  target: string;
  startedAtMs: number;
};

const MAX_PENDING_PROBES = 32;
const PERF_PROBE_SLOW_NAV_MS = 180;
const pendingNavigationProbes: NavigationProbe[] = [];

function perfProbeEnabled() {
  return process.env.EXPO_PUBLIC_PERF_PROBES === "1";
}

function perfProbeVerboseEnabled() {
  return process.env.EXPO_PUBLIC_PERF_PROBES_VERBOSE === "1";
}

function toTargetLabel(href: unknown) {
  return typeof href === "string" ? href : String(href);
}

export function startNavigationProbe(target: string, source: string) {
  if (!perfProbeEnabled()) {
    return;
  }

  const probe: NavigationProbe = {
    id: `nav-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
    source,
    target,
    startedAtMs: Date.now()
  };

  pendingNavigationProbes.push(probe);
  if (pendingNavigationProbes.length > MAX_PENDING_PROBES) {
    pendingNavigationProbes.shift();
  }
}

export function completeLatestNavigationProbe(actualPath: string) {
  if (!perfProbeEnabled()) {
    return;
  }

  const probe = pendingNavigationProbes.pop();
  if (!probe) {
    return;
  }

  const elapsedMs = Date.now() - probe.startedAtMs;
  if (!perfProbeVerboseEnabled() && elapsedMs < PERF_PROBE_SLOW_NAV_MS) {
    return;
  }

  console.info("[Perf Probe]", {
    type: "navigation_end",
    id: probe.id,
    source: probe.source,
    target: probe.target,
    actualPath,
    elapsedMs,
    timestampUtc: new Date().toISOString()
  });
}

type RouterLike = {
  push: (href: string) => void;
  replace: (href: string) => void;
  navigate?: (href: string) => void;
};

export function navigateWithProbe(
  router: RouterLike,
  href: string,
  source: string,
  mode: "navigate" | "push" | "replace" = "navigate"
) {
  startNavigationProbe(toTargetLabel(href), source);

  if (mode === "push") {
    router.push(href);
    return;
  }

  if (mode === "replace") {
    router.replace(href);
    return;
  }

  if (typeof router.navigate === "function") {
    router.navigate(href);
    return;
  }

  router.replace(href);
}
