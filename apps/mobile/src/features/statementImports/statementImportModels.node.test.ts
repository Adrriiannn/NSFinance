import assert from "node:assert/strict";
import test from "node:test";
import type { StatementCsvInspectionDto } from "../../types/api";
import {
  hasStatementImportMappingErrors,
  inferStatementDateFormat,
  suggestStatementImportMapping,
  toStatementImportMappingRequest,
  validateStatementImportMapping
} from "./statementImportModels";

function inspection(
  columns: string[],
  sample: string[]
): StatementCsvInspectionDto {
  return {
    fileName: "statement.csv",
    parserVersion: "statement-csv-v1",
    delimiter: ",",
    sourceByteCount: 128,
    dataRowCount: 1,
    columns: columns.map((name, index) => ({ index, name })),
    sampleRows: [{ rowNumber: 2, fields: sample }]
  };
}

test("mapping suggestion recognizes Irish debit and credit statement columns", () => {
  const draft = suggestStatementImportMapping(
    inspection(
      ["Booking Date", "Narrative", "Money Out", "Money In", "Currency", "Reference"],
      ["15/07/2026", "Corner shop", "12.50", "", "EUR", "abc-1"]
    ),
    "en-IE",
    "Europe/Dublin"
  );

  assert.equal(draft.dateColumn, "0");
  assert.equal(draft.descriptionColumn, "1");
  assert.equal(draft.amountMode, "debit_credit");
  assert.equal(draft.debitColumn, "2");
  assert.equal(draft.creditColumn, "3");
  assert.equal(draft.currencyColumn, "4");
  assert.equal(draft.referenceColumn, "5");
  assert.equal(draft.dateFormat, "dd/MM/yyyy");
  assert.equal(draft.dateValueKind, "date");
});

test("signed amount and instant formats are inferred without changing source signs", () => {
  const draft = suggestStatementImportMapping(
    inspection(
      ["Transaction Date", "Description", "Amount"],
      ["2026-07-15 13:42:05", "Salary", "2500.00"]
    ),
    "en-IE",
    "Europe/Dublin"
  );

  assert.equal(draft.amountMode, "signed");
  assert.equal(draft.amountColumn, "2");
  assert.equal(draft.dateFormat, "yyyy-MM-dd HH:mm:ss");
  assert.equal(draft.dateValueKind, "instant");
  assert.equal(draft.amountSign, "as_is");
});

test("ISO timestamp inference preserves UTC and fractional offset syntax", () => {
  assert.deepEqual(inferStatementDateFormat("2026-07-15T13:42:05Z", "en-IE"), {
    format: "yyyy-MM-dd'T'HH:mm:ssK",
    valueKind: "instant"
  });
  assert.deepEqual(
    inferStatementDateFormat("2026-07-15T13:42:05.123+01:00", "en-IE"),
    {
      format: "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
      valueKind: "instant"
    }
  );
});

test("US slash dates are month-first while Irish dates remain day-first", () => {
  assert.deepEqual(inferStatementDateFormat("07/15/2026", "en-US"), {
    format: "MM/dd/yyyy",
    valueKind: "date"
  });
  assert.deepEqual(inferStatementDateFormat("15/07/2026", "en-IE"), {
    format: "dd/MM/yyyy",
    valueKind: "date"
  });
});

test("mapping validation rejects missing and conflicting financial columns", () => {
  const draft = suggestStatementImportMapping(
    inspection(["Date", "Description", "Amount"], ["15/07/2026", "Shop", "-10"]),
    "en-IE",
    "Europe/Dublin"
  );
  draft.descriptionColumn = draft.dateColumn;
  draft.amountColumn = "";

  const errors = validateStatementImportMapping(draft);
  assert.equal(errors.columns, "Date and description must use different columns.");
  assert.equal(errors.amountColumn, "Select the signed amount column.");
  assert.equal(hasStatementImportMappingErrors(errors), true);
});

test("mapping request excludes irrelevant amount columns", () => {
  const draft = suggestStatementImportMapping(
    inspection(
      ["Date", "Description", "Debit", "Credit"],
      ["15/07/2026", "Shop", "10", ""]
    ),
    "en-IE",
    "Europe/Dublin"
  );

  const request = toStatementImportMappingRequest("account-1", ",", draft);
  assert.equal(request.amountMode, "debit_credit");
  assert.equal(request.amountColumn, null);
  assert.equal(request.debitColumn, 2);
  assert.equal(request.creditColumn, 3);
  assert.equal(request.currencyColumn, null);
});
