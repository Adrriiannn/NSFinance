import * as Location from "expo-location";
import { findCountryByCode } from "../reference/geoData";
import { getFallbackCurrencyForCountryCode } from "./deviceLocaleProfile";

export type OptionalDeviceLocationResult =
  | {
      status: "granted";
      latitude: number;
      longitude: number;
      timezone: string | null;
      countryCode: string | null;
      countryName: string | null;
      currencyCode: string | null;
    }
  | {
      status: "permission_denied" | "service_unavailable";
      reason: string;
    };

function normalize(value?: string | null) {
  const nextValue = value?.trim();
  return nextValue && nextValue.length > 0 ? nextValue : null;
}

export async function requestOptionalDeviceLocation(): Promise<OptionalDeviceLocationResult> {
  const serviceEnabled = await Location.hasServicesEnabledAsync();
  if (!serviceEnabled) {
    return {
      status: "service_unavailable",
      reason: "Location services are disabled on this device."
    };
  }

  const permission = await Location.requestForegroundPermissionsAsync();
  if (permission.status !== "granted") {
    return {
      status: "permission_denied",
      reason: "Location permission was not granted."
    };
  }

  const position = await Location.getCurrentPositionAsync({
    accuracy: Location.Accuracy.Balanced
  });

  let countryCode: string | null = null;
  let countryName: string | null = null;

  try {
    const reverseGeocoded = await Location.reverseGeocodeAsync({
      latitude: position.coords.latitude,
      longitude: position.coords.longitude
    });
    const bestMatch = reverseGeocoded[0];
    countryCode = normalize(bestMatch?.isoCountryCode)?.toUpperCase() ?? null;
    countryName = countryCode
      ? findCountryByCode(countryCode)?.name ?? normalize(bestMatch?.country)
      : normalize(bestMatch?.country);
  } catch {
    countryCode = null;
    countryName = null;
  }

  return {
    status: "granted",
    latitude: position.coords.latitude,
    longitude: position.coords.longitude,
    timezone: null,
    countryCode,
    countryName,
    currencyCode: getFallbackCurrencyForCountryCode(countryCode)
  };
}
