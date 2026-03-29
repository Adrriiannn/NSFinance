import { getCalendars, getLocales } from "expo-localization";
import * as Location from "expo-location";
import {
  resolveCountryMetadataByCode,
  resolveCountryMetadataByName,
  resolveCurrencyForCountryCode
} from "../reference/countryMetadata";

export type LocationProfileSource = "gps" | "locale" | "manual";

export type DeviceLocationProfile = {
  countryCode: string | null;
  countryName: string | null;
  currencyCode: string | null;
  timezone: string | null;
  localeTag: string | null;
  source: LocationProfileSource;
};

type ResolveDeviceLocationProfileOptions = {
  requestGps?: boolean;
};

function normalize(value?: string | null) {
  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : null;
}

function logLocationEvent(event: string, metadata?: Record<string, unknown>) {
  console.info("[LocationProfile]", {
    event,
    ...metadata
  });
}

export function getLocaleLocationProfile(): DeviceLocationProfile {
  const calendar = getCalendars()[0];
  const locale = getLocales()[0];

  const localeCountryCode = normalize(locale?.regionCode)?.toUpperCase() ?? null;
  const countryMetadata = resolveCountryMetadataByCode(localeCountryCode);
  const localeCurrencyCode = normalize(locale?.currencyCode)?.toUpperCase();
  const localeTag = normalize(locale?.languageTag);

  return {
    countryCode: countryMetadata?.countryCode ?? localeCountryCode,
    countryName: countryMetadata?.countryName ?? null,
    currencyCode:
      localeCurrencyCode
      ?? countryMetadata?.currencyCode
      ?? resolveCurrencyForCountryCode(localeCountryCode),
    timezone: normalize(calendar?.timeZone),
    localeTag,
    source: "locale"
  };
}

export async function resolveDeviceLocationProfile(
  options?: ResolveDeviceLocationProfileOptions
): Promise<DeviceLocationProfile> {
  const localeProfile = getLocaleLocationProfile();
  const shouldRequestGps = options?.requestGps === true;

  if (!shouldRequestGps) {
    logLocationEvent("resolved", {
      source: localeProfile.source,
      countryCode: localeProfile.countryCode,
      reason: "gps_not_requested"
    });
    return localeProfile;
  }

  const servicesEnabled = await Location.hasServicesEnabledAsync();
  if (!servicesEnabled) {
    logLocationEvent("permission_result", {
      status: "services_disabled"
    });
    logLocationEvent("resolved", {
      source: localeProfile.source,
      countryCode: localeProfile.countryCode,
      reason: "services_disabled_fallback"
    });
    return localeProfile;
  }

  const permission = await Location.requestForegroundPermissionsAsync();
  logLocationEvent("permission_result", {
    status: permission.status,
    canAskAgain: permission.canAskAgain
  });

  if (permission.status !== "granted") {
    logLocationEvent("resolved", {
      source: localeProfile.source,
      countryCode: localeProfile.countryCode,
      reason: "permission_denied_fallback"
    });
    return localeProfile;
  }

  try {
    const position = await Location.getCurrentPositionAsync({
      accuracy: Location.Accuracy.Balanced
    });

    const reverseGeocoded = await Location.reverseGeocodeAsync({
      latitude: position.coords.latitude,
      longitude: position.coords.longitude
    });

    const bestMatch = reverseGeocoded[0];
    const gpsCountryCode = normalize(bestMatch?.isoCountryCode)?.toUpperCase() ?? null;
    const gpsCountryName = normalize(bestMatch?.country);
    const timezoneFromGeocode = normalize(
      (bestMatch as { timezone?: string | null } | undefined)?.timezone
    );

    const countryMetadata = resolveCountryMetadataByCode(gpsCountryCode)
      ?? resolveCountryMetadataByName(gpsCountryName);

    const resolvedCountryCode = countryMetadata?.countryCode ?? gpsCountryCode ?? localeProfile.countryCode;
    const resolvedCountryName = countryMetadata?.countryName ?? gpsCountryName ?? localeProfile.countryName;
    const resolvedCurrencyCode =
      countryMetadata?.currencyCode
      ?? resolveCurrencyForCountryCode(resolvedCountryCode)
      ?? localeProfile.currencyCode;
    const resolvedTimezone = timezoneFromGeocode ?? localeProfile.timezone;

    const didResolveCountry = Boolean(resolvedCountryCode || resolvedCountryName);
    const resolvedProfile: DeviceLocationProfile = {
      countryCode: resolvedCountryCode,
      countryName: resolvedCountryName,
      currencyCode: resolvedCurrencyCode,
      timezone: resolvedTimezone,
      localeTag: localeProfile.localeTag,
      source: didResolveCountry ? "gps" : "locale"
    };

    logLocationEvent("resolved", {
      source: resolvedProfile.source,
      countryCode: resolvedProfile.countryCode,
      countryName: resolvedProfile.countryName
    });

    return resolvedProfile;
  } catch (error) {
    logLocationEvent("resolved", {
      source: localeProfile.source,
      countryCode: localeProfile.countryCode,
      reason: "gps_exception_fallback",
      error: error instanceof Error ? error.message : "unknown_error"
    });
    return localeProfile;
  }
}
