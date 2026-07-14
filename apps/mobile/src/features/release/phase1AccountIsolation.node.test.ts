import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  buildAccountStorageKey,
  isSameAccountStorageScope,
  normalizeAccountStorageScope
} from "../../lib/storage/accountScope";
import {
  getUtf8ByteLength,
  splitSecureStoreValue
} from "../../lib/storage/secureStoreChunking";

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");

function readMobileSource(...segments: string[]) {
  return readFileSync(join(mobileRoot, ...segments), "utf8");
}

test("account storage keys are deterministic and isolate synthetic users", () => {
  const accountAKey = buildAccountStorageKey("nsfinance.test.account.v1", "Account-A");
  const accountBKey = buildAccountStorageKey("nsfinance.test.account.v1", "Account-B");

  assert.equal(
    accountAKey,
    `nsfinance.test.account.v1.${normalizeAccountStorageScope("Account-A")}`
  );
  assert.equal(
    accountBKey,
    `nsfinance.test.account.v1.${normalizeAccountStorageScope("Account-B")}`
  );
  assert.notEqual(accountAKey, accountBKey);
  assert.equal(isSameAccountStorageScope("Account-A", " account-a "), true);
  assert.equal(isSameAccountStorageScope("Account-A", "Account-B"), false);
  assert.throws(() => normalizeAccountStorageScope("   "), /authenticated user ID/i);
});

test("unsafe account identifiers remain collision-free SecureStore keys", () => {
  const encodedEmailLikeId = normalizeAccountStorageScope("qa@example.test");
  const literalLookalike = normalizeAccountStorageScope(encodedEmailLikeId);

  assert.match(encodedEmailLikeId, /^[a-f0-9]+$/);
  assert.notEqual(encodedEmailLikeId, literalLookalike);
});

test("encrypted JSON chunking preserves Unicode and respects the byte ceiling", () => {
  const source = `${"a".repeat(13)}Euro € Gaeilge sláinte 😀 ${"z".repeat(29)}`;
  const chunks = splitSecureStoreValue(source, 20);

  assert.equal(chunks.join(""), source);
  assert.ok(chunks.length > 1);
  chunks.forEach((chunk) => assert.ok(getUtf8ByteLength(chunk) <= 20));
});

test("React Query is memory-only and authentication owns transition cleanup", () => {
  const queryProvider = readMobileSource("src", "providers", "QueryProvider.tsx");
  const authProvider = readMobileSource("src", "providers", "AuthProvider.tsx");

  assert.doesNotMatch(queryProvider, /\bdehydrate\b|\bhydrate\b|writeJsonFileStorage/);
  assert.match(queryProvider, /clearAccountQueryState/);
  assert.match(queryProvider, /queryClient\.clear\(\)/);
  assert.match(authProvider, /await clearAccountQueryState\(\);[\s\S]*?setSession\(null\)/);
  assert.match(authProvider, /isSameAccountStorageScope\(previousUserId, response\.user\.id\)/);
});

test("planner and Companion projections are encrypted and account-scoped", () => {
  const plannerProvider = readMobileSource("src", "providers", "PlannerProvider.tsx");
  const chatHistory = readMobileSource("src", "features", "planner", "chatHistory.ts");
  const companionScreen = readMobileSource("src", "screens", "CompanionScreen.tsx");

  assert.match(plannerProvider, /nsfinance\.planner\.state\.account\.v1/);
  assert.match(plannerProvider, /readSecureJson|writeSecureJson/);
  assert.doesNotMatch(plannerProvider, /readJsonFileStorage|writeJsonFileStorage|nsfinance\.planner\.state"/);
  assert.match(chatHistory, /nsfinance\.companion\.presentation\.account\.v1/);
  assert.match(chatHistory, /buildAccountStorageKey\(CHAT_PRESENTATION_NAMESPACE, userId\)/);
  assert.match(chatHistory, /getAIChatThreads|loadCompanionChatMessages/);
  assert.doesNotMatch(chatHistory, /readJsonFileStorage|writeJsonFileStorage|companionChatsCache/);
  const presentationProjection = chatHistory.match(
    /current\[chat\.conversationThreadId\] = \{([\s\S]*?)\n    \};/
  )?.[1] ?? "";
  assert.doesNotMatch(presentationProjection, /messages/);
  assert.match(companionScreen, /loadedHistoryUserIdRef\.current !== userId/);
  assert.match(companionScreen, /archiveAIChatThread/);
});

test("legacy global data is deletion-only and guest account stores are forbidden", () => {
  const lifecycle = readMobileSource("src", "lib", "storage", "mobileStorageLifecycle.ts");
  const enrichment = readMobileSource("src", "features", "banking", "enrichmentDial.storage.ts");
  const assistantDock = readMobileSource("src", "layout", "adaptive", "assistantDock.storage.ts");

  for (const key of [
    "nsfinance.react-query.cache.v1",
    "nsfinance.planner.state",
    "nsfinance.planner.companion.chat_history",
    "nsfinance.expense_plans.v1",
    "nsfinance.expense_plan_builder.v1",
    "nsfinance.expense_plan_community.v1"
  ]) {
    assert.match(lifecycle, new RegExp(key.replaceAll(".", "\\.")));
  }

  assert.match(lifecycle, /deleteJsonFileStorage/);
  assert.match(lifecycle, /SecureStore\.deleteItemAsync/);
  assert.doesNotMatch(enrichment, /\?\? "guest"/);
  assert.doesNotMatch(assistantDock, /\?\? "guest"/);
  assert.match(enrichment, /if \(!userId\) \{\s*return/);
  assert.match(assistantDock, /if \(!userId\) \{\s*return/);
});
