import { AppState, type AppStateStatus } from "react-native";
import { useEffect, useMemo, useRef, useState } from "react";

type GreetingBucket = "earlyMorning" | "morning" | "afternoon" | "evening" | "lateNight";

const EARLY_MORNING_GREETINGS = [
  "Good morning",
  "Morning",
  "Welcome back",
  "Ready for today?",
  "Starting early?",
  "Up already?",
  "Fresh start",
  "New day ahead"
];

const greetingPools: Record<GreetingBucket, string[]> = {
  earlyMorning: EARLY_MORNING_GREETINGS,
  // Preserve existing morning bucket segmentation while using the finalized pool.
  morning: EARLY_MORNING_GREETINGS,

  afternoon: [
    "Good afternoon",
    "Afternoon",
    "Welcome back",
    "How’s today going?",
    "Keeping pace?",
    "All on track?",
    "Still focused?"
  ],

  evening: [
    "Good evening",
    "Evening",
    "Welcome back",
    "Winding down",
    "How was today?",
    "All wrapped up?",
    "Taking it easy?",
    "Quiet evening"
  ],

  lateNight: [
    "Still online?",
    "Late hours",
    "Long day?",
    "Quiet night",
    "Still at it?",
    "Night mode",
    "Up late?"
  ]
};

export function formatGreetingForDisplay(greeting: string, name?: string | null): string {
  const normalizedGreeting = greeting.trim();
  const normalizedName = name?.trim() ?? "";

  if (!normalizedGreeting) {
    return normalizedName;
  }

  if (!normalizedName) {
    return normalizedGreeting;
  }

  const isQuestion = /\?+$/.test(normalizedGreeting);
  const greetingBase = isQuestion
    ? normalizedGreeting.replace(/\?+$/, "").trim()
    : normalizedGreeting;

  if (!greetingBase) {
    return normalizedName;
  }

  return isQuestion
    ? `${greetingBase}, ${normalizedName}?`
    : `${greetingBase}, ${normalizedName}`;
}

function getGreetingBucket(now: Date): GreetingBucket {
  const hour = now.getHours();
  if (hour >= 5 && hour < 8) {
    return "earlyMorning";
  }

  if (hour >= 8 && hour < 12) {
    return "morning";
  }

  if (hour >= 12 && hour < 17) {
    return "afternoon";
  }

  if (hour >= 17 && hour < 22) {
    return "evening";
  }

  return "lateNight";
}

function pickGreeting(
  now: Date,
  lastGreeting: string | null,
  recentGreetings: string[]
) {
  const bucket = getGreetingBucket(now);
  const choices = greetingPools[bucket];
  const filtered = choices.filter(
    (item) => item !== lastGreeting && !recentGreetings.includes(item)
  );
  const fallback = choices.filter((item) => item !== lastGreeting);
  const nextChoices = filtered.length > 0 ? filtered : fallback.length > 0 ? fallback : choices;
  return {
    bucket,
    value: nextChoices[Math.floor(Math.random() * nextChoices.length)]
  };
}

function formatFallbackTime(now: Date): string {
  const hours = now.getHours().toString().padStart(2, "0");
  const minutes = now.getMinutes().toString().padStart(2, "0");
  return `${hours}:${minutes}`;
}

function formatFallbackDate(now: Date): string {
  const day = now.getDate();
  const month = now.toLocaleString("en-IE", { month: "short" });
  const weekday = now.toLocaleString("en-IE", { weekday: "short" });
  return `${weekday}, ${day} ${month}`;
}

function safeFormat(formatter: () => string, fallback: string): string {
  try {
    const formatted = formatter().trim();
    if (formatted.length > 0) {
      return formatted;
    }
  } catch {
    // Fall back to deterministic local formatting.
  }

  return fallback;
}

export function useLocalClock() {
  const [now, setNow] = useState(() => new Date());
  const [greeting, setGreeting] = useState(() => pickGreeting(new Date(), null, []).value);
  const bucketRef = useRef<GreetingBucket>(getGreetingBucket(new Date()));
  const lastGreetingRef = useRef<string | null>(null);
  const recentGreetingsRef = useRef<string[]>([]);

  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 60_000);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    lastGreetingRef.current = greeting;
    recentGreetingsRef.current = [greeting, ...recentGreetingsRef.current.filter((item) => item !== greeting)].slice(0, 3);
  }, [greeting]);

  useEffect(() => {
    const bucket = getGreetingBucket(now);
    if (bucketRef.current === bucket) {
      return;
    }

    const next = pickGreeting(now, lastGreetingRef.current, recentGreetingsRef.current);
    bucketRef.current = next.bucket;
    lastGreetingRef.current = next.value;
    setGreeting(next.value);
  }, [now]);

  useEffect(() => {
    const onAppState = (nextState: AppStateStatus) => {
      if (nextState !== "active") {
        return;
      }

      const nextNow = new Date();
      const nextGreeting = pickGreeting(
        nextNow,
        lastGreetingRef.current,
        recentGreetingsRef.current
      );
      bucketRef.current = nextGreeting.bucket;
      lastGreetingRef.current = nextGreeting.value;
      setGreeting(nextGreeting.value);
      setNow(nextNow);
    };

    const subscription = AppState.addEventListener("change", onAppState);
    return () => subscription.remove();
  }, []);

  return useMemo(() => {
    const timeLabel = safeFormat(
      () =>
        new Intl.DateTimeFormat("en-IE", {
          hour: "2-digit",
          minute: "2-digit",
          hour12: false
        }).format(now),
      formatFallbackTime(now)
    );

    const dateLabel = safeFormat(
      () =>
        new Intl.DateTimeFormat("en-IE", {
          weekday: "short",
          day: "numeric",
          month: "short"
        }).format(now),
      formatFallbackDate(now)
    );

    return {
      greeting,
      timeLabel,
      dateLabel
    };
  }, [greeting, now]);
}
