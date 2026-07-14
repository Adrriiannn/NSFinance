import * as SecureStore from "expo-secure-store";
import { splitSecureStoreValue } from "./secureStoreChunking";

const MANIFEST_VERSION = 1;
const mutationQueues = new Map<string, Promise<void>>();

type SecureJsonManifest = {
  version: typeof MANIFEST_VERSION;
  generation: string;
  chunkCount: number;
};

function getManifestKey(baseKey: string) {
  return `${baseKey}.manifest`;
}

function getChunkKey(baseKey: string, generation: string, index: number) {
  return `${baseKey}.generation.${generation}.chunk.${index}`;
}

function parseManifest(raw: string | null): SecureJsonManifest | null {
  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as Partial<SecureJsonManifest>;
    if (
      parsed.version !== MANIFEST_VERSION
      || typeof parsed.generation !== "string"
      || !/^[a-z0-9]+$/.test(parsed.generation)
      || !Number.isInteger(parsed.chunkCount)
      || (parsed.chunkCount ?? 0) < 1
    ) {
      return null;
    }

    return parsed as SecureJsonManifest;
  } catch {
    return null;
  }
}

async function deleteManifestChunks(baseKey: string, manifest: SecureJsonManifest | null) {
  if (!manifest) {
    return;
  }

  await Promise.all(
    Array.from({ length: manifest.chunkCount }, (_, index) =>
      SecureStore.deleteItemAsync(getChunkKey(baseKey, manifest.generation, index))
    )
  );
}

export async function readSecureJson<T>(baseKey: string): Promise<T | null> {
  await mutationQueues.get(baseKey)?.catch(() => undefined);
  const manifest = parseManifest(await SecureStore.getItemAsync(getManifestKey(baseKey)));
  if (!manifest) {
    return null;
  }

  const chunks = await Promise.all(
    Array.from({ length: manifest.chunkCount }, (_, index) =>
      SecureStore.getItemAsync(getChunkKey(baseKey, manifest.generation, index))
    )
  );

  if (chunks.some((chunk) => chunk === null)) {
    return null;
  }

  try {
    return JSON.parse(chunks.join("")) as T;
  } catch {
    return null;
  }
}

async function writeSecureJsonImmediately(baseKey: string, value: unknown): Promise<void> {
  const manifestKey = getManifestKey(baseKey);
  const previousManifest = parseManifest(await SecureStore.getItemAsync(manifestKey));
  const generation = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 10)}`;
  const chunks = splitSecureStoreValue(JSON.stringify(value));

  for (let index = 0; index < chunks.length; index += 1) {
    await SecureStore.setItemAsync(getChunkKey(baseKey, generation, index), chunks[index]);
  }

  const nextManifest: SecureJsonManifest = {
    version: MANIFEST_VERSION,
    generation,
    chunkCount: chunks.length
  };
  await SecureStore.setItemAsync(manifestKey, JSON.stringify(nextManifest));
  await deleteManifestChunks(baseKey, previousManifest);
}

async function deleteSecureJsonImmediately(baseKey: string): Promise<void> {
  const manifestKey = getManifestKey(baseKey);
  const manifest = parseManifest(await SecureStore.getItemAsync(manifestKey));
  await SecureStore.deleteItemAsync(manifestKey);
  await deleteManifestChunks(baseKey, manifest);
}

function enqueueMutation(baseKey: string, mutation: () => Promise<void>): Promise<void> {
  const previous = mutationQueues.get(baseKey) ?? Promise.resolve();
  const next = previous.catch(() => undefined).then(mutation);
  mutationQueues.set(baseKey, next);

  return next.finally(() => {
    if (mutationQueues.get(baseKey) === next) {
      mutationQueues.delete(baseKey);
    }
  });
}

export function writeSecureJson(baseKey: string, value: unknown): Promise<void> {
  return enqueueMutation(baseKey, () => writeSecureJsonImmediately(baseKey, value));
}

export function deleteSecureJson(baseKey: string): Promise<void> {
  return enqueueMutation(baseKey, () => deleteSecureJsonImmediately(baseKey));
}
