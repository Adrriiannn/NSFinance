import { Ionicons } from "@expo/vector-icons";
import { useNavigation, usePreventRemove } from "@react-navigation/native";
import * as FileSystem from "expo-file-system/legacy";
import * as ImagePicker from "expo-image-picker";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  AppState,
  Alert,
  Image,
  Modal,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View
} from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ModalSelectField } from "../../../src/components/ui/ModalSelectField";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { TextField } from "../../../src/components/ui/TextField";
import { HeaderActionButton, HeaderShell } from "../../../src/layout/appHeader";
import {
  countryCodeToFlag,
  findCountryByCode,
  normalizePhoneNumber,
  supportedCountries,
  supportedCurrencies,
  supportedTimezones
} from "../../../src/lib/reference/geoData";
import {
  getLocaleLocationProfile,
  resolveDeviceLocationProfile,
  type DeviceLocationProfile
} from "../../../src/lib/device/deviceLocationProfile";
import {
  resolveCountryMetadataByCode,
  resolveCountryMetadataByName
} from "../../../src/lib/reference/countryMetadata";
import { showFlashMessage } from "../../../src/lib/flashMessage";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";
import {
  useUpdateUserProfileMutation,
  useUserProfileQuery
} from "../../../src/features/users/useUserSettings";

const financialFocusOptions = [
  "Save more money",
  "Reduce unnecessary spending",
  "Track subscriptions",
  "Improve financial discipline",
  "Understand my spending habits",
  "Plan big purchases",
  "Pay off debt",
  "Build financial awareness",
  "Manage family finances",
  "Manage business finances"
] as const;

const employmentOptions = [
  { label: "Employed", value: "employed" },
  { label: "Student", value: "student" },
  { label: "Freelancer", value: "freelancer" },
  { label: "Business owner", value: "business_owner" },
  { label: "Other", value: "other" }
];

const incomeStabilityOptions = [
  { label: "Stable", value: "stable" },
  { label: "Irregular", value: "irregular" },
  { label: "Seasonal", value: "seasonal" }
];

const concernOptions = [
  { label: "Saving", value: "saving" },
  { label: "Debt", value: "debt" },
  { label: "Budgeting", value: "budgeting" },
  { label: "Planning", value: "planning" },
  { label: "Awareness", value: "awareness" },
  { label: "Subscriptions", value: "subscriptions" }
];

const monthOptions = [
  { label: "January", value: "1" },
  { label: "February", value: "2" },
  { label: "March", value: "3" },
  { label: "April", value: "4" },
  { label: "May", value: "5" },
  { label: "June", value: "6" },
  { label: "July", value: "7" },
  { label: "August", value: "8" },
  { label: "September", value: "9" },
  { label: "October", value: "10" },
  { label: "November", value: "11" },
  { label: "December", value: "12" }
];

const MIN_PROFILE_AGE_YEARS = 8;
const MAX_PROFILE_AGE_YEARS = 120;
const NS_TAG_MAX_LENGTH = 12;
const NS_TAG_PATTERN = /^[a-z0-9_-]{2,12}$/i;
const currentYear = new Date().getFullYear();
const latestBirthYear = currentYear;
const earliestBirthYear = currentYear - MAX_PROFILE_AGE_YEARS;
const yearOptions = Array.from({ length: latestBirthYear - earliestBirthYear + 1 }, (_, index) => {
  const year = latestBirthYear - index;
  return { label: `${year}`, value: `${year}` };
});
const DIAL_ITEM_HEIGHT = 44;
const DEFAULT_TIMEZONE_FALLBACK =
  Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
type PhoneFormatRule = {
  minDigits: number;
  maxDigits: number;
  groups: number[];
  allowLeadingZero?: boolean;
};

const PHONE_FORMAT_RULES: Record<string, PhoneFormatRule> = {
  IE: { minDigits: 9, maxDigits: 9, groups: [2, 3, 4], allowLeadingZero: true },
  GB: { minDigits: 10, maxDigits: 10, groups: [4, 3, 3], allowLeadingZero: true },
  AT: { minDigits: 8, maxDigits: 12, groups: [3, 3, 3, 3], allowLeadingZero: true },
  BE: { minDigits: 9, maxDigits: 9, groups: [3, 2, 2, 2], allowLeadingZero: true },
  BG: { minDigits: 9, maxDigits: 9, groups: [2, 3, 4], allowLeadingZero: true },
  HR: { minDigits: 9, maxDigits: 9, groups: [2, 3, 4], allowLeadingZero: true },
  CY: { minDigits: 8, maxDigits: 8, groups: [2, 3, 3] },
  CZ: { minDigits: 9, maxDigits: 9, groups: [3, 3, 3] },
  DK: { minDigits: 8, maxDigits: 8, groups: [2, 2, 2, 2] },
  EE: { minDigits: 7, maxDigits: 8, groups: [3, 2, 3] },
  FI: { minDigits: 8, maxDigits: 10, groups: [2, 3, 3, 2], allowLeadingZero: true },
  FR: { minDigits: 9, maxDigits: 9, groups: [1, 2, 2, 2, 2], allowLeadingZero: true },
  DE: { minDigits: 10, maxDigits: 11, groups: [3, 3, 3, 2], allowLeadingZero: true },
  GR: { minDigits: 10, maxDigits: 10, groups: [3, 3, 4], allowLeadingZero: true },
  HU: { minDigits: 9, maxDigits: 9, groups: [2, 3, 4], allowLeadingZero: true },
  IT: { minDigits: 9, maxDigits: 10, groups: [3, 3, 4] },
  LV: { minDigits: 8, maxDigits: 8, groups: [2, 3, 3] },
  LT: { minDigits: 8, maxDigits: 8, groups: [3, 2, 3], allowLeadingZero: true },
  LU: { minDigits: 9, maxDigits: 9, groups: [3, 3, 3] },
  MT: { minDigits: 8, maxDigits: 8, groups: [4, 4] },
  NL: { minDigits: 9, maxDigits: 9, groups: [2, 3, 4], allowLeadingZero: true },
  PL: { minDigits: 9, maxDigits: 9, groups: [3, 3, 3] },
  PT: { minDigits: 9, maxDigits: 9, groups: [3, 3, 3] },
  RO: { minDigits: 9, maxDigits: 9, groups: [2, 3, 4], allowLeadingZero: true },
  SK: { minDigits: 9, maxDigits: 9, groups: [3, 3, 3], allowLeadingZero: true },
  SI: { minDigits: 8, maxDigits: 8, groups: [2, 3, 3], allowLeadingZero: true },
  ES: { minDigits: 9, maxDigits: 9, groups: [3, 3, 3] },
  SE: { minDigits: 9, maxDigits: 9, groups: [3, 3, 3], allowLeadingZero: true },
  NO: { minDigits: 8, maxDigits: 8, groups: [3, 2, 3] },
  IS: { minDigits: 7, maxDigits: 7, groups: [3, 4] },
  CH: { minDigits: 9, maxDigits: 9, groups: [2, 3, 2, 2], allowLeadingZero: true },
  AU: { minDigits: 9, maxDigits: 9, groups: [1, 4, 4], allowLeadingZero: true },
  US: { minDigits: 10, maxDigits: 10, groups: [3, 3, 4] }
};

const DEFAULT_PHONE_FORMAT_RULE: PhoneFormatRule = {
  minDigits: 6,
  maxDigits: 12,
  groups: [3, 3, 3, 3],
  allowLeadingZero: true
};

function dayCountForMonth(year: number, monthOneBased: number) {
  return new Date(year, monthOneBased, 0).getDate();
}

function formatTimezoneClock(timezoneId: string, referenceDate: Date) {
  try {
    return new Intl.DateTimeFormat("en-GB", {
      hour: "2-digit",
      minute: "2-digit",
      hour12: false,
      timeZone: timezoneId
    }).format(referenceDate);
  } catch {
    return "--:--";
  }
}

function formatMemberSince(value?: string | null) {
  if (!value) {
    return "Member since recently";
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return "Member since recently";
  }

  return `Member since ${new Intl.DateTimeFormat("en-GB", {
    month: "short",
    year: "numeric"
  }).format(parsed)}`;
}

function getInitials(name: string) {
  const normalized = name
    .split(" ")
    .map((part) => part.trim())
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? "")
    .join("");

  return normalized || "NS";
}

function parsePhone(countryDefault: string, value?: string | null) {
  const raw = (value ?? "").trim();
  if (!raw) {
    return { countryCode: countryDefault, localNumber: "" };
  }

  const sorted = [...supportedCountries].sort((left, right) => right.dialCode.length - left.dialCode.length);
  for (const country of sorted) {
    if (raw.startsWith(country.dialCode)) {
      const countryCode = country.code;
      const national = raw.slice(country.dialCode.length);
      return {
        countryCode,
        localNumber: formatPhoneInputByCountry(countryCode, national)
      };
    }
  }

  return { countryCode: countryDefault, localNumber: formatPhoneInputByCountry(countryDefault, raw) };
}

function parseDobToParts(value?: string | null) {
  if (!value) {
    return { day: "", month: "", year: "" };
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return { day: "", month: "", year: "" };
  }

  return {
    day: `${date.getUTCDate()}`,
    month: `${date.getUTCMonth() + 1}`,
    year: `${date.getUTCFullYear()}`
  };
}

function resolveSupportedCountryCode(value?: string | null) {
  const normalized = value?.trim().toUpperCase();
  if (!normalized) {
    return "IE";
  }

  return findCountryByCode(normalized) ? normalized : "IE";
}

function resolveCountryName(profileCountry: string | null | undefined, localeProfile: DeviceLocationProfile) {
  const trimmed = profileCountry?.trim();
  if (trimmed) {
    return trimmed;
  }

  if (localeProfile.countryName) {
    return localeProfile.countryName;
  }

  if (localeProfile.countryCode) {
    return findCountryByCode(localeProfile.countryCode)?.name ?? "Ireland";
  }

  return "Ireland";
}

function resolveTimezone(profileTimezone: string | null | undefined, localeProfile: DeviceLocationProfile) {
  const trimmed = profileTimezone?.trim();
  if (trimmed) {
    return trimmed;
  }

  if (localeProfile.timezone) {
    return localeProfile.timezone;
  }

  return DEFAULT_TIMEZONE_FALLBACK;
}

function resolveCurrency(profileCurrency: string | null | undefined, localeProfile: DeviceLocationProfile) {
  const normalizedProfile = profileCurrency?.trim().toUpperCase();
  if (normalizedProfile && supportedCurrencies.some((currency) => currency.code === normalizedProfile)) {
    return normalizedProfile;
  }

  const normalizedLocale = localeProfile.currencyCode?.trim().toUpperCase();
  if (normalizedLocale && supportedCurrencies.some((currency) => currency.code === normalizedLocale)) {
    return normalizedLocale;
  }

  return "EUR";
}

function getDobPlaceholder(referenceDate = new Date()) {
  const day = `${referenceDate.getDate()}`;
  const monthLabel = new Intl.DateTimeFormat("en-GB", { month: "short" }).format(referenceDate);
  const year = `${referenceDate.getFullYear()}`;

  return {
    day,
    monthLabel,
    year,
    fullLabel: `${day} ${monthLabel} ${year}`
  };
}

function normalizeNsTag(rawValue?: string | null) {
  if (!rawValue) {
    return "";
  }

  return rawValue.trim().replace(/^@+/, "").toLowerCase();
}

function formatNsTag(rawValue?: string | null) {
  const normalized = normalizeNsTag(rawValue);
  if (!normalized) {
    return "@";
  }

  return `@${normalized}`;
}

function toPhoneDigits(rawValue: string) {
  return rawValue.replace(/\D+/g, "");
}

function formatDigitsByGroups(rawDigits: string, groups: number[]) {
  if (!rawDigits) {
    return "";
  }

  const chunks: string[] = [];
  let cursor = 0;
  for (const groupSize of groups) {
    if (cursor >= rawDigits.length) {
      break;
    }

    const end = Math.min(cursor + groupSize, rawDigits.length);
    chunks.push(rawDigits.slice(cursor, end));
    cursor = end;
  }

  if (cursor < rawDigits.length) {
    chunks.push(rawDigits.slice(cursor));
  }

  return chunks.join(" ");
}

function chunkDigitsByGroups(rawDigits: string, groups: number[]) {
  if (!rawDigits) {
    return [];
  }

  const chunks: string[] = [];
  let cursor = 0;
  for (const groupSize of groups) {
    if (cursor >= rawDigits.length) {
      break;
    }

    const end = Math.min(cursor + groupSize, rawDigits.length);
    chunks.push(rawDigits.slice(cursor, end));
    cursor = end;
  }

  if (cursor < rawDigits.length) {
    chunks.push(rawDigits.slice(cursor));
  }

  return chunks;
}

function getPhoneFormatRule(countryCode: string) {
  const normalized = countryCode.trim().toUpperCase();
  return PHONE_FORMAT_RULES[normalized] ?? DEFAULT_PHONE_FORMAT_RULE;
}

function clampDigitsToCountryLength(countryCode: string, rawValue: string) {
  const rule = getPhoneFormatRule(countryCode);
  const rawDigits = toPhoneDigits(rawValue);
  if (!rawDigits) {
    return rawDigits;
  }

  const normalizedDigits =
    rule.allowLeadingZero && rawDigits.startsWith("0")
      ? `0${rawDigits.slice(1).replace(/^0+/, "")}`
      : rawDigits;

  const maxDigits = rule.maxDigits + (rule.allowLeadingZero ? 1 : 0);
  if (normalizedDigits.length <= maxDigits) {
    return normalizedDigits;
  }

  return normalizedDigits.slice(0, maxDigits);
}

function formatPhoneInputByCountry(countryCode: string, rawValue: string) {
  const rule = getPhoneFormatRule(countryCode);
  const digits = clampDigitsToCountryLength(countryCode, rawValue);
  if (!digits) {
    return "";
  }

  if (rule.allowLeadingZero && digits.startsWith("0")) {
    const coreDigits = digits.slice(1);
    const limitedCore = coreDigits.slice(0, rule.maxDigits);
    const chunks = chunkDigitsByGroups(limitedCore, rule.groups);
    if (chunks.length === 0) {
      return "0";
    }

    chunks[0] = `0${chunks[0]}`;
    return chunks.join(" ");
  }

  const limitedDigits = digits.slice(0, rule.maxDigits);
  return formatDigitsByGroups(limitedDigits, rule.groups);
}

function buildGenericPhonePlaceholder(countryCode: string) {
  const rule = getPhoneFormatRule(countryCode);
  const maxDigits = Math.max(rule.minDigits, rule.maxDigits);
  const digits = Array.from({ length: maxDigits }, (_, index) => `${(index + 1) % 10}`).join("");
  return formatDigitsByGroups(digits.slice(0, rule.maxDigits), rule.groups);
}

function validatePhoneForCountry(countryCode: string, rawValue: string) {
  const rule = getPhoneFormatRule(countryCode);
  const digits = toPhoneDigits(rawValue);
  if (!digits) {
    return undefined;
  }

  if (rule.allowLeadingZero && digits.startsWith("0")) {
    const coreDigits = digits.slice(1);
    if (coreDigits.length < rule.minDigits) {
      return "TOO_SHORT";
    }

    if (coreDigits.length > rule.maxDigits) {
      return "TOO_LONG";
    }

    return undefined;
  }

  if (digits.length < rule.minDigits) {
    return "TOO_SHORT";
  }

  if (digits.length > rule.maxDigits) {
    return "TOO_LONG";
  }

  return undefined;
}

function buildDobIso(day: string, month: string, year: string) {
  if (!day && !month && !year) {
    return null;
  }

  if (!day || !month || !year) {
    return undefined;
  }

  const parsed = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day)));
  if (Number.isNaN(parsed.getTime())) {
    return undefined;
  }

  if (parsed.getUTCDate() !== Number(day) || parsed.getUTCMonth() + 1 !== Number(month)) {
    return undefined;
  }

  return parsed.toISOString();
}

function isAtLeastAge(dob: Date, minimumAgeYears: number) {
  const now = new Date();
  let age = now.getUTCFullYear() - dob.getUTCFullYear();
  const monthDiff = now.getUTCMonth() - dob.getUTCMonth();
  const dayDiff = now.getUTCDate() - dob.getUTCDate();
  if (monthDiff < 0 || (monthDiff === 0 && dayDiff < 0)) {
    age -= 1;
  }
  return age >= minimumAgeYears;
}

async function persistAvatarLocally(uri: string) {
  const root = `${FileSystem.documentDirectory ?? ""}profile`;
  const info = await FileSystem.getInfoAsync(root);
  if (!info.exists) {
    await FileSystem.makeDirectoryAsync(root, { intermediates: true });
  }

  const targetUri = `${root}/avatar-${Date.now()}.jpg`;
  await FileSystem.copyAsync({ from: uri, to: targetUri });
  return targetUri;
}

export default function ProfileSettingsScreen() {
  const router = useRouter();
  const navigation = useNavigation();
  const profileQuery = useUserProfileQuery();
  const updateMutation = useUpdateUserProfileMutation();
  const [deviceLocaleProfile, setDeviceLocaleProfile] = useState<DeviceLocationProfile>(() =>
    getLocaleLocationProfile()
  );
  const defaultPhoneCountryCode = useMemo(
    () => resolveSupportedCountryCode(deviceLocaleProfile.countryCode),
    [deviceLocaleProfile.countryCode]
  );

  const [primaryEmail, setPrimaryEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [profileImageUri, setProfileImageUri] = useState("");
  const [profileBio, setProfileBio] = useState("");
  const [phoneCountryCode, setPhoneCountryCode] = useState(defaultPhoneCountryCode);
  const [phoneLocalNumber, setPhoneLocalNumber] = useState("");
  const [dateDay, setDateDay] = useState("");
  const [dateMonth, setDateMonth] = useState("");
  const [dateYear, setDateYear] = useState("");
  const [country, setCountry] = useState(() => resolveCountryName(null, deviceLocaleProfile));
  const [timezone, setTimezone] = useState(() => resolveTimezone(null, deviceLocaleProfile));
  const [preferredCurrency, setPreferredCurrency] = useState(() => resolveCurrency(null, deviceLocaleProfile));
  const [financialFocus, setFinancialFocus] = useState<string[]>([]);
  const [employmentStatus, setEmploymentStatus] = useState<string | null>(null);
  const [incomeStability, setIncomeStability] = useState<string | null>(null);
  const [primaryConcern, setPrimaryConcern] = useState<string | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const [initialSnapshot, setInitialSnapshot] = useState("");
  const [confirmLeaveVisible, setConfirmLeaveVisible] = useState(false);
  const [pendingLeaveAction, setPendingLeaveAction] = useState<null | (() => void)>(null);
  const [dobDialVisible, setDobDialVisible] = useState(false);
  const [isResolvingLocation, setIsResolvingLocation] = useState(false);
  const [hasManualTimezoneOverride, setHasManualTimezoneOverride] = useState(false);
  const [hasManualCurrencyOverride, setHasManualCurrencyOverride] = useState(false);
  const [timezoneClock, setTimezoneClock] = useState(() => new Date());
  const dobPlaceholder = useMemo(() => getDobPlaceholder(), []);
  const deviceLocaleProfileRef = useRef(deviceLocaleProfile);
  const dayDialRef = useRef<ScrollView | null>(null);
  const monthDialRef = useRef<ScrollView | null>(null);
  const yearDialRef = useRef<ScrollView | null>(null);

  useEffect(() => {
    deviceLocaleProfileRef.current = deviceLocaleProfile;
  }, [deviceLocaleProfile]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") {
        setDeviceLocaleProfile(getLocaleLocationProfile());
      }
    });

    return () => {
      subscription.remove();
    };
  }, []);

  useEffect(() => {
    if (!phoneCountryCode) {
      setPhoneCountryCode(defaultPhoneCountryCode);
    }
  }, [defaultPhoneCountryCode, phoneCountryCode]);

  useEffect(() => {
    if (!profileQuery.data) {
      return;
    }

    const profile = profileQuery.data;
    const localeDefaults = deviceLocaleProfileRef.current;
    const resolvedCountry = resolveCountryName(profile.countryRegion, localeDefaults);
    const resolvedTimezone = resolveTimezone(profile.timezone, localeDefaults);
    const resolvedCurrency = resolveCurrency(profile.preferredCurrency, localeDefaults);
    const parsedPhone = parsePhone(defaultPhoneCountryCode, profile.phoneNumber);
    const parsedDob = parseDobToParts(profile.dateOfBirth);

    setPrimaryEmail(profile.primaryEmail ?? "");
    setFullName(profile.fullName);
    setDisplayName(formatNsTag(profile.displayName));
    setProfileImageUri(profile.profileImageUrl ?? "");
    setProfileBio(profile.profileSubtitle ?? "");
    setPhoneCountryCode(parsedPhone.countryCode);
    setPhoneLocalNumber(parsedPhone.localNumber);
    setDateDay(parsedDob.day);
    setDateMonth(parsedDob.month);
    setDateYear(parsedDob.year);
    setCountry(resolvedCountry);
    setTimezone(resolvedTimezone);
    setPreferredCurrency(resolvedCurrency);
    setHasManualTimezoneOverride(false);
    setHasManualCurrencyOverride(false);
    setFinancialFocus(profile.financialFocus ?? []);
    setEmploymentStatus(profile.employmentStatus ?? null);
    setIncomeStability(profile.incomeStability ?? null);
    setPrimaryConcern(profile.primaryFinancialConcern ?? null);

    const snapshotFromProfile = JSON.stringify({
      primaryEmail: (profile.primaryEmail ?? "").trim(),
      fullName: profile.fullName.trim(),
      displayName: normalizeNsTag(profile.displayName),
      profileImageUri: (profile.profileImageUrl ?? "").trim(),
      profileBio: (profile.profileSubtitle ?? "").trim(),
      phoneCountryCode: parsedPhone.countryCode,
      phoneLocalNumber: parsedPhone.localNumber.trim(),
      dateDay: parsedDob.day,
      dateMonth: parsedDob.month,
      dateYear: parsedDob.year,
      country: resolvedCountry,
      timezone: resolvedTimezone,
      preferredCurrency: resolvedCurrency,
      financialFocus: [...(profile.financialFocus ?? [])].sort(),
      employmentStatus: profile.employmentStatus ?? null,
      incomeStability: profile.incomeStability ?? null,
      primaryConcern: profile.primaryFinancialConcern ?? null
    });
    setInitialSnapshot(snapshotFromProfile);
  }, [defaultPhoneCountryCode, profileQuery.data]);

  const formSnapshot = useMemo(
    () =>
      JSON.stringify({
        primaryEmail: primaryEmail.trim(),
        fullName: fullName.trim(),
        displayName: normalizeNsTag(displayName),
        profileImageUri: profileImageUri.trim(),
        profileBio: profileBio.trim(),
        phoneCountryCode,
        phoneLocalNumber: phoneLocalNumber.trim(),
        dateDay,
        dateMonth,
        dateYear,
        country,
        timezone,
        preferredCurrency,
        financialFocus: [...financialFocus].sort(),
        employmentStatus,
        incomeStability,
        primaryConcern
      }),
    [
      country,
      dateDay,
      dateMonth,
      dateYear,
      displayName,
      employmentStatus,
      financialFocus,
      fullName,
      incomeStability,
      phoneCountryCode,
      phoneLocalNumber,
      preferredCurrency,
      primaryConcern,
      primaryEmail,
      profileBio,
      profileImageUri,
      timezone
    ]
  );

  useEffect(() => {
    const interval = setInterval(() => {
      setTimezoneClock(new Date());
    }, 30_000);

    return () => clearInterval(interval);
  }, []);

  const hasUnsavedChanges = initialSnapshot.length > 0 && formSnapshot !== initialSnapshot;
  const initials = useMemo(
    () => getInitials(fullName || displayName || "NSFinance"),
    [displayName, fullName]
  );

  const countryOptions = supportedCountries.map((item) => ({
    label: `${countryCodeToFlag(item.code)} ${item.code} ${item.dialCode}`,
    value: item.code
  }));
  const countryNameOptions = supportedCountries.map((item) => ({
    label: item.name,
    value: item.name
  }));
  const timezoneOptions = useMemo(
    () => {
      const options = supportedTimezones.map((item) => ({
        label: `${item.label} - ${formatTimezoneClock(item.id, timezoneClock)}`,
        value: item.id
      }));

      if (timezone && !options.some((option) => option.value === timezone)) {
        options.unshift({
          label: `${timezone} - ${formatTimezoneClock(timezone, timezoneClock)}`,
          value: timezone
        });
      }

      return options;
    },
    [timezoneClock, timezone]
  );
  const currencyOptions = supportedCurrencies.map((item) => ({
    label: `${item.code} - ${item.name}`,
    value: item.code
  }));
  const monthDialValues = monthOptions.map((item) => item.value);
  const yearDialValues = yearOptions.map((item) => item.value);
  const dayDialValues = useMemo(() => {
    const year = Number(dateYear);
    const month = Number(dateMonth);
    const maxDays =
      Number.isFinite(year) && Number.isFinite(month) && month >= 1 && month <= 12
        ? dayCountForMonth(year, month)
        : 31;

    return Array.from({ length: maxDays }, (_, index) => `${index + 1}`);
  }, [dateMonth, dateYear]);

  const selectedMonthLabel =
    monthOptions.find((item) => item.value === dateMonth)?.label ?? "Month";
  const phonePlaceholder = useMemo(() => {
    return buildGenericPhonePlaceholder(phoneCountryCode) || "12 345 6789";
  }, [phoneCountryCode]);
  const dobAgeWarning = useMemo(() => {
    const dobIso = buildDobIso(dateDay, dateMonth, dateYear);
    if (dobIso === undefined || dobIso === null) {
      return null;
    }

    const dob = new Date(dobIso);
    if (!isAtLeastAge(dob, MIN_PROFILE_AGE_YEARS)) {
      return "Please choose an earlier birth year to continue.";
    }

    return null;
  }, [dateDay, dateMonth, dateYear]);

  useEffect(() => {
    setPhoneLocalNumber((current) => formatPhoneInputByCountry(phoneCountryCode, current));
  }, [phoneCountryCode]);

  useEffect(() => {
    if (!dateDay || !dateMonth || !dateYear) {
      return;
    }

    const limit = dayCountForMonth(Number(dateYear), Number(dateMonth));
    if (Number(dateDay) > limit) {
      setDateDay(`${limit}`);
    }
  }, [dateDay, dateMonth, dateYear]);

  const scrollDialToValue = (
    ref: React.MutableRefObject<ScrollView | null>,
    values: string[],
    value: string
  ) => {
    const index = Math.max(0, values.indexOf(value));
    ref.current?.scrollTo({
      y: index * DIAL_ITEM_HEIGHT,
      animated: false
    });
  };

  useEffect(() => {
    if (!dobDialVisible) {
      return;
    }

    const timer = setTimeout(() => {
      if (dateDay) {
        scrollDialToValue(dayDialRef, dayDialValues, dateDay);
      }
      if (dateMonth) {
        scrollDialToValue(monthDialRef, monthDialValues, dateMonth);
      }
      if (dateYear) {
        scrollDialToValue(yearDialRef, yearDialValues, dateYear);
      }
    }, 20);

    return () => clearTimeout(timer);
  }, [dateDay, dateMonth, dateYear, dayDialValues, dobDialVisible, monthDialValues, yearDialValues]);

  const applyDialValue = (field: "day" | "month" | "year", offsetY: number, values: string[]) => {
    const index = Math.round(offsetY / DIAL_ITEM_HEIGHT);
    const bounded = Math.max(0, Math.min(index, values.length - 1));
    const value = values[bounded];

    if (field === "day") {
      setDateDay(value);
      return;
    }

    if (field === "month") {
      setDateMonth(value);
      return;
    }

    setDateYear(value);
  };

  const openDobDial = () => {
    const now = new Date();
    const defaultDob = new Date(
      Date.UTC(
        now.getUTCFullYear() - MIN_PROFILE_AGE_YEARS,
        now.getUTCMonth(),
        now.getUTCDate()
      )
    );

    if (!dateYear) {
      setDateYear(`${defaultDob.getUTCFullYear()}`);
    }
    if (!dateMonth) {
      setDateMonth(`${defaultDob.getUTCMonth() + 1}`);
    }
    if (!dateDay) {
      setDateDay(`${defaultDob.getUTCDate()}`);
    }
    setDobDialVisible(true);
  };

  const runPendingLeave = (afterLeaveAction?: () => void) => {
    const action = pendingLeaveAction;
    setInitialSnapshot(formSnapshot);
    setPendingLeaveAction(null);
    setConfirmLeaveVisible(false);
    action?.();
    afterLeaveAction?.();
  };

  const handleSave = async ({ showSuccessToast = true }: { showSuccessToast?: boolean } = {}) => {
    setLocalError(null);

    const dobIso = buildDobIso(dateDay, dateMonth, dateYear);
    if (dobIso === undefined) {
      setLocalError("Date of birth must include a valid day, month, and year.");
      return false;
    }
    if (dobIso !== null) {
      const dob = new Date(dobIso);
      if (!isAtLeastAge(dob, MIN_PROFILE_AGE_YEARS)) {
        setLocalError("Please choose an earlier birth year to continue.");
        return false;
      }
    }

    const normalizedNsTag = normalizeNsTag(displayName);
    if (!NS_TAG_PATTERN.test(normalizedNsTag) || normalizedNsTag.length > NS_TAG_MAX_LENGTH) {
      setLocalError("NS Tag can include letters, numbers, '_' or '-', and must be 2-12 characters.");
      return false;
    }

    const selectedDialCode = findCountryByCode(phoneCountryCode)?.dialCode ?? "+353";
    const phoneDigits = toPhoneDigits(phoneLocalNumber);
    const phoneLengthValidation = phoneDigits
      ? validatePhoneForCountry(phoneCountryCode, phoneDigits)
      : undefined;
    if (phoneLengthValidation) {
      setLocalError("Phone number does not match the selected country format.");
      return false;
    }

    const normalizedPhone = phoneDigits
      ? normalizePhoneNumber(selectedDialCode, phoneDigits)
      : null;

    try {
      await updateMutation.mutateAsync({
        primaryEmail: primaryEmail.trim(),
        fullName: fullName.trim(),
        displayName: normalizedNsTag,
        handle: normalizedNsTag,
        profileImageUrl: profileImageUri.trim() || null,
        profileSubtitle: profileBio.trim() || null,
        timezone,
        locale: profileQuery.data?.locale || deviceLocaleProfile.localeTag || "en-IE",
        preferredCurrency,
        onboardingStatus: profileQuery.data?.onboardingStatus ?? "completed",
        biometricUnlockEnabled: profileQuery.data?.biometricUnlockEnabled ?? false,
        twoFactorEnabled: profileQuery.data?.twoFactorEnabled ?? false,
        phoneNumber: normalizedPhone,
        dateOfBirth: dobIso,
        countryRegion: country || null,
        financialFocus,
        employmentStatus,
        incomeStability,
        primaryFinancialConcern: primaryConcern
      });

      setInitialSnapshot(formSnapshot);
      if (showSuccessToast) {
        showFlashMessage("Settings saved successfully.");
      }
      return true;
    } catch (error) {
      setLocalError(error instanceof Error ? error.message : "Failed to save profile.");
      return false;
    }
  };

  usePreventRemove(hasUnsavedChanges, (event) => {
    setConfirmLeaveVisible(true);
    setPendingLeaveAction(() => () => navigation.dispatch(event.data.action));
  });

  const handleBack = () => {
    if (!hasUnsavedChanges) {
      router.back();
      return;
    }

    setConfirmLeaveVisible(true);
    setPendingLeaveAction(() => () => router.back());
  };

  const handlePickAvatar = async () => {
    try {
      const permission = await ImagePicker.requestMediaLibraryPermissionsAsync();
      if (!permission.granted) {
        Alert.alert("Permission needed", "Allow gallery access to change your profile picture.");
        return;
      }

      const result = await ImagePicker.launchImageLibraryAsync({
        mediaTypes: ["images"],
        allowsEditing: true,
        aspect: [1, 1],
        quality: 0.8
      });

      if (result.canceled || !result.assets.length) {
        return;
      }

      const localUri = await persistAvatarLocally(result.assets[0].uri);
      setProfileImageUri(localUri);
    } catch (error) {
      setLocalError(error instanceof Error ? error.message : "Could not update profile picture.");
    }
  };

  const handleUseCurrentLocation = async () => {
    setLocalError(null);
    setIsResolvingLocation(true);

    try {
      const resolvedProfile = await resolveDeviceLocationProfile({ requestGps: true });
      const localeDefaults = deviceLocaleProfileRef.current;
      const metadata = resolveCountryMetadataByCode(resolvedProfile.countryCode)
        ?? resolveCountryMetadataByName(resolvedProfile.countryName);

      const fallbackCountryName = resolveCountryName(undefined, localeDefaults);
      const nextCountryName = metadata?.countryName ?? resolvedProfile.countryName ?? fallbackCountryName;
      const nextCountryCode = metadata?.countryCode ?? resolvedProfile.countryCode ?? null;
      const nextPhoneCountryCode = resolveSupportedCountryCode(nextCountryCode);

      const defaultCurrency = resolveCurrency(undefined, localeDefaults);
      const suggestedCurrency = (
        metadata?.currencyCode
        ?? resolvedProfile.currencyCode
        ?? defaultCurrency
      )?.toUpperCase() ?? defaultCurrency;
      const currencyLooksDefault =
        !preferredCurrency
        || preferredCurrency === defaultCurrency;
      const shouldReplaceCurrency =
        !hasManualCurrencyOverride
        && currencyLooksDefault;
      const nextCurrency = shouldReplaceCurrency ? suggestedCurrency : preferredCurrency;

      const defaultTimezone = resolveTimezone(undefined, localeDefaults);
      const timezoneLooksDefault =
        !timezone
        || timezone === defaultTimezone;
      const shouldReplaceTimezone =
        !hasManualTimezoneOverride
        && timezoneLooksDefault;
      const candidateTimezone = resolvedProfile.timezone ?? defaultTimezone;
      const nextTimezone = shouldReplaceTimezone ? candidateTimezone : timezone;

      setCountry(nextCountryName);
      setPhoneCountryCode(nextPhoneCountryCode);
      setPreferredCurrency(nextCurrency);
      setTimezone(nextTimezone);
      if (shouldReplaceCurrency) {
        setHasManualCurrencyOverride(false);
      }
      if (shouldReplaceTimezone) {
        setHasManualTimezoneOverride(false);
      }

      if (resolvedProfile.source === "gps") {
        showFlashMessage("Location applied from your current position.");
      } else {
        showFlashMessage("Location permission unavailable. Applied device locale fallback.", { tone: "info" });
      }
    } catch (error) {
      setLocalError(error instanceof Error ? error.message : "Could not read device location.");
    } finally {
      setIsResolvingLocation(false);
    }
  };

  const toggleFinancialFocus = (value: string) => {
    setFinancialFocus((current) => {
      if (current.includes(value)) {
        return current.filter((item) => item !== value);
      }

      if (current.length >= 6) {
        return current;
      }

      return [...current, value];
    });
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset scrollable={false}>
      <HeaderShell
        preset="secondaryDetail"
        title="Profile"
        leadingAction={
          <HeaderActionButton
            icon={<Ionicons name="arrow-back" size={20} color={palette.textPrimary} />}
            onPress={handleBack}
            accessibilityLabel="Go back"
          />
        }
      />

      {profileQuery.isError ? (
        <ErrorState
          title="Could not load profile"
          message={profileQuery.error.message}
          onRetry={() => {
            void profileQuery.refetch();
          }}
        />
      ) : (
        <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={styles.scrollContent}>
          <GlassCard style={styles.identityHero}>
            <Pressable
              onPress={() => void handlePickAvatar()}
              style={({ pressed }) => [styles.avatarCircle, pressed ? styles.avatarPressed : null]}
            >
              {profileImageUri ? (
                <Image source={{ uri: profileImageUri }} style={styles.avatarImage} />
              ) : (
                <Text style={styles.avatarText}>{initials}</Text>
              )}
              <View style={styles.avatarBadge}>
                <Ionicons name="add" size={12} color={palette.textPrimary} />
              </View>
            </Pressable>

            <View style={styles.identityTextWrap}>
              <Text style={styles.identityName}>{fullName || "Your profile"}</Text>
              <Text style={styles.identityDisplay}>{formatNsTag(displayName)}</Text>
              <Text style={styles.identityMeta}>{formatMemberSince(profileQuery.data?.createdUtc)}</Text>
            </View>
          </GlassCard>

          <GlassCard style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Identity</Text>
            <TextField
              label="Email"
              value={primaryEmail}
              onChangeText={setPrimaryEmail}
              autoCapitalize="none"
              keyboardType="email-address"
            />
            <TextField label="Full name" value={fullName} onChangeText={setFullName} />
            <TextField
              label="NS Tag"
              value={displayName}
              onChangeText={(nextValue) => {
                const normalized = normalizeNsTag(nextValue).slice(0, NS_TAG_MAX_LENGTH);
                setDisplayName(nextValue.trimStart().startsWith("@") ? formatNsTag(normalized) : normalized);
              }}
              autoCapitalize="none"
              autoCorrect={false}
              placeholder="@ns_tag"
              maxLength={NS_TAG_MAX_LENGTH + 1}
              onBlur={() => {
                setDisplayName(formatNsTag(displayName));
              }}
            />
            <TextField
              label="Profile bio"
              value={profileBio}
              onChangeText={setProfileBio}
              placeholder="This space is yours!"
            />

            <View style={styles.phoneRow}>
              <View style={styles.phoneCountryWrap}>
                <ModalSelectField
                  label="Code"
                  value={phoneCountryCode}
                  options={countryOptions}
                  onChange={setPhoneCountryCode}
                  placeholder="Country"
                />
              </View>
              <View style={styles.phoneInputWrap}>
                <TextField
                  label="Phone number (optional)"
                  value={phoneLocalNumber}
                  onChangeText={(nextValue) => {
                    setPhoneLocalNumber(formatPhoneInputByCountry(phoneCountryCode, nextValue));
                  }}
                  keyboardType="phone-pad"
                  placeholder={phonePlaceholder}
                />
              </View>
            </View>

            <Text style={styles.dateGroupLabel}>Date of birth</Text>
            <View style={styles.dateRow}>
              <Pressable
                style={({ pressed }) => [styles.dateFieldButton, pressed ? styles.dateFieldButtonPressed : null]}
                onPress={openDobDial}
              >
                <Text style={styles.dateFieldLabel}>Day</Text>
                <Text style={styles.dateFieldValue}>{dateDay || dobPlaceholder.day}</Text>
              </Pressable>
              <Pressable
                style={({ pressed }) => [styles.dateFieldButton, pressed ? styles.dateFieldButtonPressed : null]}
                onPress={openDobDial}
              >
                <Text style={styles.dateFieldLabel}>Month</Text>
                <Text style={styles.dateFieldValue}>{dateMonth ? selectedMonthLabel : dobPlaceholder.monthLabel}</Text>
              </Pressable>
              <Pressable
                style={({ pressed }) => [styles.dateFieldButton, pressed ? styles.dateFieldButtonPressed : null]}
                onPress={openDobDial}
              >
                <Text style={styles.dateFieldLabel}>Year</Text>
                <Text style={styles.dateFieldValue}>{dateYear || dobPlaceholder.year}</Text>
              </Pressable>
            </View>
            {dobAgeWarning ? <Text style={styles.dobWarningText}>{dobAgeWarning}</Text> : null}

            <ModalSelectField
              label="Country"
              value={country}
              options={countryNameOptions}
              onChange={setCountry}
              placeholder="Select country"
            />
            <ModalSelectField
              label="Timezone"
              value={timezone}
              options={timezoneOptions}
              onChange={(value) => {
                setHasManualTimezoneOverride(true);
                setTimezone(value);
              }}
              placeholder="Select timezone"
              sheetMaxHeightRatio={0.4}
            />
            <ModalSelectField
              label="Preferred currency"
              value={preferredCurrency}
              options={currencyOptions}
              onChange={(value) => {
                setHasManualCurrencyOverride(true);
                setPreferredCurrency(value);
              }}
              placeholder="Select currency"
            />
            <PrimaryButton
              label="Use current location"
              onPress={() => {
                void handleUseCurrentLocation();
              }}
              isLoading={isResolvingLocation}
            />
          </GlassCard>

          <GlassCard style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Financial focus</Text>
            <Text style={styles.sectionDescription}>
              Select what you want NSFinance to optimize first. You can pick up to 6 priorities.
            </Text>
            <View style={styles.focusWrap}>
              {financialFocusOptions.map((option) => {
                const selected = financialFocus.includes(option);
                return (
                  <Pressable
                    key={option}
                    onPress={() => toggleFinancialFocus(option)}
                    style={({ pressed }) => [
                      styles.focusChip,
                      selected ? styles.focusChipSelected : null,
                      pressed ? styles.focusChipPressed : null
                    ]}
                  >
                    <Text style={[styles.focusChipText, selected ? styles.focusChipTextSelected : null]}>
                      {option}
                    </Text>
                  </Pressable>
                );
              })}
            </View>
          </GlassCard>

          <GlassCard style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Financial profile (optional)</Text>
            <ModalSelectField
              label="Employment status"
              value={employmentStatus}
              options={employmentOptions}
              onChange={(value) => setEmploymentStatus(value)}
              placeholder="Select employment status"
            />
            <ModalSelectField
              label="Income stability"
              value={incomeStability}
              options={incomeStabilityOptions}
              onChange={(value) => setIncomeStability(value)}
              placeholder="Select income stability"
            />
            <ModalSelectField
              label="Primary concern"
              value={primaryConcern}
              options={concernOptions}
              onChange={(value) => setPrimaryConcern(value)}
              placeholder="Select primary concern"
            />
          </GlassCard>

          {localError ? <Text style={styles.errorText}>{localError}</Text> : null}

          <PrimaryButton
            label="Save profile"
            onPress={() => {
              void handleSave();
            }}
            isLoading={updateMutation.isPending}
            disabled={!fullName.trim() || !normalizeNsTag(displayName) || !primaryEmail.trim()}
          />
        </ScrollView>
      )}

      <Modal visible={confirmLeaveVisible} transparent animationType="fade" onRequestClose={() => setConfirmLeaveVisible(false)}>
        <Pressable style={styles.modalOverlay} onPress={() => setConfirmLeaveVisible(false)}>
          <Pressable style={styles.modalCard} onPress={() => undefined}>
            <Text style={styles.modalTitle}>You have unsaved changes</Text>
            <Text style={styles.modalBody}>Are you sure you want to leave?</Text>
            <PrimaryButton
              label="Save changes"
              onPress={() => {
                void (async () => {
                  const success = await handleSave({ showSuccessToast: false });
                  if (success) {
                    runPendingLeave(() => {
                      showFlashMessage("Settings saved successfully.");
                    });
                  }
                })();
              }}
              isLoading={updateMutation.isPending}
            />
            <Pressable
              style={({ pressed }) => [styles.discardButton, pressed ? styles.discardPressed : null]}
              onPress={() => runPendingLeave()}
            >
              <Text style={styles.discardText}>Discard changes</Text>
            </Pressable>
          </Pressable>
        </Pressable>
      </Modal>

      <Modal visible={dobDialVisible} transparent animationType="fade" onRequestClose={() => setDobDialVisible(false)}>
        <View style={styles.modalOverlay}>
          <Pressable style={styles.modalBackdrop} onPress={() => setDobDialVisible(false)} />
          <View style={styles.modalCard}>
            <Text style={styles.modalTitle}>Select date of birth</Text>
            <Text style={styles.modalBody}>Swipe each dial vertically to set day, month, and year.</Text>
            {dobAgeWarning ? <Text style={styles.dobWarningText}>{dobAgeWarning}</Text> : null}

            <View style={styles.dialRow}>
              <View style={styles.dialColumn}>
                <Text style={styles.dialLabel}>Day</Text>
                <View style={styles.dialWheelWrap}>
                  <ScrollView
                    ref={dayDialRef}
                    showsVerticalScrollIndicator={false}
                    snapToInterval={DIAL_ITEM_HEIGHT}
                    decelerationRate="fast"
                    bounces={false}
                    contentContainerStyle={styles.dialWheelContent}
                    onMomentumScrollEnd={(event) => {
                      applyDialValue("day", event.nativeEvent.contentOffset.y, dayDialValues);
                    }}
                  >
                    {dayDialValues.map((value) => (
                      <View key={value} style={styles.dialItem}>
                        <Text style={styles.dialItemText}>{value.padStart(2, "0")}</Text>
                      </View>
                    ))}
                  </ScrollView>
                  <View pointerEvents="none" style={styles.dialIndicator} />
                </View>
              </View>

              <View style={styles.dialColumn}>
                <Text style={styles.dialLabel}>Month</Text>
                <View style={styles.dialWheelWrap}>
                  <ScrollView
                    ref={monthDialRef}
                    showsVerticalScrollIndicator={false}
                    snapToInterval={DIAL_ITEM_HEIGHT}
                    decelerationRate="fast"
                    bounces={false}
                    contentContainerStyle={styles.dialWheelContent}
                    onMomentumScrollEnd={(event) => {
                      applyDialValue("month", event.nativeEvent.contentOffset.y, monthDialValues);
                    }}
                  >
                    {monthOptions.map((value) => (
                      <View key={value.value} style={styles.dialItem}>
                        <Text style={styles.dialItemText}>{value.label}</Text>
                      </View>
                    ))}
                  </ScrollView>
                  <View pointerEvents="none" style={styles.dialIndicator} />
                </View>
              </View>

              <View style={styles.dialColumn}>
                <Text style={styles.dialLabel}>Year</Text>
                <View style={styles.dialWheelWrap}>
                  <ScrollView
                    ref={yearDialRef}
                    showsVerticalScrollIndicator={false}
                    snapToInterval={DIAL_ITEM_HEIGHT}
                    decelerationRate="fast"
                    bounces={false}
                    contentContainerStyle={styles.dialWheelContent}
                    onMomentumScrollEnd={(event) => {
                      applyDialValue("year", event.nativeEvent.contentOffset.y, yearDialValues);
                    }}
                  >
                    {yearOptions.map((value) => (
                      <View key={value.value} style={styles.dialItem}>
                        <Text style={styles.dialItemText}>{value.label}</Text>
                      </View>
                    ))}
                  </ScrollView>
                  <View pointerEvents="none" style={styles.dialIndicator} />
                </View>
              </View>
            </View>

            <PrimaryButton label="Done" onPress={() => setDobDialVisible(false)} />
          </View>
        </View>
      </Modal>
    </ScreenContainer>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {
    paddingTop: 0
  },
  scrollContent: {
    gap: spacing[12],
    paddingTop: spacing[10],
    paddingBottom: spacing[12]
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: spacing[16]
  },
  headerTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  headerSpacer: {
    width: 42
  },
  identityHero: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12]
  },
  avatarCircle: {
    width: 62,
    height: 62,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.85)",
    backgroundColor: "rgba(242,140,40,0.22)",
    alignItems: "center",
    justifyContent: "center"
  },
  avatarPressed: {
    opacity: 0.9
  },
  avatarText: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  avatarImage: {
    width: "100%",
    height: "100%",
    borderRadius: 6
  },
  avatarBadge: {
    position: "absolute",
    right: 2,
    bottom: 2,
    width: 20,
    height: 20,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.85)",
    backgroundColor: "rgba(242,140,40,0.9)",
    alignItems: "center",
    justifyContent: "center"
  },
  identityTextWrap: {
    flex: 1,
    gap: 2
  },
  identityName: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  identityDisplay: {
    color: palette.primaryGlow,
    ...typography.caption
  },
  identityMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  sectionCard: {
    gap: spacing[12]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  sectionDescription: {
    color: palette.textSecondary,
    ...typography.caption
  },
  phoneRow: {
    flexDirection: "row",
    gap: spacing[8]
  },
  phoneCountryWrap: {
    width: "42%"
  },
  phoneInputWrap: {
    flex: 1
  },
  dateRow: {
    flexDirection: "row",
    gap: spacing[8]
  },
  dateGroupLabel: {
    color: palette.textSecondary,
    ...typography.fieldLabel
  },
  dateFieldButton: {
    flex: 1
      ,
    minHeight: 52,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8],
    justifyContent: "space-between"
  },
  dateFieldButtonPressed: {
    opacity: 0.9
  },
  dateFieldLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  dateFieldValue: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  focusWrap: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  focusChip: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    minHeight: 34,
    alignItems: "center",
    justifyContent: "center"
  },
  focusChipSelected: {
    borderColor: palette.primaryGlow,
    backgroundColor: "rgba(242,140,40,0.24)"
  },
  focusChipPressed: {
    opacity: 0.88
  },
  focusChipText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  focusChipTextSelected: {
    color: palette.textPrimary,
    fontWeight: "600"
  },
  errorText: {
    color: palette.negative,
    ...typography.caption
  },
  dobWarningText: {
    color: palette.caution,
    ...typography.caption
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: palette.overlay,
    justifyContent: "center",
    alignItems: "center",
    paddingHorizontal: spacing[16]
  },
  modalBackdrop: {
    ...StyleSheet.absoluteFillObject
  },
  modalCard: {
    width: "100%",
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.sheet,
    padding: spacing[16],
    gap: spacing[12]
  },
  modalTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  modalBody: {
    color: palette.textSecondary,
    ...typography.body2
  },
  dialRow: {
    flexDirection: "row",
    gap: spacing[8]
  },
  dialColumn: {
    flex: 1,
    gap: spacing[8]
  },
  dialLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  dialWheelWrap: {
    height: DIAL_ITEM_HEIGHT * 4,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    overflow: "hidden"
  },
  dialWheelContent: {
    paddingVertical: DIAL_ITEM_HEIGHT * 1.5
  },
  dialItem: {
    height: DIAL_ITEM_HEIGHT,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[8]
  },
  dialItemText: {
    color: palette.textPrimary,
    ...typography.body2
  },
  dialIndicator: {
    position: "absolute",
    left: 0,
    right: 0,
    top: DIAL_ITEM_HEIGHT * 1.5,
    height: DIAL_ITEM_HEIGHT,
    borderTopWidth: 1,
    borderBottomWidth: 1,
    borderColor: "rgba(242,140,40,0.55)",
    backgroundColor: "rgba(242,140,40,0.1)"
  },
  discardButton: {
    minHeight: 42,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: "rgba(244,104,119,0.52)",
    backgroundColor: "rgba(90,16,30,0.26)",
    alignItems: "center",
    justifyContent: "center"
  },
  discardPressed: {
    opacity: 0.9
  },
  discardText: {
    color: palette.negative,
    ...typography.body2,
    fontWeight: "600"
  }
}));



