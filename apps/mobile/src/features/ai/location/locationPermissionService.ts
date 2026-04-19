import * as Linking from "expo-linking";
import * as Location from "expo-location";
import * as SecureStore from "expo-secure-store";
import {
  normalizeForegroundPermissionState as normalizePermissionStateLogic,
  type NormalizedForegroundPermissionState
} from "./locationPermissionLogic";

export type NormalizedLocationPermissionState = NormalizedForegroundPermissionState;

export type ForegroundLocationSnapshot = {
  latitude: number;
  longitude: number;
  accuracyMeters: number | null;
  capturedAtUtc: string;
  localityLabel: string | null;
};

export type LocationUxState = {
  bootExplainerShown: boolean;
  lastPermissionState: NormalizedLocationPermissionState;
  lastSuccessfulLocationSnapshotAtUtc: string | null;
};

export type RequestLocationAccessResult = {
  permissionState: NormalizedLocationPermissionState;
  snapshot: ForegroundLocationSnapshot | null;
};

const LOCATION_UX_STATE_KEY = "nsfinance_location_ux_state_v1";

const defaultUxState: LocationUxState = {
  bootExplainerShown: false,
  lastPermissionState: "unknown",
  lastSuccessfulLocationSnapshotAtUtc: null
};

function normalizeForegroundPermissionState(
  permission: Location.LocationPermissionResponse,
  servicesEnabled: boolean
): NormalizedLocationPermissionState {
  return normalizePermissionStateLogic(
    {
      status:
        permission.status === Location.PermissionStatus.GRANTED
          ? "granted"
          : permission.status === Location.PermissionStatus.UNDETERMINED
            ? "undetermined"
            : "denied",
      canAskAgain: permission.canAskAgain
    },
    servicesEnabled
  );
}

function normalizeLocalityLabel(value?: string | null): string | null {
  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : null;
}

async function persistUxState(next: LocationUxState): Promise<void> {
  await SecureStore.setItemAsync(LOCATION_UX_STATE_KEY, JSON.stringify(next));
}

export async function getLocationUxState(): Promise<LocationUxState> {
  const raw = await SecureStore.getItemAsync(LOCATION_UX_STATE_KEY);
  if (!raw) {
    return defaultUxState;
  }

  try {
    const parsed = JSON.parse(raw) as Partial<LocationUxState>;
    return {
      bootExplainerShown: parsed.bootExplainerShown === true,
      lastPermissionState:
        parsed.lastPermissionState ?? defaultUxState.lastPermissionState,
      lastSuccessfulLocationSnapshotAtUtc:
        parsed.lastSuccessfulLocationSnapshotAtUtc ?? null
    };
  } catch {
    return defaultUxState;
  }
}

export async function markBootExplainerShown(): Promise<void> {
  const current = await getLocationUxState();
  await persistUxState({
    ...current,
    bootExplainerShown: true
  });
}

export async function getForegroundLocationPermissionState(): Promise<NormalizedLocationPermissionState> {
  try {
    const [permission, servicesEnabled] = await Promise.all([
      Location.getForegroundPermissionsAsync(),
      Location.hasServicesEnabledAsync()
    ]);
    const resolved = normalizeForegroundPermissionState(permission, servicesEnabled);
    const current = await getLocationUxState();
    await persistUxState({
      ...current,
      lastPermissionState: resolved
    });
    console.info("[ChatLocationPermission]", {
      event: "permission_state_resolved",
      state: resolved,
      canAskAgain: permission.canAskAgain
    });
    return resolved;
  } catch (error) {
    console.info("[ChatLocationPermission]", {
      event: "permission_state_resolution_failed",
      reason: error instanceof Error ? error.message : "unknown"
    });
    return "unavailable";
  }
}

export async function requestForegroundLocationAccess(): Promise<RequestLocationAccessResult> {
  try {
    const servicesEnabled = await Location.hasServicesEnabledAsync();
    if (!servicesEnabled) {
      const current = await getLocationUxState();
      await persistUxState({
        ...current,
        lastPermissionState: "unavailable"
      });
      return {
        permissionState: "unavailable",
        snapshot: null
      };
    }

    const permission = await Location.requestForegroundPermissionsAsync();
    const permissionState = normalizeForegroundPermissionState(permission, servicesEnabled);
    if (permissionState !== "granted") {
      const current = await getLocationUxState();
      await persistUxState({
        ...current,
        lastPermissionState: permissionState
      });
      return {
        permissionState,
        snapshot: null
      };
    }

    const snapshot = await getFreshForegroundLocationSnapshot(true);
    const current = await getLocationUxState();
    await persistUxState({
      ...current,
      lastPermissionState: permissionState,
      lastSuccessfulLocationSnapshotAtUtc: snapshot?.capturedAtUtc ?? current.lastSuccessfulLocationSnapshotAtUtc
    });
    return {
      permissionState,
      snapshot
    };
  } catch (error) {
    console.info("[ChatLocationPermission]", {
      event: "request_location_access_failed",
      reason: error instanceof Error ? error.message : "unknown"
    });
    return {
      permissionState: "unavailable",
      snapshot: null
    };
  }
}

export async function getFreshForegroundLocationSnapshot(
  forceFresh: boolean
): Promise<ForegroundLocationSnapshot | null> {
  try {
    const permission = await Location.getForegroundPermissionsAsync();
    if (permission.status !== Location.PermissionStatus.GRANTED) {
      return null;
    }

    const now = Date.now();
    const lastKnown = forceFresh
      ? null
      : await Location.getLastKnownPositionAsync({
          maxAge: 2 * 60 * 1000,
          requiredAccuracy: 300
        });
    const position =
      lastKnown
      ?? (await Location.getCurrentPositionAsync({
        accuracy: Location.Accuracy.Balanced
      }));
    if (!position?.coords) {
      return null;
    }

    const reverse = await Location.reverseGeocodeAsync({
      latitude: position.coords.latitude,
      longitude: position.coords.longitude
    });
    const locality = reverse[0];
    const localityLabel = normalizeLocalityLabel(
      locality?.city
      ?? locality?.subregion
      ?? locality?.district
      ?? locality?.name
      ?? locality?.region
    );
    const snapshot: ForegroundLocationSnapshot = {
      latitude: position.coords.latitude,
      longitude: position.coords.longitude,
      accuracyMeters: position.coords.accuracy ?? null,
      capturedAtUtc: new Date(position.timestamp ?? now).toISOString(),
      localityLabel
    };
    console.info("[ChatLocationPermission]", {
      event: "location_snapshot_captured",
      hasLocality: Boolean(snapshot.localityLabel)
    });
    return snapshot;
  } catch (error) {
    console.info("[ChatLocationPermission]", {
      event: "location_snapshot_failed",
      reason: error instanceof Error ? error.message : "unknown"
    });
    return null;
  }
}

export async function openLocationSettings(): Promise<void> {
  try {
    await Linking.openSettings();
  } catch (error) {
    console.info("[ChatLocationPermission]", {
      event: "open_settings_failed",
      reason: error instanceof Error ? error.message : "unknown"
    });
  }
}
