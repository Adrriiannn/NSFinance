import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { resolveAccountDetailsSectionState } from "./accountDetailsLoadState";

const accountDetailsPath = resolve(
  dirname(fileURLToPath(import.meta.url)),
  "../../../app/(tabs)/accounts/[id].tsx"
);
const importStatementPath = resolve(
  dirname(fileURLToPath(import.meta.url)),
  "../../../app/(tabs)/accounts/import-statement.tsx"
);
const buttonPath = resolve(
  dirname(fileURLToPath(import.meta.url)),
  "../../components/ui/buttons/Button.tsx"
);

test("section state distinguishes first load, blocking failure, cached failure, and ready data", () => {
  assert.equal(
    resolveAccountDetailsSectionState([{ hasData: false, isLoading: true, hasError: false }]),
    "loading"
  );
  assert.equal(
    resolveAccountDetailsSectionState([{ hasData: false, isLoading: false, hasError: true }]),
    "error"
  );
  assert.equal(
    resolveAccountDetailsSectionState([{ hasData: true, isLoading: false, hasError: true }]),
    "stale"
  );
  assert.equal(
    resolveAccountDetailsSectionState([
      { hasData: true, isLoading: false, hasError: true, hasTerminalError: true }
    ]),
    "error"
  );
  assert.equal(
    resolveAccountDetailsSectionState([{ hasData: true, isLoading: false, hasError: false }]),
    "ready"
  );
});

test("combined sections remain stale with complete cached data and fail without it", () => {
  assert.equal(
    resolveAccountDetailsSectionState([
      { hasData: true, isLoading: false, hasError: true },
      { hasData: true, isLoading: false, hasError: false }
    ]),
    "stale"
  );
  assert.equal(
    resolveAccountDetailsSectionState([
      { hasData: true, isLoading: false, hasError: false },
      { hasData: false, isLoading: false, hasError: true }
    ]),
    "error"
  );
  assert.equal(
    resolveAccountDetailsSectionState([
      { hasData: true, isLoading: false, hasError: true },
      { hasData: false, isLoading: true, hasError: false }
    ]),
    "loading"
  );
  assert.equal(
    resolveAccountDetailsSectionState([
      { hasData: false, isLoading: false, hasError: true },
      { hasData: false, isLoading: true, hasError: false }
    ]),
    "error"
  );
});

test("account details keeps secondary contracts independently recoverable", () => {
  const source = readFileSync(accountDetailsPath, "utf8");

  assert.doesNotMatch(
    source,
    /accountQuery\.error\s*\?\?\s*transactionsQuery\.error/,
    "A secondary transaction failure must not replace the complete account screen."
  );
  assert.match(source, /const connectionDetailsLoadState = resolveAccountDetailsSectionState/);
  assert.match(source, /const linkedCardsLoadState = resolveAccountDetailsSectionState/);
  assert.match(source, /const transactionsLoadState = resolveAccountDetailsSectionState/);
  assert.doesNotMatch(
    source,
    /connectionsQuery\.error\s*\?\?\s*linkedAccountsQuery\.error\s*\?\?\s*linkedCardsQuery\.error/
  );
  assert.match(source, /Some bank details are unavailable/);
  assert.match(source, /Retry bank details/);
  assert.match(source, /Linked cards are unavailable/);
  assert.match(source, /Recent activity may be out of date/);
  assert.match(source, /Recent activity is unavailable/);
  assert.match(source, /Retry recent activity/);
  assert.match(source, /isLoading=\{transactionsQuery\.isFetching\}/);
  assert.match(source, /accessibilityLiveRegion="polite"/);
  assert.match(source, /label="Import statement"/);
});

test("canonical Button exposes loading and disabled state to assistive technology", () => {
  const source = readFileSync(buttonPath, "utf8");

  assert.match(source, /accessibilityState=\{\{/);
  assert.match(source, /busy: isLoading \|\| accessibilityState\?\.busy/);
  assert.match(source, /disabled: isDisabled \|\| accessibilityState\?\.disabled/);
});

test("account routes share provenance resolution and never render full account identifiers", () => {
  const accountDetailsSource = readFileSync(accountDetailsPath, "utf8");
  const importStatementSource = readFileSync(importStatementPath, "utf8");

  assert.match(accountDetailsSource, /resolveAccountSource\(accountQuery\.data\)/);
  assert.match(importStatementSource, /isProviderProjectedAccount\(account\)/);
  assert.match(accountDetailsSource, /maskAccountIdentifier\(iban\)/);
  assert.match(accountDetailsSource, /maskAccountIdentifier\(number\)/);
  assert.doesNotMatch(accountDetailsSource, /function formatIban/);
});
