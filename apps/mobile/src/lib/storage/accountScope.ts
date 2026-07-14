export function normalizeAccountStorageScope(userId: string): string {
  const normalized = userId.trim().toLowerCase();
  if (!normalized) {
    throw new Error("An authenticated user ID is required for account storage.");
  }

  return Array.from(normalized)
    .map((character) => character.codePointAt(0)?.toString(16).padStart(6, "0") ?? "000000")
    .join("");
}

export function buildAccountStorageKey(namespace: string, userId: string): string {
  const normalizedNamespace = namespace.trim();
  if (!/^[a-zA-Z0-9._-]+$/.test(normalizedNamespace)) {
    throw new Error("Account storage namespaces may contain only letters, numbers, dots, dashes, and underscores.");
  }

  return `${normalizedNamespace}.${normalizeAccountStorageScope(userId)}`;
}

export function isSameAccountStorageScope(
  leftUserId?: string | null,
  rightUserId?: string | null
): boolean {
  if (!leftUserId || !rightUserId) {
    return false;
  }

  return normalizeAccountStorageScope(leftUserId) === normalizeAccountStorageScope(rightUserId);
}
