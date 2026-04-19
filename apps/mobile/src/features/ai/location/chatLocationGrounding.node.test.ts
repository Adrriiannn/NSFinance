import assert from "node:assert/strict";
import test from "node:test";
import {
  buildChatLocationMetadata,
  buildChatLocationState,
  bucketizeAccuracy,
  isNearbyLocationDependentPrompt
} from "./chatLocationGrounding";

test("nearby prompt detection only matches nearby place intents", () => {
  assert.equal(isNearbyLocationDependentPrompt("Find restaurants near me"), true);
  assert.equal(isNearbyLocationDependentPrompt("Any brunch places around here?"), true);
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
});

test("accuracy bucketing produces expected buckets", () => {
  assert.equal(bucketizeAccuracy(10), "high");
  assert.equal(bucketizeAccuracy(70), "medium");
  assert.equal(bucketizeAccuracy(220), "low");
  assert.equal(bucketizeAccuracy(null), null);
});
