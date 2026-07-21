import assert from "node:assert/strict";
import test from "node:test";
import { buildCategoryBreakdownBlock } from "./categoryBreakdownPresentation";
import type { InsightCategoryBreakdownDto } from "../../types/api";

function makeBreakdown(): InsightCategoryBreakdownDto {
  return {
    asOfUtc: "2026-07-21T10:00:00Z",
    monthsRequested: 2,
    currencyGroups: [
      {
        currency: "EUR",
        periods: [
          {
            year: 2026,
            month: 6,
            totalSpend: 100,
            categorizedSpend: 100,
            uncategorizedSpend: 0,
            uncategorizedTransactionCount: 0,
            isPartial: false,
            categories: [
              {
                taxonomyDomainId: 130,
                domainName: "Food & Dining",
                taxonomyCategoryId: 13010,
                categoryName: "Groceries",
                spend: 100,
                transactionCount: 4
              }
            ]
          },
          {
            year: 2026,
            month: 7,
            totalSpend: 200,
            categorizedSpend: 150,
            uncategorizedSpend: 50,
            uncategorizedTransactionCount: 3,
            isPartial: true,
            categories: [
              {
                taxonomyDomainId: 130,
                domainName: "Food & Dining",
                taxonomyCategoryId: 13010,
                categoryName: "Groceries",
                spend: 90,
                transactionCount: 5
              },
              {
                taxonomyDomainId: 230,
                domainName: "Shopping",
                taxonomyCategoryId: 23030,
                categoryName: "Electronics",
                spend: 40,
                transactionCount: 1
              },
              {
                taxonomyDomainId: 210,
                domainName: "Entertainment",
                taxonomyCategoryId: 21020,
                categoryName: "Gaming",
                spend: 10,
                transactionCount: 2
              },
              {
                taxonomyDomainId: 120,
                domainName: "Transport",
                taxonomyCategoryId: 12020,
                categoryName: "Fuel & Charging",
                spend: 5,
                transactionCount: 1
              },
              {
                taxonomyDomainId: 140,
                domainName: "Utilities",
                taxonomyCategoryId: 14010,
                categoryName: "Electricity",
                spend: 3,
                transactionCount: 1
              },
              {
                taxonomyDomainId: 190,
                domainName: "Personal Care",
                taxonomyCategoryId: 19010,
                categoryName: "Grooming & Beauty",
                spend: 2,
                transactionCount: 1
              }
            ]
          }
        ]
      }
    ]
  };
}

test("selects the requested month and shapes proportional bars", () => {
  const block = buildCategoryBreakdownBlock(makeBreakdown(), "EUR", 2026, 7);
  assert.ok(block);
  assert.equal(block.monthLabel, "July");
  assert.equal(block.bars.length, 5);
  assert.equal(block.bars[0].label, "Groceries");
  assert.ok(Math.abs(block.bars[0].share - 0.45) < 0.0001);
  assert.equal(block.remainingCategoryCount, 1);
  assert.ok(block.remainingSpendText);
  assert.equal(block.isPartial, true);
});

test("reports the uncategorized remainder and coverage honestly", () => {
  const block = buildCategoryBreakdownBlock(makeBreakdown(), "EUR", 2026, 7);
  assert.ok(block?.uncategorized);
  assert.equal(block.uncategorized.count, 3);
  assert.ok(Math.abs(block.uncategorized.share - 0.25) < 0.0001);
  assert.equal(block.coveragePercent, 75);
});

test("fully categorized months omit the uncategorized line", () => {
  const block = buildCategoryBreakdownBlock(makeBreakdown(), "EUR", 2026, 6);
  assert.ok(block);
  assert.equal(block.uncategorized, null);
  assert.equal(block.coveragePercent, 100);
});

test("returns null when the month is absent or spend-free", () => {
  assert.equal(buildCategoryBreakdownBlock(makeBreakdown(), "EUR", 2026, 1), null);
  assert.equal(buildCategoryBreakdownBlock(undefined, "EUR", 2026, 7), null);
});

test("falls back to the first currency group when the display currency is missing", () => {
  const block = buildCategoryBreakdownBlock(makeBreakdown(), "USD", 2026, 7);
  assert.ok(block);
  assert.match(block.totalText, /€/);
});
