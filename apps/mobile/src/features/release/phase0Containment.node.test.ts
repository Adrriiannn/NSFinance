import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import test from "node:test";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");

function readMobileSource(...segments: string[]) {
  return readFileSync(join(mobileRoot, ...segments), "utf8");
}

test("Home contains no account or balance debug telemetry", () => {
  const source = readMobileSource("app", "(tabs)", "index.tsx");

  assert.doesNotMatch(source, /Home Banking Timeline|logHomeEvent|console\.(?:debug|info|log)/);
});

test("product analytics consent is independent from marketing consent", () => {
  const source = readMobileSource("app", "(tabs)", "accounts", "legal-privacy.tsx");

  assert.match(source, /PRODUCT_ANALYTICS_CONSENT_TYPE = "product_analytics"/);
  assert.match(source, /consentType: PRODUCT_ANALYTICS_CONSENT_TYPE/);
  assert.doesNotMatch(source, /marketing_communications/);
  assert.doesNotMatch(source, /analyticsEnabled: flags\.analyticsEnabled/);
});

test("unfinished transfer and calendar surfaces are contained by redirects", () => {
  const accountsSource = readMobileSource("app", "(tabs)", "accounts", "index.tsx");
  const transferSource = readMobileSource("app", "(tabs)", "accounts", "transfer.tsx");
  const calendarSource = readMobileSource("app", "(tabs)", "calendar", "index.tsx");
  const tabLayoutSource = readMobileSource("app", "(tabs)", "_layout.tsx");
  const bottomNavSource = readMobileSource("src", "components", "layout", "bottomNavConfigs.ts");

  assert.doesNotMatch(accountsSource, /accounts\/transfer|Transfer money/);
  assert.match(transferSource, /Redirect href="\/\(tabs\)\/accounts"/);
  assert.match(calendarSource, /Redirect href="\/\(tabs\)\/cashflow"/);
  assert.match(tabLayoutSource, /name="calendar"[\s\S]*?href: null/);
  assert.doesNotMatch(bottomNavSource, /key: "calendar"|label: "Calendar"/);
});

test("Planning routes are compatibility shims and Activity owns category selection", () => {
  const planningRoot = join(mobileRoot, "app", "(tabs)", "planning");
  const planningIndex = readFileSync(join(planningRoot, "index.tsx"), "utf8");
  const legacyCategoryRoute = readFileSync(join(planningRoot, "categories.tsx"), "utf8");
  const activityCategoryRoute = readMobileSource("app", "(tabs)", "activity", "categories.tsx");
  const activityIndex = readMobileSource("app", "(tabs)", "activity", "index.tsx");
  const premiumTabBar = readMobileSource("src", "components", "layout", "PremiumTabBar.tsx");

  assert.match(planningIndex, /Redirect href="\/\(tabs\)"/);
  assert.match(legacyCategoryRoute, /Redirect href="\/\(tabs\)"/);
  assert.match(activityCategoryRoute, /isSupportedActivitySelection/);
  assert.doesNotMatch(activityIndex, /\(tabs\)\/planning/);
  assert.doesNotMatch(premiumTabBar, /planning|PlanningHub/);
  assert.equal(
    existsSync(join(mobileRoot, "src", "features", "expenseTracker", "ExpensePlanningProvider.tsx")),
    false
  );
  assert.equal(
    existsSync(join(mobileRoot, "src", "layout", "adaptive", "planningHubPeek.hooks.ts")),
    false
  );
});

test("About contains no unpublished operator placeholders", () => {
  const source = readMobileSource("app", "(tabs)", "accounts", "about.tsx");

  assert.doesNotMatch(source, /pending publication|will be published|planning insights/i);
});
