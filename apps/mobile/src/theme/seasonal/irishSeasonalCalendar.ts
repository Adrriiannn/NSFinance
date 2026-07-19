// Irish seasonal calendar for Automatic theme rotation (THEME-002).
//
// Seasons follow the Irish traditional calendar: Spring begins on 1 February
// (Imbolc), Summer on 1 May (Bealtaine), Autumn on 1 August (Lunasa), and
// Winter on 1 November (Samhain). Commemorative windows take precedence over
// the underlying season. Dates are evaluated against the device-local calendar
// day, which governs appearance only - finance and auth logic never read this.

export type SeasonalThemeId =
  | "spring"
  | "summer"
  | "autumn"
  | "winter"
  | "stPatricks"
  | "easter"
  | "halloween"
  | "christmas";

export type LocalCalendarDate = {
  year: number;
  /** 1-12 */
  month: number;
  /** 1-31 */
  day: number;
};

export function toLocalCalendarDate(date: Date): LocalCalendarDate {
  return {
    year: date.getFullYear(),
    month: date.getMonth() + 1,
    day: date.getDate()
  };
}

// Anonymous Gregorian computus (Meeus/Jones/Butcher). Returns Easter Sunday.
export function calculateEasterSunday(year: number): LocalCalendarDate {
  const a = year % 19;
  const b = Math.floor(year / 100);
  const c = year % 100;
  const d = Math.floor(b / 4);
  const e = b % 4;
  const f = Math.floor((b + 8) / 25);
  const g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30;
  const i = Math.floor(c / 4);
  const k = c % 4;
  const l = (32 + 2 * e + 2 * i - h - k) % 7;
  const m = Math.floor((a + 11 * h + 22 * l) / 451);
  const month = Math.floor((h + l - 7 * m + 114) / 31);
  const day = ((h + l - 7 * m + 114) % 31) + 1;

  return { year, month, day };
}

function toOrdinal(date: LocalCalendarDate): number {
  // Days since an arbitrary epoch, valid for comparisons within +-centuries.
  return Date.UTC(date.year, date.month - 1, date.day) / 86_400_000;
}

function addDays(date: LocalCalendarDate, days: number): LocalCalendarDate {
  const shifted = new Date(Date.UTC(date.year, date.month - 1, date.day + days));
  return {
    year: shifted.getUTCFullYear(),
    month: shifted.getUTCMonth() + 1,
    day: shifted.getUTCDate()
  };
}

function isWithin(
  date: LocalCalendarDate,
  start: LocalCalendarDate,
  endInclusive: LocalCalendarDate
): boolean {
  const value = toOrdinal(date);
  return value >= toOrdinal(start) && value <= toOrdinal(endInclusive);
}

type CommemorativeWindow = {
  themeId: SeasonalThemeId;
  start: LocalCalendarDate;
  endInclusive: LocalCalendarDate;
};

// Windows are deliberate product choices, versioned here and covered by tests:
// - St Patrick's: 15-17 March, the festival run-up through the day itself.
// - Easter: Good Friday through Easter Monday, derived from the computus.
// - Halloween: 24-31 October, the Samhain week.
// - Christmas: 8 December (traditional Irish start) through 6 January
//   (Nollaig na mBan), spanning the year boundary.
export function getCommemorativeWindows(year: number): CommemorativeWindow[] {
  const easterSunday = calculateEasterSunday(year);

  return [
    {
      themeId: "stPatricks",
      start: { year, month: 3, day: 15 },
      endInclusive: { year, month: 3, day: 17 }
    },
    {
      themeId: "easter",
      start: addDays(easterSunday, -2),
      endInclusive: addDays(easterSunday, 1)
    },
    {
      themeId: "halloween",
      start: { year, month: 10, day: 24 },
      endInclusive: { year, month: 10, day: 31 }
    },
    {
      themeId: "christmas",
      start: { year, month: 12, day: 8 },
      endInclusive: { year: year + 1, month: 1, day: 6 }
    }
  ];
}

export function resolveSeason(date: LocalCalendarDate): SeasonalThemeId {
  if (date.month >= 2 && date.month <= 4) {
    return "spring";
  }

  if (date.month >= 5 && date.month <= 7) {
    return "summer";
  }

  if (date.month >= 8 && date.month <= 10) {
    return "autumn";
  }

  return "winter";
}

// Commemoratives win over seasons; where two commemorative windows could ever
// overlap, the later-starting window wins (it is the more specific occasion).
export function resolveSeasonalThemeId(date: LocalCalendarDate): SeasonalThemeId {
  const windows = [
    // The Christmas window that started in the PREVIOUS year can still cover
    // early January of this year.
    ...getCommemorativeWindows(date.year - 1),
    ...getCommemorativeWindows(date.year)
  ];

  let match: CommemorativeWindow | null = null;
  for (const window of windows) {
    if (!isWithin(date, window.start, window.endInclusive)) {
      continue;
    }

    if (!match || toOrdinal(window.start) >= toOrdinal(match.start)) {
      match = window;
    }
  }

  if (match) {
    return match.themeId;
  }

  return resolveSeason(date);
}
