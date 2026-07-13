export function maskAccountEmail(email: string | null | undefined): string | null {
  const normalized = email?.trim();
  if (!normalized) {
    return null;
  }

  const separatorIndex = normalized.lastIndexOf("@");
  if (separatorIndex <= 0 || separatorIndex === normalized.length - 1) {
    return null;
  }

  const localPart = normalized.slice(0, separatorIndex);
  const domain = normalized.slice(separatorIndex + 1);
  return `${localPart.slice(0, Math.min(3, localPart.length))}****@${domain}`;
}
