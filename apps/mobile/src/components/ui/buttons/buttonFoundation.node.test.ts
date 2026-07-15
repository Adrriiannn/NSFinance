import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { sizing } from "../../../theme/tokens/sizing";
import { BUTTON_STATE_PRECEDENCE, resolveButtonVisualState } from "./button.states";

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../../..");

function readMobileSource(...segments: string[]) {
  return readFileSync(join(mobileRoot, ...segments), "utf8");
}

test("button state resolution follows the documented precedence", () => {
  assert.deepEqual(BUTTON_STATE_PRECEDENCE, ["loading", "disabled", "active", "idle"]);
  assert.equal(
    resolveButtonVisualState({
      isLoading: true,
      isDisabled: true,
      isFocused: true,
      isPressed: true
    }),
    "loading"
  );
  assert.equal(
    resolveButtonVisualState({ isDisabled: true, isFocused: true, isPressed: true }),
    "disabled"
  );
  assert.equal(resolveButtonVisualState({ isFocused: true }), "active");
  assert.equal(resolveButtonVisualState({ isPressed: true }), "active");
  assert.equal(resolveButtonVisualState({}), "idle");
});

test("canonical Button uses semantic state colors and accessible sizing", () => {
  const buttonSource = readMobileSource("src", "components", "ui", "buttons", "Button.tsx");
  const presetSource = readMobileSource(
    "src",
    "components",
    "ui",
    "buttons",
    "button.presets.ts"
  );

  assert.ok(sizing.touchTarget.minimum >= 48);
  assert.ok((presetSource.match(/sizing\.touchTarget\.minimum/g) ?? []).length >= 4);
  assert.match(presetSource, /paddingVertical:\s*spacing\[8\]/);
  assert.doesNotMatch(
    presetSource,
    /#[0-9A-F]{3,8}\b|rgba?\(|["']transparent["']/i
  );

  for (const variant of [
    "primary",
    "secondary",
    "ghost",
    "destructive",
    "icon",
    "compact",
    "pillAction"
  ]) {
    assert.match(presetSource, new RegExp(`controls\\.button\\.${variant}`));
  }

  assert.match(buttonSource, /accessibilityRole="button"/);
  assert.match(buttonSource, /busy: isLoading \|\| accessibilityState\?\.busy/);
  assert.match(buttonSource, /disabled: isDisabled \|\| accessibilityState\?\.disabled/);
  assert.match(buttonSource, /allowFontScaling/);
  assert.match(buttonSource, /maxFontSizeMultiplier=\{2\}/);
  assert.match(buttonSource, /numberOfLines=\{2\}/);
  assert.match(buttonSource, /buttonStateStyles\.focused/);
  assert.doesNotMatch(buttonSource, /adjustsFontSizeToFit/);
  assert.equal(sizing.touchTarget.minimum, 48);
});

test("IconButton requires an accessible name and the root adapter delegates", () => {
  const canonicalSource = readMobileSource(
    "src",
    "components",
    "ui",
    "buttons",
    "IconButton.tsx"
  );
  const adapterSource = readMobileSource("src", "components", "ui", "IconButton.tsx");
  const companionSource = readMobileSource("src", "screens", "CompanionScreen.tsx");

  assert.match(canonicalSource, /accessibilityLabel:\s*string;/);
  assert.doesNotMatch(canonicalSource, /accessibilityLabel\?:\s*string/);
  assert.match(canonicalSource, /<Button[\s\S]*accessibilityLabel=\{accessibilityLabel\}/);

  assert.match(
    adapterSource,
    /export \{ IconButton, type IconButtonProps \} from "\.\/buttons\/IconButton";/
  );
  assert.doesNotMatch(adapterSource, /Pressable|BaseIconButton/);
  assert.match(
    companionSource,
    /<IconButton[\s\S]{0,200}accessibilityLabel="Close chat history"/
  );
});
