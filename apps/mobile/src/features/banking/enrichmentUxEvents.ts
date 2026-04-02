type EnrichmentTooltipListener = () => void;

const listeners = new Set<EnrichmentTooltipListener>();

export function requestEnrichmentTooltip() {
  listeners.forEach((listener) => listener());
}

export function subscribeToEnrichmentTooltip(listener: EnrichmentTooltipListener) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
