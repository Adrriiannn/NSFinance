import * as SecureStore from "expo-secure-store";
import * as FileSystem from "expo-file-system/legacy";
import {
  deleteJsonFileStorage,
  readJsonFileStorage
} from "./jsonFileStore";

export const LEGACY_GLOBAL_STORAGE_KEYS = [
  "nsfinance.react-query.cache.v1",
  "nsfinance.planner.state",
  "nsfinance.planner.companion.chat_history",
  "nsfinance.expense_plans.v1",
  "nsfinance.expense_plan_builder.v1",
  "nsfinance.expense_plan_community.v1"
] as const;

const LEGACY_COMPANION_ARTIFACT_PREFIXES = [
  "nsfinance.companion.chat.summary.",
  "nsfinance.companion.chat.memory.",
  "nsfinance.companion.chat.retrieval.",
  "nsfinance.companion.chat.index."
] as const;

type LegacyChatReference = {
  id?: unknown;
};

function readLegacyChatIds(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value
    .map((candidate) => (candidate as LegacyChatReference)?.id)
    .filter((id): id is string => typeof id === "string" && id.trim().length > 0);
}

async function collectLegacyCompanionChatIds(): Promise<string[]> {
  const chatIds = new Set<string>();

  try {
    const fileValue = await readJsonFileStorage<unknown>(
      "nsfinance.planner.companion.chat_history"
    );
    readLegacyChatIds(fileValue).forEach((id) => chatIds.add(id));
  } catch {
    // Corrupt legacy data is still deleted below.
  }

  try {
    const secureValue = await SecureStore.getItemAsync(
      "nsfinance.planner.companion.chat_history"
    );
    if (secureValue) {
      readLegacyChatIds(JSON.parse(secureValue)).forEach((id) => chatIds.add(id));
    }
  } catch {
    // Corrupt legacy data is still deleted below.
  }

  return [...chatIds];
}

async function deleteLegacyProfileDirectory(): Promise<void> {
  if (!FileSystem.documentDirectory) {
    return;
  }

  await FileSystem.deleteAsync(`${FileSystem.documentDirectory}profile`, { idempotent: true });
}

async function deleteDisposableExportCache(): Promise<void> {
  if (!FileSystem.cacheDirectory) {
    return;
  }

  const entries = await FileSystem.readDirectoryAsync(FileSystem.cacheDirectory);
  await Promise.all(
    entries
      .filter((entry) => entry.startsWith("nsfinance-export-"))
      .map((entry) =>
        FileSystem.deleteAsync(`${FileSystem.cacheDirectory}${entry}`, { idempotent: true })
      )
  );
}

export async function deleteAmbiguousLegacyMobileStorage(): Promise<void> {
  const legacyChatIds = await collectLegacyCompanionChatIds();

  await Promise.allSettled([
    ...LEGACY_GLOBAL_STORAGE_KEYS.map((key) => deleteJsonFileStorage(key)),
    ...LEGACY_GLOBAL_STORAGE_KEYS.map((key) => SecureStore.deleteItemAsync(key)),
    deleteLegacyProfileDirectory(),
    deleteDisposableExportCache(),
    ...legacyChatIds.flatMap((chatId) =>
      LEGACY_COMPANION_ARTIFACT_PREFIXES.map((prefix) =>
        SecureStore.deleteItemAsync(`${prefix}${chatId}`)
      )
    )
  ]);
}
