// Deterministic seeded randomness for decorative theme layers (THEME-001).
// Decorations must vary per element without flickering between renders, so
// every variation derives from a stable seed instead of Math.random().

export function mulberry32(seed: number): () => number {
  let state = seed >>> 0;

  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let mixed = state;
    mixed = Math.imul(mixed ^ (mixed >>> 15), mixed | 1);
    mixed ^= mixed + Math.imul(mixed ^ (mixed >>> 7), mixed | 61);
    return ((mixed ^ (mixed >>> 14)) >>> 0) / 4294967296;
  };
}

export function pickSeeded<T>(random: () => number, variants: readonly T[]): T {
  const index = Math.floor(random() * variants.length);
  return variants[Math.min(index, variants.length - 1)] as T;
}

export function seededInRange(random: () => number, min: number, max: number): number {
  return min + random() * (max - min);
}
