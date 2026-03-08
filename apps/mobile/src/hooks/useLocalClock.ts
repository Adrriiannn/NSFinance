import { AppState, type AppStateStatus } from "react-native";
import { useEffect, useMemo, useRef, useState } from "react";

type GreetingBucket = "earlyMorning" | "morning" | "afternoon" | "evening" | "lateNight";

const greetingPools: Record<GreetingBucket, string[]> = {
  earlyMorning: [
    "Early morning",
    "Morning start",
    "Rise early",
    "Dawn check",
    "Start strong"
  ],
  morning: [
    "Good morning",
    "Morning",
    "Rise",
    "Fresh morning",
    "Morning focus"
  ],
  afternoon: [
    "Good afternoon",
    "Afternoon",
    "Midday check",
    "Afternoon focus",
    "Steady afternoon"
  ],
  evening: [
    "Good evening",
    "Evening",
    "Evening check",
    "Calm evening",
    "Nightfall",
    "Evening focus"
  ],
  lateNight: [
    "Late night",
    "Night owl",
    "Midnight check",
    "Quiet night",
    "Night focus"
  ]
};

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
    const timeLabel = new Intl.DateTimeFormat("en-IE", {
      hour: "numeric",
      minute: "2-digit"
    }).format(now);

    const dateLabel = new Intl.DateTimeFormat("en-IE", {
      weekday: "long",
      day: "numeric",
      month: "long"
    }).format(now);

    return {
      greeting,
      timeLabel,
      dateLabel
    };
  }, [greeting, now]);
}
