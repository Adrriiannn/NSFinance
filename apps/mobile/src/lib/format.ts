export function formatCurrency(amount: number, currency = "EUR"): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency,
    maximumFractionDigits: 2
  }).format(amount);
}

export function formatDate(isoDate: string): string {
  return new Intl.DateTimeFormat("en-IE", {
    day: "2-digit",
    month: "short",
    year: "numeric"
  }).format(new Date(isoDate));
}

export function formatLongDate(isoDate: string): string {
  return new Intl.DateTimeFormat("en-IE", {
    day: "2-digit",
    month: "long"
  }).format(new Date(isoDate));
}

// Formats a date-only value (e.g. "2026-07-17") as the calendar day it names.
// Pinned to UTC so the rendered day never shifts with the device timezone.
export function formatCalendarDateLong(isoDateOnly: string): string {
  return new Intl.DateTimeFormat("en-IE", {
    day: "2-digit",
    month: "long",
    timeZone: "UTC"
  }).format(new Date(`${isoDateOnly}T00:00:00Z`));
}

export function formatCalendarDate(isoDateOnly: string): string {
  return new Intl.DateTimeFormat("en-IE", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    timeZone: "UTC"
  }).format(new Date(`${isoDateOnly}T00:00:00Z`));
}

export function formatShortDate(isoDate: string): string {
  return new Intl.DateTimeFormat("en-IE", {
    day: "2-digit",
    month: "short"
  }).format(new Date(isoDate));
}

export function formatTime(isoDate: string): string {
  return new Intl.DateTimeFormat("en-IE", {
    hour: "2-digit",
    minute: "2-digit"
  }).format(new Date(isoDate));
}

export function formatMonthYear(date: Date): string {
  return new Intl.DateTimeFormat("en-IE", {
    month: "short",
    year: "numeric"
  }).format(date);
}

export function greetingFromTime(now = new Date()): string {
  const hour = now.getHours();
  if (hour < 12) {
    return "Good morning";
  }

  if (hour < 18) {
    return "Good afternoon";
  }

  return "Good evening";
}
