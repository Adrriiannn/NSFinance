import assert from "node:assert/strict";
import test from "node:test";
import {
  calculateEasterSunday,
  getCommemorativeWindows,
  resolveSeason,
  resolveSeasonalThemeId,
  type LocalCalendarDate
} from "./irishSeasonalCalendar";

function d(year: number, month: number, day: number): LocalCalendarDate {
  return { year, month, day };
}

test("Easter computus matches the published Gregorian dates", () => {
  assert.deepEqual(calculateEasterSunday(2024), d(2024, 3, 31));
  assert.deepEqual(calculateEasterSunday(2025), d(2025, 4, 20));
  assert.deepEqual(calculateEasterSunday(2026), d(2026, 4, 5));
  assert.deepEqual(calculateEasterSunday(2027), d(2027, 3, 28));
  assert.deepEqual(calculateEasterSunday(2028), d(2028, 4, 16));
  assert.deepEqual(calculateEasterSunday(2030), d(2030, 4, 21));
  assert.deepEqual(calculateEasterSunday(2038), d(2038, 4, 25));
});

test("Irish traditional seasons begin on the cross-quarter months", () => {
  assert.equal(resolveSeason(d(2026, 2, 1)), "spring");
  assert.equal(resolveSeason(d(2026, 4, 30)), "spring");
  assert.equal(resolveSeason(d(2026, 5, 1)), "summer");
  assert.equal(resolveSeason(d(2026, 7, 31)), "summer");
  assert.equal(resolveSeason(d(2026, 8, 1)), "autumn");
  assert.equal(resolveSeason(d(2026, 10, 31)), "autumn");
  assert.equal(resolveSeason(d(2026, 11, 1)), "winter");
  assert.equal(resolveSeason(d(2027, 1, 31)), "winter");
});

test("commemorative windows take precedence over the underlying season", () => {
  // St Patrick's inside spring.
  assert.equal(resolveSeasonalThemeId(d(2026, 3, 14)), "spring");
  assert.equal(resolveSeasonalThemeId(d(2026, 3, 15)), "stPatricks");
  assert.equal(resolveSeasonalThemeId(d(2026, 3, 17)), "stPatricks");
  assert.equal(resolveSeasonalThemeId(d(2026, 3, 18)), "spring");

  // Easter 2026 is 5 April: Good Friday 3 April through Easter Monday 6 April.
  assert.equal(resolveSeasonalThemeId(d(2026, 4, 2)), "spring");
  assert.equal(resolveSeasonalThemeId(d(2026, 4, 3)), "easter");
  assert.equal(resolveSeasonalThemeId(d(2026, 4, 5)), "easter");
  assert.equal(resolveSeasonalThemeId(d(2026, 4, 6)), "easter");
  assert.equal(resolveSeasonalThemeId(d(2026, 4, 7)), "spring");

  // Halloween closes autumn.
  assert.equal(resolveSeasonalThemeId(d(2026, 10, 23)), "autumn");
  assert.equal(resolveSeasonalThemeId(d(2026, 10, 24)), "halloween");
  assert.equal(resolveSeasonalThemeId(d(2026, 10, 31)), "halloween");
  assert.equal(resolveSeasonalThemeId(d(2026, 11, 1)), "winter");
});

test("the Christmas window spans the year boundary through Nollaig na mBan", () => {
  assert.equal(resolveSeasonalThemeId(d(2026, 12, 7)), "winter");
  assert.equal(resolveSeasonalThemeId(d(2026, 12, 8)), "christmas");
  assert.equal(resolveSeasonalThemeId(d(2026, 12, 25)), "christmas");
  assert.equal(resolveSeasonalThemeId(d(2026, 12, 31)), "christmas");
  assert.equal(resolveSeasonalThemeId(d(2027, 1, 1)), "christmas");
  assert.equal(resolveSeasonalThemeId(d(2027, 1, 6)), "christmas");
  assert.equal(resolveSeasonalThemeId(d(2027, 1, 7)), "winter");
});

test("early Easters that brush St Patrick's window resolve to the later-starting occasion", () => {
  // Easter 2008 was 23 March; Good Friday 21 March - no overlap with
  // St Patrick's 15-17 window, but assert adjacency behaves.
  assert.equal(resolveSeasonalThemeId(d(2008, 3, 17)), "stPatricks");
  assert.equal(resolveSeasonalThemeId(d(2008, 3, 21)), "easter");

  // The earliest possible Easter (22 March, e.g. 2285) yields Good Friday on
  // 20 March; windows still never overlap St Patrick's, and precedence code
  // remains deterministic for any future overlapping definitions.
  assert.deepEqual(calculateEasterSunday(2285), d(2285, 3, 22));
  assert.equal(resolveSeasonalThemeId(d(2285, 3, 20)), "easter");
  assert.equal(resolveSeasonalThemeId(d(2285, 3, 17)), "stPatricks");
});

test("every commemorative window carries valid ordering", () => {
  for (const year of [2024, 2025, 2026, 2027, 2030, 2040]) {
    for (const window of getCommemorativeWindows(year)) {
      const start = Date.UTC(window.start.year, window.start.month - 1, window.start.day);
      const end = Date.UTC(
        window.endInclusive.year,
        window.endInclusive.month - 1,
        window.endInclusive.day
      );
      assert.ok(end >= start, `${window.themeId} ${year} window must not be inverted`);
    }
  }
});
