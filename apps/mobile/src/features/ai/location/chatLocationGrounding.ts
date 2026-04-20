import type { SendAIChatMessageRequest } from "../../../types/api";

export const chatLocationMetadataKeys = {
  source: "chat_location_source",
  latitude: "chat_location_latitude",
  longitude: "chat_location_longitude",
  radiusMeters: "chat_location_radius_meters",
  typedArea: "chat_location_typed_area",
  localityLabel: "chat_location_locality_label",
  accuracyBucket: "chat_location_accuracy_bucket",
  capturedAtUtc: "chat_location_captured_at_utc",
  permissionState: "chat_location_permission_state",
  nearbyPrompt: "chat_location_nearby_prompt",
  refreshAttempted: "chat_location_refresh_attempted",
  refreshOutcome: "chat_location_refresh_outcome"
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
  "cinema",
  "cinemas",
  "movie",
  "movies",
  "theatre",
  "theater",
  "movie theatre",
  "movie theater",
  "convenience store",
  "convenience stores",
  "shop",
  "shops",
  "store",
  "stores",
  "museum",
  "museums",
  "park",
  "parks",
  "playground",
  "playgrounds",
  "pharmacy",
  "pharmacies",
  "chemist",
  "petrol station",
  "gas station",
  "fuel",
  "gym",
  "gyms",
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
  "place",
  "visit",
  "go",
  "activity",
  "activities",
  "fun"
];

const discoverySignals = [
  "things to do",
  "what can i do",
  "what should i do",
  "where should i go",
  "somewhere",
  "something fun",
  "places to visit",
  "where can i go"
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

  const hasPlaceSignal = placeSignals.some((signal) => normalized.includes(signal));
  if (hasPlaceSignal) {
    return true;
  }

  return discoverySignals.some((signal) => normalized.includes(signal));
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
        [chatLocationMetadataKeys.source]: "typed_area",
        [chatLocationMetadataKeys.typedArea]: context.typedArea
      }
    };
  }

  const locationPreference = context.localityLabel?.trim() || "current_location";
  const radiusMeters = Math.max(
    500,
    Math.min(10000, context.radiusMeters ?? defaultRadiusFromAccuracy(context.accuracyMeters))
  );
  const constraints: Record<string, string> = {
    [chatLocationMetadataKeys.source]: "gps",
    [chatLocationMetadataKeys.latitude]: context.latitude.toFixed(7),
    [chatLocationMetadataKeys.longitude]: context.longitude.toFixed(7),
    [chatLocationMetadataKeys.radiusMeters]: String(radiusMeters),
    [chatLocationMetadataKeys.capturedAtUtc]: context.capturedAtUtc
  };
  const accuracyBucket = bucketizeAccuracy(context.accuracyMeters);
  if (accuracyBucket) {
    constraints[chatLocationMetadataKeys.accuracyBucket] = accuracyBucket;
  }

  if (context.localityLabel?.trim()) {
    constraints[chatLocationMetadataKeys.localityLabel] = context.localityLabel.trim();
  }

  return {
    locationPreference,
    constraints
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

export type NearbyGpsSnapshot = {
  latitude: number;
  longitude: number;
  accuracyMeters: number | null;
  capturedAtUtc: string;
  localityLabel: string | null;
};

export type NearbyGpsResolutionOutcome = "success" | "failed" | "timeout";

export type NearbyGpsResolutionResult = {
  context: ChatGpsLocationContext | null;
  refreshAttempted: boolean;
  outcome: NearbyGpsResolutionOutcome;
};

export type ChatLocationAttachmentResolution = {
  context: ChatLocationContext | null;
  diagnosticsMetadata: Record<string, string>;
  requiresNearbyClarification: boolean;
};

export async function resolveNearbyGpsContext(
  getSnapshot: (forceFresh: boolean) => Promise<NearbyGpsSnapshot | null>,
  options?: {
    timeoutMs?: number;
    retryDelayMs?: number;
  }
): Promise<NearbyGpsResolutionResult> {
  const timeoutMs = Math.max(500, Math.min(5000, options?.timeoutMs ?? 2200));
  const retryDelayMs = Math.max(0, Math.min(600, options?.retryDelayMs ?? 120));

  const first = await tryGetSnapshotWithTimeout(getSnapshot, false, timeoutMs);
  if (first.snapshot) {
    return {
      context: toGpsContext(first.snapshot),
      refreshAttempted: first.attempted,
      outcome: "success"
    };
  }

  if (retryDelayMs > 0) {
    await delay(retryDelayMs);
  }

  const second = await tryGetSnapshotWithTimeout(getSnapshot, true, timeoutMs);
  if (second.snapshot) {
    return {
      context: toGpsContext(second.snapshot),
      refreshAttempted: true,
      outcome: "success"
    };
  }

  return {
    context: null,
    refreshAttempted: true,
    outcome: first.timedOut || second.timedOut ? "timeout" : "failed"
  };
}

export function buildNearbyGroundingDiagnosticsMetadata(
  permissionState: string,
  result: NearbyGpsResolutionResult
): Record<string, string> {
  return buildChatLocationResolutionDiagnosticsMetadata(permissionState, result, true);
}

export function buildChatLocationResolutionDiagnosticsMetadata(
  permissionState: string,
  result: NearbyGpsResolutionResult,
  nearbyPrompt: boolean
): Record<string, string> {
  return {
    [chatLocationMetadataKeys.permissionState]: permissionState,
    [chatLocationMetadataKeys.nearbyPrompt]: nearbyPrompt ? "true" : "false",
    [chatLocationMetadataKeys.refreshAttempted]: result.refreshAttempted ? "true" : "false",
    [chatLocationMetadataKeys.refreshOutcome]: result.outcome
  };
}

export async function resolveChatLocationAttachment(
  prompt: string,
  permissionState: string,
  getSnapshot: (forceFresh: boolean) => Promise<NearbyGpsSnapshot | null>
): Promise<ChatLocationAttachmentResolution> {
  const requiresNearbyClarification = isNearbyLocationDependentPrompt(prompt);
  if (permissionState !== "granted") {
    const diagnosticsMetadata = buildChatLocationResolutionDiagnosticsMetadata(
      permissionState,
      {
        context: null,
        refreshAttempted: false,
        outcome: "failed"
      },
      requiresNearbyClarification
    );
    return {
      context: null,
      diagnosticsMetadata,
      requiresNearbyClarification
    };
  }

  const resolution = await resolveNearbyGpsContext(getSnapshot);
  const diagnosticsMetadata = buildChatLocationResolutionDiagnosticsMetadata(
    permissionState,
    resolution,
    requiresNearbyClarification
  );
  return {
    context: resolution.context,
    diagnosticsMetadata,
    requiresNearbyClarification: requiresNearbyClarification && !resolution.context
  };
}

async function tryGetSnapshotWithTimeout(
  getSnapshot: (forceFresh: boolean) => Promise<NearbyGpsSnapshot | null>,
  forceFresh: boolean,
  timeoutMs: number
): Promise<{ snapshot: NearbyGpsSnapshot | null; attempted: boolean; timedOut: boolean }> {
  const timeoutSentinel = Symbol("nearby_gps_timeout");
  let timeoutHandle: ReturnType<typeof setTimeout> | null = null;
  const timeoutPromise = new Promise<typeof timeoutSentinel>((resolve) => {
    timeoutHandle = setTimeout(() => resolve(timeoutSentinel), timeoutMs);
  });

  try {
    const raced = await Promise.race([getSnapshot(forceFresh), timeoutPromise]);
    const timedOut = raced === timeoutSentinel;
    return {
      snapshot: timedOut ? null : raced,
      attempted: true,
      timedOut
    };
  } finally {
    if (timeoutHandle) {
      clearTimeout(timeoutHandle);
    }
  }
}

function toGpsContext(snapshot: NearbyGpsSnapshot): ChatGpsLocationContext {
  return {
    source: "gps",
    latitude: snapshot.latitude,
    longitude: snapshot.longitude,
    accuracyMeters: snapshot.accuracyMeters,
    capturedAtUtc: snapshot.capturedAtUtc,
    localityLabel: snapshot.localityLabel
  };
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
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
