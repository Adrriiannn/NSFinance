const DEFAULT_SECURE_STORE_CHUNK_MAX_BYTES = 1_500;

export function getUtf8ByteLength(value: string): number {
  let length = 0;
  for (const character of value) {
    const codePoint = character.codePointAt(0) ?? 0;
    if (codePoint <= 0x7f) length += 1;
    else if (codePoint <= 0x7ff) length += 2;
    else if (codePoint <= 0xffff) length += 3;
    else length += 4;
  }
  return length;
}

export function splitSecureStoreValue(
  value: string,
  maxBytes = DEFAULT_SECURE_STORE_CHUNK_MAX_BYTES
): string[] {
  if (!Number.isInteger(maxBytes) || maxBytes < 4) {
    throw new Error("SecureStore chunk size must be an integer of at least four bytes.");
  }

  const chunks: string[] = [];
  let current = "";
  let currentBytes = 0;

  for (const character of value) {
    const characterBytes = getUtf8ByteLength(character);
    if (current && currentBytes + characterBytes > maxBytes) {
      chunks.push(current);
      current = "";
      currentBytes = 0;
    }

    current += character;
    currentBytes += characterBytes;
  }

  if (current || chunks.length === 0) {
    chunks.push(current);
  }

  return chunks;
}
