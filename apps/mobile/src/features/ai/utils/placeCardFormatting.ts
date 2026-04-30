import type { ShareContent } from "react-native";
import type { CompanionPlaceCardResult } from "../../../types/api";

export type CompanionPlaceCard = CompanionPlaceCardResult;

export type PriceSymbols = {
  activeCount: 1 | 2 | 3;
  totalCount: 3;
};

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

function interpolateColor(start: [number, number, number], end: [number, number, number], t: number): string {
  const r = Math.round(start[0] + (end[0] - start[0]) * t);
  const g = Math.round(start[1] + (end[1] - start[1]) * t);
  const b = Math.round(start[2] + (end[2] - start[2]) * t);
  return `rgb(${r}, ${g}, ${b})`;
}

export function formatDistanceKm(distanceMeters?: number | null): string | null {
  if (typeof distanceMeters !== "number" || !Number.isFinite(distanceMeters) || distanceMeters < 0) {
    return null;
  }

  const km = distanceMeters / 1000;
  const value = km >= 10 ? `${Math.round(km)}km` : `${km.toFixed(1)}km`;
  return `${value} away`;
}

export function getDistanceColor(distanceMeters?: number | null): string {
  if (typeof distanceMeters !== "number" || !Number.isFinite(distanceMeters)) {
    return "#7C8794";
  }

  const km = distanceMeters / 1000;
  const t = clamp((km - 0.2) / (25 - 0.2), 0, 1);
  return interpolateColor([242, 140, 40], [221, 68, 68], t);
}

export function formatRating(rating?: number | null): string | null {
  if (typeof rating !== "number" || !Number.isFinite(rating) || rating <= 0) {
    return null;
  }

  const rounded = Math.round(rating * 10) / 10;
  return Number.isInteger(rounded) ? `${rounded}/5` : `${rounded.toFixed(1)}/5`;
}

export function getRatingColor(rating?: number | null, highColor = "#F28C28"): string {
  if (typeof rating !== "number" || !Number.isFinite(rating)) {
    return "#7C8794";
  }

  const t = clamp((rating - 1) / 4, 0, 1);
  return interpolateColor([255, 45, 45], hexToRgb(highColor), t);
}

export function getCategoryColor(category?: string | null, seed?: string | null): string {
  const source = `${category ?? ""} ${seed ?? ""}`.trim().toLowerCase();
  if (!source) {
    return "#D58A3A";
  }

  let hash = 0;
  for (let index = 0; index < source.length; index += 1) {
    hash = (hash * 31 + source.charCodeAt(index)) >>> 0;
  }

  const hue = 18 + (hash % 34);
  const saturation = 62 + (hash % 18);
  const lightness = 48 + (hash % 12);
  return `hsl(${hue}, ${saturation}%, ${lightness}%)`;
}

export function formatPriceLevel(priceLevel?: CompanionPlaceCard["priceLevel"]): PriceSymbols | null {
  if (priceLevel === null || typeof priceLevel === "undefined") {
    return null;
  }

  if (typeof priceLevel === "number") {
    if (priceLevel <= 0) {
      return null;
    }

    return {
      activeCount: priceLevel <= 1 ? 1 : priceLevel === 2 ? 2 : 3,
      totalCount: 3
    };
  }

  const normalized = priceLevel.trim().toLowerCase();
  if (!normalized) {
    return null;
  }

  if (normalized.includes("inexpensive") || normalized.includes("cheap") || normalized === "1") {
    return { activeCount: 1, totalCount: 3 };
  }

  if (normalized.includes("moderate") || normalized === "2") {
    return { activeCount: 2, totalCount: 3 };
  }

  if (normalized.includes("expensive") || normalized.includes("premium") || normalized === "3" || normalized === "4") {
    return { activeCount: 3, totalCount: 3 };
  }

  return null;
}

export function formatDuration(minutes?: number | null): string | null {
  if (typeof minutes !== "number" || !Number.isFinite(minutes) || minutes <= 0) {
    return null;
  }

  const rounded = Math.max(1, Math.round(minutes));
  if (rounded < 60) {
    return `${rounded}m`;
  }

  const hours = Math.floor(rounded / 60);
  const mins = rounded % 60;
  return mins === 0 ? `${hours}h` : `${hours}h ${mins}m`;
}

export function formatWebsiteDisplay(url?: string | null): string | null {
  const parsed = parseUrl(url);
  if (!parsed) {
    return null;
  }

  const host = parsed.hostname.replace(/^www\./i, "");
  const path = parsed.pathname && parsed.pathname !== "/" ? parsed.pathname.replace(/\/$/, "") : "";
  const display = `${host}${path}`;
  return display.length > 28 ? `${display.slice(0, 25)}...` : display;
}

export function formatPhoneDisplay(phone?: string | null): string | null {
  if (!phone?.trim()) {
    return null;
  }

  const trimmed = phone.trim();
  const digits = trimmed.replace(/[^\d+]/g, "");
  const localIrish = digits.startsWith("+353") ? `0${digits.slice(4)}` : digits;
  if (/^01\d{7}$/.test(localIrish)) {
    return `(01) ${localIrish.slice(2, 5)} ${localIrish.slice(5)}`;
  }

  if (/^08\d{8}$/.test(localIrish)) {
    return `${localIrish.slice(0, 3)} ${localIrish.slice(3, 6)} ${localIrish.slice(6)}`;
  }

  return trimmed;
}

export function normalizePhoneForTel(phone?: string | null): string | null {
  if (!phone?.trim()) {
    return null;
  }

  const normalized = phone.replace(/[^\d+]/g, "");
  return normalized.length > 0 ? normalized : null;
}

export function buildDirectionsUrl(place: CompanionPlaceCard): string | null {
  if (place.googleMapsUri?.trim()) {
    return place.googleMapsUri.trim();
  }

  if (typeof place.latitude === "number" && typeof place.longitude === "number") {
    return `https://www.google.com/maps/dir/?api=1&destination=${place.latitude},${place.longitude}`;
  }

  const destination = place.formattedAddress?.trim() || place.shortFormattedAddress?.trim() || place.name?.trim();
  return destination ? `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(destination)}` : null;
}

export function buildSharePayload(place: CompanionPlaceCard): ShareContent {
  const address = place.formattedAddress || place.shortFormattedAddress;
  const link = place.googleMapsUri || place.websiteUrl || buildDirectionsUrl(place);
  const parts = [place.name, address, link].filter(Boolean);
  return {
    title: place.name,
    message: parts.join("\n")
  };
}

export function ensureLinkingUrl(url?: string | null): string | null {
  if (!url?.trim()) {
    return null;
  }

  const trimmed = url.trim();
  return /^[a-z][a-z\d+\-.]*:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`;
}

export function humanizeCategory(value?: string | null): string | null {
  if (!value?.trim()) {
    return null;
  }

  const spaced = value.trim().replace(/_/g, " ").replace(/\s+/g, " ");
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

function parseUrl(url?: string | null): URL | null {
  const candidate = ensureLinkingUrl(url);
  if (!candidate) {
    return null;
  }

  try {
    return new URL(candidate);
  } catch {
    return null;
  }
}

function hexToRgb(hex: string): [number, number, number] {
  const normalized = hex.replace("#", "").trim();
  if (!/^[\da-f]{6}$/i.test(normalized)) {
    return [242, 140, 40];
  }

  return [
    Number.parseInt(normalized.slice(0, 2), 16),
    Number.parseInt(normalized.slice(2, 4), 16),
    Number.parseInt(normalized.slice(4, 6), 16)
  ];
}
