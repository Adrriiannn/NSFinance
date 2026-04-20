import assert from "node:assert/strict";
import test from "node:test";
import {
  buildNearbyGroundingDiagnosticsMetadata,
  buildChatLocationResolutionDiagnosticsMetadata,
  buildChatLocationMetadata,
  buildChatLocationState,
  bucketizeAccuracy,
  isNearbyLocationDependentPrompt,
  resolveChatLocationAttachment,
  resolveNearbyGpsContext
} from "./chatLocationGrounding";

test("nearby prompt detection only matches nearby place intents", () => {
  assert.equal(isNearbyLocationDependentPrompt("Find restaurants near me"), true);
  assert.equal(isNearbyLocationDependentPrompt("Any brunch places around here?"), true);
  assert.equal(isNearbyLocationDependentPrompt("Can you show cinemas near me?"), true);
  assert.equal(isNearbyLocationDependentPrompt("Movie theatres nearby"), true);
  assert.equal(isNearbyLocationDependentPrompt("Convenience stores near me"), true);
  assert.equal(isNearbyLocationDependentPrompt("How is my monthly budget doing?"), false);
  assert.equal(isNearbyLocationDependentPrompt("Find restaurants in Dublin city centre"), false);
});

test("gps metadata payload includes bounded radius and optional locality", () => {
  const metadata = buildChatLocationMetadata({
    source: "gps",
    latitude: 53.3571234,
    longitude: -6.44789,
    capturedAtUtc: "2026-04-19T11:40:00.000Z",
    accuracyMeters: 18,
    localityLabel: "Lucan village",
    radiusMeters: null
  });
  const state = buildChatLocationState({
    source: "gps",
    latitude: 53.3571234,
    longitude: -6.44789,
    capturedAtUtc: "2026-04-19T11:40:00.000Z",
    accuracyMeters: 18,
    localityLabel: "Lucan village",
    radiusMeters: null
  });

  assert.equal(metadata.chat_location_source, "gps");
  assert.equal(metadata.chat_location_latitude, "53.3571234");
  assert.equal(metadata.chat_location_longitude, "-6.4478900");
  assert.equal(metadata.chat_location_radius_meters, "1200");
  assert.equal(metadata.chat_location_accuracy_bucket, "high");
  assert.equal(metadata.chat_location_locality_label, "Lucan village");
  assert.equal(state?.locationPreference, "Lucan village");
  assert.equal(state?.constraints?.chat_location_source, "gps");
  assert.equal(state?.constraints?.chat_location_latitude, "53.3571234");
  assert.equal(state?.constraints?.chat_location_longitude, "-6.4478900");
  assert.equal(state?.constraints?.chat_location_radius_meters, "1200");
  assert.equal(state?.constraints?.chat_location_accuracy_bucket, "high");
  assert.equal(state?.constraints?.chat_location_locality_label, "Lucan village");
});

test("typed area metadata and state are structured", () => {
  const metadata = buildChatLocationMetadata({
    source: "typed_area",
    typedArea: "Dublin city centre"
  });
  const state = buildChatLocationState({
    source: "typed_area",
    typedArea: "Dublin city centre"
  });

  assert.deepEqual(metadata, {
    chat_location_source: "typed_area",
    chat_location_typed_area: "Dublin city centre"
  });
  assert.equal(state?.locationPreference, "Dublin city centre");
  assert.equal(state?.constraints?.chat_location_source, "typed_area");
  assert.equal(state?.constraints?.chat_location_typed_area, "Dublin city centre");
});

test("accuracy bucketing produces expected buckets", () => {
  assert.equal(bucketizeAccuracy(10), "high");
  assert.equal(bucketizeAccuracy(70), "medium");
  assert.equal(bucketizeAccuracy(220), "low");
  assert.equal(bucketizeAccuracy(null), null);
});

test("nearby gps resolution returns first snapshot when available", async () => {
  const result = await resolveNearbyGpsContext(async (forceFresh) => {
    assert.equal(forceFresh, false);
    return {
      latitude: 53.35,
      longitude: -6.44,
      accuracyMeters: 25,
      capturedAtUtc: "2026-04-20T10:00:00Z",
      localityLabel: "Lucan"
    };
  });

  assert.equal(result.outcome, "success");
  assert.equal(result.refreshAttempted, true);
  assert.equal(result.context?.source, "gps");
  assert.equal(result.context?.latitude, 53.35);
});

test("nearby gps resolution retries with force-fresh and surfaces diagnostics", async () => {
  let callCount = 0;
  const result = await resolveNearbyGpsContext(async (forceFresh) => {
    callCount += 1;
    if (callCount === 1) {
      assert.equal(forceFresh, false);
      return null;
    }

    assert.equal(forceFresh, true);
    return {
      latitude: 53.34,
      longitude: -6.42,
      accuracyMeters: 40,
      capturedAtUtc: "2026-04-20T10:01:00Z",
      localityLabel: "Dublin"
    };
  });

  assert.equal(callCount, 2);
  assert.equal(result.outcome, "success");
  assert.equal(result.refreshAttempted, true);
  const metadata = buildNearbyGroundingDiagnosticsMetadata("granted", result);
  assert.equal(metadata.chat_location_permission_state, "granted");
  assert.equal(metadata.chat_location_refresh_attempted, "true");
  assert.equal(metadata.chat_location_refresh_outcome, "success");
});

test("nearby gps resolution reports failed when no snapshot is available", async () => {
  const result = await resolveNearbyGpsContext(async () => null, {
    timeoutMs: 500,
    retryDelayMs: 0
  });

  assert.equal(result.outcome, "failed");
  assert.equal(result.refreshAttempted, true);
  assert.equal(result.context, null);
});

test("chat location diagnostics allow non-nearby marker", () => {
  const metadata = buildChatLocationResolutionDiagnosticsMetadata(
    "granted",
    {
      context: null,
      refreshAttempted: true,
      outcome: "failed"
    },
    false
  );
  assert.equal(metadata.chat_location_nearby_prompt, "false");
  assert.equal(metadata.chat_location_permission_state, "granted");
  assert.equal(metadata.chat_location_refresh_attempted, "true");
});

test("chat location attachment resolves gps for non-nearby prompts when permission is granted", async () => {
  const attachment = await resolveChatLocationAttachment(
    "what can i do later tonight?",
    "granted",
    async (forceFresh) => {
      assert.equal(forceFresh, false);
      return {
        latitude: 53.35,
        longitude: -6.26,
        accuracyMeters: 22,
        capturedAtUtc: "2026-04-20T19:30:00Z",
        localityLabel: "Dublin"
      };
    }
  );

  assert.equal(attachment.context?.source, "gps");
  assert.equal(attachment.requiresNearbyClarification, false);
  assert.equal(attachment.diagnosticsMetadata.chat_location_nearby_prompt, "false");
});

test("chat location attachment keeps non-nearby prompts non-blocking when location cannot be resolved", async () => {
  const attachment = await resolveChatLocationAttachment(
    "where can i buy a ps5",
    "granted",
    async () => null
  );

  assert.equal(attachment.context, null);
  assert.equal(attachment.requiresNearbyClarification, false);
  assert.equal(attachment.diagnosticsMetadata.chat_location_nearby_prompt, "false");
});

test("chat location attachment keeps nearby prompts strict when permission is not granted", async () => {
  const attachment = await resolveChatLocationAttachment(
    "cinemas near me",
    "denied_can_ask_again",
    async () => null
  );

  assert.equal(attachment.context, null);
  assert.equal(attachment.requiresNearbyClarification, true);
  assert.equal(attachment.diagnosticsMetadata.chat_location_nearby_prompt, "true");
});
