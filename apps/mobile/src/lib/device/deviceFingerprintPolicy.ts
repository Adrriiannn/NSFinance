function hashSeed(input: string) {
  let hash = 5381;
  for (let index = 0; index < input.length; index += 1) {
    hash = ((hash << 5) + hash) + input.charCodeAt(index);
    hash |= 0;
  }

  return Math.abs(hash).toString(36);
}

export function buildDeviceFingerprint({
  platform,
  platformScopedId,
  fallbackParts
}: {
  platform: string;
  platformScopedId?: string | null;
  fallbackParts: (string | null | undefined)[];
}) {
  const normalizedPlatform = platform.trim().toLowerCase() || "unknown";
  const normalizedScopedId = platformScopedId?.trim();
  if (normalizedScopedId) {
    return `${normalizedPlatform}:id:${hashSeed(normalizedScopedId)}`;
  }

  const normalizedFallback = fallbackParts
    .map((part) => part?.trim())
    .filter((part): part is string => Boolean(part));
  if (normalizedFallback.length === 0) {
    return `${normalizedPlatform}:unknown-device`;
  }

  return `${normalizedPlatform}:fallback:${hashSeed(normalizedFallback.join("|"))}`;
}
