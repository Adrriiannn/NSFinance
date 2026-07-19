# AI Module Boundary (2026-07-19)

Authority: `NSFinance/Project Management/Execution Plan - Salvage And UX Overhaul.md` (vault).

This module is split into five dispositions. Do not add new capability to
quarantined or rebuild-scoped areas without consulting the execution plan.

| Bucket | Scope | Disposition |
| --- | --- | --- |
| A | Transport/plumbing: `AIClient*`, `AzureOpenAI*`, `MockAIProviderTransport`, model router, circuit breaker, options, telemetry, failure recording | Keep |
| B | Conversation persistence: `Conversation{Thread,Message,Turn,Summary,State}*`, `PersistentConversationContext*`, thread endpoints | Keep |
| C | Merchant investigation: `AIBackedMerchantInvestigationService`, `MerchantInvestigation*` | Keep and finish — seed of CAT-001 categorization |
| D | Decision core: `ConversationBehavior/Intelligence/Layer/Mode/Inference/Policies/PromptBuilders`, `TurnInterpretation*`, `CompanionSemanticIntent*`, `UserChat*`, `FinancialAdvice*`, ad-hoc evaluators, `UserFinancialContextProfile*` | Rebuild after Phase 2-4 domains exist (AI-001/AI-002 context-packet architecture). Existing path keeps serving chat behind current guards until then |
| E | Places/nearby: `CompanionPlace*`, `GooglePlaces*`, `PlaceRegistry/Result/ShortLivedCache*`, `CompanionLocality/LocationGrounding/Nearby*`, `RealWorld*`, ambiguity-guard catalogue, `GetPlacePhotoEndpoint` | Quarantined: config-off (`AI:Places:Enabled=false` in base settings) and no new investment until Phase 7's budget-grounded Companion gate |

Rationale: the module's technical depth exceeds proven product correctness
(vault: AI Architecture Current State). Financial truth, categorization, and
the budget domain come first; the Companion decision layer is rebuilt on
versioned context packets once those exist.
