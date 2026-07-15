export function maskAccountIdentifier(value?: string | null) {
  const compact = value?.replace(/[^0-9A-Za-z]/g, "").toUpperCase() ?? "";
  if (!compact) {
    return null;
  }

  if (compact.length <= 4) {
    return "Hidden";
  }

  return `Ending ${compact.slice(-4)}`;
}
