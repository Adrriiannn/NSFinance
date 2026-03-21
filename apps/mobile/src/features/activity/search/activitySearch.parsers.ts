import {
  ACTIVITY_SEARCH_MAX_DATE_SUGGESTIONS
} from "./activitySearch.constants";
import type {
  ActivityDateParseResult,
  ActivityDateSuggestion,
  ActivityDateSuggestionResult
} from "./activitySearch.types";

const MONTH_MAP: Record<string, number> = {
  january: 0,
  jan: 0,
  february: 1,
  feb: 1,
  march: 2,
  mar: 2,
  april: 3,
  apr: 3,
  may: 4,
  june: 5,
  jun: 5,
  july: 6,
  jul: 6,
  august: 7,
  aug: 7,
  september: 8,
  sept: 8,
  sep: 8,
  october: 9,
  oct: 9,
  november: 10,
  nov: 10,
  december: 11,
  dec: 11
};

const WEEKDAY_NAMES = [
  "Sunday",
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday"
] as const;

function toStartOfDay(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function addDays(date: Date, days: number) {
  const value = new Date(date);
  value.setDate(value.getDate() + days);
  return value;
}

function toStartOfIsoWeek(date: Date) {
  const day = date.getDay();
  const offsetToMonday = day === 0 ? -6 : 1 - day;
  return addDays(toStartOfDay(date), offsetToMonday);
}

function ordinalSuffix(day: number) {
  const remainder = day % 10;
  const teen = day % 100;

  if (teen >= 11 && teen <= 13) {
    return "th";
  }

  if (remainder === 1) {
    return "st";
  }
  if (remainder === 2) {
    return "nd";
  }
  if (remainder === 3) {
    return "rd";
  }
  return "th";
}

function buildValidatedDate(year: number, month: number, day: number) {
  const date = new Date(year, month, day);
  if (date.getFullYear() !== year || date.getMonth() !== month || date.getDate() !== day) {
    return null;
  }

  return date;
}

function normalizeYear(rawYear: string) {
  const year = Number(rawYear);
  if (!Number.isFinite(year)) {
    return null;
  }

  if (rawYear.length === 2) {
    return year >= 70 ? 1900 + year : 2000 + year;
  }

  return year;
}

export function formatActivityDateDisplay(date: Date) {
  const day = date.getDate();
  const month = new Intl.DateTimeFormat("en-GB", { month: "long" }).format(date);
  return `${day}${ordinalSuffix(day)} of ${month}`;
}

export function toActivityIsoDate(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function buildCandidateDate(month: number, day: number, now: Date) {
  const currentYearCandidate = new Date(now.getFullYear(), month, day);
  if (currentYearCandidate.getMonth() !== month || currentYearCandidate.getDate() !== day) {
    return null;
  }

  const tomorrow = addDays(toStartOfDay(now), 1).getTime();
  if (currentYearCandidate.getTime() <= tomorrow) {
    return currentYearCandidate;
  }

  const previousYearCandidate = new Date(now.getFullYear() - 1, month, day);
  return previousYearCandidate;
}

function parseExplicitDateInput(input: string, now: Date): Date | null {
  const trimmed = input.trim().toLowerCase();
  if (!trimmed) {
    return null;
  }

  const dayMonthNumeric = trimmed.match(/^(\d{1,2})[.\-/](\d{1,2})(?:[.\-/](\d{2,4}))?$/);
  if (dayMonthNumeric) {
    const day = Number(dayMonthNumeric[1]);
    const month = Number(dayMonthNumeric[2]) - 1;
    const explicitYear = dayMonthNumeric[3] ? normalizeYear(dayMonthNumeric[3]) : null;
    if (explicitYear !== null) {
      return buildValidatedDate(explicitYear, month, day);
    }
    return buildCandidateDate(month, day, now);
  }

  const dayMonthText = trimmed.match(/^(\d{1,2})\s+([a-z]+)(?:\s+(\d{2,4}))?$/);
  if (dayMonthText) {
    const day = Number(dayMonthText[1]);
    const month = MONTH_MAP[dayMonthText[2]];
    if (month !== undefined) {
      const explicitYear = dayMonthText[3] ? normalizeYear(dayMonthText[3]) : null;
      if (explicitYear !== null) {
        return buildValidatedDate(explicitYear, month, day);
      }
      return buildCandidateDate(month, day, now);
    }
  }

  const monthDayText = trimmed.match(/^([a-z]+)\s+(\d{1,2})(?:\s+(\d{2,4}))?$/);
  if (monthDayText) {
    const month = MONTH_MAP[monthDayText[1]];
    const day = Number(monthDayText[2]);
    if (month !== undefined) {
      const explicitYear = monthDayText[3] ? normalizeYear(monthDayText[3]) : null;
      if (explicitYear !== null) {
        return buildValidatedDate(explicitYear, month, day);
      }
      return buildCandidateDate(month, day, now);
    }
  }

  return null;
}

function resolveWeekdayFromInput(input: string) {
  const normalized = input.trim().toLowerCase();
  if (!normalized) {
    return null;
  }

  const exactMatch = WEEKDAY_NAMES.findIndex((weekday) => weekday.toLowerCase() === normalized);
  if (exactMatch >= 0) {
    return exactMatch;
  }

  if (normalized.length < 2) {
    return null;
  }

  const partialMatches = WEEKDAY_NAMES
    .map((name, index) => ({ index, name: name.toLowerCase() }))
    .filter((item) => item.name.startsWith(normalized));

  if (partialMatches.length === 1) {
    return partialMatches[0].index;
  }

  return null;
}

function buildWeekdaySuggestions(weekday: number, now: Date): ActivityDateSuggestion[] {
  const nowDay = toStartOfDay(now);
  const todayWeekday = nowDay.getDay();
  const mondayIndexedToday = todayWeekday === 0 ? 7 : todayWeekday;
  const mondayIndexedTarget = weekday === 0 ? 7 : weekday;
  const hasOccurredThisWeek = mondayIndexedToday >= mondayIndexedTarget;
  const exactMatchCount = hasOccurredThisWeek ? 4 : 3;
  const suggestions: ActivityDateSuggestion[] = [];
  const currentWeekStart = toStartOfIsoWeek(nowDay);

  let cursor = new Date(nowDay);
  while (suggestions.length < exactMatchCount) {
    if (cursor.getDay() === weekday) {
      const cursorWeekStart = toStartOfIsoWeek(cursor);
      const weeksAgo = Math.max(
        0,
        Math.round((currentWeekStart.getTime() - cursorWeekStart.getTime()) / (7 * 24 * 60 * 60 * 1000))
      );
      const relativeHint = describeRelativeWeekDistance(weeksAgo);

      suggestions.push({
        id: `exact-${toActivityIsoDate(cursor)}`,
        mode: "exact",
        isoDate: toActivityIsoDate(cursor),
        label: formatActivityDateDisplay(cursor),
        hintLabel: `${WEEKDAY_NAMES[weekday]}, ${relativeHint}`
      });
    }
    cursor = addDays(cursor, -1);
  }

  suggestions.push({
    id: `weekday-${weekday}`,
    mode: "weekday",
    weekday,
    label: WEEKDAY_NAMES[weekday],
    hintLabel: `All ${WEEKDAY_NAMES[weekday]}s`
  });

  return suggestions.slice(0, ACTIVITY_SEARCH_MAX_DATE_SUGGESTIONS);
}

function describeRelativeWeekDistance(weeksAgo: number) {
  if (weeksAgo === 0) {
    return "this week";
  }

  if (weeksAgo === 1) {
    return "a week ago";
  }

  if (weeksAgo > 1) {
    return `${weeksAgo} weeks ago`;
  }

  if (weeksAgo === -1) {
    return "in a week";
  }

  return `in ${Math.abs(weeksAgo)} weeks`;
}

function buildRelativeWeekHintForDate(date: Date, now: Date) {
  const currentWeekStart = toStartOfIsoWeek(toStartOfDay(now));
  const targetWeekStart = toStartOfIsoWeek(toStartOfDay(date));
  const weeksDelta = Math.round(
    (currentWeekStart.getTime() - targetWeekStart.getTime()) / (7 * 24 * 60 * 60 * 1000)
  );
  const weekday = WEEKDAY_NAMES[date.getDay()] ?? "Weekday";
  return `${weekday}, ${describeRelativeWeekDistance(weeksDelta)}`;
}

function buildWeekAgoSuggestions(now: Date): ActivityDateSuggestion[] {
  const weekAgo = addDays(toStartOfDay(now), -7);
  const offsets = [-2, -1, 0, 1, 2];
  return offsets.map((offset) => {
    const date = addDays(weekAgo, offset);
    return {
      id: `exact-${toActivityIsoDate(date)}`,
      mode: "exact",
      isoDate: toActivityIsoDate(date),
      label: formatActivityDateDisplay(date)
    };
  });
}

function buildYesterdaySuggestion(now: Date): ActivityDateSuggestion[] {
  const yesterday = addDays(toStartOfDay(now), -1);
  return [
    {
      id: `exact-${toActivityIsoDate(yesterday)}`,
      mode: "exact",
      isoDate: toActivityIsoDate(yesterday),
      label: formatActivityDateDisplay(yesterday),
      hintLabel: "Yesterday"
    }
  ];
}

function matchesYesterdayInput(normalized: string) {
  if (!normalized) {
    return false;
  }

  if (normalized === "yesterday") {
    return true;
  }

  return normalized.length >= 3 && "yesterday".startsWith(normalized);
}

export function parseActivityDateInput(
  rawInput: string,
  now = new Date()
): ActivityDateParseResult {
  const normalized = rawInput.trim().toLowerCase();
  if (!normalized) {
    return { kind: "none" };
  }

  const explicitDate = parseExplicitDateInput(normalized, now);
  if (explicitDate) {
    return {
      kind: "exact",
      date: explicitDate,
      displayLabel: formatActivityDateDisplay(explicitDate)
    };
  }

  const weekday = resolveWeekdayFromInput(normalized);
  if (weekday !== null) {
    return {
      kind: "weekday",
      weekday,
      displayLabel: WEEKDAY_NAMES[weekday]
    };
  }

  return { kind: "none" };
}

export function buildActivityDateSuggestions(
  rawInput: string,
  now = new Date()
): ActivityDateSuggestionResult {
  const normalized = rawInput.trim().toLowerCase();
  if (!normalized) {
    return {
      parseResult: { kind: "none" },
      suggestions: []
    };
  }

  if (matchesYesterdayInput(normalized)) {
    return {
      parseResult: { kind: "none" },
      suggestions: buildYesterdaySuggestion(now)
    };
  }

  if (normalized === "a week ago") {
    return {
      parseResult: { kind: "none" },
      suggestions: buildWeekAgoSuggestions(now)
    };
  }

  const parseResult = parseActivityDateInput(rawInput, now);
  if (parseResult.kind === "weekday") {
    return {
      parseResult,
      suggestions: buildWeekdaySuggestions(parseResult.weekday, now)
    };
  }

  if (parseResult.kind === "exact") {
    return {
      parseResult,
      suggestions: [
        {
          id: `exact-${toActivityIsoDate(parseResult.date)}`,
          mode: "exact",
          isoDate: toActivityIsoDate(parseResult.date),
          label: parseResult.displayLabel,
          hintLabel: buildRelativeWeekHintForDate(parseResult.date, now)
        }
      ]
    };
  }

  return {
    parseResult,
    suggestions: []
  };
}

export function getWeekdayLabel(weekday: number) {
  return WEEKDAY_NAMES[weekday] ?? "Weekday";
}
