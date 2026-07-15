import type {
  StatementCsvInspectionDto,
  StatementImportMappingRequest
} from "../../types/api";

export type StatementImportMappingDraft = {
  dateColumn: string;
  descriptionColumn: string;
  amountColumn: string;
  debitColumn: string;
  creditColumn: string;
  currencyColumn: string;
  referenceColumn: string;
  dateFormat: string;
  dateValueKind: "date" | "instant";
  amountMode: "signed" | "debit_credit";
  amountSign: "as_is" | "invert";
  locale: string;
  timeZoneId: string;
};

export type StatementImportMappingErrors = Partial<
  Record<
    | "dateColumn"
    | "descriptionColumn"
    | "amountColumn"
    | "debitColumn"
    | "creditColumn"
    | "dateFormat"
    | "locale"
    | "timeZoneId"
    | "columns",
    string
  >
>;

const headerAliases = {
  date: ["date", "bookingdate", "bookeddate", "transactiondate", "valuedate", "timestamp"],
  description: [
    "description",
    "details",
    "merchant",
    "narrative",
    "transactiondescription",
    "payee"
  ],
  amount: ["amount", "value", "transactionamount", "signedamount"],
  debit: ["debit", "moneyout", "withdrawal", "paidout", "debitamount"],
  credit: ["credit", "moneyin", "deposit", "paidin", "creditamount"],
  currency: ["currency", "currencycode", "curr"],
  reference: ["reference", "ref", "transactionreference", "transactionid"]
} as const;

function normalizeHeader(value: string): string {
  return value.normalize("NFKC").toLowerCase().replace(/[^a-z0-9]/g, "");
}

function findColumn(
  inspection: StatementCsvInspectionDto,
  aliases: readonly string[]
): string {
  const normalized = inspection.columns.map((column) => ({
    index: column.index,
    name: normalizeHeader(column.name)
  }));
  const exact = normalized.find((column) => aliases.includes(column.name));
  if (exact) {
    return String(exact.index);
  }

  const partial = normalized.find((column) =>
    aliases.some((alias) => alias.length >= 5 && column.name.includes(alias))
  );
  return partial ? String(partial.index) : "";
}

function firstSampleValue(
  inspection: StatementCsvInspectionDto,
  columnIndex: string
): string {
  const index = Number(columnIndex);
  if (!Number.isInteger(index) || index < 0) {
    return "";
  }

  for (const row of inspection.sampleRows) {
    const value = row.fields[index]?.trim();
    if (value) {
      return value;
    }
  }

  return "";
}

export function inferStatementDateFormat(
  sample: string,
  locale: string
): { format: string; valueKind: "date" | "instant" } {
  const trimmed = sample.trim();
  const timeMatch = trimmed.match(
    /(?:T|\s)(\d{1,2}):(\d{2})(?::(\d{2}))?(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})?$/
  );
  const hasTime = timeMatch !== null;

  const timeFormat = (() => {
    if (!timeMatch) {
      return "";
    }

    const separator = trimmed.includes("T") ? "'T'" : " ";
    const hour = timeMatch[1].length === 1 ? "H" : "HH";
    const seconds = timeMatch[3] ? ":ss" : "";
    const fraction = timeMatch[4] ? ".FFFFFFF" : "";
    const offset = timeMatch[5] ? "K" : "";
    return `${separator}${hour}:mm${seconds}${fraction}${offset}`;
  })();

  if (/^\d{4}-\d{2}-\d{2}/.test(trimmed)) {
    return {
      format: hasTime ? `yyyy-MM-dd${timeFormat}` : "yyyy-MM-dd",
      valueKind: hasTime ? "instant" : "date"
    };
  }

  const separator = trimmed.includes("-") ? "-" : "/";
  const monthFirst = locale.toLowerCase().startsWith("en-us");
  const datePart = monthFirst
    ? `MM${separator}dd${separator}yyyy`
    : `dd${separator}MM${separator}yyyy`;
  return {
    format: hasTime ? `${datePart}${timeFormat}` : datePart,
    valueKind: hasTime ? "instant" : "date"
  };
}

export function suggestStatementImportMapping(
  inspection: StatementCsvInspectionDto,
  locale: string,
  timeZoneId: string
): StatementImportMappingDraft {
  const dateColumn = findColumn(inspection, headerAliases.date);
  const amountColumn = findColumn(inspection, headerAliases.amount);
  const debitColumn = findColumn(inspection, headerAliases.debit);
  const creditColumn = findColumn(inspection, headerAliases.credit);
  const dateSuggestion = inferStatementDateFormat(
    firstSampleValue(inspection, dateColumn),
    locale
  );

  return {
    dateColumn,
    descriptionColumn: findColumn(inspection, headerAliases.description),
    amountColumn,
    debitColumn,
    creditColumn,
    currencyColumn: findColumn(inspection, headerAliases.currency),
    referenceColumn: findColumn(inspection, headerAliases.reference),
    dateFormat: dateSuggestion.format,
    dateValueKind: dateSuggestion.valueKind,
    amountMode: amountColumn || !(debitColumn && creditColumn) ? "signed" : "debit_credit",
    amountSign: "as_is",
    locale: locale.trim() || "en-IE",
    timeZoneId: timeZoneId.trim() || "Europe/Dublin"
  };
}

function parseRequiredColumn(value: string): number | null {
  if (!value.trim()) {
    return null;
  }

  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 0 ? parsed : null;
}

function parseOptionalColumn(value: string): number | null {
  if (!value) {
    return null;
  }

  return parseRequiredColumn(value);
}

export function validateStatementImportMapping(
  draft: StatementImportMappingDraft
): StatementImportMappingErrors {
  const errors: StatementImportMappingErrors = {};
  const dateColumn = parseRequiredColumn(draft.dateColumn);
  const descriptionColumn = parseRequiredColumn(draft.descriptionColumn);

  if (dateColumn === null) {
    errors.dateColumn = "Select the transaction date column.";
  }
  if (descriptionColumn === null) {
    errors.descriptionColumn = "Select the description column.";
  }
  if (dateColumn !== null && descriptionColumn !== null && dateColumn === descriptionColumn) {
    errors.columns = "Date and description must use different columns.";
  }

  if (draft.amountMode === "signed") {
    if (parseRequiredColumn(draft.amountColumn) === null) {
      errors.amountColumn = "Select the signed amount column.";
    }
  } else {
    const debitColumn = parseRequiredColumn(draft.debitColumn);
    const creditColumn = parseRequiredColumn(draft.creditColumn);
    if (debitColumn === null) {
      errors.debitColumn = "Select the money-out column.";
    }
    if (creditColumn === null) {
      errors.creditColumn = "Select the money-in column.";
    }
    if (debitColumn !== null && creditColumn !== null && debitColumn === creditColumn) {
      errors.columns = "Money in and money out must use different columns.";
    }
  }

  if (!draft.dateFormat.trim()) {
    errors.dateFormat = "Enter the date format used by the statement.";
  }
  if (!draft.locale.trim()) {
    errors.locale = "Locale is required for dates and decimal separators.";
  }
  if (!draft.timeZoneId.trim()) {
    errors.timeZoneId = "Time zone is required.";
  }

  return errors;
}

export function toStatementImportMappingRequest(
  accountId: string,
  delimiter: string,
  draft: StatementImportMappingDraft
): StatementImportMappingRequest {
  return {
    accountId,
    delimiter,
    dateColumn: parseRequiredColumn(draft.dateColumn)!,
    descriptionColumn: parseRequiredColumn(draft.descriptionColumn)!,
    amountColumn: draft.amountMode === "signed" ? parseOptionalColumn(draft.amountColumn) : null,
    debitColumn: draft.amountMode === "debit_credit" ? parseOptionalColumn(draft.debitColumn) : null,
    creditColumn: draft.amountMode === "debit_credit" ? parseOptionalColumn(draft.creditColumn) : null,
    currencyColumn: parseOptionalColumn(draft.currencyColumn),
    referenceColumn: parseOptionalColumn(draft.referenceColumn),
    dateFormat: draft.dateFormat.trim(),
    dateValueKind: draft.dateValueKind,
    amountMode: draft.amountMode,
    amountSign: draft.amountSign,
    locale: draft.locale.trim(),
    timeZoneId: draft.timeZoneId.trim()
  };
}

export function hasStatementImportMappingErrors(
  errors: StatementImportMappingErrors
): boolean {
  return Object.values(errors).some(Boolean);
}
