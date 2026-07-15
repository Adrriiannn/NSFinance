import assert from "node:assert/strict";
import { registerHooks } from "node:module";
import test from "node:test";

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

const statementImportsApi = import("./statementImportsApi");

test("statement row filters serialize in a stable order", async () => {
  const { buildStatementImportRowsPath } = await statementImportsApi;
  assert.equal(
    buildStatementImportRowsPath("batch / one", {
      reviewDisposition: "pending",
      duplicateClassification: "likely",
      validationStatus: "valid",
      pageSize: 100,
      cursor: "v1:+/=?&"
    }),
    "/api/imports/statements/batch%20%2F%20one/rows?cursor=v1%3A%2B%2F%3D%3F%26&pageSize=100&validationStatus=valid&duplicateClassification=likely&reviewDisposition=pending"
  );
});

test("empty and null row filters remain absent", async () => {
  const { buildStatementImportRowsPath } = await statementImportsApi;
  assert.equal(
    buildStatementImportRowsPath("batch-1", {
      cursor: "",
      pageSize: null,
      validationStatus: null,
      duplicateClassification: null,
      reviewDisposition: null
    }),
    "/api/imports/statements/batch-1/rows"
  );
});
