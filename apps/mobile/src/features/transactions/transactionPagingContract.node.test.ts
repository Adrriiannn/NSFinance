import assert from "node:assert/strict";
import { registerHooks } from "node:module";
import test from "node:test";
import { queryKeys } from "../../lib/api/queryKeys";
import type { TransactionPageRequest } from "../../types/api";

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier === "react-native") {
      return { shortCircuit: true, url: "test:react-native" };
    }

    return nextResolve(specifier, context);
  },
  load(url, context, nextLoad) {
    if (url === "test:react-native") {
      return {
        format: "module",
        shortCircuit: true,
        source: 'export const Platform = { OS: "android" };'
      };
    }

    return nextLoad(url, context);
  }
});

const transactionsApi = import("./transactionsApi");

test("transaction page parameters serialize in a deterministic order", async () => {
  const { buildTransactionPagePath } = await transactionsApi;

  assert.equal(
    buildTransactionPagePath({
      direction: "expense",
      toUtc: "2026-08-01T00:00:00.000Z",
      fromUtc: "2026-07-01T00:00:00.000Z",
      accountId: "account-1",
      cursor: "cursor-2",
      pageSize: 25
    }),
    "/api/transactions/page?pageSize=25&cursor=cursor-2&accountId=account-1&fromUtc=2026-07-01T00%3A00%3A00.000Z&toUtc=2026-08-01T00%3A00%3A00.000Z&direction=expense"
  );
});

test("opaque cursor characters round-trip exactly through URLSearchParams", async () => {
  const { buildTransactionPagePath } = await transactionsApi;
  const cursor = "v2:+/=_-%25?& #~";
  const path = buildTransactionPagePath({ cursor });
  const query = path.slice(path.indexOf("?") + 1);
  const searchParams = new URLSearchParams(query);

  assert.equal(searchParams.get("cursor"), cursor);
  assert.equal(searchParams.getAll("cursor").length, 1);
});

test("omitted values stay absent while explicit empty cursors remain explicit", async () => {
  const { buildTransactionPagePath } = await transactionsApi;

  assert.equal(buildTransactionPagePath(), "/api/transactions/page");
  assert.equal(
    buildTransactionPagePath({
      pageSize: null,
      cursor: null,
      accountId: undefined,
      fromUtc: null,
      toUtc: undefined,
      direction: null
    }),
    "/api/transactions/page"
  );
  assert.equal(buildTransactionPagePath({ cursor: "" }), "/api/transactions/page?cursor=");
});

test("transaction page query keys normalize equivalent filters", () => {
  const first: TransactionPageRequest = {
    pageSize: 50,
    accountId: "account-1",
    fromUtc: "2026-07-01T00:00:00Z",
    direction: "income"
  };
  const reordered: TransactionPageRequest = {
    direction: "income",
    fromUtc: "2026-07-01T00:00:00Z",
    accountId: "account-1",
    cursor: undefined,
    pageSize: 50,
    toUtc: null
  };

  assert.deepEqual(queryKeys.transactions.page(first), queryKeys.transactions.page(reordered));
  assert.notDeepEqual(
    queryKeys.transactions.page(first),
    queryKeys.transactions.page({ ...first, cursor: "next-page" })
  );
});

test("infinite pages query keys normalize equivalent requests and stay distinct from single pages", () => {
  const defaulted = queryKeys.transactions.pages();
  const explicit = queryKeys.transactions.pages({
    pageSize: null,
    accountId: null,
    fromUtc: null,
    toUtc: null,
    direction: null
  });

  assert.deepEqual(defaulted, explicit);
  assert.equal(defaulted[1], "pages");

  const singlePage = queryKeys.transactions.page({});
  assert.notDeepEqual(defaulted, singlePage);

  const filtered = queryKeys.transactions.pages({ accountId: "acc-1", pageSize: 25 });
  assert.notDeepEqual(defaulted, filtered);
  assert.deepEqual(filtered, queryKeys.transactions.pages({ pageSize: 25, accountId: "acc-1" }));
});
