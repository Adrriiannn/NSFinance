import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import test from "node:test";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { normalizeSystemInset } from "../../theme/systemInsets";

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");

function readMobileSource(...segments: string[]) {
  return readFileSync(join(mobileRoot, ...segments), "utf8");
}

function collectCodeFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      return collectCodeFiles(path);
    }

    return /\.(?:ts|tsx|js|jsx)$/.test(entry.name) ? [path] : [];
  });
}

test("system inset normalization preserves gesture and three-button navigation", () => {
  assert.equal(normalizeSystemInset(0), 0);
  assert.equal(normalizeSystemInset(16), 16);
  assert.equal(normalizeSystemInset(48), 48);
  assert.equal(normalizeSystemInset(-12), 0);
  assert.equal(normalizeSystemInset(Number.NaN), 0);
  assert.equal(normalizeSystemInset(Number.POSITIVE_INFINITY), 0);
});

test("the shared modal viewport owns every safe-area edge", () => {
  const systemModal = readMobileSource(
    "src",
    "components",
    "ui",
    "surfaces",
    "SystemModal.tsx"
  );
  const companion = readMobileSource("src", "screens", "CompanionScreen.tsx");

  assert.match(systemModal, /useSafeAreaInsets/);
  assert.match(systemModal, /paddingTop:[\s\S]*?insets\.top/);
  assert.match(systemModal, /paddingRight:[\s\S]*?insets\.right/);
  assert.match(systemModal, /paddingBottom:[\s\S]*?insets\.bottom/);
  assert.match(systemModal, /paddingLeft:[\s\S]*?insets\.left/);
  assert.match(
    systemModal,
    /backgroundColor:\s*safeAreaBackgroundColor\s*\?\?\s*["']transparent["']/
  );
  assert.match(companion, /<SystemModal visible=\{historyVisible\}/);
  assert.match(companion, /<SystemModal[\s\S]*?visible=\{editChatVisible\}/);
});

test("raw React Native Modal imports are centralized", () => {
  const sourceRoots = [join(mobileRoot, "app"), join(mobileRoot, "src")];
  const primitivePath = join(
    mobileRoot,
    "src",
    "components",
    "ui",
    "surfaces",
    "SystemModal.tsx"
  );
  const rawModalImport = /import\s*\{[^}]*\bModal(?:\s+as\s+\w+)?\b[^}]*\}\s*from\s*["']react-native["']/s;
  const offenders = sourceRoots
    .flatMap(collectCodeFiles)
    .filter((path) => path !== primitivePath)
    .filter((path) => rawModalImport.test(readFileSync(path, "utf8")))
    .map((path) => relative(mobileRoot, path));

  assert.deepEqual(offenders, []);
});

test("fixed navigation and feedback surfaces consume runtime insets", () => {
  const floatingNav = readMobileSource(
    "src",
    "components",
    "layout",
    "FloatingBottomNav.tsx"
  );
  const flashToast = readMobileSource(
    "src",
    "components",
    "feedback",
    "GlobalFlashToast.tsx"
  );
  const progressDial = readMobileSource(
    "src",
    "components",
    "feedback",
    "GlobalEnrichmentProgressDial.tsx"
  );
  const screenContainer = readMobileSource(
    "src",
    "components",
    "ui",
    "ScreenContainer.tsx"
  );

  assert.match(floatingNav, /getEffectiveBottomSystemInset\(insets\.bottom\)/);
  assert.match(flashToast, /insets\.top/);
  assert.match(progressDial, /screenHeight - insets\.bottom/);
  assert.match(screenContainer, /useRuntimeBottomInsetPolicy/);
});
