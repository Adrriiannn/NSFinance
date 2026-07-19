import assert from "node:assert/strict";
import test from "node:test";
import { formatMerchantDisplayName, hasDistinctStatementText } from "./merchantDisplay";

test("provider prefixes and store numbers clean up for display", () => {
  assert.equal(formatMerchantDisplayName("VDC-TESCO STORES 3"), "Tesco Stores");
  assert.equal(formatMerchantDisplayName("VDC-APPLEGREEN SAN"), "Applegreen San");
  assert.equal(formatMerchantDisplayName("VDP-SP MANSCAPED"), "SP Manscaped");
  assert.equal(formatMerchantDisplayName("VDC-LIDL IRELAND L"), "Lidl Ireland L");
  assert.equal(formatMerchantDisplayName("POS DUNNES STORES 118"), "Dunnes Stores");
});

test("mixed-case, short, and dotted names survive intact", () => {
  assert.equal(formatMerchantDisplayName("Spotify"), "Spotify");
  assert.equal(formatMerchantDisplayName("VDP-VOLA.RO SRL AD"), "Vola.Ro Srl AD");
  assert.equal(formatMerchantDisplayName("*MOBI SAVINGS-109 *M"), "Mobi Savings-109 *M");
});

test("degenerate inputs fall back to the original text", () => {
  assert.equal(formatMerchantDisplayName(""), "");
  assert.equal(formatMerchantDisplayName("VDC-"), "VDC-");
  assert.equal(formatMerchantDisplayName("  VDC-A  "), "VDC-A");
});

test("distinct statement text is detected for the detail view", () => {
  assert.equal(hasDistinctStatementText("VDC-TESCO STORES 3"), true);
  assert.equal(hasDistinctStatementText("Spotify"), false);
});
