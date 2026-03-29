import { findCountryByCode, supportedCountries, supportedCurrencies } from "./geoData";

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

export type CountryMetadata = {
  countryCode: string;
  countryName: string;
  dialCode: string;
  currencyCode: string | null;
};

function normalize(value?: string | null) {
  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : null;
}

export function resolveCurrencyForCountryCode(countryCode?: string | null) {
  const normalizedCountryCode = normalize(countryCode)?.toUpperCase();
  if (!normalizedCountryCode) {
    return null;
  }

  const directMatch = supportedCurrencies.find(
    (currency) => currency.regionCode.toUpperCase() === normalizedCountryCode
  );
  if (directMatch) {
    return directMatch.code;
  }

  if (EURO_REGION_CODES.has(normalizedCountryCode)) {
    return "EUR";
  }

  return null;
}

export function resolveCountryMetadataByCode(countryCode?: string | null): CountryMetadata | null {
  const normalizedCountryCode = normalize(countryCode)?.toUpperCase();
  if (!normalizedCountryCode) {
    return null;
  }

  const country = findCountryByCode(normalizedCountryCode);
  if (!country) {
    return null;
  }

  return {
    countryCode: country.code,
    countryName: country.name,
    dialCode: country.dialCode,
    currencyCode: resolveCurrencyForCountryCode(country.code)
  };
}

export function resolveCountryMetadataByName(countryName?: string | null): CountryMetadata | null {
  const normalizedCountryName = normalize(countryName)?.toLowerCase();
  if (!normalizedCountryName) {
    return null;
  }

  const country = supportedCountries.find(
    (item) => item.name.trim().toLowerCase() === normalizedCountryName
  );
  if (!country) {
    return null;
  }

  return resolveCountryMetadataByCode(country.code);
}

export function resolveDialCodeForCountryCode(countryCode?: string | null) {
  return resolveCountryMetadataByCode(countryCode)?.dialCode ?? null;
}

