import * as FileSystem from "expo-file-system/legacy";

const STORAGE_DIRECTORY =
  `${FileSystem.documentDirectory ?? FileSystem.cacheDirectory}nsfinance-storage/`;

function getStoragePath(key: string) {
  const normalizedKey = key.replace(/[^a-zA-Z0-9._-]/g, "_");
  return `${STORAGE_DIRECTORY}${normalizedKey}.json`;
}

async function ensureStorageDirectory() {
  const info = await FileSystem.getInfoAsync(STORAGE_DIRECTORY);
  if (!info.exists) {
    await FileSystem.makeDirectoryAsync(STORAGE_DIRECTORY, { intermediates: true });
  }
}

export async function readJsonFileStorage<T>(key: string): Promise<T | null> {
  const path = getStoragePath(key);
  const info = await FileSystem.getInfoAsync(path);
  if (!info.exists) {
    return null;
  }

  const raw = await FileSystem.readAsStringAsync(path);
  return JSON.parse(raw) as T;
}

export async function writeJsonFileStorage(key: string, value: unknown): Promise<void> {
  await ensureStorageDirectory();
  await FileSystem.writeAsStringAsync(getStoragePath(key), JSON.stringify(value));
}

export async function deleteJsonFileStorage(key: string): Promise<void> {
  const path = getStoragePath(key);
  const info = await FileSystem.getInfoAsync(path);
  if (!info.exists) {
    return;
  }

  await FileSystem.deleteAsync(path, { idempotent: true });
}
