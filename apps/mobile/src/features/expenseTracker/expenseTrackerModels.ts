import type {
  ExpenseTaxonomyDomainDto,
  ExpenseTaxonomySubcategoryDto,
  ExpenseTrackerEntryDto
} from "../../types/api";

export type ExpenseTrackerQuickRange = "all" | "today" | "week" | "month";
export type ExpenseTrackerSortOrder = "newest" | "oldest" | "highest" | "lowest";

export const expenseTrackerPaymentSourceOptions = [
  { label: "Cash", value: "Cash", icon: "cash-outline" },
  { label: "AIB", value: "AIB", icon: "card-outline" },
  { label: "BOI", value: "BOI", icon: "card-outline" },
  { label: "Revolut", value: "Revolut", icon: "phone-portrait-outline" },
  { label: "Credit Card", value: "Credit Card", icon: "card-outline" },
  { label: "Savings", value: "Savings", icon: "wallet-outline" },
  { label: "Other", value: "Other", icon: "layers-outline" }
] as const;

export const expenseTrackerStatusOptions = [
  { label: "Completed", value: "completed" },
  { label: "Planned", value: "planned" }
] as const;

export const expenseTrackerQuickRangeOptions = [
  { label: "All", value: "all" },
  { label: "Today", value: "today" },
  { label: "This week", value: "week" },
  { label: "This month", value: "month" }
] as const;

export const expenseTrackerSortOptions = [
  { label: "Newest", value: "newest" },
  { label: "Oldest", value: "oldest" },
  { label: "Highest", value: "highest" },
  { label: "Lowest", value: "lowest" }
] as const;

const domainVisuals: Record<number, { color: string; icon: string }> = {
  100: { color: "#68D7A9", icon: "home-outline" },
  110: { color: "#7CD4FF", icon: "leaf-outline" },
  120: { color: "#89A8FF", icon: "car-outline" },
  130: { color: "#FFB86C", icon: "restaurant-outline" },
  140: { color: "#7CB6FF", icon: "flash-outline" },
  150: { color: "#9B8CFF", icon: "shield-checkmark-outline" },
  160: { color: "#66D6D2", icon: "medkit-outline" },
  170: { color: "#F28A8A", icon: "card-outline" },
  180: { color: "#F6C75F", icon: "trending-up-outline" },
  190: { color: "#FF9B7E", icon: "sparkles-outline" },
  200: { color: "#6ED0A8", icon: "people-outline" },
  210: { color: "#C08CFF", icon: "game-controller-outline" },
  220: { color: "#6AC9FF", icon: "airplane-outline" },
  230: { color: "#FF8FA3", icon: "bag-handle-outline" },
  240: { color: "#FFA77B", icon: "gift-outline" },
  250: { color: "#8ED9C4", icon: "paw-outline" },
  260: { color: "#AFA1FF", icon: "school-outline" },
  270: { color: "#F0A15B", icon: "receipt-outline" },
  280: { color: "#F6C75F", icon: "repeat-outline" },
  290: { color: "#7AA8FF", icon: "briefcase-outline" },
  300: { color: "#C997FF", icon: "heart-outline" },
  310: { color: "#95B3D7", icon: "document-text-outline" },
  900: { color: "#A1B1C9", icon: "swap-horizontal-outline" },
  910: { color: "#78D1A3", icon: "cash-outline" },
  920: { color: "#8CA4FF", icon: "repeat-outline" }
};

const categoryIconOverrides: Record<number, string> = {
  10010: "business-outline",
  10030: "hammer-outline",
  10040: "construct-outline",
  10050: "bed-outline",
  11010: "basket-outline",
  11040: "flower-outline",
  12010: "train-outline",
  12020: "flash-outline",
  12050: "car-sport-outline",
  12070: "document-text-outline",
  13010: "basket-outline",
  13020: "restaurant-outline",
  13030: "cafe-outline",
  14040: "phone-portrait-outline",
  15040: "car-outline",
  16040: "medkit-outline",
  17010: "card-outline",
  18030: "bar-chart-outline",
  19040: "fitness-outline",
  20040: "people-outline",
  21020: "game-controller-outline",
  22020: "bed-outline",
  23010: "shirt-outline",
  24030: "heart-outline",
  25020: "medkit-outline",
  26010: "school-outline",
  27010: "receipt-outline",
  28010: "play-circle-outline",
  29020: "desktop-outline",
  30010: "heart-circle-outline",
  31010: "document-text-outline"
};


const subcategoryIconOverrides: Record<number, string> = {
  100101: "business-outline",
  100102: "home-outline",
  100103: "trending-down-outline",
  100201: "receipt-outline",
  100301: "construct-outline",
  100309: "sparkles-outline",
  110101: "flask-outline",
  110401: "leaf-outline",
  120101: "bus-outline",
  120201: "car-sport-outline",
  120703: "document-text-outline",
  130101: "basket-outline",
  130201: "restaurant-outline",
  130301: "cafe-outline",
  140101: "flash-outline",
  140404: "phone-portrait-outline",
  150101: "shield-checkmark-outline",
  160101: "medkit-outline",
  170101: "card-outline",
  180101: "wallet-outline",
  190101: "cut-outline",
  200101: "happy-outline",
  210101: "ticket-outline",
  220101: "airplane-outline",
  230101: "shirt-outline",
  240101: "gift-outline",
  250101: "paw-outline",
  260101: "school-outline",
  270101: "receipt-outline",
  280101: "play-circle-outline",
  290101: "briefcase-outline",
  300101: "heart-circle-outline",
  310101: "document-text-outline"
};

const subcategoryKeywordIcons: Array<{ pattern: RegExp; icon: string }> = [
  { pattern: /rent|leasehold|housing association|room \/ shared/i, icon: "business-outline" },
  { pattern: /mortgage|home equity/i, icon: "home-outline" },
  { pattern: /interest|fees|late payment|penalties|fines/i, icon: "receipt-outline" },
  { pattern: /repair|maintenance|service|contractor|handyman|plumbing|electrical|roof|locksmith/i, icon: "construct-outline" },
  { pattern: /cleaning/i, icon: "sparkles-outline" },
  { pattern: /security|alarm|camera|monitoring/i, icon: "shield-outline" },
  { pattern: /moving|storage|accommodation/i, icon: "cube-outline" },
  { pattern: /furniture|mattress|bedding|d?cor|decor|blinds|rug|carpet/i, icon: "bed-outline" },
  { pattern: /appliance/i, icon: "hardware-chip-outline" },
  { pattern: /supplies|consumables|paper|laundry|kitchen|storage|organization|bulbs|batteries/i, icon: "basket-outline" },
  { pattern: /garden|plants|soil|seeds|lawn|planters|watering|landscaping|tree trimming|pool/i, icon: "leaf-outline" },
  { pattern: /paint|hardware|tools|materials|fixtures|sealants|fixings|workshop/i, icon: "hammer-outline" },
  { pattern: /bus|coach/i, icon: "bus-outline" },
  { pattern: /train|rail/i, icon: "train-outline" },
  { pattern: /tram|metro|luas|subway/i, icon: "subway-outline" },
  { pattern: /ferry/i, icon: "boat-outline" },
  { pattern: /taxi|ride-hailing|airport transfer/i, icon: "car-outline" },
  { pattern: /petrol|gasoline|diesel|fuel/i, icon: "car-sport-outline" },
  { pattern: /charging|ev/i, icon: "flash-outline" },
  { pattern: /parking|toll|congestion|clamping|towing/i, icon: "car-outline" },
  { pattern: /bicycle|bike|cycling|helmet/i, icon: "bicycle-outline" },
  { pattern: /scooter/i, icon: "navigate-outline" },
  { pattern: /driving|license|permit|test/i, icon: "document-text-outline" },
  { pattern: /flight|airplane|baggage|seat selection/i, icon: "airplane-outline" },
  { pattern: /supermarket|groceries|produce|meat|seafood|dairy|bakery|pantry|snacks|beverages/i, icon: "basket-outline" },
  { pattern: /restaurant|dining|food court|lunch/i, icon: "restaurant-outline" },
  { pattern: /caf?|coffee|tea|juice/i, icon: "cafe-outline" },
  { pattern: /alcohol|cocktail|bar|pub|nightlife|drinks/i, icon: "wine-outline" },
  { pattern: /delivery|takeaway|carryout|meal kits|prepared meal|catering/i, icon: "bicycle-outline" },
  { pattern: /electricity|meter/i, icon: "flash-outline" },
  { pattern: /gas|heating|boiler|firewood|pellets|oil|propane/i, icon: "flame-outline" },
  { pattern: /water|waste|bin|recycling|septic/i, icon: "water-outline" },
  { pattern: /internet|broadband|landline|mobile|phone|calling/i, icon: "phone-portrait-outline" },
  { pattern: /tv|media|satellite|broadcast/i, icon: "tv-outline" },
  { pattern: /insurance|cover|protection/i, icon: "shield-checkmark-outline" },
  { pattern: /doctor|general practitioner|specialist|therap/i, icon: "medkit-outline" },
  { pattern: /dental|teeth|orthodont/i, icon: "medical-outline" },
  { pattern: /vision|eye|glasses|contact lenses/i, icon: "eye-outline" },
  { pattern: /prescription|medication|drugs|insulin|antibiotics/i, icon: "medkit-outline" },
  { pattern: /hospital|surgery|emergency|ambulance|x-rays|mri|ct/i, icon: "fitness-outline" },
  { pattern: /credit card|loan|repayment|debt|arrears|collection|settlement/i, icon: "card-outline" },
  { pattern: /savings|pension|retirement|brokerage|stock|bond|crypto|investment/i, icon: "trending-up-outline" },
  { pattern: /hair|barber|salon|skincare|makeup|nail|spa|beauty/i, icon: "cut-outline" },
  { pattern: /toothpaste|soap|shampoo|deodorant|hygiene|shaving|toiletr/i, icon: "water-outline" },
  { pattern: /gym|fitness|yoga|pilates|trainer|massage|wellness|supplements/i, icon: "barbell-outline" },
  { pattern: /daycare|nanny|babysitter|preschool|childminder/i, icon: "happy-outline" },
  { pattern: /elder care|assisted living|caregiver|dependent/i, icon: "people-outline" },
  { pattern: /cinema|concert|theatre|museum|festival|event|sports events/i, icon: "ticket-outline" },
  { pattern: /video game|gaming|console/i, icon: "game-controller-outline" },
  { pattern: /books|ebook|audiobook|magazines|music/i, icon: "book-outline" },
  { pattern: /hotel|hostel|lodging|vacation rental|camping|resort/i, icon: "bed-outline" },
  { pattern: /passport|visa|tour|attraction|travel sim|roaming/i, icon: "map-outline" },
  { pattern: /clothing|workwear|sportswear|underwear|maternity/i, icon: "shirt-outline" },
  { pattern: /bags|belt|wallet|jewelry|watch|sunglasses|shoes/i, icon: "bag-handle-outline" },
  { pattern: /phones|tablets|laptops|headphones|smartwatch|electronics/i, icon: "phone-portrait-outline" },
  { pattern: /gift|party|cards|wrap|donation|charitable|community support/i, icon: "gift-outline" },
  { pattern: /pet|vet|boarding|dog walking|adoption|litter/i, icon: "paw-outline" },
  { pattern: /tuition|school|university|exam|textbook|study|course|learning/i, icon: "school-outline" },
  { pattern: /tax|vat|customs|duties|filing|accountant/i, icon: "receipt-outline" },
  { pattern: /netflix|spotify|streaming|subscription|membership|premium/i, icon: "play-circle-outline" },
  { pattern: /office|coworking|software|hosting|marketing|client|business/i, icon: "briefcase-outline" },
  { pattern: /tithes|church|mosque|temple|spiritual|ritual|mission/i, icon: "heart-circle-outline" },
  { pattern: /legal|solicitor|attorney|court|notary|broker|advisor|document/i, icon: "document-text-outline" }
];

export function getExpenseTrackerSubcategoryVisual(input: {
  domainId?: number | null;
  categoryId?: number | null;
  subcategoryId?: number | null;
  subcategoryName?: string | null;
}) {
  const base = getExpenseTrackerVisual({ domainId: input.domainId, categoryId: input.categoryId });
  const overrideIcon = input.subcategoryId ? subcategoryIconOverrides[input.subcategoryId] : null;
  const keywordIcon = input.subcategoryName
    ? subcategoryKeywordIcons.find((item) => item.pattern.test(input.subcategoryName ?? ""))?.icon ?? null
    : null;

  return {
    color: base.color,
    icon: overrideIcon ?? keywordIcon ?? base.icon
  };
}

export function getExpenseTrackerEntryCategoryLabel(entry: ExpenseTrackerEntryDto) {
  return entry.categoryName ?? entry.categoryLabel ?? "Uncategorized";
}

export function getExpenseTrackerEntrySubcategoryLabel(entry: ExpenseTrackerEntryDto) {
  return entry.subcategoryName ?? entry.categoryLabel ?? entry.categoryName ?? "Uncategorized";
}

export function getExpenseTrackerVisual(input: {
  domainId?: number | null;
  categoryId?: number | null;
}) {
  const domainVisual = (input.domainId ? domainVisuals[input.domainId] : null) ?? {
    color: "#9AAAC7",
    icon: "ellipse-outline"
  };

  return {
    color: domainVisual.color,
    icon: (input.categoryId ? categoryIconOverrides[input.categoryId] : null) ?? domainVisual.icon
  };
}

export function flattenVisibleExpenseTaxonomy(domains: ExpenseTaxonomyDomainDto[]) {
  return domains
    .filter((domain) => domain.isUserSelectable && !domain.isSystemDomain && domain.isActive)
    .flatMap((domain) =>
      domain.categories
        .filter((category) => category.isUserSelectable && category.isActive)
        .flatMap((category) =>
          category.subcategories
            .filter((subcategory) => subcategory.isUserSelectable && subcategory.isActive)
            .map((subcategory) => ({
              domain,
              category,
              subcategory
            }))
        )
    );
}

