import * as Device from "expo-device";
import { Platform } from "react-native";
import type { DeviceContextDto } from "../../types/api";

const GENERIC_DEVICE_LABEL_PATTERNS = [
  /^android$/i,
  /^android device$/i,
  /^iphone$/i,
  /^ipad$/i,
  /^ios device$/i,
  /^phone$/i,
  /^tablet$/i,
  /^unknown device$/i
];

function normalizeValue(value?: string | null) {
  const nextValue = value?.trim();
  return nextValue && nextValue.length > 0 ? nextValue : null;
}

function isGenericDeviceLabel(value: string) {
  return GENERIC_DEVICE_LABEL_PATTERNS.some((pattern) => pattern.test(value));
}

function buildManufacturerModelLabel() {
  const manufacturer = normalizeValue(Device.manufacturer);
  const modelName = normalizeValue(Device.modelName);

  if (manufacturer && modelName) {
    if (modelName.toLowerCase().startsWith(manufacturer.toLowerCase())) {
      return modelName;
    }

    return `${manufacturer} ${modelName}`;
  }

  return modelName ?? manufacturer;
}

function resolveDeviceLabel() {
  const explicitName = normalizeValue(Device.deviceName);
  if (explicitName && !isGenericDeviceLabel(explicitName)) {
    return explicitName;
  }

  const manufacturerModel = buildManufacturerModelLabel();
  if (manufacturerModel) {
    return manufacturerModel;
  }

  if (Platform.OS === "android") {
    return "Android phone";
  }

  if (Platform.OS === "ios") {
    return "iPhone";
  }

  return "Unknown device";
}

export function buildDeviceContext(): DeviceContextDto {
  return {
    deviceLabel: resolveDeviceLabel(),
    platform: Platform.OS,
    osVersion: normalizeValue(Device.osVersion)
  };
}

