import type { SendAIChatMessageRequest } from "../../../types/api";

export const chatLocationMetadataKeys = {
  source: "chat_location_source",
  latitude: "chat_location_latitude",
  longitude: "chat_location_longitude",
  radiusMeters: "chat_location_radius_meters",
  typedArea: "chat_location_typed_area",
  localityLabel: "chat_location_locality_label",
  accuracyBucket: "chat_location_accuracy_bucket",
  capturedAtUtc: "chat_location_captured_at_utc"
} as const;

const nearbySignals = [
  "near me",
  "nearby",
  "around me",
  "around here",
  "close to me",
  "where i am"
];

const placeSignals = [
  "restaurant",
  "restaurants",
  "cafe",
  "cafes",
  "coffee",
  "dining",
  "brunch",
  "bar",
  "pub",
  "places",
  "place"
];

export type ChatGpsLocationContext = {
  source: "gps";
  latitude: number;
  longitude: number;
  capturedAtUtc: string;
  radiusMeters?: number | null;
  localityLabel?: string | null;
  accuracyMeters?: number | null;
};

export type ChatTypedAreaLocationContext = {
  source: "typed_area";
  typedArea: string;
};

export type ChatLocationContext =
  | ChatGpsLocationContext
  | ChatTypedAreaLocationContext;

export function isNearbyLocationDependentPrompt(prompt: string): boolean {
  const normalized = prompt.trim().toLowerCase();
  if (normalized.length === 0) {
    return false;
  }

  const hasNearbySignal = nearbySignals.some((signal) => normalized.includes(signal));
  if (!hasNearbySignal) {
    return false;
  }

  return placeSignals.some((signal) => normalized.includes(signal));
}

export function normalizeTypedArea(value: string): string | null {
  const normalized = value.trim();
  return normalized.length > 0 ? normalized : null;
}

export function buildChatLocationMetadata(
  context: ChatLocationContext
): Record<string, string> {
  if (context.source === "typed_area") {
    return {
      [chatLocationMetadataKeys.source]: "typed_area",
      [chatLocationMetadataKeys.typedArea]: context.typedArea
    };
  }

  const metadata: Record<string, string> = {
    [chatLocationMetadataKeys.source]: "gps",
    [chatLocationMetadataKeys.latitude]: context.latitude.toFixed(7),
    [chatLocationMetadataKeys.longitude]: context.longitude.toFixed(7),
    [chatLocationMetadataKeys.capturedAtUtc]: context.capturedAtUtc
  };
  const radiusMeters = Math.max(
    500,
    Math.min(10000, context.radiusMeters ?? defaultRadiusFromAccuracy(context.accuracyMeters))
  );
  metadata[chatLocationMetadataKeys.radiusMeters] = radiusMeters.toString();

  const accuracyBucket = bucketizeAccuracy(context.accuracyMeters);
  if (accuracyBucket) {
    metadata[chatLocationMetadataKeys.accuracyBucket] = accuracyBucket;
  }

  if (context.localityLabel?.trim()) {
    metadata[chatLocationMetadataKeys.localityLabel] = context.localityLabel.trim();
  }

  return metadata;
}

export function buildChatLocationState(
  context: ChatLocationContext
): SendAIChatMessageRequest["state"] {
  if (context.source === "typed_area") {
    return {
      locationPreference: context.typedArea,
      constraints: {
        [chatLocationMetadataKeys.source]: "typed_area"
      }
    };
  }

  const locationPreference = context.localityLabel?.trim() || "current_location";
  return {
    locationPreference,
    constraints: {
      [chatLocationMetadataKeys.source]: "gps",
      [chatLocationMetadataKeys.radiusMeters]: String(
        Math.max(500, Math.min(10000, context.radiusMeters ?? defaultRadiusFromAccuracy(context.accuracyMeters)))
      )
    }
  };
}

export function bucketizeAccuracy(accuracyMeters: number | null | undefined): string | null {
  if (accuracyMeters == null || Number.isNaN(accuracyMeters) || accuracyMeters <= 0) {
    return null;
  }

  if (accuracyMeters <= 30) {
    return "high";
  }

  if (accuracyMeters <= 100) {
    return "medium";
  }

  return "low";
}

function defaultRadiusFromAccuracy(accuracyMeters: number | null | undefined): number {
  if (accuracyMeters == null || Number.isNaN(accuracyMeters) || accuracyMeters <= 0) {
    return 2500;
  }

  if (accuracyMeters <= 30) {
    return 1200;
  }

  if (accuracyMeters <= 100) {
    return 2500;
  }

  return 4000;
}
