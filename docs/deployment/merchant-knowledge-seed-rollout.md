# Merchant Knowledge Seed Rollout

`MerchantKnowledge` is seeded from the category characteristics catalog
(`libs/shared/.../CategoryCharacteristics*.cs`) once per
`CategoryCharacteristicsCatalog.Version`. Seeding runs at the start of the
merchant categorization backfill that follows a user's global sync, so a new
catalog version reaches every user's next sync. The `Categorization:SeedApply`
setting controls how far a new version is allowed to go.

The decision record, impact analysis, and evidence live in the Obsidian vault
note `Project Management/Catalog v6 Seed Bump - Impact Analysis` (CAT-001).

## Modes

| Mode | Knowledge base | Transactions | Use |
| --- | --- | --- | --- |
| `Off` (default) | Unchanged; logs `Merchant knowledge seed pending` | Categorized from live rows only | State after deploying a new version |
| `DryRun` | Unchanged; logs `Merchant knowledge seed preview` with insert/retarget/deactivation counts and the plan hash | Live rows only | Check the expected deltas |
| `Pilot` | Unchanged | Users in `PilotUserIds` match against the pending version (in memory); everyone else uses live rows | QA-identity check |
| `Apply` | Inserts, retargets, deactivates, and reopens parked candidates once, recorded in `MerchantKnowledgeSeedRuns` | Every user's next sync fills uncategorized rows from the new knowledge | Rollout |
| `Revert` | Undoes the applied version's knowledge changes from its ledger record | Existing assignments are not touched (see rollback) | Knowledge rollback |

A reverted version is not re-applied by switching back to `Apply`; ship a new
catalog version instead. The seed plan hash is pinned per version by
`MerchantKnowledgeSeedPlanTests`, so a signal change without a version bump
fails the build.

Azure App Service settings (no redeploy needed to change them):

```text
Categorization__SeedApply__Mode=Off|DryRun|Pilot|Apply|Revert
Categorization__SeedApply__PilotUserIds__0=<QA identity user id>
```

The kill switch for all automated categorization remains
`Categorization__BackfillOnGlobalSyncEnabled=false`.

## Gates

1. **Read-only discovery** (aggregate, global rows only; run by or with explicit
   approval from the owner):

   ```sql
   SELECT count(*) AS seed_rows,
          bool_or("NormalizedPattern" = 'ADVANCE PITSTOP') AS at_least_48db1133,
          bool_or("NormalizedPattern" = 'ALARM SERVICE')   AS at_least_a28f0a55,
          bool_or("NormalizedPattern" = 'AA IRELAND')      AS at_least_b12107be,
          bool_or("NormalizedPattern" = 'ADOPTION')        AS at_least_10bffde4,
          bool_or("NormalizedPattern" = 'ACCOUNT FEE')     AS at_head
   FROM "MerchantKnowledge"
   WHERE "UserId" IS NULL AND "Source" = 'seed';

   SELECT "NormalizedPattern", count(*)
   FROM "MerchantKnowledge"
   WHERE "UserId" IS NULL
   GROUP BY 1 HAVING count(*) > 1;

   SELECT "CategorizationSignal", count(*)
   FROM "Transactions"
   WHERE "CategorizationRuleKey" = 'merchant_knowledge'
     AND "CategorizationSignal" IN ('BAR', 'PUB', 'TIER', 'MACE', 'GALA', 'RUGS', 'SPA',
                                    'HUMM', 'SIXT', 'MAINTENANCE', 'PAYDAY', 'AVC', 'DFS', 'MRI')
   GROUP BY 1 ORDER BY 2 DESC;
   ```

2. **Dark deploy** with `Mode=Off`. Expect the `seed pending` log line and an
   empty `MerchantKnowledgeSeedRuns` table.
3. **Backup**: record the PostgreSQL point-in-time-restore timestamp (see
   `postgres-restore-rehearsal.md`).
4. **Dry run**: `Mode=DryRun`, one QA-identity sync. The logged counts for v6
   must match the snapshot identified in gate 1:

   | Production seeded v5 at | Inserts | Retargets | Deactivations |
   | --- | --- | --- | --- |
   | `48db1133` | 568 | 24 | 4 |
   | `a28f0a55` | 521 | 24 | 5 |
   | `b12107be` | 375 | 15 | 9 |
   | `10bffde4` | 281 | 13 | 14 |
   | `1be4472d` | 29 | 0 | 23 |

   The v6 plan hash is `b7959e643529d728d523d47a94fdce456598dbb88dc76533a4ddbe71a9d598ba`.
   Pre-v4 seed rows no longer in the plan, if any, add to the deactivation count.
5. **Pilot**: `Mode=Pilot` with the QA identity in `PilotUserIds`; review every
   newly categorized QA row in the app.
6. **Apply** in a watched session, then watch per-signal assignment counts.

## Rollback

1. Set `Categorization__SeedApply__Mode=Revert` and trigger one sync. The
   ledger row moves to `reverted`, inserted seeds are deactivated, retargeted
   seeds get their prior values back, deactivated seeds are reactivated, and
   candidates still parked by the version return to review. Then set
   `Mode=Off`.
2. Transaction assignments are reverted separately, with approval, a fresh
   backup, and the QA identity first. Every v6 assignment filled a row whose
   taxonomy was entirely null, so resetting it to null restores the pre-bump
   state exactly. Rows the user has since taught or corrected are excluded.

   ```sql
   -- Preview first: replace the SELECT list with count(*) and review.
   UPDATE "Transactions" t
   SET "TaxonomyDomainId" = NULL,
       "TaxonomyCategoryId" = NULL,
       "TaxonomySubcategoryId" = NULL,
       "CategorizationRuleKey" = NULL,
       "CategorizationSignal" = NULL,
       "CategorizationCharacteristicsVersion" = NULL,
       "CategorizedUtc" = NULL
   FROM "FinancialAccounts" a
   WHERE a."Id" = t."FinancialAccountId"
     AND a."UserId" = @user_id
     AND t."CategorizationRuleKey" = 'merchant_knowledge'
     AND t."CategorizationCharacteristicsVersion" = 6
     AND t."CategorizedUtc" >= @applied_or_pilot_utc
     AND NOT EXISTS (
         SELECT 1 FROM "MerchantKnowledge" k
         WHERE k."NormalizedPattern" = t."CategorizationSignal"
           AND (k."UserId" = a."UserId"
                OR (k."UserId" IS NULL AND k."Source" = 'ai_investigation')));
   ```

   Seed rows untouched by v6 keep their older version stamp, so they are never
   selected. AI-researched rows and user-taught patterns are excluded
   explicitly because they also stamp the current version.
