// Transitional merchant display cleanup per the accepted Page Target Register:
// rows show a cleaned merchant name while the untouched statement text stays
// visible on the transaction detail. Server-side normalization arrives with
// the CAT-001 pipeline; this is deterministic display-only tidying.

// Leading scheme prefixes Irish card providers attach to statement text.
const PROVIDER_PREFIX_PATTERN = /^(?:VDC|VDP|POSC?|CNC)[-* ]\s*/i;

// Trailing pure-numeric store identifiers ("TESCO STORES 3").
const TRAILING_STORE_NUMBER_PATTERN = /\s+\d{1,4}$/;

function titleCaseWord(word: string): string {
  if (word.length <= 2) {
    return word;
  }

  const isAllCaps = word === word.toUpperCase() && word !== word.toLowerCase();
  if (!isAllCaps) {
    return word;
  }

  return word
    .split(/([.\-/])/)
    .map((segment) =>
      /[.\-/]/.test(segment) || segment.length === 0
        ? segment
        : segment.charAt(0).toUpperCase() + segment.slice(1).toLowerCase()
    )
    .join("");
}

export function formatMerchantDisplayName(rawDescription: string): string {
  const original = rawDescription?.trim() ?? "";
  if (original.length === 0) {
    return original;
  }

  let cleaned = original.replace(/^\*+/, "").trim();
  cleaned = cleaned.replace(PROVIDER_PREFIX_PATTERN, "");
  cleaned = cleaned.replace(/\s{2,}/g, " ").trim();
  cleaned = cleaned.replace(TRAILING_STORE_NUMBER_PATTERN, "");

  if (cleaned.length < 2) {
    return original;
  }

  return cleaned.split(" ").map(titleCaseWord).join(" ");
}

export function hasDistinctStatementText(rawDescription: string): boolean {
  const cleaned = formatMerchantDisplayName(rawDescription);
  return cleaned !== (rawDescription?.trim() ?? "");
}
