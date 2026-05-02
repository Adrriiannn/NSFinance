using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceSearchStrategyPromptBuilder : ICompanionPlaceSearchStrategyPromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PromptBuildResult BuildPrompt(UserChatRequest request, CompanionSemanticIntent intent)
    {
        var system = """
You are planning Google Places searches for a financial companion app.
Return strict JSON only. Do not include markdown.

Separate brand/entity, role/category, modifiers, hard requirements, negative requirements, soft preferences, non-searchable preferences, location intent, and search variants.

Rules:
- Do not treat generic categories as brands.
- Do not treat style/modifier terms as brands.
- Do not treat "fine dining", "car parks", "coffee shops", "restaurants", "museums", "pharmacies", "post offices", or similar categories as named entities.
- Return 1 to 4 search variants only. Do not pad variants.
- Variants must point to the same intended place type.
- Do not add coffee/cafe unless the requested role is coffee_shop/cafe.
- If a named entity may have multiple roles, specify the requested role and excluded sibling roles.
- If uncertain about a named entity, set verificationRequired=true and lower confidence.
- For named entities that may operate under a parent company, renamed brand, subsidiary, or operating brand, include only strongly-known relationshipAliases and matching search variants. Examples: Facebook office may use Meta, YouTube office may use Google, Instagram/WhatsApp/Oculus office may use Meta.
- Use categoryStrictness strict for banks/ATMs/parking/post offices/pharmacies/petrol stations when the user asked for that role.
- Use categoryStrictness compatible for restaurant subtypes such as fine dining.
""";

        var examples = """
Examples:

AIB banks near me ->
{"canonicalQuery":"AIB bank","entity":{"rawEntityText":"AIB","canonicalName":"AIB","aliases":["AIB","Allied Irish Bank"],"isBrandOrNamedEntity":true,"requiresEntityLock":true,"verificationRequired":true,"confidence":0.92},"role":{"requestedRole":"bank_branch","requiredCoreRoles":["bank","financial_institution"],"acceptableSubRoles":["bank"],"excludedSiblingRoles":["atm"],"modifiers":[],"categoryStrictness":"strict"},"searchVariants":[{"query":"AIB bank","purpose":"primary","requiresEntityMatch":true,"requiresRoleMatch":true,"confidence":0.93},{"query":"Allied Irish Bank","purpose":"alias","requiresEntityMatch":true,"requiresRoleMatch":true,"confidence":0.88}],"hardRequirements":[],"negativeRequirements":["atm"],"softPreferences":[],"nonSearchablePreferences":[],"rankingGoal":"brand_match_then_distance","maxCandidatePoolSize":50,"maxVisibleCards":10,"confidence":0.91,"warnings":[]}

AIB ATMs near me ->
{"canonicalQuery":"AIB ATM","entity":{"rawEntityText":"AIB","canonicalName":"AIB","aliases":["AIB","Allied Irish Bank"],"isBrandOrNamedEntity":true,"requiresEntityLock":true,"verificationRequired":true,"confidence":0.92},"role":{"requestedRole":"atm","requiredCoreRoles":["atm"],"acceptableSubRoles":["atm"],"excludedSiblingRoles":["bank"],"modifiers":[],"categoryStrictness":"strict"},"searchVariants":[{"query":"AIB ATM","purpose":"primary","requiresEntityMatch":true,"requiresRoleMatch":true,"confidence":0.93},{"query":"Allied Irish Bank ATM","purpose":"alias","requiresEntityMatch":true,"requiresRoleMatch":true,"confidence":0.88}],"hardRequirements":[],"negativeRequirements":["bank"],"softPreferences":[],"nonSearchablePreferences":[],"rankingGoal":"brand_match_then_distance","maxCandidatePoolSize":50,"maxVisibleCards":10,"confidence":0.91,"warnings":[]}

fine dining restaurants near me ->
{"canonicalQuery":"fine dining restaurants","entity":null,"role":{"requestedRole":"restaurant","requiredCoreRoles":["restaurant"],"acceptableSubRoles":["restaurant","irish_restaurant","french_restaurant","asian_restaurant","european_restaurant","italian_restaurant","seafood_restaurant","fine_dining_restaurant"],"excludedSiblingRoles":["fast_food_restaurant","meal_takeaway","cafe"],"modifiers":["fine_dining","upscale"],"categoryStrictness":"compatible"},"searchVariants":[{"query":"fine dining restaurants","purpose":"primary","requiresEntityMatch":false,"requiresRoleMatch":true,"confidence":0.9},{"query":"upscale restaurants","purpose":"role_disambiguation","requiresEntityMatch":false,"requiresRoleMatch":true,"confidence":0.78}],"hardRequirements":[],"negativeRequirements":["fast_food_restaurant","meal_takeaway"],"softPreferences":["fine_dining","upscale"],"nonSearchablePreferences":[],"rankingGoal":"concept_fit_then_distance","maxCandidatePoolSize":50,"maxVisibleCards":10,"confidence":0.9,"warnings":[]}

car parks near me ->
{"canonicalQuery":"car parks","entity":null,"role":{"requestedRole":"parking","requiredCoreRoles":["parking"],"acceptableSubRoles":["parking","parking_lot","parking_garage"],"excludedSiblingRoles":["park","tourist_attraction"],"modifiers":[],"categoryStrictness":"strict"},"searchVariants":[{"query":"car parks","purpose":"primary","requiresEntityMatch":false,"requiresRoleMatch":true,"confidence":0.9},{"query":"parking","purpose":"role_disambiguation","requiresEntityMatch":false,"requiresRoleMatch":true,"confidence":0.75}],"hardRequirements":[],"negativeRequirements":["park"],"softPreferences":[],"nonSearchablePreferences":[],"rankingGoal":"parking_match_then_distance","maxCandidatePoolSize":50,"maxVisibleCards":10,"confidence":0.9,"warnings":[]}

coffee shops near me ->
{"canonicalQuery":"coffee shops","entity":null,"role":{"requestedRole":"coffee_shop","requiredCoreRoles":["coffee_shop","cafe"],"acceptableSubRoles":["coffee_shop","cafe"],"excludedSiblingRoles":[],"modifiers":[],"categoryStrictness":"compatible"},"searchVariants":[{"query":"coffee shops","purpose":"primary","requiresEntityMatch":false,"requiresRoleMatch":true,"confidence":0.9},{"query":"cafe","purpose":"role_disambiguation","requiresEntityMatch":false,"requiresRoleMatch":true,"confidence":0.78},{"query":"coffee","purpose":"role_disambiguation","requiresEntityMatch":false,"requiresRoleMatch":true,"confidence":0.72}],"hardRequirements":[],"negativeRequirements":[],"softPreferences":[],"nonSearchablePreferences":[],"rankingGoal":"intent_fit_then_distance","maxCandidatePoolSize":50,"maxVisibleCards":10,"confidence":0.9,"warnings":[]}

IKEA near me -> entity IKEA, role store/loose, one variant "IKEA".
Applegreen petrol stations near me -> entity Applegreen, role gas_station strict.
An Post post offices near me -> entity An Post, role post_office strict.
Facebook office Dublin -> entity Facebook with relationshipAliases [{"name":"Meta","relationshipType":"parent_company"}], role office compatible or loose, variants may include "Facebook office Dublin" and "Meta office Dublin".
bike shops near me -> no entity, role bicycle_store/shop compatible.
dog-friendly cafes around me -> no entity, role coffee_shop/cafe, soft preference dog_friendly.
late-night pharmacies in Dublin 2 -> no entity, role pharmacy strict, hard/time requirement open_late.
""";

        var context = new
        {
            userMessage = request.UserMessage,
            semanticIntent = new
            {
                intent.PlaceQuery,
                intent.BrandOrEntity,
                role = intent.Role,
                intent.HardFilters,
                intent.NegativeFilters,
                intent.SoftPreferences,
                intent.NonSearchablePreferences,
                location = intent.Location,
                intent.RankingGoal,
                intent.RequestedMaxResults,
                intent.Confidence
            }
        };

        return new PromptBuildResult(
            system,
            [
                AIMessage.Developer(examples),
                AIMessage.User(JsonSerializer.Serialize(context, JsonOptions))
            ],
            "companion_place_search_strategy_v1",
            ["places_search_strategy_prompt_v1"]);
    }
}
