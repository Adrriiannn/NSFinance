import assert from "node:assert/strict";
import test from "node:test";
import { isTopLevelExitRoute } from "./exitRoutePolicy";

test("visible primary tabs are exit surfaces", () => {
  assert.equal(isTopLevelExitRoute(["(tabs)"]), true);
  assert.equal(isTopLevelExitRoute(["(tabs)", "index"]), true);
  assert.equal(isTopLevelExitRoute(["(tabs)", "accounts"]), true);
  assert.equal(isTopLevelExitRoute(["(tabs)", "activity"]), true);
  assert.equal(isTopLevelExitRoute(["(tabs)", "cashflow"]), true);
});

test("companion is not an exit surface because Back must return to the launching tab", () => {
  assert.equal(isTopLevelExitRoute(["(tabs)", "companion"]), false);
});

test("hidden legacy groups and nested routes are not exit surfaces", () => {
  assert.equal(isTopLevelExitRoute(["(tabs)", "planning"]), false);
  assert.equal(isTopLevelExitRoute(["(tabs)", "calendar"]), false);
  assert.equal(isTopLevelExitRoute(["(tabs)", "accounts", "[id]"]), false);
  assert.equal(isTopLevelExitRoute(["(tabs)", "activity", "categories"]), false);
});

test("non-tab routes are never exit surfaces", () => {
  assert.equal(isTopLevelExitRoute([]), false);
  assert.equal(isTopLevelExitRoute(["(auth)"]), false);
  assert.equal(isTopLevelExitRoute(["legal", "terms"]), false);
});
