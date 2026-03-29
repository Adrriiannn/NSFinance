import { getCalendars, getLocales } from "expo-localization";
import { findCountryByCode, supportedCurrencies } from "../reference/geoData";

const EURO_REGION_CODES = new Set([
  "AT",
  "BE",
  "CY",
  "DE",
  "EE",
  "ES",
  "FI",
  "FR",
  "GR",
  "HR",
  "IE",
  "IT",
  "LT",
  "LU",
  "LV",
  "MT",
  "NL",
  "PT",
  "SI",
  "SK"
]);

function normalize(value?: string | null) {
  const nextValue = value?.trim();
  return nextValue && nextValue.length > 0 ? nextValue : null;
}

function resolveCurrencyFromRegion(regionCode: string | null) {
  if (!regionCode) {
    return null;
  }

  const normalizedRegion = regionCode.toUpperCase();
  const directMatch = supportedCurrencies.find(
    (currency) => currency.regionCode.toUpperCase() === normalizedRegion
  );

  if (directMatch) {
    return directMatch.code;
  }

  if (EURO_REGION_CODES.has(normalizedRegion)) {
    return "EUR";
  }

  return null;
}

export type DeviceLocaleProfile = {
  timezone: string | null;
  countryCode: string | null;
  countryName: string | null;
  currencyCode: string | null;
  localeTag: string | null;
};

export function getDeviceLocaleProfile(): DeviceLocaleProfile {
  const calendar = getCalendars()[0];
  const locale = getLocales()[0];

  const timezone = normalize(calendar?.timeZone);
  const countryCode = normalize(locale?.regionCode)?.toUpperCase() ?? null;
  const countryName = countryCode ? findCountryByCode(countryCode)?.name ?? null : null;
  const localeTag = normalize(locale?.languageTag ?? locale?.languageCode);
  const currencyCode =
    normalize(locale?.currencyCode)?.toUpperCase()
    ?? resolveCurrencyFromRegion(countryCode);

  return {
    timezone,
    countryCode,
    countryName,
    currencyCode,
    localeTag
  };
}

export function getFallbackCurrencyForCountryCode(countryCode: string | null) {
  return resolveCurrencyFromRegion(countryCode);
}

