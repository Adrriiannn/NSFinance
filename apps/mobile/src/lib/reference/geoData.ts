export type SupportedCountry = {
  code: string;
  name: string;
  dialCode: string;
  ibanPlaceholder: string;
};

export type SupportedCurrency = {
  code: string;
  name: string;
  symbol: string;
  regionCode: string;
};

export type TimezoneOption = {
  id: string;
  label: string;
};

export const supportedCountries: SupportedCountry[] = [
  { code: "IE", name: "Ireland", dialCode: "+353", ibanPlaceholder: "IE29AIBK93115212345678" },
  { code: "GB", name: "United Kingdom", dialCode: "+44", ibanPlaceholder: "GB29NWBK60161331926819" },
  { code: "AT", name: "Austria", dialCode: "+43", ibanPlaceholder: "AT611904300234573201" },
  { code: "BE", name: "Belgium", dialCode: "+32", ibanPlaceholder: "BE68539007547034" },
  { code: "BG", name: "Bulgaria", dialCode: "+359", ibanPlaceholder: "BG80BNBG96611020345678" },
  { code: "HR", name: "Croatia", dialCode: "+385", ibanPlaceholder: "HR1210010051863000160" },
  { code: "CY", name: "Cyprus", dialCode: "+357", ibanPlaceholder: "CY17002001280000001200527600" },
  { code: "CZ", name: "Czech Republic", dialCode: "+420", ibanPlaceholder: "CZ6508000000192000145399" },
  { code: "DK", name: "Denmark", dialCode: "+45", ibanPlaceholder: "DK5000400440116243" },
  { code: "EE", name: "Estonia", dialCode: "+372", ibanPlaceholder: "EE382200221020145685" },
  { code: "FI", name: "Finland", dialCode: "+358", ibanPlaceholder: "FI2112345600000785" },
  { code: "FR", name: "France", dialCode: "+33", ibanPlaceholder: "FR7630006000011234567890189" },
  { code: "DE", name: "Germany", dialCode: "+49", ibanPlaceholder: "DE89370400440532013000" },
  { code: "GR", name: "Greece", dialCode: "+30", ibanPlaceholder: "GR1601101250000000012300695" },
  { code: "HU", name: "Hungary", dialCode: "+36", ibanPlaceholder: "HU42117730161111101800000000" },
  { code: "IT", name: "Italy", dialCode: "+39", ibanPlaceholder: "IT60X0542811101000000123456" },
  { code: "LV", name: "Latvia", dialCode: "+371", ibanPlaceholder: "LV80BANK0000435195001" },
  { code: "LT", name: "Lithuania", dialCode: "+370", ibanPlaceholder: "LT121000011101001000" },
  { code: "LU", name: "Luxembourg", dialCode: "+352", ibanPlaceholder: "LU280019400644750000" },
  { code: "MT", name: "Malta", dialCode: "+356", ibanPlaceholder: "MT84MALT011000012345MTLCAST001S" },
  { code: "NL", name: "Netherlands", dialCode: "+31", ibanPlaceholder: "NL91ABNA0417164300" },
  { code: "PL", name: "Poland", dialCode: "+48", ibanPlaceholder: "PL61109010140000071219812874" },
  { code: "PT", name: "Portugal", dialCode: "+351", ibanPlaceholder: "PT50000201231234567890154" },
  { code: "RO", name: "Romania", dialCode: "+40", ibanPlaceholder: "RO49AAAA1B31007593840000" },
  { code: "SK", name: "Slovakia", dialCode: "+421", ibanPlaceholder: "SK3112000000198742637541" },
  { code: "SI", name: "Slovenia", dialCode: "+386", ibanPlaceholder: "SI56192001234567892" },
  { code: "ES", name: "Spain", dialCode: "+34", ibanPlaceholder: "ES9121000418450200051332" },
  { code: "SE", name: "Sweden", dialCode: "+46", ibanPlaceholder: "SE4550000000058398257466" },
  { code: "NO", name: "Norway", dialCode: "+47", ibanPlaceholder: "NO9386011117947" },
  { code: "IS", name: "Iceland", dialCode: "+354", ibanPlaceholder: "IS140159260076545510730339" },
  { code: "CH", name: "Switzerland", dialCode: "+41", ibanPlaceholder: "CH9300762011623852957" },
  { code: "AU", name: "Australia", dialCode: "+61", ibanPlaceholder: "AU000000000000000000" },
  { code: "US", name: "United States", dialCode: "+1", ibanPlaceholder: "US routing/account number" }
];

export const supportedCurrencies: SupportedCurrency[] = [
  { code: "EUR", name: "Euro", symbol: "EUR", regionCode: "EU" },
  { code: "GBP", name: "Pound Sterling", symbol: "GBP", regionCode: "GB" },
  { code: "USD", name: "US Dollar", symbol: "USD", regionCode: "US" },
  { code: "AUD", name: "Australian Dollar", symbol: "AUD", regionCode: "AU" },
  { code: "BGN", name: "Bulgarian Lev", symbol: "BGN", regionCode: "BG" },
  { code: "CZK", name: "Czech Koruna", symbol: "CZK", regionCode: "CZ" },
  { code: "DKK", name: "Danish Krone", symbol: "DKK", regionCode: "DK" },
  { code: "HUF", name: "Hungarian Forint", symbol: "HUF", regionCode: "HU" },
  { code: "PLN", name: "Polish Zloty", symbol: "PLN", regionCode: "PL" },
  { code: "RON", name: "Romanian Leu", symbol: "RON", regionCode: "RO" },
  { code: "SEK", name: "Swedish Krona", symbol: "SEK", regionCode: "SE" },
  { code: "NOK", name: "Norwegian Krone", symbol: "NOK", regionCode: "NO" },
  { code: "ISK", name: "Icelandic Krona", symbol: "ISK", regionCode: "IS" },
  { code: "CHF", name: "Swiss Franc", symbol: "CHF", regionCode: "CH" }
];

export const supportedTimezones: TimezoneOption[] = [
  { id: "Europe/Dublin", label: "Dublin (GMT/BST)" },
  { id: "Europe/London", label: "London (GMT/BST)" },
  { id: "Europe/Paris", label: "Paris (CET/CEST)" },
  { id: "Europe/Berlin", label: "Berlin (CET/CEST)" },
  { id: "Europe/Bucharest", label: "Bucharest (EET/EEST)" },
  { id: "Europe/Stockholm", label: "Stockholm (CET/CEST)" },
  { id: "Australia/Sydney", label: "Sydney (AEST/AEDT)" },
  { id: "Australia/Perth", label: "Perth (AWST)" },
  { id: "America/New_York", label: "New York (EST/EDT)" },
  { id: "America/Chicago", label: "Chicago (CST/CDT)" },
  { id: "America/Los_Angeles", label: "Los Angeles (PST/PDT)" }
];

export function formatIbanWithSpacing(rawValue: string) {
  const compact = rawValue.replace(/\s+/g, "");
  return compact.replace(/(.{4})/g, "$1 ").trim();
}

export function findCountryByCode(code: string) {
  return supportedCountries.find((country) => country.code === code);
}

export function normalizePhoneNumber(dialCode: string, localNumber: string) {
  const digitsOnly = localNumber.replace(/\D+/g, "");
  const withoutLeadingZero = digitsOnly.replace(/^0+/, "");
  return `${dialCode}${withoutLeadingZero}`;
}

export function countryCodeToFlag(countryCode: string) {
  const normalized = countryCode.trim().toUpperCase();
  if (!/^[A-Z]{2}$/.test(normalized)) {
    return "🏳️";
  }

  return String.fromCodePoint(
    normalized.charCodeAt(0) - 65 + 0x1f1e6,
    normalized.charCodeAt(1) - 65 + 0x1f1e6
  );
}
