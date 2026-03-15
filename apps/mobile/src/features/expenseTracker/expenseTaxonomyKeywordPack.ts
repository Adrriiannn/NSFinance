export type ExpenseTaxonomyKeywordEntry = {
  displayName: string;
  keywords: string[];
  aliases?: string[];
  merchantHints?: string[];
};

export const expenseTaxonomyKeywordPack: Readonly<Record<number, ExpenseTaxonomyKeywordEntry>> = {
  100101: {
    displayName: "Rent",
    keywords: [
      "rent",
      "monthly rent",
      "tenancy",
      "tenant",
      "landlord",
      "lease",
      "apartment rent",
      "house rent",
      "flat rent",
      "room rent",
      "rental payment",
      "accommodation rent",

      // Adding some common misspellings and variations

      "rnt",
      "rennt",
      "tenacy",
      "tenent",
      "land lord",
      "leese",
      "apt rent",
      "hous rent",
    ],
    aliases: [
      "rent money",
      "rent is due",
      "monthly housing payment",
      "lease renewal",
      "new tenancy",
      "paying rent",
    ]
  },
  100102: {
    displayName: "Mortgage Principal",
    keywords: [
      "mortgage",
      "mortgage principal",
      "home loan principal",
      "loan repayment",
      "housing loan",
      "principal payment",
      "mortgage repayment",
      "house loan principal",

      // Adding some common misspellings and variations

      "morgage",
      "princpal",
      "principla",
      "mortage",
      "mortgag",
      "lona",
      "hous loan",
    ]
  },
  100103: {
    displayName: "Mortgage Interest",
    keywords: [
      "mortgage",
      "mortgage interest",
      "loan interest",
      "housing loan interest",
      "mortgage charge",
      "interest payment",
      "home loan interest",

      // Adding some common misspellings and variations

      "mortage",
      "intrest",
      "morgage",
      "mortage",
      "mortgag",
      "lona",
      "hous loan",
    ]
  },
  100104: {
    displayName: "Mortgage Insurance",
    keywords: [
      "mortgage insurance",
      "home loan insurance",
      "PMI",
      "mortgage protection",
      "lender insurance",
      "loan cover",

      // Adding some common misspellings and variations

      "mortage insurance",
      "mortgage insurence",
      "mortgag",
      "mortage",
      "mortgag",
      "lona",
      "hous loan",
    ]
  },
  100105: {
    displayName: "Ground Rent / Leasehold Charges",
    keywords: [
      "ground rent",
      "leasehold",
      "leasehold charge",
      "property lease fee",
      "lease fee",
      "land rent",
      "estate lease charge",
      "charge",


      // Adding some common misspellings and variations

      "grond rent",
      "lease hold",
      "leese",
    ]
  },
  100106: {
    displayName: "Rent Deposit",
    keywords: [
      "deposit",
      "rent deposit",
      "security deposit",
      "tenancy deposit",
      "booking deposit",
      "housing deposit",
      "rental deposit",


      // Adding some common misspellings and variations

      "depositt",
      "depozit",
      "rent deposite",
      "depoit",
      "deoiut",
    ],
    aliases: [
      "security deposit back",
      "new tenancy deposit",
      "move in deposit",
      "rental deposit return",
      "deposit for apartment",
      "deposit for flat",
    ]
  },
  100107: {
    displayName: "Housing Association Payments",
    keywords: [
      "payments",
      "housing association",
      "social housing",
      "association payment",
      "public housing",
      "supported housing",
      "housing scheme payment",

      // Adding some common misspellings and variations

      "hous",
      "asociation",
      "assosiation",
      "asosiation",
    ]
  },
  100108: {
    displayName: "Room / Shared Housing Payments",
    keywords: [
      "rent",
      "room",
      "shared",
      "shared housing",
      "room payment",
      "roommate rent",
      "house share",
      "shared rent",
      "room lease",
      "shared accommodation",

      // Adding some common misspellings and variations

      "rom",
      "shared hous",
      "shard",
    ]
  },
  100201: {
    displayName: "Property Taxes",
    keywords: [
      "tax",
      "taxes",
      "property tax",
      "house tax",
      "real estate tax",
      "property charge",
      "home tax",
      "building tax",


      // Adding some common misspellings and variations

      "proprty tax",
      "propert",
      "proprty",
      "taxs",
    ]
  },
  100202: {
    displayName: "Local Property Tax / Council Tax",
    keywords: [
      "property",
      "council tax",
      "local property tax",
      "LPT",
      "council bill",
      "municipality tax",
      "local authority tax",

      // Adding some common misspellings and variations

      "council taxe",
      "counsil",
      "counchil",
      "cauncil",
      "proprty",
      "authorty",
    ]
  },
  100203: {
    displayName: "HOA / Condo Fees",
    keywords: [
      "HOA",
      "condo fee",
      "homeowners association",
      "building fee",
      "condo charge",
      "management fee",
      "association fee",

      // Adding some common misspellings and variations

      "cond",
      "hia",
      "mangement",
      "asosiation",
      "assosiation",
    ]
  },
  100204: {
    displayName: "Building Management Fees",
    keywords: [
      "building management",
      "service charge",
      "maintenance fee",
      "estate management",
      "block management",
      "management company fee",

      // Adding some common misspellings and variations

      "managment",
      "build",
      "buildin",
      "servise",
      "bloc",
    ]
  },
  100205: {
    displayName: "Registration / Land Fees",
    keywords: [
      "land fee",
      "land registry",
      "property registration",
      "deed fee",
      "title registration",
      "registry fee",


      // Adding some common misspellings and variations

      "lnad",
      "registartion",
      "proprty",
    ]
  },
  100206: {
    displayName: "Housing Permits & Inspection Fees",
    keywords: [
      "permit",
      "inspection fee",
      "housing permit",
      "building inspection",
      "occupancy permit",
      "planning fee",
      "survey fee",
      // Adding some common misspellings and variations

      "permits",
      "housingpermitsinspection",
      "inspetion",
      "housing permits and inspetion fees",
      "inspecction",
      "housing permits and inspecction fees",
    ]
  },
  100301: {
    displayName: "General Repairs",
    keywords: [
      "repair",
      "general repair",
      "home repair",
      "fix",
      "maintenance",
      "broken item",
      "repairman",
      "household repair",
      // Adding some common misspellings and variations

      "repairs",
      "generalrepairs",
      "genral",
      "genral repairs",
      "geneeral",
      "geneeral repairs",
    ]
  },
  100302: {
    displayName: "Plumbing Repairs",
    keywords: [
      "plumber",
      "plumbing",
      "leak",
      "pipe",
      "drain",
      "blocked sink",
      "toilet repair",
      "burst pipe",
      "water leak",
      // Adding some common misspellings and variations

      "repairs",
      "plumbingrepairs",
      "pluming",
      "pluming repairs",
      "plumbbing",
      "plumbbing repairs",
    ]
  },
  100303: {
    displayName: "Electrical Repairs",
    keywords: [
      "electrician",
      "electrical",
      "wiring",
      "fuse",
      "socket",
      "switch repair",
      "power issue",
      "electrical fault",
      // Adding some common misspellings and variations

      "repairs",
      "electricalrepairs",
      "electical",
      "electical repairs",
      "electrrical",
      "electrrical repairs",
    ]
  },
  100304: {
    displayName: "Roofing Repairs",
    keywords: [
      "roof",
      "roofing",
      "roof leak",
      "shingles",
      "tiles",
      "gutter roof",
      "roof repair",
      "attic leak",
      // Adding some common misspellings and variations

      "repairs",
      "roofingrepairs",
      "repirs",
      "roofing repirs",
      "repaairs",
      "roofing repaairs",
    ]
  },
  100305: {
    displayName: "Appliance Repairs",
    keywords: [
      "appliance repair",
      "washing machine repair",
      "fridge repair",
      "oven repair",
      "dishwasher repair",
      "tumble dryer repair",
      // Adding some common misspellings and variations

      "repairs",
      "appliancerepairs",
      "applance",
      "applance repairs",
      "appliiance",
      "appliiance repairs",
    ],
    aliases: [
      "fix broken fridge",
      "washing machine broken",
      "repair my oven",
      "dishwasher repair visit",
      "appliance service call",
      "fix freezer",
    ]
  },
  100306: {
    displayName: "Locksmith",
    keywords: [
      "locksmith",
      "lock",
      "key",
      "lockout",
      "door lock",
      "key replacement",
      "lock change",
      "deadbolt",
      // Adding some common misspellings and variations

      "lockmith",
      "lockssmith",
      "locskmith",
    ]
  },
  100307: {
    displayName: "Pest Control Services",
    keywords: [
      "pest control",
      "exterminator",
      "bugs",
      "mice",
      "rats",
      "ants",
      "cockroaches",
      "infestation",
      "fumigation",
      // Adding some common misspellings and variations

      "pestcontrol",
      "conrol",
      "pest conrol services",
      "conttrol",
      "pest conttrol services",
      "cotnrol",
    ]
  },
  100308: {
    displayName: "Handyman Services",
    keywords: [
      "handyman",
      "odd jobs",
      "home service",
      "fixing",
      "maintenance worker",
      "small repairs",
      "home help",
      // Adding some common misspellings and variations

      "handman",
      "handyyman",
      "hanydman",
    ]
  },
  100309: {
    displayName: "Home Cleaning Services",
    keywords: [
      "cleaner",
      "cleaning service",
      "maid",
      "deep cleaning",
      "house cleaning",
      "end of tenancy clean",
      "domestic cleaning",
      // Adding some common misspellings and variations

      "home",
      "homecleaning",
      "cleaing",
      "home cleaing services",
      "cleanning",
      "home cleanning services",
    ]
  },
  100310: {
    displayName: "Emergency Repairs",
    keywords: [
      "emergency repair",
      "urgent fix",
      "emergency plumber",
      "emergency electrician",
      "urgent maintenance",
      "after-hours repair",
      // Adding some common misspellings and variations

      "repairs",
      "emergencyrepairs",
      "emerency",
      "emerency repairs",
      "emerggency",
      "emerggency repairs",
    ]
  },
  100401: {
    displayName: "Painting & Decorating",
    keywords: [
      "painting",
      "decorating",
      "paint job",
      "wallpaper",
      "repainting",
      "interior paint",
      "exterior paint",
      "decorator",
      // Adding some common misspellings and variations

      "paintingdecorating",
      "decorting",
      "painting and decorting",
      "decoraating",
      "painting and decoraating",
      "decoarting",
    ]
  },
  100402: {
    displayName: "Flooring",
    keywords: [
      "flooring",
      "floor",
      "carpet fitting",
      "laminate",
      "wood floor",
      "vinyl floor",
      "tile floor",
      // Adding some common misspellings and variations

      "flooing",
      "floorring",
      "floroing",
    ]
  },
  100403: {
    displayName: "Kitchen Renovation",
    keywords: [
      "kitchen renovation",
      "kitchen remodel",
      "new kitchen",
      "cabinets",
      "countertops",
      "kitchen fitting",
      // Adding some common misspellings and variations

      "kitchenrenovation",
      "renovtion",
      "kitchen renovtion",
      "renovaation",
      "kitchen renovaation",
      "renoavtion",
    ]
  },
  100404: {
    displayName: "Bathroom Renovation",
    keywords: [
      "bathroom renovation",
      "bathroom remodel",
      "shower install",
      "bathtub",
      "bathroom fitting",
      "tile bathroom",
      // Adding some common misspellings and variations

      "bathroomrenovation",
      "renovtion",
      "bathroom renovtion",
      "renovaation",
      "bathroom renovaation",
      "renoavtion",
    ]
  },
  100405: {
    displayName: "Carpentry / Joinery",
    keywords: [
      "carpenter",
      "carpentry",
      "joinery",
      "woodwork",
      "custom shelving",
      "fitted wardrobe",
      "timber work",
      // Adding some common misspellings and variations

      "carpentryjoinery",
      "carpntry",
      "carpntry joinery",
      "carpeentry",
      "carpeentry joinery",
      "carepntry",
    ]
  },
  100406: {
    displayName: "Windows & Doors",
    keywords: [
      "windows",
      "doors",
      "window replacement",
      "door replacement",
      "double glazing",
      "front door",
      "sliding door",
      // Adding some common misspellings and variations

      "windowsdoors",
      "winows",
      "winows and doors",
      "winddows",
      "winddows and doors",
      "widnows",
    ]
  },
  100407: {
    displayName: "Insulation",
    keywords: [
      "insulation",
      "attic insulation",
      "wall insulation",
      "draft proofing",
      "energy efficiency",
      "loft insulation",
      // Adding some common misspellings and variations

      "insultion",
      "insulaation",
      "insualtion",
    ]
  },
  100408: {
    displayName: "Smart Home Installation",
    keywords: [
      "smart home",
      "smart lock",
      "smart lights",
      "thermostat install",
      "home automation",
      "smart camera install",
      // Adding some common misspellings and variations

      "installation",
      "smarthomeinstallation",
      "instalation",
      "smart home instalation",
      "installlation",
      "smart home installlation",
    ]
  },
  100409: {
    displayName: "Accessibility Upgrades",
    keywords: [
      "accessibility",
      "ramp",
      "grab bars",
      "stairlift",
      "accessible bathroom",
      "mobility access",
      "disability access",
      // Adding some common misspellings and variations

      "upgrades",
      "accessibilityupgrades",
      "accessbility",
      "accessbility upgrades",
      "accessiibility",
      "accessiibility upgrades",
    ]
  },
  100410: {
    displayName: "Contractor / Labor Costs",
    keywords: [
      "contractor",
      "labor",
      "labour",
      "tradesperson",
      "builders",
      "renovation labor",
      "installation labor",
      "workmen",
      // Adding some common misspellings and variations

      "contractorlabor",
      "contrctor",
      "contrctor labor costs",
      "contraactor",
      "contraactor labor costs",
      "contarctor",
    ]
  },
  100501: {
    displayName: "Furniture",
    keywords: [
      "furniture",
      "sofa",
      "couch",
      "table",
      "chair",
      "bed frame",
      "wardrobe",
      "dresser",
      "bookshelf",
      // Adding some common misspellings and variations

      "furnture",
      "furniiture",
      "furinture",
    ],
    merchantHints: [
      "ikea",
      "ez living",
      "ez living furniture",
      "dfs",
      "jysk",
      "harvey norman",
      "michael murphy home furnishing",
    ]
  },
  100502: {
    displayName: "Mattresses & Bedding",
    keywords: [
      "mattress",
      "bedding",
      "duvet",
      "pillow",
      "sheets",
      "blanket",
      "comforter",
      "bed linen",
      // Adding some common misspellings and variations

      "mattresses",
      "mattressesbedding",
      "mattrsses",
      "mattrsses and bedding",
      "mattreesses",
      "mattreesses and bedding",
    ]
  },
  100503: {
    displayName: "Large Appliances",
    keywords: [
      "fridge",
      "refrigerator",
      "washing machine",
      "dryer",
      "oven",
      "cooker",
      "dishwasher",
      "freezer",
      // Adding some common misspellings and variations

      "large",
      "appliances",
      "largeappliances",
      "applinces",
      "large applinces",
      "appliaances",
    ],
    merchantHints: [
      "currys",
      "did electrical",
      "power city",
      "powercity",
      "harvey norman",
      "euronics",
      "expert",
    ]
  },
  100504: {
    displayName: "Small Appliances",
    keywords: [
      "toaster",
      "kettle",
      "blender",
      "microwave",
      "vacuum",
      "coffee machine",
      "air fryer",
      "mixer",
      // Adding some common misspellings and variations

      "small",
      "appliances",
      "smallappliances",
      "applinces",
      "small applinces",
      "appliaances",
    ],
    merchantHints: [
      "currys",
      "did electrical",
      "power city",
      "powercity",
      "harvey norman",
      "euronics",
      "expert",
    ]
  },
  100505: {
    displayName: "Home Office Furniture",
    keywords: [
      "desk",
      "office chair",
      "filing cabinet",
      "monitor stand",
      "office shelf",
      "workstation",
      "study desk",
      // Adding some common misspellings and variations

      "home",
      "furniture",
      "homeofficefurniture",
      "furnture",
      "home office furnture",
      "furniiture",
    ]
  },
  100506: {
    displayName: "Decor & Furnishings",
    keywords: [
      "decor",
      "furnishings",
      "ornaments",
      "vase",
      "wall art",
      "lamp",
      "mirror",
      "throw",
      "cushions",
      // Adding some common misspellings and variations

      "decorfurnishings",
      "furnihings",
      "decor and furnihings",
      "furnisshings",
      "decor and furnisshings",
      "furnsihings",
    ],
    merchantHints: [
      "ikea",
      "jysk",
      "home store and more",
      "homestore and more",
      "homestore + more",
      "dunnes home",
      "harvey norman",
    ]
  },
  100507: {
    displayName: "Curtains / Blinds",
    keywords: [
      "curtains",
      "blinds",
      "drapes",
      "blackout curtains",
      "roller blinds",
      "window covering",
      "curtain rail",
      // Adding some common misspellings and variations

      "curtainsblinds",
      "curtins",
      "curtins blinds",
      "curtaains",
      "curtaains blinds",
      "curatins",
    ],
    merchantHints: [
      "ikea",
      "jysk",
      "home store and more",
      "homestore and more",
      "dunelm",
      "harvey norman",
    ]
  },
  100508: {
    displayName: "Rugs & Carpets",
    keywords: [
      "rug",
      "carpet",
      "runner",
      "floor mat",
      "hallway rug",
      "area rug",
      "carpet fitting",
      // Adding some common misspellings and variations

      "rugs",
      "carpets",
      "rugscarpets",
      "carets",
      "rugs and carets",
      "carppets",
    ],
    merchantHints: [
      "ikea",
      "jysk",
      "home store and more",
      "homestore and more",
      "harvey norman",
      "ez living",
    ]
  },
  100601: {
    displayName: "Moving Company",
    keywords: [
      "movers",
      "moving company",
      "removal company",
      "relocation service",
      "house move",
      "moving truck",
      // Adding some common misspellings and variations

      "movingcompany",
      "comany",
      "moving comany",
      "comppany",
      "moving comppany",
      "copmany",
    ],
    aliases: [
      "moving out",
      "moving house",
      "house move",
      "moving day",
      "hire movers",
      "removals bill",
    ],
    merchantHints: [
      "carey movers",
      "moveworks",
    ]
  },
  100602: {
    displayName: "Van Rental",
    keywords: [
      "van rental",
      "rental van",
      "moving van",
      "hire van",
      "transport van",
      "removals van",
      // Adding some common misspellings and variations

      "renal",
      "renttal",
      "retnal",
    ]
  },
  100603: {
    displayName: "Packing Supplies",
    keywords: [
      "boxes",
      "packing tape",
      "bubble wrap",
      "packing supplies",
      "moving boxes",
      "labels",
      "wrapping paper",
      // Adding some common misspellings and variations

      "pacing",
      "packking",
      "pakcing",
      "boes",
      "boxxes",
    ]
  },
  100604: {
    displayName: "Storage Unit",
    keywords: [
      "storage",
      "storage unit",
      "self storage",
      "locker",
      "warehouse storage",
      "temporary storage",
      // Adding some common misspellings and variations

      "storageunit",
      "stoage",
      "stoage unit",
      "storrage",
      "storrage unit",
      "stroage",
    ]
  },
  100605: {
    displayName: "Temporary Accommodation",
    keywords: [
      "temporary accommodation",
      "short stay",
      "temporary housing",
      "hotel while moving",
      "Airbnb during move",
      // Adding some common misspellings and variations

      "temporaryaccommodation",
      "accommdation",
      "temporary accommdation",
      "accommoodation",
      "temporary accommoodation",
      "accomomdation",
    ],
    aliases: [
      "temporary stay while moving",
      "hotel while moving",
      "short stay while moving",
      "airbnb during move",
      "move out stay",
      "stay while moving",
    ],
    merchantHints: [
      "airbnb",
      "booking com",
    ]
  },
  100606: {
    displayName: "New Home Setup Costs",
    keywords: [
      "new home setup",
      "move-in costs",
      "setup fees",
      "first home essentials",
      "utility start-up",
      "move-in expense",
      // Adding some common misspellings and variations

      "homesetup",
      "seup",
      "new home seup costs",
      "settup",
      "new home settup costs",
      "steup",
    ]
  },
  100701: {
    displayName: "Home Security System",
    keywords: [
      "home security",
      "alarm system",
      "burglar alarm",
      "security package",
      "smart alarm",
      "monitored alarm",
      // Adding some common misspellings and variations

      "homesecuritysystem",
      "secuity",
      "home secuity system",
      "securrity",
      "home securrity system",
      "secruity",
    ]
  },
  100702: {
    displayName: "Monitoring Fees",
    keywords: [
      "monitoring fee",
      "alarm monitoring",
      "security subscription",
      "surveillance fee",
      "monthly alarm fee",
      // Adding some common misspellings and variations

      "monitring",
      "monitooring",
      "moniotring",
    ]
  },
  100703: {
    displayName: "CCTV / Cameras",
    keywords: [
      "CCTV",
      "camera",
      "security camera",
      "video doorbell",
      "surveillance camera",
      "outdoor camera",
      // Adding some common misspellings and variations

      "cameras",
      "cctvcameras",
      "camras",
      "cctv camras",
      "cameeras",
      "cctv cameeras",
    ]
  },
  100704: {
    displayName: "Alarm Maintenance",
    keywords: [
      "alarm maintenance",
      "alarm repair",
      "sensor replacement",
      "security servicing",
      "camera servicing",
      // Adding some common misspellings and variations

      "alarmmaintenance",
      "maintnance",
      "alarm maintnance",
      "mainteenance",
      "alarm mainteenance",
      "mainetnance",
    ]
  },
  100705: {
    displayName: "Key Cutting",
    keywords: [
      "key cutting",
      "duplicate key",
      "spare key",
      "key copy",
      "lock key",
      "key duplication",
      // Adding some common misspellings and variations

      "cuting",
      "cuttting",
      "cuttin",
    ]
  },
  100706: {
    displayName: "Concierge / Building Services",
    keywords: [
      "concierge",
      "building services",
      "porter",
      "apartment services",
      "resident services",
      "front desk services",
      // Adding some common misspellings and variations

      "conciergebuilding",
      "concerge",
      "concerge building services",
      "conciierge",
      "conciierge building services",
      "conicerge",
    ]
  },
  100801: {
    displayName: "Housing Miscellaneous",
    keywords: [
      "housing misc",
      "home misc",
      "property misc",
      "housing other",
      "uncategorized home cost",
      // Adding some common misspellings and variations

      "houing",
      "houssing",
      "hosuing",
    ]
  },
  100802: {
    displayName: "Housing Fines / Penalties",
    keywords: [
      "housing fine",
      "penalty",
      "lease penalty",
      "property fine",
      "building fine",
      "housing charge penalty",
      // Adding some common misspellings and variations

      "fines",
      "penalties",
      "housingfinespenalties",
      "penaties",
      "housing fines penaties",
      "penallties",
    ]
  },
  100803: {
    displayName: "Unclassified Housing Expense",
    keywords: [
      "unknown housing",
      "unclassified housing",
      "uncategorized housing",
      "other housing expense",
      // Adding some common misspellings and variations

      "unclassifiedhousing",
      "unclasified",
      "unclasified housing expense",
      "unclasssified",
      "unclasssified housing expense",
      "unclassifeid",
    ]
  },
  110101: {
    displayName: "Cleaning Supplies",
    keywords: [
      "cleaning supplies",
      "bleach",
      "detergent",
      "spray",
      "mop",
      "disinfectant",
      "wipes",
      "cleaning products",
      // Adding some common misspellings and variations

      "cleaing",
      "cleanning",
      "clenaing",
    ],
    merchantHints: [
      "mr price",
      "home store and more",
      "homestore and more",
      "ikea",
      "woodies",
    ]
  },
  110102: {
    displayName: "Paper Products",
    keywords: [
      "toilet paper",
      "kitchen roll",
      "paper towels",
      "tissues",
      "napkins",
      "paper products",
      // Adding some common misspellings and variations

      "paperproducts",
      "prodcts",
      "paper prodcts",
      "produucts",
      "paper produucts",
      "proudcts",
    ]
  },
  110103: {
    displayName: "Laundry Supplies",
    keywords: [
      "laundry detergent",
      "fabric softener",
      "stain remover",
      "dryer sheets",
      "laundry pods",
      "washing powder",
      // Adding some common misspellings and variations

      "laudry",
      "launndry",
      "lanudry",
      "detegent",
      "deterrgent",
    ]
  },
  110104: {
    displayName: "Kitchen Consumables",
    keywords: [
      "bin bags",
      "cling film",
      "foil",
      "baking paper",
      "sponges",
      "dish soap",
      "freezer bags",
      "food storage bags",
      // Adding some common misspellings and variations

      "kitchen",
      "consumables",
      "kitchenconsumables",
      "consuables",
      "kitchen consuables",
      "consummables",
    ],
    merchantHints: [
      "ikea",
      "home store and more",
      "homestore and more",
      "woodies",
      "dunnes stores",
    ]
  },
  110105: {
    displayName: "Storage & Organization",
    keywords: [
      "storage box",
      "organizer",
      "basket",
      "containers",
      "shelving bins",
      "drawer inserts",
      "home organization",
      // Adding some common misspellings and variations

      "storageorganization",
      "organiation",
      "storage and organiation",
      "organizzation",
      "storage and organizzation",
      "organziation",
    ],
    merchantHints: [
      "ikea",
      "jysk",
      "home store and more",
      "homestore and more",
      "woodies",
    ]
  },
  110106: {
    displayName: "Light Bulbs & Batteries",
    keywords: [
      "bulb",
      "light bulb",
      "battery",
      "AA",
      "AAA",
      "LED bulb",
      "rechargeable battery",
      // Adding some common misspellings and variations

      "bulbs",
      "batteries",
      "lightbulbsbatteries",
      "battries",
      "light bulbs and battries",
      "batteeries",
    ]
  },
  110107: {
    displayName: "Home Safety Supplies",
    keywords: [
      "smoke alarm battery",
      "fire extinguisher",
      "carbon monoxide detector",
      "first aid kit",
      "safety lock",
      // Adding some common misspellings and variations

      "home",
      "homesafety",
      "safty",
      "home safty supplies",
      "safeety",
      "home safeety supplies",
    ]
  },
  110108: {
    displayName: "Pest Control Supplies",
    keywords: [
      "pest spray",
      "mouse trap",
      "ant bait",
      "roach killer",
      "pest powder",
      "fly spray",
      "bug repellent",
      // Adding some common misspellings and variations

      "control",
      "pestcontrol",
      "conrol",
      "pest conrol supplies",
      "conttrol",
      "pest conttrol supplies",
    ]
  },
  110201: {
    displayName: "Artwork",
    keywords: [
      "art",
      "artwork",
      "painting",
      "print",
      "poster",
      "framed art",
      "wall art",
      // Adding some common misspellings and variations

      "artork",
      "artwwork",
      "arwtork",
    ]
  },
  110202: {
    displayName: "Decorative Items",
    keywords: [
      "decor item",
      "ornament",
      "figurine",
      "centerpiece",
      "home accessory",
      "decorative piece",
      // Adding some common misspellings and variations

      "items",
      "decorativeitems",
      "decortive",
      "decortive items",
      "decoraative",
      "decoraative items",
    ]
  },
  110203: {
    displayName: "Candles",
    keywords: [
      "candle",
      "scented candle",
      "tealight",
      "wax melt",
      "diffuser candle",
      // Adding some common misspellings and variations

      "candles",
      "canles",
      "canddles",
      "cadnles",
      "canle",
      "canddle",
    ],
    merchantHints: [
      "yankee candle",
      "rituals",
      "homestore and more",
      "home store and more",
    ]
  },
  110204: {
    displayName: "Seasonal Decorations",
    keywords: [
      "christmas decorations",
      "halloween decor",
      "easter decor",
      "festive decor",
      "seasonal ornaments",
      // Adding some common misspellings and variations

      "seasonaldecorations",
      "decortions",
      "seasonal decortions",
      "decoraations",
      "seasonal decoraations",
      "decoartions",
    ]
  },
  110205: {
    displayName: "Wall Fixtures",
    keywords: [
      "shelf bracket",
      "wall hook",
      "coat hook",
      "wall mount",
      "picture rail",
      "mirror fixing",
      // Adding some common misspellings and variations

      "fixtures",
      "wallfixtures",
      "fixtres",
      "wall fixtres",
      "fixtuures",
      "wall fixtuures",
    ]
  },
  110206: {
    displayName: "Soft Furnishings",
    keywords: [
      "cushion",
      "throw",
      "blanket",
      "duvet cover",
      "pillow cover",
      "soft furnishing",
      // Adding some common misspellings and variations

      "furnishings",
      "softfurnishings",
      "furnihings",
      "soft furnihings",
      "furnisshings",
      "soft furnisshings",
    ]
  },
  110207: {
    displayName: "Decorative Lighting",
    keywords: [
      "lamp",
      "fairy lights",
      "accent light",
      "mood lighting",
      "bedside lamp",
      "decorative lamp",
      // Adding some common misspellings and variations

      "decorativelighting",
      "decortive",
      "decortive lighting",
      "decoraative",
      "decoraative lighting",
      "decoartive",
    ]
  },
  110301: {
    displayName: "Paint",
    keywords: [
      "paint",
      "wall paint",
      "primer",
      "gloss",
      "emulsion",
      "wood paint",
      "paint supplies",
      // Adding some common misspellings and variations

      "pant",
      "paiint",
      "piant",
    ],
    merchantHints: [
      "woodies",
      "b and q",
      "b&q",
      "homebase",
      "screwfix",
    ]
  },
  110302: {
    displayName: "Hardware",
    keywords: [
      "screws",
      "nails",
      "hinges",
      "brackets",
      "bolts",
      "tools hardware",
      "fixings",
      // Adding some common misspellings and variations

      "hardare",
      "hardwware",
      "harwdare",
      "scrws",
      "screews",
    ],
    merchantHints: [
      "woodies",
      "b and q",
      "b&q",
      "screwfix",
      "homebase",
      "chadwicks",
    ]
  },
  110303: {
    displayName: "Power Tools",
    keywords: [
      "drill",
      "saw",
      "sander",
      "grinder",
      "power tool",
      "impact driver",
      "jigsaw",
      // Adding some common misspellings and variations

      "tools",
      "powertools",
      "poer",
      "poer tools",
      "powwer",
      "powwer tools",
    ],
    merchantHints: [
      "screwfix",
      "woodies",
      "b and q",
      "b&q",
      "homebase",
    ]
  },
  110304: {
    displayName: "Building Materials",
    keywords: [
      "timber",
      "cement",
      "plaster",
      "tiles",
      "bricks",
      "boards",
      "construction material",
      // Adding some common misspellings and variations

      "building",
      "materials",
      "buildingmaterials",
      "mateials",
      "building mateials",
      "materrials",
    ],
    merchantHints: [
      "chadwicks",
      "woodies",
      "b and q",
      "b&q",
      "screwfix",
    ]
  },
  110305: {
    displayName: "Lighting Fixtures",
    keywords: [
      "light fitting",
      "ceiling light",
      "pendant light",
      "lamp fixture",
      "wall light",
      "light install",
      // Adding some common misspellings and variations

      "lighting",
      "fixtures",
      "lightingfixtures",
      "fixtres",
      "lighting fixtres",
      "fixtuures",
    ],
    merchantHints: [
      "ikea",
      "woodies",
      "b and q",
      "b&q",
      "screwfix",
    ]
  },
  110306: {
    displayName: "Bathroom Fixtures",
    keywords: [
      "tap",
      "faucet",
      "shower head",
      "towel rail",
      "toilet seat",
      "bathroom fitting",
      // Adding some common misspellings and variations

      "fixtures",
      "bathroomfixtures",
      "bathoom",
      "bathoom fixtures",
      "bathrroom",
      "bathrroom fixtures",
    ],
    merchantHints: [
      "woodies",
      "b and q",
      "b&q",
      "screwfix",
      "homebase",
    ]
  },
  110307: {
    displayName: "Kitchen Fixtures",
    keywords: [
      "sink",
      "tap",
      "backsplash",
      "cabinet handle",
      "hob fitting",
      "kitchen fitting",
      // Adding some common misspellings and variations

      "fixtures",
      "kitchenfixtures",
      "fixtres",
      "kitchen fixtres",
      "fixtuures",
      "kitchen fixtuures",
    ],
    merchantHints: [
      "ikea",
      "woodies",
      "b and q",
      "b&q",
      "screwfix",
    ]
  },
  110308: {
    displayName: "Adhesives / Sealants",
    keywords: [
      "sealant",
      "silicone",
      "glue",
      "adhesive",
      "caulk",
      "filler",
      "bonding",
      // Adding some common misspellings and variations

      "adhesives",
      "sealants",
      "adhesivessealants",
      "adheives",
      "adheives sealants",
      "adhessives",
    ],
    merchantHints: [
      "screwfix",
      "woodies",
      "b and q",
      "b&q",
      "homebase",
    ]
  },
  110309: {
    displayName: "Fasteners / Fixings",
    keywords: [
      "wall plug",
      "anchor",
      "screw",
      "bolt",
      "fastening",
      "mounting hardware",
      // Adding some common misspellings and variations

      "fasteners",
      "fixings",
      "fastenersfixings",
      "fastners",
      "fastners fixings",
      "fasteeners",
    ],
    merchantHints: [
      "screwfix",
      "woodies",
      "b and q",
      "b&q",
      "chadwicks",
    ]
  },
  110401: {
    displayName: "Plants",
    keywords: [
      "plants",
      "houseplants",
      "shrubs",
      "flowers",
      "indoor plant",
      "outdoor plant",
      // Adding some common misspellings and variations

      "plats",
      "plannts",
      "plnats",
    ]
  },
  110402: {
    displayName: "Soil / Compost",
    keywords: [
      "soil",
      "compost",
      "potting mix",
      "mulch",
      "fertilizer base",
      "garden soil",
      // Adding some common misspellings and variations

      "soilcompost",
      "comost",
      "soil comost",
      "comppost",
      "soil comppost",
      "copmost",
    ]
  },
  110403: {
    displayName: "Seeds",
    keywords: [
      "seeds",
      "grass seed",
      "vegetable seeds",
      "flower seeds",
      "plant starter",
      // Adding some common misspellings and variations

      "seds",
      "seeeds",
    ]
  },
  110404: {
    displayName: "Garden Tools",
    keywords: [
      "rake",
      "shovel",
      "trowel",
      "pruning shears",
      "watering can",
      "garden tools",
      // Adding some common misspellings and variations

      "gardentools",
      "garen",
      "garen tools",
      "gardden",
      "gardden tools",
      "gadren",
    ]
  },
  110405: {
    displayName: "Lawn Care",
    keywords: [
      "lawn mower",
      "grass cutting",
      "lawn feed",
      "weed killer",
      "lawn care",
      "grass maintenance",
      // Adding some common misspellings and variations

      "lawncare",
      "cae",
      "lawn cae",
      "carre",
      "lawn carre",
      "crae",
    ]
  },
  110406: {
    displayName: "Outdoor Furniture",
    keywords: [
      "patio furniture",
      "garden chair",
      "outdoor table",
      "deck furniture",
      "bench",
      // Adding some common misspellings and variations

      "outdoorfurniture",
      "furnture",
      "outdoor furnture",
      "furniiture",
      "outdoor furniiture",
      "furinture",
    ]
  },
  110407: {
    displayName: "BBQ / Patio Supplies",
    keywords: [
      "BBQ",
      "grill",
      "charcoal",
      "patio heater",
      "grilling tools",
      "outdoor cooking",
      // Adding some common misspellings and variations

      "paio",
      "pattio",
      "ptaio",
    ]
  },
  110408: {
    displayName: "Pots / Planters",
    keywords: [
      "pot",
      "planter",
      "flower pot",
      "hanging basket",
      "plant container",
      // Adding some common misspellings and variations

      "pots",
      "planters",
      "potsplanters",
      "planers",
      "pots planers",
      "plantters",
    ]
  },
  110409: {
    displayName: "Watering Systems",
    keywords: [
      "hose",
      "sprinkler",
      "irrigation",
      "watering system",
      "watering timer",
      "hose reel",
      // Adding some common misspellings and variations

      "systems",
      "wateringsystems",
      "wateing",
      "wateing systems",
      "waterring",
      "waterring systems",
    ]
  },
  110501: {
    displayName: "Snow Removal Supplies",
    keywords: [
      "snow shovel",
      "salt",
      "de-icer",
      "snow brush",
      "winter grit",
      "snow melt",
      // Adding some common misspellings and variations

      "removal",
      "snowremoval",
      "remval",
      "snow remval supplies",
      "remooval",
      "snow remooval supplies",
    ]
  },
  110502: {
    displayName: "Gutter Cleaning Supplies",
    keywords: [
      "gutter cleaner",
      "gutter brush",
      "gutter scoop",
      "leaf removal",
      "gutter maintenance",
      // Adding some common misspellings and variations

      "cleaning",
      "guttercleaning",
      "cleaing",
      "gutter cleaing supplies",
      "cleanning",
      "gutter cleanning supplies",
    ]
  },
  110503: {
    displayName: "Tree Trimming Services",
    keywords: [
      "tree trimming",
      "tree cutting",
      "arborist",
      "branch removal",
      "hedge cutting",
      // Adding some common misspellings and variations

      "treetrimming",
      "triming",
      "tree triming services",
      "trimmming",
      "tree trimmming services",
      "trimmin",
    ]
  },
  110504: {
    displayName: "Landscaping",
    keywords: [
      "landscaping",
      "garden design",
      "turf laying",
      "paving",
      "outdoor makeover",
      // Adding some common misspellings and variations

      "landsaping",
      "landsccaping",
      "landcsaping",
    ]
  },
  110505: {
    displayName: "Pool Maintenance",
    keywords: [
      "pool chemicals",
      "pool cleaning",
      "pool pump",
      "filter replacement",
      "pool service",
      // Adding some common misspellings and variations

      "maintenance",
      "poolmaintenance",
      "maintnance",
      "pool maintnance",
      "mainteenance",
      "pool mainteenance",
    ]
  },
  110506: {
    displayName: "Outdoor Cleaning",
    keywords: [
      "pressure washing",
      "patio cleaning",
      "deck cleaning",
      "outdoor wash",
      "driveway cleaning",
      // Adding some common misspellings and variations

      "outdoorcleaning",
      "cleaing",
      "outdoor cleaing",
      "cleanning",
      "outdoor cleanning",
      "clenaing",
    ]
  },
  110507: {
    displayName: "Fence / Shed Upkeep",
    keywords: [
      "fence repair",
      "shed maintenance",
      "fence paint",
      "shed roof",
      "gate repair",
      // Adding some common misspellings and variations

      "upkeep",
      "fenceshedupkeep",
      "upkep",
      "fence shed upkep",
      "upkeeep",
      "fence shed upkeeep",
    ]
  },
  110601: {
    displayName: "DIY Projects",
    keywords: [
      "DIY",
      "do it yourself",
      "project materials",
      "craft build",
      "self-build",
      "home project",
      // Adding some common misspellings and variations

      "projects",
      "projcts",
      "projeects",
      "proejcts",
    ]
  },
  110602: {
    displayName: "Home Workshop Supplies",
    keywords: [
      "workshop",
      "clamps",
      "sandpaper",
      "workbench supplies",
      "toolbox refills",
      "workshop materials",
      // Adding some common misspellings and variations

      "home",
      "homeworkshop",
      "workhop",
      "home workhop supplies",
      "worksshop",
      "home worksshop supplies",
    ]
  },
  110603: {
    displayName: "Craft / Build Materials for Home Use",
    keywords: [
      "plywood",
      "board",
      "craft wood",
      "resin",
      "build materials",
      "project materials",
      // Adding some common misspellings and variations

      "craftbuildmaterialshome",
      "mateials",
      "craft build mateials for home use",
      "materrials",
      "craft build materrials for home use",
      "matreials",
    ]
  },
  110604: {
    displayName: "Repairs Tools",
    keywords: [
      "wrench",
      "screwdriver",
      "pliers",
      "hammer",
      "socket set",
      "repair tools",
      // Adding some common misspellings and variations

      "repairs",
      "repairstools",
      "repirs",
      "repirs tools",
      "repaairs",
      "repaairs tools",
    ]
  },
  110605: {
    displayName: "Utility Tool Replacements",
    keywords: [
      "replacement blade",
      "drill bit",
      "saw blade",
      "tool battery",
      "tool charger",
      // Adding some common misspellings and variations

      "utility",
      "replacements",
      "utilitytoolreplacements",
      "replacments",
      "utility tool replacments",
      "replaceements",
    ]
  },
  110606: {
    displayName: "Home Miscellaneous",
    keywords: [
      "home misc",
      "house supplies other",
      "miscellaneous home",
      "uncategorized home use",
      // Adding some common misspellings and variations

      "hoe",
      "homme",
      "hmoe",
    ]
  },
  110701: {
    displayName: "Home & Garden Miscellaneous",
    keywords: [
      "home and garden misc",
      "house and garden other",
      "uncategorized home and garden",
      // Adding some common misspellings and variations

      "homegarden",
      "garen",
      "home and garen miscellaneous",
      "gardden",
      "home and gardden miscellaneous",
      "gadren",
    ]
  },
  110702: {
    displayName: "Unclassified Home & Garden Expense",
    keywords: [
      "unknown home garden",
      "unclassified home garden",
      "other home and garden expense",
      // Adding some common misspellings and variations

      "unclassifiedhomegarden",
      "unclasified",
      "unclasified home and garden expense",
      "unclasssified",
      "unclasssified home and garden expense",
      "unclassifeid",
    ]
  },
  120101: {
    displayName: "Bus",
    keywords: [
      "bus",
      "bus fare",
      "bus ticket",
      "coach local",
      "transit bus",
      "bus pass",
    ],
    aliases: [
      "bus fare",
      "bus pass",
      "local bus",
      "commuter bus",
      "city bus",
      "public bus",
    ],
    merchantHints: [
      "dublin bus",
      "go ahead",
      "go-ahead",
      "goahead",
      "bus eireann",
    ]
  },
  120102: {
    displayName: "Train",
    keywords: [
      "train",
      "rail",
      "railway",
      "train ticket",
      "rail fare",
      "commuter rail",
      // Adding some common misspellings and variations

      "trin",
      "traain",
      "tarin",
    ],
    aliases: [
      "irish train",
      "commuter train",
      "rail fare",
      "rail ticket ireland",
      "train into town",
      "train journey",
    ],
    merchantHints: [
      "irish rail",
      "iarnrod eireann",
      "iarnrod",
      "iarnrod eirean",
      "iarnrod eireann dart",
      "dart",
    ]
  },
  120103: {
    displayName: "Tram / Metro / Luas",
    keywords: [
      "tram",
      "metro",
      "luas",
      "subway light rail",
      "light rail",
      "metro fare",
      // Adding some common misspellings and variations

      "trammetroluas",
      "mero",
      "tram mero luas",
      "mettro",
      "tram mettro luas",
      "mtero",
    ],
    aliases: [
      "light rail",
      "city tram",
      "metro ride",
      "tram fare",
      "commuter tram",
      "urban rail",
    ],
    merchantHints: [
      "luas",
      "bart",
      "mta",
    ]
  },
  120104: {
    displayName: "Subway",
    keywords: [
      "subway",
      "underground",
      "tube",
      "metro underground",
      "subway fare",
      // Adding some common misspellings and variations

      "subay",
      "subwway",
      "suwbay",
    ],
    aliases: [
      "underground train",
      "metro underground",
      "city subway",
      "tube fare",
      "subway ride",
      "metro station",
    ],
    merchantHints: [
      "tube",
      "underground",
      "subway",
      "mta",
      "bart",
    ]
  },
  120105: {
    displayName: "Ferry",
    keywords: [
      "ferry",
      "boat transport",
      "ferry ticket",
      "crossing",
      "water transit",
      // Adding some common misspellings and variations

      "fery",
      "ferrry",
      "frery",
    ]
  },
  120106: {
    displayName: "Transit Passes",
    keywords: [
      "transit pass",
      "weekly pass",
      "monthly pass",
      "railcard",
      "bus pass",
      "travelcard",
      // Adding some common misspellings and variations

      "passes",
      "transitpasses",
      "trasit",
      "trasit passes",
      "trannsit",
      "trannsit passes",
    ]
  },
  120107: {
    displayName: "Park & Ride",
    keywords: [
      "park and ride",
      "park ride",
      "commuter parking",
      "transit parking",
      // Adding some common misspellings and variations

      "parkride",
      "pak",
      "pak and ride",
      "parrk",
      "parrk and ride",
      "prak",
    ]
  },
  120108: {
    displayName: "Taxi / Ride-hailing",
    keywords: [
      "taxi",
      "cab",
      "uber",
      "bolt",
      "lyft",
      "ride-hailing",
      "ride fare",
      // Adding some common misspellings and variations

      "taxiridehailing",
      "haiing",
      "taxi ride haiing",
      "hailling",
      "taxi ride hailling",
      "haliing",
    ],
    aliases: [
      "rideshare",
      "ride share",
      "cab app",
      "taxi app",
      "book an uber",
      "car home",
      "ride home",
    ],
    merchantHints: [
      "uber",
      "lyft",
      "bolt",
      "grab",
    ]
  },
  120201: {
    displayName: "Petrol",
    keywords: [
      "petrol",
      "gas",
      "gasoline",
      "fuel",
      "unleaded",
      "premium fuel",
      "filling station",
      // Adding some common misspellings and variations

      "petrolgasoline",
      "gasoine",
      "petrol gasoine",
      "gasolline",
      "petrol gasolline",
      "gasloine",
    ],
    aliases: [
      "fill up the tank",
      "fuel up",
      "top up petrol",
      "gas money",
      "paying for gas",
      "petrol station",
      "filling up",
    ],
    merchantHints: [
      "applegreen",
      "circle k",
      "texaco",
      "maxol",
      "shell",
      "bp",
      "esso",
      "aral",
      "chevron",
    ]
  },
  120202: {
    displayName: "Diesel",
    keywords: [
      "diesel",
      "fuel diesel",
      "diesel station",
      "diesel fill-up",
      // Adding some common misspellings and variations

      "dieel",
      "diessel",
      "diseel",
    ]
  },
  120203: {
    displayName: "EV Charging",
    keywords: [
      "EV charging",
      "electric charging",
      "charge point",
      "supercharger",
      "battery charging",
      // Adding some common misspellings and variations

      "charing",
      "chargging",
      "chagring",
    ]
  },
  120204: {
    displayName: "AdBlue / Fluids",
    keywords: [
      "adblue",
      "engine fluid",
      "coolant",
      "washer fluid",
      "transmission fluid",
      "car fluid",
      // Adding some common misspellings and variations

      "fluids",
      "adbluefluids",
      "adbue",
      "adbue fluids",
      "adbllue",
      "adbllue fluids",
    ]
  },
  120205: {
    displayName: "Petrol Station Convenience Purchases",
    keywords: [
      "fuel station shop",
      "petrol station snack",
      "service station purchase",
      "forecourt shop",
      // Adding some common misspellings and variations

      "convenience",
      "conveience",
      "fuel station conveience purchases",
      "convennience",
      "fuel station convennience purchases",
      "convneience",
    ],
    aliases: [
      "service station shop",
      "garage shop",
      "petrol station snack run",
      "forecourt snacks",
      "garage snacks",
      "fuel stop shop",
    ],
    merchantHints: [
      "applegreen",
      "circle k",
      "texaco",
      "maxol",
      "shell",
    ]
  },
  120301: {
    displayName: "Car Loan",
    keywords: [
      "car loan",
      "auto loan",
      "vehicle finance",
      "car payment",
      "vehicle financing",
      // Adding some common misspellings and variations

      "lon",
      "loaan",
      "laon",
    ]
  },
  120302: {
    displayName: "Lease Payment",
    keywords: [
      "lease",
      "lease payment",
      "car lease",
      "vehicle lease",
      "monthly lease",
      // Adding some common misspellings and variations

      "lese",
      "leaase",
      "laese",
    ]
  },
  120303: {
    displayName: "Registration",
    keywords: [
      "registration",
      "vehicle registration",
      "reg fee",
      "vehicle papers",
      "registration charge",
      // Adding some common misspellings and variations

      "registation",
      "registrration",
      "regisrtation",
    ]
  },
  120304: {
    displayName: "Road Tax / Motor Tax",
    keywords: [
      "road tax",
      "motor tax",
      "vehicle tax",
      "car tax",
      "annual tax disc",
      // Adding some common misspellings and variations

      "roadmotor",
      "moor",
      "road tax moor tax",
      "mottor",
      "road tax mottor tax",
      "mtoor",
    ]
  },
  120305: {
    displayName: "Vehicle Inspection / NCT / MOT",
    keywords: [
      "inspection",
      "NCT",
      "MOT",
      "emissions test",
      "roadworthiness test",
      "vehicle test",
      // Adding some common misspellings and variations

      "vehicleinspection",
      "inspetion",
      "vehicle inspetion nct mot",
      "inspecction",
      "vehicle inspecction nct mot",
      "inspcetion",
    ],
    merchantHints: [
      "nct",
      "nct centre",
      "nct center",
      "mot",
      "cvrt",
    ]
  },
  120306: {
    displayName: "Parking Permits",
    keywords: [
      "parking permit",
      "resident permit",
      "permit parking",
      "parking sticker",
      "permit fee",
      // Adding some common misspellings and variations

      "permits",
      "parkingpermits",
      "paring",
      "paring permits",
      "parkking",
      "parkking permits",
    ]
  },
  120307: {
    displayName: "Toll Tags / Devices",
    keywords: [
      "toll tag",
      "e-toll",
      "toll device",
      "transponder",
      "road toll tag",
      // Adding some common misspellings and variations

      "tags",
      "devices",
      "tolltagsdevices",
      "devces",
      "toll tags devces",
      "deviices",
    ]
  },
  120401: {
    displayName: "Routine Service",
    keywords: [
      "service",
      "annual service",
      "vehicle service",
      "maintenance service",
      "tune-up",
      // Adding some common misspellings and variations

      "routine",
      "rouine",
      "routtine",
      "rotuine",
    ],
    aliases: [
      "service check",
      "garage invoice",
      "mechanic bill",
      "my car is acting up",
      "car service",
      "garage service",
      "mechanic visit",
    ],
    merchantHints: [
      "halfords",
      "kwik fit",
      "midas",
    ]
  },
  120402: {
    displayName: "Tires",
    keywords: [
      "tires",
      "tyres",
      "wheel",
      "tire replacement",
      "puncture",
      "alignment",
      "balancing",
      // Adding some common misspellings and variations

      "ties",
      "tirres",
      "tries",
    ]
  },
  120403: {
    displayName: "Brakes",
    keywords: [
      "brakes",
      "brake pads",
      "brake discs",
      "brake fluid",
      "braking service",
      // Adding some common misspellings and variations

      "braes",
      "brakkes",
      "brkaes",
    ]
  },
  120404: {
    displayName: "Oil Change",
    keywords: [
      "oil change",
      "engine oil",
      "oil service",
      "oil filter",
      // Adding some common misspellings and variations

      "chage",
      "channge",
      "chnage",
    ]
  },
  120405: {
    displayName: "Battery Replacement",
    keywords: [
      "battery",
      "car battery",
      "battery replacement",
      "jump start battery",
      // Adding some common misspellings and variations

      "batteryreplacement",
      "replaement",
      "battery replaement",
      "replaccement",
      "battery replaccement",
      "replcaement",
    ]
  },
  120406: {
    displayName: "Bodywork",
    keywords: [
      "bodywork",
      "dent repair",
      "scratch repair",
      "panel repair",
      "paint repair",
      // Adding some common misspellings and variations

      "bodyork",
      "bodywwork",
      "bodwyork",
    ]
  },
  120407: {
    displayName: "Windshield / Glass",
    keywords: [
      "windshield",
      "windscreen",
      "glass repair",
      "chip repair",
      "window replacement",
      // Adding some common misspellings and variations

      "windshieldglass",
      "windsield",
      "windsield glass",
      "windshhield",
      "windshhield glass",
      "windhsield",
    ]
  },
  120408: {
    displayName: "Breakdown / Roadside Assistance",
    keywords: [
      "roadside assistance",
      "breakdown",
      "tow",
      "towing",
      "recovery service",
      // Adding some common misspellings and variations

      "assisance",
      "breakdown roadside assisance",
      "assisttance",
      "breakdown roadside assisttance",
      "assitsance",
      "breakdown roadside assitsance",
    ],
    aliases: [
      "car broke down",
      "roadside help",
      "tow truck",
      "vehicle recovery",
      "breakdown callout",
      "car wont start",
    ],
    merchantHints: [
      "aa",
      "rac",
    ]
  },
  120409: {
    displayName: "Parts & Accessories",
    keywords: [
      "car parts",
      "mats",
      "accessories",
      "wipers",
      "roof rack",
      "seat covers",
      // Adding some common misspellings and variations

      "partsaccessories",
      "accesories",
      "parts and accesories",
      "accesssories",
      "parts and accesssories",
      "accessoreis",
    ],
    aliases: [
      "car stuff",
      "parts for car",
      "car accessories",
      "garage parts",
      "replacement parts",
      "bits for the car",
    ],
    merchantHints: [
      "halfords",
      "autozone",
    ]
  },
  120410: {
    displayName: "Car Wash / Valeting",
    keywords: [
      "car wash",
      "valet",
      "detailing",
      "hand wash",
      "interior clean",
      "auto wash",
      // Adding some common misspellings and variations

      "valeting",
      "washvaleting",
      "valeing",
      "car wash valeing",
      "valetting",
      "car wash valetting",
    ],
    merchantHints: [
      "circle k",
      "applegreen",
      "maxol",
      "texaco",
    ]
  },
  120501: {
    displayName: "Street Parking",
    keywords: [
      "street parking",
      "meter",
      "parking meter",
      "curb parking",
      "pay and display",
      // Adding some common misspellings and variations

      "streetparking",
      "paring",
      "street paring",
      "parkking",
      "street parkking",
      "pakring",
    ]
  },
  120502: {
    displayName: "Garage / Car Park",
    keywords: [
      "garage parking",
      "car park",
      "parking garage",
      "lot fee",
      // Adding some common misspellings and variations

      "garagepark",
      "garge",
      "garge car park",
      "garaage",
      "garaage car park",
      "gaarge",
    ]
  },
  120503: {
    displayName: "Tolls",
    keywords: [
      "toll",
      "toll road",
      "bridge toll",
      "motorway toll",
      "expressway charge",
      // Adding some common misspellings and variations

      "tolls",
      "tols",
      "tollls",
      "tlols",
      "tol",
      "tolll",
    ]
  },
  120504: {
    displayName: "Congestion Charges",
    keywords: [
      "congestion charge",
      "emissions zone",
      "city driving charge",
      "clean air zone",
      // Adding some common misspellings and variations

      "charges",
      "congestioncharges",
      "congetion",
      "congetion charges",
      "congesstion",
      "congesstion charges",
    ]
  },
  120505: {
    displayName: "Traffic Fines",
    keywords: [
      "traffic fine",
      "speeding ticket",
      "violation",
      "driving fine",
      "road penalty",
      // Adding some common misspellings and variations

      "fines",
      "trafficfines",
      "trafic",
      "trafic fines",
      "trafffic",
      "trafffic fines",
    ]
  },
  120506: {
    displayName: "Clamping / Towing Fees",
    keywords: [
      "clamp",
      "clamping fee",
      "towing fee",
      "impound fee",
      "tow charge",
      // Adding some common misspellings and variations

      "clampingtowing",
      "claming",
      "claming towing fees",
      "clampping",
      "clampping towing fees",
      "clapming",
    ]
  },
  120601: {
    displayName: "Bicycle Purchase",
    keywords: [
      "bicycle",
      "bike purchase",
      "cycle",
      "new bike",
      "road bike",
      "mountain bike",
      // Adding some common misspellings and variations

      "bicyclepurchase",
      "purcase",
      "bicycle purcase",
      "purchhase",
      "bicycle purchhase",
      "purhcase",
    ]
  },
  120602: {
    displayName: "Bicycle Repairs",
    keywords: [
      "bike repair",
      "puncture",
      "chain repair",
      "brake cable",
      "cycle service",
      // Adding some common misspellings and variations

      "bicycle",
      "repairs",
      "bicyclerepairs",
      "biccle",
      "biccle repairs",
      "bicyycle",
    ]
  },
  120603: {
    displayName: "Bicycle Accessories",
    keywords: [
      "helmet",
      "lights",
      "lock",
      "pannier",
      "bike bell",
      "bike accessories",
      // Adding some common misspellings and variations

      "bicycle",
      "bicycleaccessories",
      "accesories",
      "bicycle accesories",
      "accesssories",
      "bicycle accesssories",
    ]
  },
  120604: {
    displayName: "Bike Share",
    keywords: [
      "bike share",
      "city bike",
      "hire bike",
      "rental bike",
      "shared bike",
      // Adding some common misspellings and variations

      "bikeshare",
      "shre",
      "bike shre",
      "shaare",
      "bike shaare",
      "sahre",
    ]
  },
  120605: {
    displayName: "Scooter Rental",
    keywords: [
      "scooter rental",
      "e-scooter",
      "scooter hire",
      "lime scooter",
      "bird scooter",
      // Adding some common misspellings and variations

      "scooterrental",
      "scoter",
      "scoter rental",
      "scoooter",
      "scoooter rental",
      "scootar",
    ]
  },
  120606: {
    displayName: "Scooter Purchase / Repairs",
    keywords: [
      "scooter purchase",
      "scooter repair",
      "e-scooter repair",
      "scooter battery",
      // Adding some common misspellings and variations

      "repairs",
      "scooterpurchaserepairs",
      "purcase",
      "scooter purcase repairs",
      "purchhase",
      "scooter purchhase repairs",
    ]
  },
  120607: {
    displayName: "Helmet / Safety Gear",
    keywords: [
      "helmet",
      "safety vest",
      "pads",
      "reflective gear",
      "cycling safety",
      "scooter safety",
      // Adding some common misspellings and variations

      "helmetsafetygear",
      "helet",
      "helet safety gear",
      "helmmet",
      "helmmet safety gear",
      "hemlet",
    ]
  },
  120701: {
    displayName: "Driving Lessons",
    keywords: [
      "driving lessons",
      "driving school",
      "instructor",
      "learner driver",
      "lessons",
      "driving tuition",
      // Adding some common misspellings and variations

      "drivinglessons",
      "driing",
      "driing lessons",
      "drivving",
      "drivving lessons",
      "drviing",
    ]
  },
  120702: {
    displayName: "Theory Test Fees",
    keywords: [
      "theory test",
      "permit theory",
      "written driving test",
      "learner theory exam",
      // Adding some common misspellings and variations

      "theorytest",
      "thery",
      "thery test fees",
      "theoory",
      "theoory test fees",
      "thoery",
    ]
  },
  120703: {
    displayName: "Driving Test Fees",
    keywords: [
      "driving test",
      "road test",
      "practical test",
      "license test",
      "driving exam",
      // Adding some common misspellings and variations

      "drivingtest",
      "driing",
      "driing test fees",
      "drivving",
      "drivving test fees",
      "drviing",
    ]
  },
  120704: {
    displayName: "Learner Permit",
    keywords: [
      "learner permit",
      "provisional license",
      "learner licence",
      "permit renewal",
      // Adding some common misspellings and variations

      "learnerpermit",
      "leaner",
      "leaner permit",
      "learrner",
      "learrner permit",
      "leraner",
    ]
  },
  120705: {
    displayName: "License Renewal",
    keywords: [
      "license renewal",
      "driving licence renewal",
      "renew license",
      "driving card renewal",
      // Adding some common misspellings and variations

      "licenserenewal",
      "licnse",
      "licnse renewal",
      "liceense",
      "liceense renewal",
      "liecnse",
    ]
  },
  120706: {
    displayName: "Driving School Materials",
    keywords: [
      "learner book",
      "theory app",
      "driving handbook",
      "road signs book",
      "study materials",
      // Adding some common misspellings and variations

      "school",
      "drivingschoolmaterials",
      "mateials",
      "driving school mateials",
      "materrials",
      "driving school materrials",
    ]
  },
  120707: {
    displayName: "Vehicle Licensing Admin Fees",
    keywords: [
      "admin fee",
      "license fee",
      "document fee",
      "vehicle licensing admin",
      "registry admin",
      // Adding some common misspellings and variations

      "vehiclelicensingadmin",
      "licesing",
      "vehicle licesing admin fees",
      "licennsing",
      "vehicle licennsing admin fees",
      "licnesing",
    ]
  },
  120801: {
    displayName: "Flights",
    keywords: [
      "flight",
      "airline",
      "airfare",
      "plane ticket",
      "booking fee",
      "airport flight",
      // Adding some common misspellings and variations

      "flights",
      "flihts",
      "fligghts",
      "flgihts",
      "fliht",
      "fligght",
    ]
  },
  120802: {
    displayName: "Intercity Rail",
    keywords: [
      "intercity rail",
      "long distance train",
      "rail travel",
      "train fare intercity",
      // Adding some common misspellings and variations

      "intercityrail",
      "intecity",
      "intecity rail",
      "interrcity",
      "interrcity rail",
      "intrecity",
    ]
  },
  120803: {
    displayName: "Coach / Long-Distance Bus",
    keywords: [
      "coach",
      "long-distance bus",
      "intercity bus",
      "express bus",
      "coach ticket",
      // Adding some common misspellings and variations

      "coachlongdistance",
      "distnce",
      "coach long distnce bus",
      "distaance",
      "coach long distaance bus",
      "disatnce",
    ]
  },
  120804: {
    displayName: "Car Rental",
    keywords: [
      "car rental",
      "rental car",
      "hire car",
      "vehicle hire",
      // Adding some common misspellings and variations

      "renal",
      "renttal",
      "retnal",
    ]
  },
  120805: {
    displayName: "Airport Transfers",
    keywords: [
      "airport transfer",
      "shuttle",
      "airport taxi",
      "transfer bus",
      "pickup service",
      // Adding some common misspellings and variations

      "transfers",
      "airporttransfers",
      "tranfers",
      "airport tranfers",
      "transsfers",
      "airport transsfers",
    ]
  },
  120806: {
    displayName: "Baggage Fees",
    keywords: [
      "baggage fee",
      "luggage fee",
      "checked bag",
      "carry-on fee",
      // Adding some common misspellings and variations

      "bagage",
      "bagggage",
    ]
  },
  120807: {
    displayName: "Seat Selection / Travel Add-ons",
    keywords: [
      "seat selection",
      "extra legroom",
      "priority boarding",
      "airline add-on",
      "travel extras",
      // Adding some common misspellings and variations

      "seatselectiontravel",
      "seletion",
      "seat seletion travel add ons",
      "selecction",
      "seat selecction travel add ons",
      "selcetion",
    ]
  },
  120901: {
    displayName: "Transportation Miscellaneous",
    keywords: [
      "transport misc",
      "transportation other",
      "commute misc",
      "uncategorized transport",
      // Adding some common misspellings and variations

      "transpotation",
      "transporrtation",
      "transprotation",
      "tranport",
      "transsport",
    ]
  },
  120902: {
    displayName: "Unclassified Transport Expense",
    keywords: [
      "unknown transport",
      "unclassified transportation",
      "other transport expense",
      // Adding some common misspellings and variations

      "unclassifiedtransport",
      "unclasified",
      "unclasified transport expense",
      "unclasssified",
      "unclasssified transport expense",
      "unclassifeid",
    ]
  },
  130101: {
    displayName: "Supermarket",
    keywords: [
      "supermarket",
      "grocery store",
      "grocery shop",
      // Adding some common misspellings and variations

      "superarket",
      "supermmarket",
      "supemrarket",
    ],
    aliases: [
      "grab groceries",
      "grocery run",
      "food shop",
      "big grocery shop",
      "supermarket run",
      "stocking up on food",
    ],
    merchantHints: [
      "dunnes",
      "dunnes stores",
      "tesco",
      "lidl",
      "aldi",
      "supervalu",
      "super valu",
    ]
  },
  130102: {
    displayName: "Convenience Store",
    keywords: [
      "convenience store",
      "corner shop",
      "mini market",
      "spar",
      "centra",
      "small shop",
      // Adding some common misspellings and variations

      "conveniencestore",
      "conveience",
      "conveience store",
      "convennience",
      "convennience store",
      "convneience",
    ]
  },
  130103: {
    displayName: "Fresh Produce",
    keywords: [
      "fruit",
      "vegetables",
      "produce",
      "veg shop",
      "farmers market",
      "fresh food",
      // Adding some common misspellings and variations

      "freshproduce",
      "prouce",
      "fresh prouce",
      "prodduce",
      "fresh prodduce",
      "prdouce",
    ]
  },
  130104: {
    displayName: "Meat & Seafood",
    keywords: [
      "butcher",
      "fishmonger",
      "meat",
      "seafood",
      "chicken",
      "beef",
      "turkey",
      "pork",
      "lamb",
      "rabbit",
      "grill",
      "steak",
      "shellfish",
      "fish",
      // Adding some common misspellings and variations

      "meatseafood",
      "seaood",
      "meat and seaood",
      "seaffood",
      "meat and seaffood",
      "sefaood",
    ]
  },
  130105: {
    displayName: "Dairy & Eggs",
    keywords: [
      "milk",
      "cheese",
      "yogurt",
      "butter",
      "eggs",
      "dairy",
      "cream",
      // Adding some common misspellings and variations

      "dairyeggs",
      "dary",
      "dary and eggs",
      "daiiry",
      "daiiry and eggs",
      "diary",
    ]
  },
  130106: {
    displayName: "Bakery",
    keywords: [
      "bakery",
      "bread",
      "pastries",
      "buns",
      "cake shop",
      "croissant",
      // Adding some common misspellings and variations

      "bakry",
      "bakeery",
      "baekry",
    ]
  },
  130107: {
    displayName: "Frozen Foods",
    keywords: [
      "frozen food",
      "freezer meals",
      "frozen veg",
      "frozen pizza",
      "ice cream",
      // Adding some common misspellings and variations

      "foods",
      "frozenfoods",
      "froen",
      "froen foods",
      "frozzen",
      "frozzen foods",
    ]
  },
  130108: {
    displayName: "Pantry Staples",
    keywords: [
      "rice",
      "pasta",
      "flour",
      "oil",
      "sugar",
      "spices",
      "pantry",
      "dry goods",
      // Adding some common misspellings and variations

      "staples",
      "pantrystaples",
      "stales",
      "pantry stales",
      "stapples",
      "pantry stapples",
    ]
  },
  130109: {
    displayName: "Snacks",
    keywords: [
      "snacks",
      "crisps",
      "chips",
      "chocolate",
      "biscuits",
      "candy",
      "sweets",
      // Adding some common misspellings and variations

      "snaks",
      "snaccks",
      "sncaks",
    ]
  },
  130110: {
    displayName: "Beverages",
    keywords: [
      "drinks",
      "juice",
      "soda",
      "water",
      "soft drinks",
      "fizzy drinks",
      "beverages",
      // Adding some common misspellings and variations

      "beveages",
      "beverrages",
      "bevreages",
      "driks",
      "drinnks",
    ]
  },
  130111: {
    displayName: "Household-Grocery Mixed Basket",
    keywords: [
      "grocery basket",
      "weekly shop",
      "household shopping",
      "mixed groceries",
      "supermarket run",
      // Adding some common misspellings and variations

      "houshold",
      "houshold grocery mixed basket",
      "houseehold",
      "houseehold grocery mixed basket",
      "houeshold",
      "houeshold grocery mixed basket",
    ],
    aliases: [
      "weekly shop",
      "weekly grocery shop",
      "household food shop",
      "big shop",
      "stocking up",
      "full trolley shop",
    ],
    merchantHints: [
      "dunnes",
      "tesco",
      "lidl",
      "aldi",
      "supervalu",
    ]
  },
  130201: {
    displayName: "Restaurant",
    keywords: [
      "restaurant",
      "dinner out",
      "lunch out",
      "dine in",
      "eat out",
      "meal out",
      // Adding some common misspellings and variations

      "restarant",
      "restauurant",
      "restuarant",
    ],
    merchantHints: [
      "nandos",
      "nando's",
      "wagamama",
      "milano",
      "eddie rockets",
      "camile",
      "boojum",
    ]
  },
  130202: {
    displayName: "Cafe",
    keywords: [
      "cafe",
      "café",
      "coffee shop",
      "brunch spot",
      "sandwich shop",
      // Adding some common misspellings and variations

      "cae",
      "caffe",
      "cfae",
    ],
    merchantHints: [
      "starbucks",
      "costa",
      "insomnia",
      "butlers chocolate cafe",
      "bewleys",
      "caffe nero",
      "pret a manger",
    ]
  },
  130203: {
    displayName: "Fast Food",
    keywords: [
      "fast food",
      "takeaway chain",
      "mcdonalds",
      "burger king",
      "kfc",
      "quick meal",
      // Adding some common misspellings and variations

      "fastfood",
      "fat",
      "fat food",
      "fasst",
      "fasst food",
      "fsat",
    ],
    merchantHints: [
      "mcdonalds",
      "burger king",
      "kfc",
      "subway",
      "supermacs",
      "supermac's",
      "five guys",
      "abrakebabra",
    ]
  },
  130204: {
    displayName: "Takeaway / Carryout",
    keywords: [
      "takeaway",
      "carryout",
      "to-go food",
      "takeaway meal",
      "takeaway order",
      // Adding some common misspellings and variations

      "takeawaycarryout",
      "carrout",
      "takeaway carrout",
      "carryyout",
      "takeaway carryyout",
      "caryrout",
    ],
    merchantHints: [
      "dominos",
      "domino's",
      "apache pizza",
      "four star pizza",
      "papa johns",
      "papa john's",
      "camile",
      "boojum",
    ]
  },
  130205: {
    displayName: "Delivery",
    keywords: [
      "delivery",
      "just eat",
      "deliveroo",
      "uber eats",
      "food delivery",
      "takeout delivery",
      // Adding some common misspellings and variations

      "deliery",
      "delivvery",
      "delviery",
    ],
    aliases: [
      "order takeaway",
      "order delivery",
      "food app order",
      "getting deliveroo",
      "ordering just eat",
      "ordering uber eats",
    ],
    merchantHints: [
      "deliveroo",
      "just eat",
      "uber eats",
      "flipdish",
    ]
  },
  130206: {
    displayName: "Food Court",
    keywords: [
      "food court",
      "mall food",
      "canteen food",
      "shopping centre food",
      // Adding some common misspellings and variations

      "foodcourt",
      "cort",
      "food cort",
      "couurt",
      "food couurt",
      "cuort",
    ]
  },
  130207: {
    displayName: "Fine Dining",
    keywords: [
      "fine dining",
      "tasting menu",
      "upscale restaurant",
      "premium dining",
      // Adding some common misspellings and variations

      "finedining",
      "dinng",
      "fine dinng",
      "diniing",
      "fine diniing",
      "diinng",
    ],
    merchantHints: [
      "restaurant patrick guilbaud",
      "chapter one",
      "liath",
      "variety jones",
      "bastible",
      "glovers alley",
      "aimsir",
    ]
  },
  130208: {
    displayName: "Work Lunches",
    keywords: [
      "work lunch",
      "lunch meeting",
      "office lunch",
      "business lunch",
      // Adding some common misspellings and variations

      "lunches",
      "worklunches",
      "lunhes",
      "work lunhes",
      "luncches",
      "work luncches",
    ]
  },
  130301: {
    displayName: "Coffee Shops",
    keywords: [
      "coffee",
      "latte",
      "cappuccino",
      "americano",
      "espresso",
      "coffee shop",
      "starbucks",
      // Adding some common misspellings and variations

      "shops",
      "coffeeshops",
      "cofee",
      "cofee shops",
      "cofffee",
      "cofffee shops",
    ],
    merchantHints: [
      "starbucks",
      "costa",
      "insomnia",
      "butlers chocolate cafe",
      "bewleys",
      "caffe nero",
      "joe and the juice",
    ]
  },
  130302: {
    displayName: "Tea / Juice Bars",
    keywords: [
      "tea",
      "bubble tea",
      "juice bar",
      "smoothie",
      "fresh juice",
      "tea shop",
      // Adding some common misspellings and variations

      "bars",
      "juicebars",
      "juce",
      "tea juce bars",
      "juiice",
      "tea juiice bars",
    ],
    merchantHints: [
      "chatime",
      "gong cha",
      "joe and the juice",
      "booster juice",
    ]
  },
  130303: {
    displayName: "Alcohol at Bars / Pubs",
    keywords: [
      "pub",
      "bar",
      "beer",
      "wine",
      "pint",
      "cocktail bar",
      "drinks out",
      // Adding some common misspellings and variations

      "alcohol",
      "bars",
      "pubs",
      "alcoholbarspubs",
      "alchol",
      "alchol at bars pubs",
    ],
    merchantHints: [
      "jd wetherspoon",
      "wetherspoon",
      "brewdog",
      "mcgettigans",
      "mcgettigan's",
      "porterhouse",
      "temple bar pub",
      "oneills",
      "o'neill's",
    ]
  },
  130304: {
    displayName: "Cocktails",
    keywords: [
      "cocktail",
      "mixed drink",
      "martini",
      "mojito",
      "happy hour cocktails",
      // Adding some common misspellings and variations

      "cockails",
      "cockttails",
      "coctkails",
      "cockail",
      "cockttail",
    ]
  },
  130305: {
    displayName: "Nightlife Food & Drinks",
    keywords: [
      "club drinks",
      "late night food",
      "nightlife spend",
      "bar snacks",
      // Adding some common misspellings and variations

      "nightlifefooddrinks",
      "nighlife",
      "nighlife food and drinks",
      "nighttlife",
      "nighttlife food and drinks",
      "nigthlife",
    ]
  },
  130306: {
    displayName: "Happy Hour",
    keywords: [
      "happy hour",
      "discounted drinks",
      "bar special",
      "drink deal",
      // Adding some common misspellings and variations

      "happyhour",
      "hapy",
      "hapy hour",
      "happpy",
      "happpy hour",
      "hpapy",
    ]
  },
  130307: {
    displayName: "Club Entry with Drinks",
    keywords: [
      "club entry",
      "nightclub",
      "cover charge with drink",
      "venue fee",
      // Adding some common misspellings and variations

      "drinks",
      "clubentrydrinks",
      "driks",
      "club entry with driks",
      "drinnks",
      "club entry with drinnks",
    ]
  },
  130401: {
    displayName: "Vegan / Vegetarian Specialty",
    keywords: [
      "vegan",
      "vegetarian",
      "plant-based",
      "meat free",
      "vegan food",
      "veggie specialty",
      // Adding some common misspellings and variations

      "veganvegetarianspecialty",
      "vegetrian",
      "vegan vegetrian specialty",
      "vegetaarian",
      "vegan vegetaarian specialty",
      "vegeatrian",
    ]
  },
  130402: {
    displayName: "Gluten-Free Specialty",
    keywords: [
      "gluten free",
      "celiac food",
      "GF products",
      "allergen-free bakery",
      // Adding some common misspellings and variations

      "specialty",
      "glutenfreespecialty",
      "specalty",
      "gluten free specalty",
      "speciialty",
      "gluten free speciialty",
    ]
  },
  130403: {
    displayName: "Organic Specialty",
    keywords: [
      "organic",
      "natural food",
      "health store",
      "organic produce",
      "bio food",
      // Adding some common misspellings and variations

      "specialty",
      "organicspecialty",
      "specalty",
      "organic specalty",
      "speciialty",
      "organic speciialty",
    ]
  },
  130404: {
    displayName: "Sports Nutrition / Protein",
    keywords: [
      "protein",
      "whey",
      "creatine",
      "recovery shake",
      "sports nutrition",
      "gym food",
      // Adding some common misspellings and variations

      "sportsnutritionprotein",
      "nutrtion",
      "sports nutrtion protein",
      "nutriition",
      "sports nutriition protein",
      "nutirtion",
    ]
  },
  130405: {
    displayName: "Baby Food",
    keywords: [
      "baby food",
      "puree",
      "infant food",
      "toddler snacks",
      "baby meals",
      // Adding some common misspellings and variations

      "babyfood",
      "bay",
      "bay food",
      "babby",
      "babby food",
      "bbay",
    ]
  },
  130406: {
    displayName: "Medical Diet Foods",
    keywords: [
      "diet food",
      "low sodium",
      "diabetic food",
      "medical nutrition",
      "prescribed diet food",
      // Adding some common misspellings and variations

      "foods",
      "medicaldietfoods",
      "medcal",
      "medcal diet foods",
      "mediical",
      "mediical diet foods",
    ]
  },
  130501: {
    displayName: "Meal Kits",
    keywords: [
      "meal kit",
      "hello fresh",
      "gousto",
      "prepared ingredients",
      "cooking kit",
      // Adding some common misspellings and variations

      "kits",
      "mealkits",
      "kis",
      "meal kis",
      "kitts",
      "meal kitts",
    ]
  },
  130502: {
    displayName: "Prepared Meal Subscriptions",
    keywords: [
      "meal subscription",
      "prepared meals",
      "ready meals subscription",
      "meal delivery plan",
      // Adding some common misspellings and variations

      "subscriptions",
      "preparedmealsubscriptions",
      "subscrptions",
      "prepared meal subscrptions",
      "subscriiptions",
      "prepared meal subscriiptions",
    ]
  },
  130503: {
    displayName: "Office Catering",
    keywords: [
      "office catering",
      "catering",
      "tray order",
      "work catering",
      "group catering",
      // Adding some common misspellings and variations

      "officecatering",
      "cateing",
      "office cateing",
      "caterring",
      "office caterring",
      "catreing",
    ]
  },
  130504: {
    displayName: "Personal Chef / Catering",
    keywords: [
      "personal chef",
      "private catering",
      "event meal service",
      "chef at home",
      // Adding some common misspellings and variations

      "personalchefcatering",
      "cateing",
      "personal chef cateing",
      "caterring",
      "personal chef caterring",
      "catreing",
    ]
  },
  130505: {
    displayName: "Group Food Orders",
    keywords: [
      "group order",
      "shared meal order",
      "office lunch order",
      "large takeaway",
      // Adding some common misspellings and variations

      "food",
      "orders",
      "groupfoodorders",
      "ordrs",
      "group food ordrs",
      "ordeers",
    ]
  },
  130601: {
    displayName: "Tips / Gratuities",
    keywords: [
      "tip",
      "gratuity",
      "service tip",
      "waiter tip",
      "delivery tip",
      // Adding some common misspellings and variations

      "tips",
      "gratuities",
      "tipsgratuities",
      "gratuties",
      "tips gratuties",
      "gratuiities",
    ]
  },
  130602: {
    displayName: "Food Delivery Fees",
    keywords: [
      "delivery fee",
      "service fee",
      "food app fee",
      "small order fee",
      "platform fee",
      // Adding some common misspellings and variations

      "fooddelivery",
      "deliery",
      "food deliery fees",
      "delivvery",
      "food delivvery fees",
      "delviery",
    ]
  },
  130603: {
    displayName: "Corkage / Service Charges",
    keywords: [
      "service charge",
      "corkage",
      "table charge",
      "restaurant fee",
      // Adding some common misspellings and variations

      "charges",
      "corkagecharges",
      "chages",
      "corkage service chages",
      "charrges",
      "corkage service charrges",
    ]
  },
  130604: {
    displayName: "Vending Machines",
    keywords: [
      "vending machine",
      "snack machine",
      "drink machine",
      "office vending",
      // Adding some common misspellings and variations

      "machines",
      "vendingmachines",
      "machnes",
      "vending machnes",
      "machiines",
      "vending machiines",
    ]
  },
  130605: {
    displayName: "Other Food Expense",
    keywords: [
      "food misc",
      "other dining",
      "uncategorized food",
      "unknown food expense",
      // Adding some common misspellings and variations

      "fod",
      "foood",
    ]
  },
  140101: {
    displayName: "Electricity Bill",
    keywords: [
      "electricity",
      "electric bill",
      "power bill",
      "energy bill",
      "utility electric",
      // Adding some common misspellings and variations

      "electricitybill",
      "electicity",
      "electicity bill",
      "electrricity",
      "electrricity bill",
      "elecrticity",
    ],
    aliases: [
      "electric bill",
      "power bill due",
      "pay the electric bill",
      "my electricity is due",
      "electricity payment",
      "power is due",
    ],
    merchantHints: [
      "electric ireland",
      "energia",
      "prepay power",
    ]
  },
  140102: {
    displayName: "Prepay Electricity",
    keywords: [
      "prepay electric",
      "meter top up electric",
      "prepaid electricity",
      "electricity credit",
      // Adding some common misspellings and variations

      "prepayelectricity",
      "electicity",
      "prepay electicity",
      "electrricity",
      "prepay electrricity",
      "elecrticity",
    ]
  },
  140103: {
    displayName: "Meter Top-up",
    keywords: [
      "top up",
      "pay as you go electric",
      "meter credit",
      "utility top-up",
      // Adding some common misspellings and variations

      "meer",
      "metter",
      "mteer",
    ]
  },
  140104: {
    displayName: "Electricity Arrears",
    keywords: [
      "electricity arrears",
      "overdue electric",
      "electric debt",
      "late electric payment",
      // Adding some common misspellings and variations

      "electricityarrears",
      "electicity",
      "electicity arrears",
      "electrricity",
      "electrricity arrears",
      "elecrticity",
    ]
  },
  140201: {
    displayName: "Gas Bill",
    keywords: [
      "gas bill",
      "natural gas",
      "gas payment",
      "heating gas",
      "utility gas",
      // Adding some common misspellings and variations

      "bil",
      "billl",
      "blil",
    ],
    aliases: [
      "how much is my gas bill",
      "gas is due",
      "pay the gas bill",
      "heating bill",
      "gas payment due",
      "home gas bill",
    ],
    merchantHints: [
      "bord gais",
      "bord gas",
      "bord gais energy",
      "calor gas",
      "energia",
    ]
  },
  140202: {
    displayName: "Heating Oil",
    keywords: [
      "heating oil",
      "oil tank",
      "home heating oil",
      "kerosene heating",
      // Adding some common misspellings and variations

      "heaing",
      "heatting",
      "hetaing",
    ]
  },
  140203: {
    displayName: "Propane",
    keywords: [
      "propane",
      "gas cylinder",
      "bottled gas",
      "LPG home use",
      // Adding some common misspellings and variations

      "proane",
      "proppane",
      "prpoane",
    ]
  },
  140204: {
    displayName: "Solid Fuel / Firewood / Pellets",
    keywords: [
      "firewood",
      "wood pellets",
      "coal",
      "solid fuel",
      "stove fuel",
      "fireplace fuel",
      // Adding some common misspellings and variations

      "solidfuelfirewoodpellets",
      "fireood",
      "solid fuel fireood pellets",
      "firewwood",
      "solid fuel firewwood pellets",
      "firweood",
    ]
  },
  140205: {
    displayName: "Boiler Service Fees",
    keywords: [
      "boiler service",
      "heating service",
      "furnace service",
      "annual boiler check",
      // Adding some common misspellings and variations

      "boier",
      "boiller",
      "bolier",
    ]
  },
  140301: {
    displayName: "Water Bill",
    keywords: [
      "water bill",
      "water charge",
      "utility water",
      "water payment",
      // Adding some common misspellings and variations

      "waterbill",
      "waer",
      "waer bill",
      "watter",
      "watter bill",
      "wtaer",
    ]
  },
  140302: {
    displayName: "Sewer / Wastewater",
    keywords: [
      "sewer",
      "wastewater",
      "sewage charge",
      "drainage fee",
      // Adding some common misspellings and variations

      "sewerwastewater",
      "wasteater",
      "sewer wasteater",
      "wastewwater",
      "sewer wastewwater",
      "wastweater",
    ]
  },
  140303: {
    displayName: "Bin / Refuse Collection",
    keywords: [
      "bin charge",
      "refuse collection",
      "trash pickup",
      "garbage collection",
      "waste collection",
      // Adding some common misspellings and variations

      "refusecollection",
      "colletion",
      "bin refuse colletion",
      "collecction",
      "bin refuse collecction",
      "collcetion",
    ]
  },
  140304: {
    displayName: "Recycling Fees",
    keywords: [
      "recycling fee",
      "recycle collection",
      "green bin",
      "waste recycle charge",
      // Adding some common misspellings and variations

      "recyling",
      "recyccling",
      "reccyling",
    ]
  },
  140305: {
    displayName: "Septic Tank Service",
    keywords: [
      "septic tank",
      "tank emptying",
      "septic service",
      "wastewater tank service",
      // Adding some common misspellings and variations

      "septictank",
      "sepic",
      "sepic tank service",
      "septtic",
      "septtic tank service",
      "setpic",
    ]
  },
  140401: {
    displayName: "Home Internet",
    keywords: [
      "internet",
      "broadband",
      "wifi",
      "home internet",
      "ISP",
      "fibre",
      "internet bill",
      // Adding some common misspellings and variations

      "homeinternet",
      "intenet",
      "home intenet",
      "interrnet",
      "home interrnet",
      "intrenet",
    ],
    aliases: [
      "my internet is due",
      "broadband bill",
      "wifi bill",
      "home wifi",
      "pay internet",
      "internet payment due",
    ],
    merchantHints: [
      "eir",
      "vodafone",
      "three",
      "virgin media",
      "sky broadband",
    ]
  },
  140402: {
    displayName: "Broadband Installation",
    keywords: [
      "broadband install",
      "internet setup",
      "router setup",
      "fibre install",
      "connection fee",
      // Adding some common misspellings and variations

      "installation",
      "broadbandinstallation",
      "instalation",
      "broadband instalation",
      "installlation",
      "broadband installlation",
    ]
  },
  140403: {
    displayName: "Landline",
    keywords: [
      "landline",
      "home phone",
      "fixed line",
      "phone bill landline",
      // Adding some common misspellings and variations

      "landine",
      "landlline",
      "lanldine",
    ]
  },
  140404: {
    displayName: "Mobile Phone Bill",
    keywords: [
      "mobile bill",
      "phone bill",
      "cell phone",
      "mobile plan",
      "SIM plan",
      "monthly phone bill",
      // Adding some common misspellings and variations

      "mobilephonebill",
      "moble",
      "moble phone bill",
      "mobiile",
      "mobiile phone bill",
      "moible",
    ],
    aliases: [
      "phone bill is due",
      "mobile plan bill",
      "cell bill",
      "pay my phone bill",
      "monthly phone bill",
      "mobile bill due",
    ],
    merchantHints: [
      "eir",
      "vodafone",
      "three",
      "48",
      "gomo",
      "go mo",
      "tesco mobile",
    ]
  },
  140405: {
    displayName: "Mobile Top-up",
    keywords: [
      "phone top up",
      "mobile recharge",
      "prepaid phone",
      "top-up credit",
      "airtime",
      // Adding some common misspellings and variations

      "moble",
      "mobiile",
      "moible",
      "phne",
      "phoone",
    ]
  },
  140406: {
    displayName: "Family Phone Plan",
    keywords: [
      "family plan",
      "multiple lines",
      "shared phone plan",
      "family mobile bill",
      // Adding some common misspellings and variations

      "familyphone",
      "famly",
      "famly phone plan",
      "famiily",
      "famiily phone plan",
      "faimly",
    ]
  },
  140407: {
    displayName: "International Calling",
    keywords: [
      "international calls",
      "roaming call",
      "overseas calling",
      "calling credit",
      "phone abroad",
      // Adding some common misspellings and variations

      "internationalcalling",
      "interntional",
      "interntional calling",
      "internaational",
      "internaational calling",
      "interantional",
    ]
  },
  140501: {
    displayName: "Cable TV",
    keywords: [
      "cable",
      "cable tv",
      "tv package",
      "television bill",
      "cable subscription",
      // Adding some common misspellings and variations

      "cale",
      "cabble",
      "cbale",
    ],
    merchantHints: [
      "virgin media",
      "sky",
      "vodafone tv",
    ]
  },
  140502: {
    displayName: "Satellite TV",
    keywords: [
      "satellite",
      "dish tv",
      "sky tv",
      "satellite package",
      "satellite bill",
      // Adding some common misspellings and variations

      "satelite",
      "satelllite",
      "satlelite",
    ],
    merchantHints: [
      "sky",
      "freesat",
    ]
  },
  140503: {
    displayName: "TV License / Broadcast License",
    keywords: [
      "tv licence",
      "broadcast licence",
      "television licence",
      "TV tax",
      // Adding some common misspellings and variations

      "license",
      "licensebroadcastlicense",
      "broacast",
      "tv license broacast license",
      "broaddcast",
      "tv license broaddcast license",
    ]
  },
  140504: {
    displayName: "Home Phone-TV Bundles",
    keywords: [
      "bundle",
      "broadband tv phone bundle",
      "triple play",
      "telecom bundle",
      // Adding some common misspellings and variations

      "home",
      "bundles",
      "homephonebundles",
      "bunles",
      "home phone tv bunles",
      "bunddles",
    ],
    merchantHints: [
      "eir",
      "sky",
      "virgin media",
      "vodafone",
    ]
  },
  140601: {
    displayName: "Utility Deposit",
    keywords: [
      "utility deposit",
      "service deposit",
      "electricity deposit",
      "water deposit",
      // Adding some common misspellings and variations

      "utilitydeposit",
      "depsit",
      "utility depsit",
      "depoosit",
      "utility depoosit",
      "deopsit",
    ]
  },
  140602: {
    displayName: "Reconnection Fee",
    keywords: [
      "reconnection fee",
      "reconnect charge",
      "service reconnect",
      "restore service fee",
      // Adding some common misspellings and variations

      "reconnction",
      "reconneection",
      "reconenction",
    ]
  },
  140603: {
    displayName: "Installation Fee",
    keywords: [
      "installation fee",
      "setup fee",
      "service install",
      "utility setup charge",
      // Adding some common misspellings and variations

      "instalation",
      "installlation",
      "installatoin",
    ]
  },
  140604: {
    displayName: "Late Payment Fee",
    keywords: [
      "late fee",
      "overdue fee",
      "service penalty",
      "utility penalty",
      "delayed payment charge",
      // Adding some common misspellings and variations

      "lae",
      "latte",
      "ltae",
    ]
  },
  140605: {
    displayName: "Service Transfer Fee",
    keywords: [
      "transfer fee",
      "move service",
      "account transfer charge",
      "service change fee",
      // Adding some common misspellings and variations

      "tranfer",
      "transsfer",
      "trasnfer",
    ]
  },
  140701: {
    displayName: "Utilities Miscellaneous",
    keywords: [
      "utility misc",
      "utilities other",
      "communication misc",
      "uncategorized utility",
      // Adding some common misspellings and variations

      "utilties",
      "utiliities",
      "utiilties",
      "utiity",
      "utillity",
    ],
    aliases: [
      "pay the bills",
      "utility bills",
      "household bills",
      "my bills are due",
      "monthly bills",
      "home bills",
    ]
  },
  140702: {
    displayName: "Shared Utility Contribution",
    keywords: [
      "shared bills",
      "roommate utilities",
      "split utilities",
      "bill contribution",
      "shared internet",
      // Adding some common misspellings and variations

      "utility",
      "sharedutilitycontribution",
      "contriution",
      "shared utility contriution",
      "contribbution",
      "shared utility contribbution",
    ]
  },
  150101: {
    displayName: "Health Insurance Premium",
    keywords: [
      "health insurance",
      "medical insurance",
      "health premium",
      "healthcare cover",
      // Adding some common misspellings and variations

      "healthinsurancepremium",
      "insuance",
      "health insuance premium",
      "insurrance",
      "health insurrance premium",
      "insruance",
    ],
    merchantHints: [
      "vhi",
      "vhi healthcare",
      "laya",
      "laya healthcare",
      "irish life health",
      "level health",
    ]
  },
  150102: {
    displayName: "Dental Insurance",
    keywords: [
      "dental insurance",
      "teeth insurance",
      "dental premium",
      // Adding some common misspellings and variations

      "dentalinsurance",
      "insuance",
      "dental insuance",
      "insurrance",
      "dental insurrance",
      "insruance",
    ],
    merchantHints: [
      "decare",
      "vhi",
      "laya",
      "irish life health",
    ]
  },
  150103: {
    displayName: "Vision Insurance",
    keywords: [
      "vision insurance",
      "eye insurance",
      "optical cover",
      // Adding some common misspellings and variations

      "visioninsurance",
      "insuance",
      "vision insuance",
      "insurrance",
      "vision insurrance",
      "insruance",
    ],
    merchantHints: [
      "decare",
      "vhi",
      "laya",
      "irish life health",
    ]
  },
  150104: {
    displayName: "Supplemental Medical Insurance",
    keywords: [
      "supplemental insurance",
      "extra medical cover",
      "top-up insurance",
      // Adding some common misspellings and variations

      "suppleental",
      "suppleental medical insurance",
      "supplemmental",
      "supplemmental medical insurance",
      "supplmeental",
      "supplmeental medical insurance",
    ]
  },
  150105: {
    displayName: "Travel Medical Insurance",
    keywords: [
      "travel medical",
      "travel health cover",
      "overseas health insurance",
      // Adding some common misspellings and variations

      "travelmedicalinsurance",
      "insuance",
      "travel medical insuance",
      "insurrance",
      "travel medical insurrance",
      "insruance",
    ]
  },
  150201: {
    displayName: "Life Insurance",
    keywords: [
      "life insurance",
      "life cover",
      "death benefit cover",
      // Adding some common misspellings and variations

      "lifeinsurance",
      "insuance",
      "life insuance",
      "insurrance",
      "life insurrance",
      "insruance",
    ]
  },
  150202: {
    displayName: "Disability Insurance",
    keywords: [
      "disability insurance",
      "disability cover",
      "income replacement cover",
      // Adding some common misspellings and variations

      "disabilityinsurance",
      "disablity",
      "disablity insurance",
      "disabiility",
      "disabiility insurance",
      "disaiblity",
    ]
  },
  150203: {
    displayName: "Income Protection",
    keywords: [
      "income protection",
      "salary protection",
      "sickness cover",
      "earnings cover",
      // Adding some common misspellings and variations

      "incomeprotection",
      "protetion",
      "income protetion",
      "protecction",
      "income protecction",
      "protcetion",
    ]
  },
  150204: {
    displayName: "Critical Illness Cover",
    keywords: [
      "critical illness",
      "serious illness cover",
      "illness protection",
      // Adding some common misspellings and variations

      "criticalillnesscover",
      "critcal",
      "critcal illness cover",
      "critiical",
      "critiical illness cover",
      "criitcal",
    ]
  },
  150205: {
    displayName: "Funeral Insurance",
    keywords: [
      "funeral insurance",
      "burial cover",
      "final expense cover",
      // Adding some common misspellings and variations

      "funeralinsurance",
      "insuance",
      "funeral insuance",
      "insurrance",
      "funeral insurrance",
      "insruance",
    ]
  },
  150301: {
    displayName: "Homeowners Insurance",
    keywords: [
      "homeowners insurance",
      "home cover",
      "house insurance",
      // Adding some common misspellings and variations

      "homeownersinsurance",
      "homeoners",
      "homeoners insurance",
      "homeowwners",
      "homeowwners insurance",
      "homewoners",
    ],
    merchantHints: [
      "aviva",
      "axa",
      "allianz",
      "fbd",
      "zurich",
    ]
  },
  150302: {
    displayName: "Renters Insurance",
    keywords: [
      "renters insurance",
      "tenant insurance",
      "contents cover rental",
      // Adding some common misspellings and variations

      "rentersinsurance",
      "insuance",
      "renters insuance",
      "insurrance",
      "renters insurrance",
      "insruance",
    ]
  },
  150303: {
    displayName: "Landlord Insurance",
    keywords: [
      "landlord insurance",
      "rental property insurance",
      "letting insurance",
      // Adding some common misspellings and variations

      "landlordinsurance",
      "insuance",
      "landlord insuance",
      "insurrance",
      "landlord insurrance",
      "insruance",
    ]
  },
  150304: {
    displayName: "Contents Insurance",
    keywords: [
      "contents insurance",
      "household contents",
      "belongings cover",
      // Adding some common misspellings and variations

      "contentsinsurance",
      "insuance",
      "contents insuance",
      "insurrance",
      "contents insurrance",
      "insruance",
    ],
    merchantHints: [
      "aviva",
      "axa",
      "allianz",
      "fbd",
      "zurich",
    ]
  },
  150305: {
    displayName: "Flood Insurance",
    keywords: [
      "flood insurance",
      "water damage cover",
      "flood cover",
      // Adding some common misspellings and variations

      "floodinsurance",
      "insuance",
      "flood insuance",
      "insurrance",
      "flood insurrance",
      "insruance",
    ]
  },
  150306: {
    displayName: "Disaster Insurance",
    keywords: [
      "disaster insurance",
      "storm cover",
      "earthquake cover",
      "catastrophe insurance",
      // Adding some common misspellings and variations

      "disasterinsurance",
      "insuance",
      "disaster insuance",
      "insurrance",
      "disaster insurrance",
      "insruance",
    ]
  },
  150401: {
    displayName: "Car Insurance",
    keywords: [
      "car insurance",
      "auto insurance",
      "motor insurance",
      "vehicle cover",
      // Adding some common misspellings and variations

      "insuance",
      "insurrance",
      "insruance",
    ],
    merchantHints: [
      "aviva",
      "axa",
      "allianz",
      "fbd",
      "zurich",
      "the aa",
    ]
  },
  150402: {
    displayName: "Motorcycle Insurance",
    keywords: [
      "motorcycle insurance",
      "bike insurance",
      "motorbike cover",
      // Adding some common misspellings and variations

      "motorcycleinsurance",
      "motorycle",
      "motorycle insurance",
      "motorccycle",
      "motorccycle insurance",
      "motocrycle",
    ]
  },
  150403: {
    displayName: "Commercial Vehicle Insurance",
    keywords: [
      "van insurance",
      "commercial vehicle cover",
      "fleet insurance",
      // Adding some common misspellings and variations

      "commercialvehicleinsurance",
      "commecial",
      "commecial vehicle insurance",
      "commerrcial",
      "commerrcial vehicle insurance",
      "commrecial",
    ]
  },
  150404: {
    displayName: "Breakdown Cover",
    keywords: [
      "breakdown cover",
      "roadside cover",
      "recovery cover",
      "assistance cover",
      // Adding some common misspellings and variations

      "breakdowncover",
      "breadown",
      "breadown cover",
      "breakkdown",
      "breakkdown cover",
      "brekadown",
    ],
    merchantHints: [
      "the aa",
      "aa ireland",
      "rac",
    ]
  },
  150405: {
    displayName: "Windscreen Cover",
    keywords: [
      "windscreen cover",
      "glass cover",
      "windshield insurance",
      // Adding some common misspellings and variations

      "windscreencover",
      "windsreen",
      "windsreen cover",
      "windsccreen",
      "windsccreen cover",
      "windcsreen",
    ]
  },
  150501: {
    displayName: "Travel Insurance",
    keywords: [
      "travel insurance",
      "trip cover",
      "holiday insurance",
      // Adding some common misspellings and variations

      "travelinsurance",
      "insuance",
      "travel insuance",
      "insurrance",
      "travel insurrance",
      "insruance",
    ]
  },
  150502: {
    displayName: "Flight Insurance",
    keywords: [
      "flight insurance",
      "airfare protection",
      "flight cover",
      // Adding some common misspellings and variations

      "flightinsurance",
      "insuance",
      "flight insuance",
      "insurrance",
      "flight insurrance",
      "insruance",
    ]
  },
  150503: {
    displayName: "Event Insurance",
    keywords: [
      "event insurance",
      "ticket cover",
      "liability event cover",
      // Adding some common misspellings and variations

      "eventinsurance",
      "insuance",
      "event insuance",
      "insurrance",
      "event insurrance",
      "insruance",
    ]
  },
  150504: {
    displayName: "Wedding Insurance",
    keywords: [
      "wedding insurance",
      "ceremony cover",
      "event wedding protection",
      // Adding some common misspellings and variations

      "weddinginsurance",
      "insuance",
      "wedding insuance",
      "insurrance",
      "wedding insurrance",
      "insruance",
    ]
  },
  150505: {
    displayName: "Ticket Protection",
    keywords: [
      "ticket protection",
      "ticket insurance",
      "booking protection",
      // Adding some common misspellings and variations

      "ticketprotection",
      "protetion",
      "ticket protetion",
      "protecction",
      "ticket protecction",
      "protcetion",
    ]
  },
  150601: {
    displayName: "Pet Insurance",
    keywords: [
      "pet insurance",
      "dog insurance",
      "cat insurance",
      "animal cover",
      // Adding some common misspellings and variations

      "insuance",
      "insurrance",
      "insruance",
    ]
  },
  150602: {
    displayName: "Gadget / Device Insurance",
    keywords: [
      "phone insurance",
      "gadget insurance",
      "device cover",
      "laptop cover",
      // Adding some common misspellings and variations

      "gadgetdeviceinsurance",
      "insuance",
      "gadget device insuance",
      "insurrance",
      "gadget device insurrance",
      "insruance",
    ]
  },
  150603: {
    displayName: "Identity Theft Protection",
    keywords: [
      "identity theft protection",
      "fraud protection",
      "ID monitoring",
      // Adding some common misspellings and variations

      "identitytheftprotection",
      "protetion",
      "identity theft protetion",
      "protecction",
      "identity theft protecction",
      "protcetion",
    ]
  },
  150604: {
    displayName: "Legal Protection Insurance",
    keywords: [
      "legal insurance",
      "legal cover",
      "dispute protection",
      // Adding some common misspellings and variations

      "legalprotectioninsurance",
      "protetion",
      "legal protetion insurance",
      "protecction",
      "legal protecction insurance",
      "protcetion",
    ]
  },
  150701: {
    displayName: "Insurance Deductibles",
    keywords: [
      "deductible",
      "excess",
      "insurance excess",
      "claim excess",
      // Adding some common misspellings and variations

      "deductibles",
      "insurancedeductibles",
      "deducibles",
      "insurance deducibles",
      "deducttibles",
      "insurance deducttibles",
    ]
  },
  150702: {
    displayName: "Insurance Co-pays",
    keywords: [
      "copay",
      "co-pay",
      "medical copay",
      "insurance share",
      // Adding some common misspellings and variations

      "pays",
      "insurancepays",
      "insuance",
      "insuance co pays",
      "insurrance",
      "insurrance co pays",
    ]
  },
  150703: {
    displayName: "Other Insurance Costs",
    keywords: [
      "insurance misc",
      "insurance fee",
      "other cover costs",
      "uncategorized insurance",
      // Adding some common misspellings and variations

      "insuance",
      "insurrance",
      "insruance",
    ]
  },
  160101: {
    displayName: "General Practitioner Visits",
    keywords: [
      "GP",
      "doctor",
      "doctor visit",
      "family doctor",
      "clinic visit",
      "general practitioner",
      // Adding some common misspellings and variations

      "visits",
      "generalpractitionervisits",
      "practiioner",
      "general practiioner visits",
      "practittioner",
      "general practittioner visits",
    ]
  },
  160102: {
    displayName: "Pediatrician",
    keywords: [
      "pediatrician",
      "child doctor",
      "kids doctor",
      "paediatrician",
      // Adding some common misspellings and variations

      "pediatician",
      "pediatrrician",
      "pediartician",
    ]
  },
  160103: {
    displayName: "Obstetrician / Gynecologist",
    keywords: [
      "OBGYN",
      "gynecologist",
      "obstetrician",
      "gynae",
      "women's doctor",
      // Adding some common misspellings and variations

      "obstetriciangynecologist",
      "gynecoogist",
      "obstetrician gynecoogist",
      "gynecollogist",
      "obstetrician gynecollogist",
      "gynecloogist",
    ]
  },
  160104: {
    displayName: "Dermatologist",
    keywords: [
      "dermatologist",
      "skin doctor",
      "skin clinic",
      "mole check",
      // Adding some common misspellings and variations

      "dermatlogist",
      "dermatoologist",
      "dermaotlogist",
    ]
  },
  160105: {
    displayName: "Cardiologist",
    keywords: [
      "cardiologist",
      "heart doctor",
      "heart specialist",
      // Adding some common misspellings and variations

      "cardioogist",
      "cardiollogist",
      "cardiloogist",
    ]
  },
  160106: {
    displayName: "Neurologist",
    keywords: [
      "neurologist",
      "nerve specialist",
      "brain doctor",
      // Adding some common misspellings and variations

      "neuroogist",
      "neurollogist",
      "neurloogist",
    ]
  },
  160107: {
    displayName: "Gastroenterologist",
    keywords: [
      "gastroenterologist",
      "stomach specialist",
      "digestive doctor",
      "GI specialist",
      // Adding some common misspellings and variations

      "gastroentrologist",
      "gastroenteerologist",
      "gastroenetrologist",
    ]
  },
  160108: {
    displayName: "Endocrinologist",
    keywords: [
      "endocrinologist",
      "hormone specialist",
      "thyroid doctor",
      "diabetes specialist",
      // Adding some common misspellings and variations

      "endocriologist",
      "endocrinnologist",
      "endocrniologist",
    ]
  },
  160109: {
    displayName: "Psychiatrist",
    keywords: [
      "psychiatrist",
      "mental health doctor",
      "medication psychiatry",
      // Adding some common misspellings and variations

      "psychitrist",
      "psychiaatrist",
      "psychaitrist",
    ]
  },
  160110: {
    displayName: "Psychologist / Therapist",
    keywords: [
      "psychologist",
      "therapist",
      "counseling",
      "counselling",
      "mental health session",
      // Adding some common misspellings and variations

      "psychologisttherapist",
      "psychoogist",
      "psychoogist therapist",
      "psychollogist",
      "psychollogist therapist",
      "psychloogist",
    ]
  },
  160111: {
    displayName: "Chiropractor",
    keywords: [
      "chiropractor",
      "spine adjustment",
      "chiropractic",
      "back adjustment",
      // Adding some common misspellings and variations

      "chiropactor",
      "chiroprractor",
      "chirorpactor",
    ]
  },
  160112: {
    displayName: "Physical Therapist",
    keywords: [
      "physio",
      "physical therapist",
      "physiotherapy",
      "rehab therapy",
      // Adding some common misspellings and variations

      "physicaltherapist",
      "therpist",
      "physical therpist",
      "theraapist",
      "physical theraapist",
      "thearpist",
    ]
  },
  160113: {
    displayName: "Occupational Therapist",
    keywords: [
      "occupational therapist",
      "OT",
      "rehabilitation support",
      "daily living therapy",
      // Adding some common misspellings and variations

      "occupationaltherapist",
      "occupaional",
      "occupaional therapist",
      "occupattional",
      "occupattional therapist",
      "occuptaional",
    ]
  },
  160114: {
    displayName: "Speech Therapist",
    keywords: [
      "speech therapist",
      "speech therapy",
      "language therapy",
      // Adding some common misspellings and variations

      "speechtherapist",
      "therpist",
      "speech therpist",
      "theraapist",
      "speech theraapist",
      "thearpist",
    ]
  },
  160201: {
    displayName: "Routine Checkups & Cleanings",
    keywords: [
      "dentist",
      "dental cleaning",
      "dental checkup",
      "scale and polish",
      "routine dentist",
      // Adding some common misspellings and variations

      "checkups",
      "cleanings",
      "routinecheckupscleanings",
      "cleaings",
      "routine checkups and cleaings",
      "cleannings",
    ],
    aliases: [
      "dentist appointment",
      "tooth pain",
      "teeth pain",
      "jaw ache dentist",
      "dental visit",
      "toothache",
    ],
    merchantHints: [
      "smiles dental",
      "dental care ireland",
      "truly dental",
    ]
  },
  160202: {
    displayName: "Fillings & Extractions",
    keywords: [
      "filling",
      "tooth extraction",
      "cavity",
      "dentist filling",
      "pulled tooth",
      // Adding some common misspellings and variations

      "fillings",
      "extractions",
      "fillingsextractions",
      "extrations",
      "fillings and extrations",
      "extracctions",
    ]
  },
  160203: {
    displayName: "Root Canals",
    keywords: [
      "root canal",
      "endodontic",
      "tooth nerve treatment",
      // Adding some common misspellings and variations

      "canals",
      "rootcanals",
      "canls",
      "root canls",
      "canaals",
      "root canaals",
    ],
    aliases: [
      "root pain",
      "tooth nerve pain",
      "infected tooth",
      "root canal treatment",
      "nerve pain tooth",
      "deep tooth pain",
    ]
  },
  160204: {
    displayName: "Crowns & Bridges",
    keywords: [
      "crown",
      "dental crown",
      "bridge",
      "tooth bridge",
      // Adding some common misspellings and variations

      "crowns",
      "bridges",
      "crownsbridges",
      "briges",
      "crowns and briges",
      "briddges",
    ]
  },
  160205: {
    displayName: "Dentures",
    keywords: [
      "dentures",
      "false teeth",
      "partial denture",
      "full denture",
      // Adding some common misspellings and variations

      "dentres",
      "dentuures",
      "denutres",
    ]
  },
  160206: {
    displayName: "Orthodontics",
    keywords: [
      "braces",
      "invisalign",
      "orthodontist",
      "retainers",
      "teeth straightening",
      // Adding some common misspellings and variations

      "orthodontics",
      "orthodntics",
      "orthodoontics",
      "orthoodntics",
      "braes",
      "bracces",
    ]
  },
  160207: {
    displayName: "Teeth Whitening",
    keywords: [
      "whitening",
      "teeth whitening",
      "bleach trays",
      "cosmetic dental whitening",
      // Adding some common misspellings and variations

      "teethwhitening",
      "whitning",
      "teeth whitning",
      "whiteening",
      "teeth whiteening",
      "whietning",
    ]
  },
  160208: {
    displayName: "Oral Surgery",
    keywords: [
      "oral surgery",
      "wisdom tooth",
      "jaw surgery",
      "gum surgery",
      // Adding some common misspellings and variations

      "oralsurgery",
      "surery",
      "oral surery",
      "surggery",
      "oral surggery",
      "sugrery",
    ],
    aliases: [
      "jaw ache",
      "wisdom tooth pain",
      "gum operation",
      "oral surgeon",
      "tooth surgery",
      "mouth surgery",
    ]
  },
  160301: {
    displayName: "Eye Exams",
    keywords: [
      "eye exam",
      "optician",
      "eye test",
      "vision test",
      "optical exam",
      // Adding some common misspellings and variations

      "exams",
      "exms",
      "exaams",
      "eaxms",
      "exm",
      "exaam",
    ],
    merchantHints: [
      "specsavers",
      "vision express",
      "boots opticians",
    ]
  },
  160302: {
    displayName: "Eyeglasses",
    keywords: [
      "glasses",
      "frames",
      "lenses",
      "prescription glasses",
      "spectacles",
      // Adding some common misspellings and variations

      "eyeglasses",
      "eyeglsses",
      "eyeglaasses",
      "eyegalsses",
      "glases",
      "glassses",
    ],
    merchantHints: [
      "specsavers",
      "vision express",
      "boots opticians",
    ]
  },
  160303: {
    displayName: "Contact Lenses",
    keywords: [
      "contacts",
      "contact lenses",
      "lens solution",
      "daily lenses",
      "monthly lenses",
      // Adding some common misspellings and variations

      "contactlenses",
      "conact",
      "conact lenses",
      "conttact",
      "conttact lenses",
      "cotnact",
    ]
  },
  160304: {
    displayName: "Prescription Sunglasses",
    keywords: [
      "prescription sunglasses",
      "tinted lenses",
      "sunglasses with prescription",
      // Adding some common misspellings and variations

      "prescriptionsunglasses",
      "prescrption",
      "prescrption sunglasses",
      "prescriiption",
      "prescriiption sunglasses",
      "prescirption",
    ]
  },
  160305: {
    displayName: "LASIK / Vision Correction Surgery",
    keywords: [
      "lasik",
      "laser eye surgery",
      "vision correction",
      "eye surgery",
      // Adding some common misspellings and variations

      "corretion",
      "lasik vision corretion surgery",
      "correcction",
      "lasik vision correcction surgery",
      "corrcetion",
      "lasik vision corrcetion surgery",
    ]
  },
  160306: {
    displayName: "Eye Medications",
    keywords: [
      "eye drops",
      "prescription eye meds",
      "glaucoma drops",
      "eye ointment",
      // Adding some common misspellings and variations

      "medications",
      "medictions",
      "medicaations",
      "mediactions",
      "drps",
      "droops",
    ]
  },
  160307: {
    displayName: "Eye Patches & Supplies",
    keywords: [
      "eye patch",
      "lens case",
      "eye care supplies",
      "cleaning solution",
      // Adding some common misspellings and variations

      "patches",
      "pathes",
      "patcches",
      "pacthes",
      "pach",
      "pattch",
    ]
  },
  160401: {
    displayName: "Prescription Drugs",
    keywords: [
      "prescription",
      "medication",
      "medicine",
      "pharmacy",
      "doctor prescription",
      // Adding some common misspellings and variations

      "drugs",
      "prescriptiondrugs",
      "prescrption",
      "prescrption drugs",
      "prescriiption",
      "prescriiption drugs",
    ],
    merchantHints: [
      "boots",
      "mccabes pharmacy",
      "lloyds pharmacy",
      "hickeys pharmacy",
      "allcare pharmacy",
      "meaghers pharmacy",
    ]
  },
  160402: {
    displayName: "Mail-Order Prescriptions",
    keywords: [
      "mail-order prescription",
      "pharmacy delivery",
      "online pharmacy",
      // Adding some common misspellings and variations

      "prescriptions",
      "mailorderprescriptions",
      "prescrptions",
      "mail order prescrptions",
      "prescriiptions",
      "mail order prescriiptions",
    ]
  },
  160403: {
    displayName: "Insulin & Diabetic Supplies",
    keywords: [
      "insulin",
      "glucose strips",
      "diabetic supplies",
      "lancets",
      "blood sugar supplies",
      // Adding some common misspellings and variations

      "insulindiabetic",
      "diabtic",
      "insulin and diabtic supplies",
      "diabeetic",
      "insulin and diabeetic supplies",
      "diaebtic",
    ]
  },
  160404: {
    displayName: "Birth Control",
    keywords: [
      "birth control",
      "contraception",
      "pill",
      "IUD",
      "contraceptive",
      // Adding some common misspellings and variations

      "birthcontrol",
      "conrol",
      "birth conrol",
      "conttrol",
      "birth conttrol",
      "cotnrol",
    ]
  },
  160405: {
    displayName: "Allergy Medications",
    keywords: [
      "antihistamine",
      "allergy meds",
      "hay fever medicine",
      "allergy pills",
      // Adding some common misspellings and variations

      "medications",
      "allergymedications",
      "medictions",
      "allergy medictions",
      "medicaations",
      "allergy medicaations",
    ]
  },
  160406: {
    displayName: "Mental Health Medications",
    keywords: [
      "antidepressant",
      "anxiety meds",
      "ADHD medication",
      "mental health prescription",
      // Adding some common misspellings and variations

      "medications",
      "mentalhealthmedications",
      "medictions",
      "mental health medictions",
      "medicaations",
      "mental health medicaations",
    ]
  },
  160407: {
    displayName: "Pain Relievers",
    keywords: [
      "painkillers",
      "pain relief",
      "ibuprofen",
      "paracetamol",
      "acetaminophen",
      // Adding some common misspellings and variations

      "relievers",
      "painrelievers",
      "relivers",
      "pain relivers",
      "relieevers",
      "pain relieevers",
    ]
  },
  160408: {
    displayName: "Antibiotics",
    keywords: [
      "antibiotics",
      "infection meds",
      "prescription antibiotics",
      // Adding some common misspellings and variations

      "antibotics",
      "antibiiotics",
      "antiibotics",
    ]
  },
  160501: {
    displayName: "Hearing Aids & Batteries",
    keywords: [
      "hearing aid",
      "hearing batteries",
      "hearing support",
      "hearing device",
      // Adding some common misspellings and variations

      "aids",
      "hearingaidsbatteries",
      "battries",
      "hearing aids and battries",
      "batteeries",
      "hearing aids and batteeries",
    ]
  },
  160502: {
    displayName: "Crutches / Walkers / Wheelchairs",
    keywords: [
      "crutches",
      "walker",
      "wheelchair",
      "mobility aid",
      "walking frame",
      // Adding some common misspellings and variations

      "walkers",
      "wheelchairs",
      "crutcheswalkerswheelchairs",
      "wheelhairs",
      "crutches walkers wheelhairs",
      "wheelcchairs",
    ]
  },
  160503: {
    displayName: "Blood Pressure Monitors",
    keywords: [
      "blood pressure monitor",
      "BP monitor",
      "cuff",
      "blood pressure machine",
      // Adding some common misspellings and variations

      "monitors",
      "bloodpressuremonitors",
      "moniors",
      "blood pressure moniors",
      "monittors",
      "blood pressure monittors",
    ]
  },
  160504: {
    displayName: "Glucose Meters & Test Strips",
    keywords: [
      "glucose meter",
      "test strips",
      "diabetic monitor",
      "blood sugar meter",
      // Adding some common misspellings and variations

      "meters",
      "glucosemetersteststrips",
      "gluose",
      "gluose meters and test strips",
      "gluccose",
      "gluccose meters and test strips",
    ]
  },
  160505: {
    displayName: "CPAP Machines & Supplies",
    keywords: [
      "CPAP",
      "sleep apnea machine",
      "CPAP mask",
      "CPAP tubing",
      // Adding some common misspellings and variations

      "machines",
      "cpapmachines",
      "machnes",
      "cpap machnes and supplies",
      "machiines",
      "cpap machiines and supplies",
    ]
  },
  160506: {
    displayName: "Nebulizers & Inhalers",
    keywords: [
      "nebulizer",
      "inhaler",
      "asthma inhaler",
      "breathing machine",
      // Adding some common misspellings and variations

      "nebulizers",
      "inhalers",
      "nebulizersinhalers",
      "nebulzers",
      "nebulzers and inhalers",
      "nebuliizers",
    ]
  },
  160507: {
    displayName: "Prosthetics & Orthotics",
    keywords: [
      "prosthetic",
      "orthotic",
      "brace",
      "support insert",
      "custom orthotic",
      // Adding some common misspellings and variations

      "prosthetics",
      "orthotics",
      "prostheticsorthotics",
      "prostetics",
      "prostetics and orthotics",
      "prosthhetics",
    ]
  },
  160508: {
    displayName: "Compression Stockings",
    keywords: [
      "compression stockings",
      "support socks",
      "medical stockings",
      // Adding some common misspellings and variations

      "compressionstockings",
      "comprssion",
      "comprssion stockings",
      "compreession",
      "compreession stockings",
      "comperssion",
    ]
  },
  160509: {
    displayName: "Ostomy Supplies",
    keywords: [
      "ostomy",
      "stoma supplies",
      "pouch",
      "ostomy bag",
      // Adding some common misspellings and variations

      "ostmy",
      "ostoomy",
      "osotmy",
    ]
  },
  160510: {
    displayName: "Catheters & Urological Supplies",
    keywords: [
      "catheter",
      "urology supplies",
      "drainage bag",
      "urinary supplies",
      // Adding some common misspellings and variations

      "catheters",
      "urological",
      "cathetersurological",
      "uroloical",
      "catheters and uroloical supplies",
      "urologgical",
    ]
  },
  160601: {
    displayName: "Hospital Stays",
    keywords: [
      "hospital stay",
      "inpatient",
      "admission",
      "ward stay",
      "overnight hospital",
      // Adding some common misspellings and variations

      "stays",
      "hospitalstays",
      "hosptal",
      "hosptal stays",
      "hospiital",
      "hospiital stays",
    ]
  },
  160602: {
    displayName: "Emergency Room Visits",
    keywords: [
      "ER",
      "emergency room",
      "A&E",
      "emergency department",
      "urgent care visit",
      // Adding some common misspellings and variations

      "visits",
      "emergencyroomvisits",
      "emerency",
      "emerency room visits",
      "emerggency",
      "emerggency room visits",
    ]
  },
  160603: {
    displayName: "Outpatient Surgery",
    keywords: [
      "outpatient surgery",
      "day surgery",
      "same day operation",
      // Adding some common misspellings and variations

      "outpatientsurgery",
      "outpaient",
      "outpaient surgery",
      "outpattient",
      "outpattient surgery",
      "outptaient",
    ]
  },
  160604: {
    displayName: "Anesthesia Fees",
    keywords: [
      "anesthesia",
      "anaesthesia",
      "anesthetist",
      "anaesthetist fee",
      // Adding some common misspellings and variations

      "anestesia",
      "anesthhesia",
      "aneshtesia",
    ]
  },
  160605: {
    displayName: "Diagnostic Procedures",
    keywords: [
      "biopsy",
      "endoscopy",
      "colonoscopy",
      "diagnostic procedure",
      "medical test procedure",
      // Adding some common misspellings and variations

      "procedures",
      "diagnosticprocedures",
      "diagnstic",
      "diagnstic procedures",
      "diagnoostic",
      "diagnoostic procedures",
    ]
  },
  160606: {
    displayName: "Ambulance & Emergency Transport",
    keywords: [
      "ambulance",
      "emergency transport",
      "medical transport",
      "paramedic transport",
      // Adding some common misspellings and variations

      "ambuance",
      "ambuance and emergency transport",
      "ambullance",
      "ambullance and emergency transport",
      "ambluance",
      "ambluance and emergency transport",
    ]
  },
  160607: {
    displayName: "Lab Tests & Blood Work",
    keywords: [
      "lab test",
      "blood work",
      "blood test",
      "pathology",
      "lab fees",
      // Adding some common misspellings and variations

      "tests",
      "testsbloodwork",
      "blod",
      "lab tests and blod work",
      "bloood",
      "lab tests and bloood work",
    ]
  },
  160608: {
    displayName: "X-Rays / MRIs / CT Scans",
    keywords: [
      "xray",
      "x-ray",
      "MRI",
      "CT scan",
      "imaging",
      "scan",
      "radiology",
      // Adding some common misspellings and variations

      "rays",
      "mris",
      "scans",
      "raysmrisscans",
      "scns",
      "x rays mris ct scns",
    ]
  },
  160701: {
    displayName: "Therapy Sessions",
    keywords: [
      "therapy",
      "counseling",
      "therapy session",
      "couples therapy",
      "family therapy",
      // Adding some common misspellings and variations

      "sessions",
      "therapysessions",
      "sessons",
      "therapy sessons",
      "sessiions",
      "therapy sessiions",
    ]
  },
  160702: {
    displayName: "Counseling Services",
    keywords: [
      "counseling",
      "counselling",
      "guidance",
      "support session",
      "counselling service",
      // Adding some common misspellings and variations

      "counsling",
      "counseeling",
      "counesling",
    ]
  },
  160703: {
    displayName: "Addiction Treatment Programs",
    keywords: [
      "addiction treatment",
      "rehab",
      "substance treatment",
      "recovery program",
      // Adding some common misspellings and variations

      "programs",
      "addictiontreatmentprograms",
      "addition",
      "addition treatment programs",
      "addicction",
      "addicction treatment programs",
    ]
  },
  160704: {
    displayName: "Rehab Facility Stays",
    keywords: [
      "rehab stay",
      "rehab facility",
      "rehabilitation center",
      "recovery facility",
      // Adding some common misspellings and variations

      "stays",
      "rehabfacilitystays",
      "faciity",
      "rehab faciity stays",
      "facillity",
      "rehab facillity stays",
    ]
  },
  160705: {
    displayName: "Smoking Cessation Programs",
    keywords: [
      "stop smoking",
      "smoking cessation",
      "nicotine program",
      "quit smoking help",
      // Adding some common misspellings and variations

      "programs",
      "smokingcessationprograms",
      "cesstion",
      "smoking cesstion programs",
      "cessaation",
      "smoking cessaation programs",
    ]
  },
  160706: {
    displayName: "Eating Disorder Treatment",
    keywords: [
      "eating disorder",
      "ED treatment",
      "recovery program",
      "specialist clinic",
      // Adding some common misspellings and variations

      "eatingdisordertreatment",
      "treament",
      "eating disorder treament",
      "treattment",
      "eating disorder treattment",
      "tretament",
    ]
  },
  160707: {
    displayName: "Autism & ADHD Assessments",
    keywords: [
      "autism assessment",
      "ADHD assessment",
      "neurodiversity evaluation",
      "diagnostic assessment",
      // Adding some common misspellings and variations

      "assessments",
      "autismadhdassessments",
      "assesments",
      "autism and adhd assesments",
      "assesssments",
      "autism and adhd assesssments",
    ]
  },
  160801: {
    displayName: "Vaccinations",
    keywords: [
      "vaccine",
      "vaccination",
      "flu shot",
      "booster",
      "immunization",
      // Adding some common misspellings and variations

      "vaccinations",
      "vaccintions",
      "vaccinaations",
      "vacciantions",
      "vacine",
      "vacccine",
    ]
  },
  160802: {
    displayName: "Cancer Screenings",
    keywords: [
      "mammogram",
      "colonoscopy screening",
      "smear test",
      "screening",
      "cancer check",
      // Adding some common misspellings and variations

      "screenings",
      "cancerscreenings",
      "screeings",
      "cancer screeings",
      "screennings",
      "cancer screennings",
    ]
  },
  160803: {
    displayName: "STD Testing",
    keywords: [
      "STD test",
      "STI test",
      "sexual health screening",
      "clinic test",
      // Adding some common misspellings and variations

      "testing",
      "tesing",
      "testting",
      "tetsing",
      "tet",
      "tesst",
    ]
  },
  160804: {
    displayName: "Genetic Testing",
    keywords: [
      "genetic test",
      "DNA health test",
      "hereditary screening",
      "gene screening",
      // Adding some common misspellings and variations

      "testing",
      "genetictesting",
      "gentic",
      "gentic testing",
      "geneetic",
      "geneetic testing",
    ]
  },
  160805: {
    displayName: "Weight-Loss Programs",
    keywords: [
      "weight loss program",
      "obesity treatment",
      "diet clinic",
      "medically supervised weight loss",
      // Adding some common misspellings and variations

      "programs",
      "weightlossprograms",
      "progams",
      "weight loss progams",
      "progrrams",
      "weight loss progrrams",
    ]
  },
  160806: {
    displayName: "Fertility Treatments & IVF",
    keywords: [
      "fertility",
      "IVF",
      "embryo treatment",
      "fertility clinic",
      "conception treatment",
      // Adding some common misspellings and variations

      "treatments",
      "fertilitytreatments",
      "treatents",
      "fertility treatents and ivf",
      "treatmments",
      "fertility treatmments and ivf",
    ]
  },
  160807: {
    displayName: "Prenatal Classes & Supplies",
    keywords: [
      "prenatal class",
      "birthing class",
      "pregnancy supplies",
      "maternity prep",
      // Adding some common misspellings and variations

      "classes",
      "prenatalclasses",
      "prental",
      "prental classes and supplies",
      "prenaatal",
      "prenaatal classes and supplies",
    ]
  },
  160901: {
    displayName: "Mileage to Medical Appointments",
    keywords: [
      "medical mileage",
      "doctor travel",
      "appointment mileage",
      "health travel cost",
      // Adding some common misspellings and variations

      "appointments",
      "mileagemedicalappointments",
      "appoinments",
      "mileage to medical appoinments",
      "appointtments",
      "mileage to medical appointtments",
    ]
  },
  160902: {
    displayName: "Public Transit for Medical Visits",
    keywords: [
      "bus to hospital",
      "clinic transport",
      "medical transit",
      "doctor appointment transport",
      // Adding some common misspellings and variations

      "public",
      "publictransitmedicalvisits",
      "medcal",
      "public transit for medcal visits",
      "mediical",
      "public transit for mediical visits",
    ]
  },
  160903: {
    displayName: "Lodging for Out-of-Town Treatment",
    keywords: [
      "hotel for treatment",
      "medical lodging",
      "hospital stay hotel",
      "treatment travel stay",
      // Adding some common misspellings and variations

      "town",
      "lodgingtowntreatment",
      "treament",
      "lodging for out of town treament",
      "treattment",
      "lodging for out of town treattment",
    ]
  },
  160904: {
    displayName: "Home Modifications",
    keywords: [
      "ramp",
      "grab bars",
      "accessibility install",
      "medical home adaptation",
      "support rails",
      // Adding some common misspellings and variations

      "modifications",
      "homemodifications",
      "modifiations",
      "home modifiations",
      "modificcations",
      "home modificcations",
    ]
  },
  160905: {
    displayName: "Service Animals & Training",
    keywords: [
      "service dog",
      "guide dog",
      "support animal",
      "service animal training",
      // Adding some common misspellings and variations

      "animals",
      "animalstraining",
      "traiing",
      "service animals and traiing",
      "trainning",
      "service animals and trainning",
    ]
  },
  160906: {
    displayName: "Special Transportation Services",
    keywords: [
      "patient transport",
      "mobility taxi",
      "accessible transport",
      "medical ride service",
      // Adding some common misspellings and variations

      "special",
      "transportation",
      "specialtransportation",
      "transpotation",
      "special transpotation services",
      "transporrtation",
    ]
  },
  161001: {
    displayName: "Over-the-Counter Medications",
    keywords: [
      "OTC meds",
      "over the counter",
      "pharmacy",
      "cough syrup",
      "non prescription medicine",
      // Adding some common misspellings and variations

      "medications",
      "overcountermedications",
      "medictions",
      "over the counter medictions",
      "medicaations",
      "over the counter medicaations",
    ],
    merchantHints: [
      "boots",
      "mccabes pharmacy",
      "lloyds pharmacy",
      "hickeys pharmacy",
      "allcare pharmacy",
      "meaghers pharmacy",
    ]
  },
  161002: {
    displayName: "Menstrual Care Products",
    keywords: [
      "pads",
      "tampons",
      "menstrual cup",
      "sanitary products",
      "period products",
      // Adding some common misspellings and variations

      "care",
      "menstrualcareproducts",
      "mensrual",
      "mensrual care products",
      "mensttrual",
      "mensttrual care products",
    ]
  },
  161003: {
    displayName: "Lactation Supplies",
    keywords: [
      "breast pump",
      "bottles",
      "nursing pads",
      "lactation",
      "feeding supplies",
      // Adding some common misspellings and variations

      "lacttion",
      "lactaation",
      "lacattion",
      "brest",
      "breaast",
      "pup",
    ]
  },
  161004: {
    displayName: "Medical Alert Devices",
    keywords: [
      "medical alert",
      "panic button",
      "emergency device",
      "alert bracelet",
      // Adding some common misspellings and variations

      "devices",
      "medicalalertdevices",
      "devces",
      "medical alert devces",
      "deviices",
      "medical alert deviices",
    ]
  },
  161005: {
    displayName: "Lead-Based Paint Removal",
    keywords: [
      "lead paint removal",
      "lead remediation",
      "toxic paint removal",
      // Adding some common misspellings and variations

      "based",
      "leadbasedpaintremoval",
      "remval",
      "lead based paint remval",
      "remooval",
      "lead based paint remooval",
    ]
  },
  161006: {
    displayName: "Special Diets",
    keywords: [
      "celiac diet",
      "PKU diet",
      "medically required food",
      "therapeutic diet",
      // Adding some common misspellings and variations

      "special",
      "diets",
      "specialdiets",
      "speial",
      "speial diets",
      "speccial",
    ]
  },
  170101: {
    displayName: "Credit Card Payment",
    keywords: [
      "credit card payment",
      "card bill",
      "visa payment",
      "mastercard payment",
      "Amex payment",
      // Adding some common misspellings and variations

      "creditcard",
      "creit",
      "creit card payment",
      "creddit",
      "creddit card payment",
      "crdeit",
    ]
  },
  170102: {
    displayName: "Credit Card Interest",
    keywords: [
      "card interest",
      "APR",
      "finance charge",
      "interest on card",
      // Adding some common misspellings and variations

      "credit",
      "creditcardinterest",
      "inteest",
      "credit card inteest",
      "interrest",
      "credit card interrest",
    ]
  },
  170103: {
    displayName: "Late Fees",
    keywords: [
      "late fee",
      "missed payment fee",
      "overdue card fee",
      "penalty fee",
      // Adding some common misspellings and variations

      "lae",
      "latte",
      "ltae",
    ]
  },
  170104: {
    displayName: "Cash Advance Fees",
    keywords: [
      "cash advance",
      "withdrawal fee",
      "card cash fee",
      // Adding some common misspellings and variations

      "cashadvance",
      "advnce",
      "cash advnce fees",
      "advaance",
      "cash advaance fees",
      "adavnce",
    ]
  },
  170105: {
    displayName: "Balance Transfer Fees",
    keywords: [
      "balance transfer",
      "transfer fee",
      "debt transfer fee",
      "promo transfer fee",
      // Adding some common misspellings and variations

      "balancetransfer",
      "tranfer",
      "balance tranfer fees",
      "transsfer",
      "balance transsfer fees",
      "trasnfer",
    ]
  },
  170201: {
    displayName: "Personal Loan Repayment",
    keywords: [
      "personal loan",
      "loan repayment",
      "installment loan",
      "monthly loan payment",
      // Adding some common misspellings and variations

      "personalloanrepayment",
      "repament",
      "personal loan repament",
      "repayyment",
      "personal loan repayyment",
      "repyament",
    ]
  },
  170202: {
    displayName: "Personal Loan Interest",
    keywords: [
      "personal loan interest",
      "loan APR",
      "borrowing interest",
      // Adding some common misspellings and variations

      "personalloaninterest",
      "inteest",
      "personal loan inteest",
      "interrest",
      "personal loan interrest",
      "intreest",
    ]
  },
  170203: {
    displayName: "Payday Loan",
    keywords: [
      "payday loan",
      "short term loan",
      "wage advance loan",
      // Adding some common misspellings and variations

      "paydayloan",
      "payay",
      "payay loan",
      "paydday",
      "paydday loan",
      "padyay",
    ]
  },
  170204: {
    displayName: "Buy Now Pay Later Repayment",
    keywords: [
      "BNPL",
      "klarna",
      "afterpay",
      "clearpay",
      "installment purchase payment",
      // Adding some common misspellings and variations

      "later",
      "repayment",
      "laterrepayment",
      "repament",
      "buy now pay later repament",
      "repayyment",
    ]
  },
  170205: {
    displayName: "Installment Plan Payment",
    keywords: [
      "installment plan",
      "monthly installment",
      "financing plan",
      "split payment",
      // Adding some common misspellings and variations

      "instalment",
      "installlment",
      "instlalment",
    ]
  },
  170301: {
    displayName: "Student Loan Repayment",
    keywords: [
      "student loan",
      "education loan payment",
      "university loan payment",
      // Adding some common misspellings and variations

      "repayment",
      "studentloanrepayment",
      "repament",
      "student loan repament",
      "repayyment",
      "student loan repayyment",
    ]
  },
  170302: {
    displayName: "Student Loan Interest",
    keywords: [
      "student loan interest",
      "education debt interest",
      // Adding some common misspellings and variations

      "studentloaninterest",
      "inteest",
      "student loan inteest",
      "interrest",
      "student loan interrest",
      "intreest",
    ]
  },
  170303: {
    displayName: "Tuition Financing",
    keywords: [
      "tuition finance",
      "school finance plan",
      "education installment",
      // Adding some common misspellings and variations

      "financing",
      "tuitionfinancing",
      "finacing",
      "tuition finacing",
      "finanncing",
      "tuition finanncing",
    ]
  },
  170304: {
    displayName: "Loan Servicing Fees",
    keywords: [
      "servicing fee",
      "admin fee loan",
      "student loan fee",
      // Adding some common misspellings and variations

      "loanservicing",
      "servcing",
      "loan servcing fees",
      "serviicing",
      "loan serviicing fees",
      "serivcing",
    ]
  },
  170401: {
    displayName: "Car Loan Payment",
    keywords: [
      "car payment",
      "auto loan",
      "vehicle finance",
      "monthly car loan",
      // Adding some common misspellings and variations

      "lon",
      "loaan",
      "laon",
    ]
  },
  170402: {
    displayName: "Motorcycle Loan Payment",
    keywords: [
      "bike loan",
      "motorcycle finance",
      "motorbike payment",
      // Adding some common misspellings and variations

      "motorcycleloan",
      "motorycle",
      "motorycle loan payment",
      "motorccycle",
      "motorccycle loan payment",
      "motocrycle",
    ]
  },
  170403: {
    displayName: "Vehicle Loan Interest",
    keywords: [
      "auto loan interest",
      "car finance interest",
      "vehicle borrowing interest",
      // Adding some common misspellings and variations

      "vehicleloaninterest",
      "inteest",
      "vehicle loan inteest",
      "interrest",
      "vehicle loan interrest",
      "intreest",
    ]
  },
  170501: {
    displayName: "Mortgage Principal",
    keywords: [
      "mortgage principal",
      "home loan principal",
      "housing debt principal",
      // Adding some common misspellings and variations

      "mortgageprincipal",
      "prinipal",
      "mortgage prinipal",
      "princcipal",
      "mortgage princcipal",
      "pricnipal",
    ]
  },
  170502: {
    displayName: "Mortgage Interest",
    keywords: [
      "mortgage interest",
      "home loan interest",
      "property debt interest",
      // Adding some common misspellings and variations

      "mortgageinterest",
      "inteest",
      "mortgage inteest",
      "interrest",
      "mortgage interrest",
      "intreest",
    ]
  },
  170503: {
    displayName: "Refinancing Fees",
    keywords: [
      "refinance fee",
      "remortgage fee",
      "refinancing cost",
      "mortgage refinancing",
      // Adding some common misspellings and variations

      "refinncing",
      "refinaancing",
      "refianncing",
      "refiance",
      "refinnance",
    ]
  },
  170504: {
    displayName: "Home Equity Loan Payment",
    keywords: [
      "home equity loan",
      "equity payment",
      "second mortgage payment",
      // Adding some common misspellings and variations

      "homeequityloan",
      "equty",
      "home equty loan payment",
      "equiity",
      "home equiity loan payment",
      "eqiuty",
    ]
  },
  170505: {
    displayName: "Home Equity Line Payment",
    keywords: [
      "HELOC",
      "equity line payment",
      "property credit line payment",
      // Adding some common misspellings and variations

      "home",
      "homeequityline",
      "equty",
      "home equty line payment",
      "equiity",
      "home equiity line payment",
    ]
  },
  170601: {
    displayName: "Loan Repayment to Family",
    keywords: [
      "repay family",
      "family loan",
      "loan to parents",
      "loan to sibling",
      // Adding some common misspellings and variations

      "repayment",
      "loanrepaymentfamily",
      "repament",
      "loan repament to family",
      "repayyment",
      "loan repayyment to family",
    ]
  },
  170602: {
    displayName: "Loan Repayment to Friends",
    keywords: [
      "repay friend",
      "friend loan",
      "informal debt",
      "private repayment",
      // Adding some common misspellings and variations

      "friends",
      "loanrepaymentfriends",
      "repament",
      "loan repament to friends",
      "repayyment",
      "loan repayyment to friends",
    ]
  },
  170603: {
    displayName: "Informal Borrowing Fees",
    keywords: [
      "informal loan fee",
      "private borrowing fee",
      "arrangement fee",
      // Adding some common misspellings and variations

      "informalborrowing",
      "borrwing",
      "informal borrwing fees",
      "borroowing",
      "informal borroowing fees",
      "bororwing",
    ]
  },
  170701: {
    displayName: "Debt Collection Payment",
    keywords: [
      "collection payment",
      "collector",
      "debt agency",
      "debt collection",
      // Adding some common misspellings and variations

      "debtcollection",
      "colletion",
      "debt colletion payment",
      "collecction",
      "debt collecction payment",
      "collcetion",
    ]
  },
  170702: {
    displayName: "Settlement Payment",
    keywords: [
      "settlement",
      "debt settlement",
      "negotiated payment",
      "payoff amount",
      // Adding some common misspellings and variations

      "settlment",
      "settleement",
      "settelment",
    ]
  },
  170703: {
    displayName: "Arrears Payment",
    keywords: [
      "arrears",
      "overdue debt",
      "back payment",
      "late debt payment",
      // Adding some common misspellings and variations

      "arrars",
      "arreears",
      "arerars",
    ]
  },
  170704: {
    displayName: "Court-ordered Debt Payment",
    keywords: [
      "court debt",
      "legal repayment",
      "judgment payment",
      "ordered payment",
      // Adding some common misspellings and variations

      "courtordereddebt",
      "ordred",
      "court ordred debt payment",
      "ordeered",
      "court ordeered debt payment",
      "oredred",
    ]
  },
  170801: {
    displayName: "Debt Consolidation Payment",
    keywords: [
      "debt consolidation",
      "consolidation loan",
      "merged debt payment",
      // Adding some common misspellings and variations

      "debtconsolidation",
      "consoldation",
      "debt consoldation payment",
      "consoliidation",
      "debt consoliidation payment",
      "consoildation",
    ]
  },
  170802: {
    displayName: "Loan Application Fees",
    keywords: [
      "application fee",
      "loan fee",
      "processing fee",
      "origination fee",
      // Adding some common misspellings and variations

      "loanapplication",
      "appliation",
      "loan appliation fees",
      "appliccation",
      "loan appliccation fees",
      "applciation",
    ]
  },
  170803: {
    displayName: "Other Loan Costs",
    keywords: [
      "loan misc",
      "debt misc",
      "other borrowing cost",
      "uncategorized loan",
      // Adding some common misspellings and variations

      "lon",
      "loaan",
      "laon",
    ]
  },
  180101: {
    displayName: "Emergency Fund Contribution",
    keywords: [
      "emergency fund",
      "rainy day fund",
      "safety fund",
      "emergency savings",
      // Adding some common misspellings and variations

      "contribution",
      "emergencyfundcontribution",
      "contriution",
      "emergency fund contriution",
      "contribbution",
      "emergency fund contribbution",
    ]
  },
  180102: {
    displayName: "General Savings Transfer",
    keywords: [
      "savings transfer",
      "put in savings",
      "transfer to savings",
      "save money",
      // Adding some common misspellings and variations

      "general",
      "generalsavingstransfer",
      "tranfer",
      "general savings tranfer",
      "transsfer",
      "general savings transsfer",
    ]
  },
  180103: {
    displayName: "Sinking Fund Contribution",
    keywords: [
      "sinking fund",
      "target savings",
      "bucket saving",
      "planned savings",
      // Adding some common misspellings and variations

      "contribution",
      "sinkingfundcontribution",
      "contriution",
      "sinking fund contriution",
      "contribbution",
      "sinking fund contribbution",
    ]
  },
  180104: {
    displayName: "Holiday Savings",
    keywords: [
      "holiday savings",
      "vacation savings",
      "travel savings",
      "trip fund",
      // Adding some common misspellings and variations

      "holidaysavings",
      "holday",
      "holday savings",
      "holiiday",
      "holiiday savings",
      "hoilday",
    ]
  },
  180105: {
    displayName: "Down Payment Savings",
    keywords: [
      "down payment",
      "house deposit savings",
      "home fund",
      "first home savings",
      // Adding some common misspellings and variations

      "downsavings",
      "savngs",
      "down payment savngs",
      "saviings",
      "down payment saviings",
      "saivngs",
    ]
  },
  180201: {
    displayName: "Pension Contribution",
    keywords: [
      "pension",
      "pension contribution",
      "retirement plan",
      "workplace pension",
      // Adding some common misspellings and variations

      "pensioncontribution",
      "contriution",
      "pension contriution",
      "contribbution",
      "pension contribbution",
      "contrbiution",
    ]
  },
  180202: {
    displayName: "Retirement Account Contribution",
    keywords: [
      "IRA",
      "PRSA",
      "retirement account",
      "retirement contribution",
      // Adding some common misspellings and variations

      "contriution",
      "retirement account contriution",
      "contribbution",
      "retirement account contribbution",
      "contrbiution",
      "retirement account contrbiution",
    ]
  },
  180203: {
    displayName: "Employer Match Top-up",
    keywords: [
      "employer match",
      "top-up",
      "pension match",
      "retirement matching",
      // Adding some common misspellings and variations

      "employermatch",
      "emplyer",
      "emplyer match top up",
      "emplooyer",
      "emplooyer match top up",
      "empolyer",
    ]
  },
  180204: {
    displayName: "Retirement Fees",
    keywords: [
      "pension fee",
      "retirement account fee",
      "management fee retirement",
      // Adding some common misspellings and variations

      "retirment",
      "retireement",
      "retierment",
      "penion",
      "penssion",
    ]
  },
  180301: {
    displayName: "Brokerage Contribution",
    keywords: [
      "brokerage transfer",
      "investing account transfer",
      "stock account funding",
      // Adding some common misspellings and variations

      "contribution",
      "brokeragecontribution",
      "contriution",
      "brokerage contriution",
      "contribbution",
      "brokerage contribbution",
    ]
  },
  180302: {
    displayName: "ETF Purchase",
    keywords: [
      "ETF",
      "exchange traded fund",
      "fund purchase",
      "index fund",
      // Adding some common misspellings and variations

      "purcase",
      "purchhase",
      "purhcase",
    ]
  },
  180303: {
    displayName: "Stock Purchase",
    keywords: [
      "stock",
      "shares",
      "equity purchase",
      "company stock",
      // Adding some common misspellings and variations

      "stockpurchase",
      "purcase",
      "stock purcase",
      "purchhase",
      "stock purchhase",
      "purhcase",
    ]
  },
  180304: {
    displayName: "Mutual Fund Purchase",
    keywords: [
      "mutual fund",
      "fund investment",
      "managed fund purchase",
      // Adding some common misspellings and variations

      "mutualfundpurchase",
      "purcase",
      "mutual fund purcase",
      "purchhase",
      "mutual fund purchhase",
      "purhcase",
    ]
  },
  180305: {
    displayName: "Bond Purchase",
    keywords: [
      "bond",
      "treasury",
      "fixed income",
      "bond investment",
      // Adding some common misspellings and variations

      "purchase",
      "bondpurchase",
      "purcase",
      "bond purcase",
      "purchhase",
      "bond purchhase",
    ]
  },
  180306: {
    displayName: "Crypto Purchase",
    keywords: [
      "crypto",
      "bitcoin",
      "ethereum",
      "digital asset",
      "coin purchase",
      // Adding some common misspellings and variations

      "cryptopurchase",
      "purcase",
      "crypto purcase",
      "purchhase",
      "crypto purchhase",
      "purhcase",
    ]
  },
  180307: {
    displayName: "Robo-advisor Contribution",
    keywords: [
      "robo advisor",
      "automated investing",
      "managed portfolio contribution",
      // Adding some common misspellings and variations

      "roboadvisorcontribution",
      "contriution",
      "robo advisor contriution",
      "contribbution",
      "robo advisor contribbution",
      "contrbiution",
    ]
  },
  180401: {
    displayName: "Trading Fees",
    keywords: [
      "trading fee",
      "commission",
      "transaction fee",
      "broker commission",
      // Adding some common misspellings and variations

      "traing",
      "tradding",
      "trdaing",
    ]
  },
  180402: {
    displayName: "Platform Fees",
    keywords: [
      "platform fee",
      "brokerage fee",
      "app fee",
      "account fee",
      // Adding some common misspellings and variations

      "platorm",
      "platfform",
      "plaftorm",
    ]
  },
  180403: {
    displayName: "Custody Fees",
    keywords: [
      "custody fee",
      "safekeeping fee",
      "account holding fee",
      // Adding some common misspellings and variations

      "cusody",
      "custtody",
      "cutsody",
    ]
  },
  180404: {
    displayName: "Advisory Fees",
    keywords: [
      "advisory fee",
      "advisor fee",
      "wealth management fee",
      "planning fee",
      // Adding some common misspellings and variations

      "adviory",
      "advissory",
      "advsiory",
    ]
  },
  180405: {
    displayName: "Fund Management Fees",
    keywords: [
      "management fee",
      "expense ratio",
      "fund fee",
      "annual charge",
      // Adding some common misspellings and variations

      "fundmanagement",
      "managment",
      "fund managment fees",
      "manageement",
      "fund manageement fees",
      "manaegment",
    ]
  },
  180501: {
    displayName: "College Savings",
    keywords: [
      "college fund",
      "university savings",
      "education savings",
      "tuition savings",
      // Adding some common misspellings and variations

      "collegesavings",
      "colege",
      "colege savings",
      "colllege",
      "colllege savings",
      "savngs",
    ]
  },
  180502: {
    displayName: "Child Savings Account",
    keywords: [
      "child savings",
      "kids account",
      "child fund",
      "junior savings",
      // Adding some common misspellings and variations

      "childsavingsaccount",
      "accunt",
      "child savings accunt",
      "accoount",
      "child savings accoount",
      "acocunt",
    ]
  },
  180503: {
    displayName: "Trust Fund Contribution",
    keywords: [
      "trust fund",
      "beneficiary trust",
      "trust contribution",
      // Adding some common misspellings and variations

      "trustfundcontribution",
      "contriution",
      "trust fund contriution",
      "contribbution",
      "trust fund contribbution",
      "contrbiution",
    ]
  },
  180601: {
    displayName: "Gold / Precious Metals",
    keywords: [
      "gold",
      "silver",
      "precious metals",
      "bullion",
      "metal investment",
      // Adding some common misspellings and variations

      "goldpreciousmetals",
      "precous",
      "gold precous metals",
      "preciious",
      "gold preciious metals",
      "preicous",
    ]
  },
  180602: {
    displayName: "Collectibles as Investment",
    keywords: [
      "collectible investment",
      "art investment",
      "trading card investment",
      "memorabilia investment",
      // Adding some common misspellings and variations

      "collectibles",
      "collectiblesinvestment",
      "collecibles",
      "collecibles as investment",
      "collecttibles",
      "collecttibles as investment",
    ]
  },
  180603: {
    displayName: "Investment Tax Payments",
    keywords: [
      "capital gains tax",
      "dividend tax",
      "investment tax",
      "trading tax",
      // Adding some common misspellings and variations

      "invesment",
      "investtment",
      "invetsment",
      "captal",
      "capiital",
      "gans",
    ]
  },
  180604: {
    displayName: "Other Investment Transfers",
    keywords: [
      "investment transfer",
      "account funding other",
      "securities transfer",
      // Adding some common misspellings and variations

      "transfers",
      "investmenttransfers",
      "invesment",
      "other invesment transfers",
      "investtment",
      "other investtment transfers",
    ]
  },
  190101: {
    displayName: "Haircuts / Barber",
    keywords: [
      "haircut",
      "barber",
      "trim",
      "salon haircut",
      "fade",
      "hair appointment",
      // Adding some common misspellings and variations

      "haircuts",
      "haircutsbarber",
      "hairuts",
      "hairuts barber",
      "hairccuts",
      "hairccuts barber",
    ],
    merchantHints: [
      "peter mark",
      "toni and guy",
      "toni&guy",
    ]
  },
  190102: {
    displayName: "Salon Services",
    keywords: [
      "salon",
      "blow dry",
      "styling",
      "color",
      "highlights",
      "beauty salon",
      // Adding some common misspellings and variations

      "saon",
      "sallon",
      "slaon",
    ],
    merchantHints: [
      "peter mark",
      "toni and guy",
      "toni&guy",
      "brown thomas",
    ]
  },
  190103: {
    displayName: "Skincare",
    keywords: [
      "skincare",
      "moisturizer",
      "serum",
      "cleanser",
      "toner",
      "face cream",
      // Adding some common misspellings and variations

      "skinare",
      "skinccare",
      "skicnare",
    ],
    merchantHints: [
      "boots",
      "brown thomas",
      "space nk",
      "mccabes pharmacy",
      "meaghers pharmacy",
    ]
  },
  190104: {
    displayName: "Makeup",
    keywords: [
      "makeup",
      "foundation",
      "mascara",
      "lipstick",
      "cosmetics",
      "beauty products",
      // Adding some common misspellings and variations

      "makup",
      "makeeup",
      "maekup",
    ],
    merchantHints: [
      "boots",
      "brown thomas",
      "space nk",
      "sephora",
    ]
  },
  190105: {
    displayName: "Nail Services",
    keywords: [
      "nails",
      "manicure",
      "pedicure",
      "nail salon",
      "gel nails",
      // Adding some common misspellings and variations

      "nal",
      "naiil",
      "nial",
      "nals",
      "naiils",
    ]
  },
  190106: {
    displayName: "Waxing / Threading",
    keywords: [
      "waxing",
      "threading",
      "eyebrow threading",
      "hair removal",
      // Adding some common misspellings and variations

      "waxingthreading",
      "threding",
      "waxing threding",
      "threaading",
      "waxing threaading",
      "thraeding",
    ]
  },
  190107: {
    displayName: "Spa Treatments",
    keywords: [
      "spa",
      "facial",
      "sauna",
      "spa day",
      "relaxation treatment",
      // Adding some common misspellings and variations

      "treatments",
      "treatents",
      "treatmments",
      "treamtents",
    ]
  },
  190201: {
    displayName: "Toothpaste / Oral Hygiene",
    keywords: [
      "toothpaste",
      "toothbrush",
      "floss",
      "mouthwash",
      "oral care",
      // Adding some common misspellings and variations

      "hygiene",
      "toothpasteoralhygiene",
      "toothaste",
      "toothaste oral hygiene",
      "toothppaste",
      "toothppaste oral hygiene",
    ],
    merchantHints: [
      "boots",
      "mccabes pharmacy",
      "allcare pharmacy",
      "hickeys pharmacy",
    ]
  },
  190202: {
    displayName: "Soap / Body Wash",
    keywords: [
      "soap",
      "shower gel",
      "body wash",
      "hand soap",
      // Adding some common misspellings and variations

      "soapbodywash",
      "boy",
      "soap boy wash",
      "boddy",
      "soap boddy wash",
      "bdoy",
    ]
  },
  190203: {
    displayName: "Shampoo / Conditioner",
    keywords: [
      "shampoo",
      "conditioner",
      "hair wash",
      "scalp care",
      // Adding some common misspellings and variations

      "shampooconditioner",
      "condiioner",
      "shampoo condiioner",
      "condittioner",
      "shampoo condittioner",
      "condtiioner",
    ]
  },
  190204: {
    displayName: "Deodorant",
    keywords: [
      "deodorant",
      "antiperspirant",
      "body spray",
      // Adding some common misspellings and variations

      "deodrant",
      "deodoorant",
      "deoodrant",
    ]
  },
  190205: {
    displayName: "Feminine Hygiene",
    keywords: [
      "feminine hygiene",
      "pads",
      "tampons",
      "liners",
      "intimate care",
      // Adding some common misspellings and variations

      "femininehygiene",
      "femiine",
      "femiine hygiene",
      "feminnine",
      "feminnine hygiene",
      "femniine",
    ]
  },
  190206: {
    displayName: "Shaving Supplies",
    keywords: [
      "razor",
      "shaving cream",
      "blades",
      "beard trimmer",
      "shave gel",
      // Adding some common misspellings and variations

      "shaing",
      "shavving",
      "shvaing",
      "raor",
      "razzor",
    ]
  },
  190207: {
    displayName: "Toiletry Refills",
    keywords: [
      "refill",
      "toiletries refill",
      "refill soap",
      "refill shampoo",
      "hygiene refill",
      // Adding some common misspellings and variations

      "toiletry",
      "refills",
      "toiletryrefills",
      "toiltry",
      "toiltry refills",
      "toileetry",
    ]
  },
  190301: {
    displayName: "Dry Cleaning",
    keywords: [
      "dry cleaning",
      "suit cleaning",
      "garment cleaning",
      "cleaner",
      // Adding some common misspellings and variations

      "cleaing",
      "cleanning",
      "clenaing",
    ]
  },
  190302: {
    displayName: "Laundry Services",
    keywords: [
      "laundromat",
      "laundry service",
      "wash and fold",
      "coin laundry",
      // Adding some common misspellings and variations

      "laudry",
      "launndry",
      "lanudry",
      "laundomat",
      "laundrromat",
    ]
  },
  190303: {
    displayName: "Tailoring / Alterations",
    keywords: [
      "tailoring",
      "alteration",
      "hem",
      "resize",
      "clothing adjustment",
      // Adding some common misspellings and variations

      "alterations",
      "tailoringalterations",
      "altertions",
      "tailoring altertions",
      "alteraations",
      "tailoring alteraations",
    ]
  },
  190304: {
    displayName: "Shoe Repair",
    keywords: [
      "shoe repair",
      "heel repair",
      "sole replacement",
      "cobbler",
      // Adding some common misspellings and variations

      "shoerepair",
      "repir",
      "shoe repir",
      "repaair",
      "shoe repaair",
      "reapir",
    ]
  },
  190401: {
    displayName: "Gym Membership",
    keywords: [
      "gym",
      "gym fee",
      "fitness membership",
      "health club",
      // Adding some common misspellings and variations

      "membeship",
      "memberrship",
      "membreship",
    ],
    merchantHints: [
      "flyefit",
      "flye fit",
      "bd gyms",
      "ben dunne gyms",
      "gym plus",
      "one escape",
    ]
  },
  190402: {
    displayName: "Yoga / Pilates",
    keywords: [
      "yoga",
      "pilates",
      "studio class",
      "yoga pass",
      // Adding some common misspellings and variations

      "yogapilates",
      "piltes",
      "yoga piltes",
      "pilaates",
      "yoga pilaates",
      "pialtes",
    ]
  },
  190403: {
    displayName: "Fitness Classes",
    keywords: [
      "fitness class",
      "spin",
      "HIIT",
      "dance fitness",
      "class pack",
      // Adding some common misspellings and variations

      "classes",
      "fitnessclasses",
      "clases",
      "fitness clases",
      "classses",
      "fitness classses",
    ]
  },
  190404: {
    displayName: "Personal Trainer",
    keywords: [
      "PT",
      "trainer",
      "coach",
      "personal trainer session",
      // Adding some common misspellings and variations

      "personaltrainer",
      "persnal",
      "persnal trainer",
      "persoonal",
      "persoonal trainer",
      "perosnal",
    ]
  },
  190405: {
    displayName: "Massage",
    keywords: [
      "massage",
      "sports massage",
      "deep tissue",
      "body massage",
      // Adding some common misspellings and variations

      "masage",
      "masssage",
    ]
  },
  190406: {
    displayName: "Supplements / Vitamins",
    keywords: [
      "vitamins",
      "supplements",
      "omega",
      "multivitamin",
      "minerals",
      // Adding some common misspellings and variations

      "supplementsvitamins",
      "supplments",
      "supplments vitamins",
      "suppleements",
      "suppleements vitamins",
      "suppelments",
    ]
  },
  190407: {
    displayName: "Wellness Apps",
    keywords: [
      "meditation app",
      "fitness app",
      "wellness subscription",
      "health app",
      // Adding some common misspellings and variations

      "apps",
      "wellnessapps",
      "welless",
      "welless apps",
      "wellnness",
      "wellnness apps",
    ]
  },
  190501: {
    displayName: "Fragrances",
    keywords: [
      "perfume",
      "cologne",
      "fragrance",
      "scent",
      // Adding some common misspellings and variations

      "fragrances",
      "fragrnces",
      "fragraances",
      "fragarnces",
      "perume",
      "perffume",
    ]
  },
  190502: {
    displayName: "Tanning",
    keywords: [
      "tanning",
      "spray tan",
      "sunbed",
      "self tan",
      // Adding some common misspellings and variations

      "taning",
      "tannning",
      "tannin",
    ]
  },
  190503: {
    displayName: "Cosmetic Procedures",
    keywords: [
      "cosmetic treatment",
      "botox",
      "fillers",
      "aesthetic treatment",
      // Adding some common misspellings and variations

      "procedures",
      "cosmeticprocedures",
      "proceures",
      "cosmetic proceures",
      "proceddures",
      "cosmetic proceddures",
    ]
  },
  190504: {
    displayName: "Other Self-care Spending",
    keywords: [
      "self care misc",
      "beauty other",
      "personal care other",
      // Adding some common misspellings and variations

      "spending",
      "selfcarespending",
      "spening",
      "other self care spening",
      "spendding",
      "other self care spendding",
    ]
  },
  200101: {
    displayName: "Daycare",
    keywords: [
      "daycare",
      "nursery",
      "child care center",
      "creche",
      // Adding some common misspellings and variations

      "dayare",
      "dayccare",
      "dacyare",
    ]
  },
  200102: {
    displayName: "Nanny / Babysitter",
    keywords: [
      "nanny",
      "babysitter",
      "sitter",
      "child minder",
      "babysitting",
      // Adding some common misspellings and variations

      "nannybabysitter",
      "babystter",
      "nanny babystter",
      "babysiitter",
      "nanny babysiitter",
      "babyistter",
    ]
  },
  200103: {
    displayName: "After-school Care",
    keywords: [
      "after school",
      "aftercare",
      "school club care",
      "pickup care",
      // Adding some common misspellings and variations

      "afterschoolcare",
      "schol",
      "after schol care",
      "schoool",
      "after schoool care",
      "scohol",
    ]
  },
  200104: {
    displayName: "Preschool",
    keywords: [
      "preschool",
      "playschool",
      "early learning",
      "pre-k",
      // Adding some common misspellings and variations

      "preshool",
      "prescchool",
      "precshool",
    ]
  },
  200105: {
    displayName: "Summer Camp Childcare",
    keywords: [
      "summer camp",
      "holiday camp",
      "school break camp",
      "kids camp",
      // Adding some common misspellings and variations

      "childcare",
      "summercampchildcare",
      "chilcare",
      "summer camp chilcare",
      "childdcare",
      "summer camp childdcare",
    ]
  },
  200106: {
    displayName: "Childminder",
    keywords: [
      "childminder",
      "child minder",
      "home childcare",
      "local minder",
      // Adding some common misspellings and variations

      "childinder",
      "childmminder",
      "chilmdinder",
    ]
  },
  200201: {
    displayName: "Diapers & Wipes",
    keywords: [
      "diapers",
      "nappies",
      "wipes",
      "baby wipes",
      "nappy supplies",
      // Adding some common misspellings and variations

      "diaperswipes",
      "diaers",
      "diaers and wipes",
      "diappers",
      "diappers and wipes",
      "dipaers",
    ]
  },
  200202: {
    displayName: "Formula",
    keywords: [
      "formula",
      "baby formula",
      "infant milk",
      "bottle formula",
      // Adding some common misspellings and variations

      "forula",
      "formmula",
      "fomrula",
    ]
  },
  200203: {
    displayName: "Baby Gear",
    keywords: [
      "stroller",
      "pram",
      "buggy",
      "carrier",
      "high chair",
      "cot gear",
      // Adding some common misspellings and variations

      "baby",
      "babygear",
      "bay",
      "bay gear",
      "babby",
      "babby gear",
    ]
  },
  200204: {
    displayName: "School Lunches",
    keywords: [
      "school lunch",
      "lunch money",
      "cafeteria",
      "canteen payment",
      // Adding some common misspellings and variations

      "lunches",
      "schoollunches",
      "lunhes",
      "school lunhes",
      "luncches",
      "school luncches",
    ],
    aliases: [
      "paying for my kids school lunches",
      "kids school lunch money",
      "school canteen money",
      "lunch account top up",
      "school lunch payment",
      "kids lunch account",
    ]
  },
  200205: {
    displayName: "School Supplies",
    keywords: [
      "school supplies",
      "notebooks",
      "pencils",
      "school bag",
      "stationery school",
      // Adding some common misspellings and variations

      "schol",
      "schoool",
      "scohol",
    ]
  },
  200206: {
    displayName: "Kids Clothing",
    keywords: [
      "kids clothes",
      "children clothing",
      "baby clothes",
      "school uniform wear",
      // Adding some common misspellings and variations

      "kidsclothing",
      "cloting",
      "kids cloting",
      "clothhing",
      "kids clothhing",
      "clohting",
    ]
  },
  200207: {
    displayName: "Baby Furniture",
    keywords: [
      "crib",
      "cot",
      "changing table",
      "baby chair",
      "nursery furniture",
      // Adding some common misspellings and variations

      "babyfurniture",
      "furnture",
      "baby furnture",
      "furniiture",
      "baby furniiture",
      "furinture",
    ]
  },
  200301: {
    displayName: "Sports Fees",
    keywords: [
      "kids sport",
      "football fee",
      "swimming lessons",
      "club fee",
      "sports class",
      // Adding some common misspellings and variations

      "spots",
      "sporrts",
      "sprots",
      "kis",
      "kidds",
      "sprt",
    ]
  },
  200302: {
    displayName: "Music Lessons",
    keywords: [
      "piano lessons",
      "guitar lessons",
      "music class",
      "music tuition",
      // Adding some common misspellings and variations

      "musiclessons",
      "lesons",
      "music lesons",
      "lesssons",
      "music lesssons",
      "muic",
    ]
  },
  200303: {
    displayName: "Dance Lessons",
    keywords: [
      "dance class",
      "ballet",
      "hip hop class",
      "dance tuition",
      // Adding some common misspellings and variations

      "lessons",
      "dancelessons",
      "lesons",
      "dance lesons",
      "lesssons",
      "dance lesssons",
    ]
  },
  200304: {
    displayName: "Tutoring",
    keywords: [
      "tutor",
      "tutoring",
      "extra lessons",
      "private tuition",
      // Adding some common misspellings and variations

      "tutoing",
      "tutorring",
      "tutroing",
      "tuor",
      "tuttor",
    ]
  },
  200305: {
    displayName: "Clubs / Scouts",
    keywords: [
      "scouts",
      "brownies",
      "youth club",
      "activity club",
      "after-school club",
      // Adding some common misspellings and variations

      "clubs",
      "clubsscouts",
      "scots",
      "clubs scots",
      "scouuts",
      "clubs scouuts",
    ]
  },
  200306: {
    displayName: "Kids Entertainment",
    keywords: [
      "soft play",
      "kids cinema",
      "trampoline park",
      "children entertainment",
      // Adding some common misspellings and variations

      "kidsentertainment",
      "entertinment",
      "kids entertinment",
      "entertaainment",
      "kids entertaainment",
      "enteratinment",
    ]
  },
  200307: {
    displayName: "Birthday Parties",
    keywords: [
      "birthday party",
      "kids party",
      "party venue",
      "birthday entertainer",
      // Adding some common misspellings and variations

      "parties",
      "birthdayparties",
      "birtday",
      "birtday parties",
      "birthhday",
      "birthhday parties",
    ]
  },
  200401: {
    displayName: "Elder Care",
    keywords: [
      "elder care",
      "senior care",
      "aged care",
      "older parent support",
      // Adding some common misspellings and variations

      "eldercare",
      "eler",
      "eler care",
      "eldder",
      "eldder care",
      "edler",
    ]
  },
  200402: {
    displayName: "Assisted Living Support",
    keywords: [
      "assisted living",
      "care home support",
      "senior residence support",
      // Adding some common misspellings and variations

      "assistedliving",
      "assited",
      "assited living support",
      "assissted",
      "assissted living support",
      "asssited",
    ]
  },
  200403: {
    displayName: "Respite Care",
    keywords: [
      "respite care",
      "temporary care",
      "caregiver break",
      "support stay",
      // Adding some common misspellings and variations

      "respitecare",
      "resite",
      "resite care",
      "resppite",
      "resppite care",
      "repsite",
    ]
  },
  200404: {
    displayName: "In-home Support Services",
    keywords: [
      "home help",
      "carer",
      "in-home support",
      "caregiver visit",
      "support worker",
      // Adding some common misspellings and variations

      "hoe",
      "homme",
      "hmoe",
      "hep",
      "hellp",
    ]
  },
  200405: {
    displayName: "Caregiver Support",
    keywords: [
      "caregiver support",
      "support services",
      "respite support",
      "care assistance",
      // Adding some common misspellings and variations

      "careiver",
      "careggiver",
      "cargeiver",
    ]
  },
  200406: {
    displayName: "Adult Day Care",
    keywords: [
      "adult day care",
      "senior day center",
      "day support service",
      // Adding some common misspellings and variations

      "adultcare",
      "adlt",
      "adlt day care",
      "aduult",
      "aduult day care",
      "audlt",
    ]
  },
  200501: {
    displayName: "Family Celebrations",
    keywords: [
      "family event",
      "celebration",
      "reunion",
      "party family",
      "home gathering",
      // Adding some common misspellings and variations

      "celebrations",
      "familycelebrations",
      "celebrtions",
      "family celebrtions",
      "celebraations",
      "family celebraations",
    ]
  },
  200502: {
    displayName: "School Events",
    keywords: [
      "school event",
      "fundraiser",
      "school trip contribution",
      "school performance",
      // Adding some common misspellings and variations

      "events",
      "schoolevents",
      "evets",
      "school evets",
      "evennts",
      "school evennts",
    ]
  },
  200503: {
    displayName: "Family Travel",
    keywords: [
      "family trip",
      "travel with kids",
      "family holiday",
      "family outing",
      // Adding some common misspellings and variations

      "familytravel",
      "famly",
      "famly travel",
      "famiily",
      "famiily travel",
      "faimly",
    ]
  },
  200504: {
    displayName: "Children's Gifts",
    keywords: [
      "kids gifts",
      "presents for child",
      "toys gift",
      "children's present",
      // Adding some common misspellings and variations

      "childrengifts",
      "chilren",
      "chilren s gifts",
      "childdren",
      "childdren s gifts",
      "chidlren",
    ]
  },
  200505: {
    displayName: "Child Maintenance / Support",
    keywords: [
      "child support",
      "maintenance payment",
      "family support payment",
      // Adding some common misspellings and variations

      "childmaintenance",
      "maintnance",
      "child maintnance support",
      "mainteenance",
      "child mainteenance support",
      "mainetnance",
    ]
  },
  200506: {
    displayName: "Adoption / Foster Costs",
    keywords: [
      "adoption fee",
      "foster cost",
      "foster support",
      "adoption services",
      // Adding some common misspellings and variations

      "adoptionfoster",
      "adopion",
      "adopion foster costs",
      "adopttion",
      "adopttion foster costs",
      "adotpion",
    ]
  },
  200507: {
    displayName: "Family Legal Support",
    keywords: [
      "custody legal fee",
      "family solicitor",
      "family mediation",
      "support legal",
      // Adding some common misspellings and variations

      "familylegal",
      "famly",
      "famly legal support",
      "famiily",
      "famiily legal support",
      "faimly",
    ]
  },
  210101: {
    displayName: "Cinema",
    keywords: [
      "cinema",
      "movie",
      "film ticket",
      "theater movie",
      "multiplex",
      // Adding some common misspellings and variations

      "cinma",
      "cineema",
      "cienma",
    ],
    merchantHints: [
      "omniplex",
      "imc",
      "cineworld",
      "odeon",
      "lighthouse cinema",
      "irish film institute",
      "stella cinema",
    ]
  },
  210102: {
    displayName: "Concerts",
    keywords: [
      "concert",
      "gig",
      "live music",
      "festival ticket",
      "band ticket",
      // Adding some common misspellings and variations

      "concerts",
      "concrts",
      "conceerts",
      "conecrts",
      "conert",
      "conccert",
    ]
  },
  210103: {
    displayName: "Theatre",
    keywords: [
      "theatre",
      "theater",
      "play",
      "musical",
      "stage show",
      // Adding some common misspellings and variations

      "thetre",
      "theaatre",
      "thaetre",
    ]
  },
  210104: {
    displayName: "Museums",
    keywords: [
      "museum",
      "gallery",
      "exhibition",
      "cultural ticket",
      // Adding some common misspellings and variations

      "museums",
      "musums",
      "museeums",
      "muesums",
      "musum",
      "museeum",
    ]
  },
  210105: {
    displayName: "Theme Parks",
    keywords: [
      "theme park",
      "amusement park",
      "rollercoaster",
      "attraction park",
      // Adding some common misspellings and variations

      "parks",
      "themeparks",
      "paks",
      "theme paks",
      "parrks",
      "theme parrks",
    ]
  },
  210106: {
    displayName: "Festivals",
    keywords: [
      "festival",
      "fair",
      "event pass",
      "cultural festival",
      // Adding some common misspellings and variations

      "festivals",
      "festvals",
      "festiivals",
      "fesitvals",
      "festval",
      "festiival",
    ]
  },
  210107: {
    displayName: "Sports Events",
    keywords: [
      "match ticket",
      "stadium",
      "sports event",
      "game ticket",
      "league ticket",
      // Adding some common misspellings and variations

      "events",
      "sportsevents",
      "evets",
      "sports evets",
      "evennts",
      "sports evennts",
    ]
  },
  210201: {
    displayName: "Video Games",
    keywords: [
      "video game",
      "game purchase",
      "game download",
      "console game",
      "PC game",
      // Adding some common misspellings and variations

      "games",
      "videogames",
      "gaes",
      "video gaes",
      "gammes",
      "video gammes",
    ]
  },
  210202: {
    displayName: "In-Game Purchases",
    keywords: [
      "microtransaction",
      "skin",
      "battle pass",
      "gems",
      "coins",
      "in app game purchase",
      // Adding some common misspellings and variations

      "purchases",
      "gamepurchases",
      "purcases",
      "in game purcases",
      "purchhases",
      "in game purchhases",
    ]
  },
  210203: {
    displayName: "Consoles",
    keywords: [
      "console",
      "playstation",
      "xbox",
      "switch",
      "gaming console",
      // Adding some common misspellings and variations

      "consoles",
      "consles",
      "consooles",
      "conosles",
      "conole",
      "conssole",
    ]
  },
  210204: {
    displayName: "Gaming Accessories",
    keywords: [
      "controller",
      "headset",
      "gaming mouse",
      "keyboard",
      "charging dock",
      // Adding some common misspellings and variations

      "accessories",
      "gamingaccessories",
      "accesories",
      "gaming accesories",
      "accesssories",
      "gaming accesssories",
    ]
  },
  210205: {
    displayName: "Online Gaming Subscriptions",
    keywords: [
      "ps plus",
      "xbox game pass",
      "nintendo online",
      "gaming subscription",
      // Adding some common misspellings and variations

      "subscriptions",
      "onlinegamingsubscriptions",
      "subscrptions",
      "online gaming subscrptions",
      "subscriiptions",
      "online gaming subscriiptions",
    ]
  },
  210301: {
    displayName: "Art Supplies",
    keywords: [
      "paints",
      "brushes",
      "sketchbook",
      "canvas",
      "pencils",
      "art materials",
      // Adding some common misspellings and variations

      "paits",
      "painnts",
    ]
  },
  210302: {
    displayName: "Craft Supplies",
    keywords: [
      "glue",
      "yarn",
      "beads",
      "scrapbooking",
      "craft kit",
      "craft materials",
      // Adding some common misspellings and variations

      "crft",
      "craaft",
      "carft",
      "gle",
      "gluue",
    ]
  },
  210303: {
    displayName: "Sewing / Knitting",
    keywords: [
      "sewing",
      "knitting",
      "crochet",
      "fabric",
      "needles",
      "thread",
      // Adding some common misspellings and variations

      "sewingknitting",
      "kniting",
      "sewing kniting",
      "knittting",
      "sewing knittting",
      "knittin",
    ]
  },
  210304: {
    displayName: "Photography Hobby",
    keywords: [
      "camera hobby",
      "film",
      "lens",
      "memory card",
      "photo printing",
      "tripod",
      // Adding some common misspellings and variations

      "photography",
      "photographyhobby",
      "photoraphy",
      "photoraphy hobby",
      "photoggraphy",
      "photoggraphy hobby",
    ]
  },
  210305: {
    displayName: "Pottery / Ceramics",
    keywords: [
      "pottery",
      "ceramics",
      "clay",
      "kiln",
      "wheel class",
      // Adding some common misspellings and variations

      "potteryceramics",
      "ceraics",
      "pottery ceraics",
      "cerammics",
      "pottery cerammics",
      "cermaics",
    ]
  },
  210306: {
    displayName: "DIY Hobby Kits",
    keywords: [
      "model kit",
      "make your own kit",
      "DIY hobby",
      "beginner kit",
      // Adding some common misspellings and variations

      "kits",
      "hobbykits",
      "hoby",
      "diy hoby kits",
      "hobbby",
      "diy hobbby kits",
    ]
  },
  210401: {
    displayName: "Sports Equipment",
    keywords: [
      "football boots",
      "racket",
      "ball",
      "weights",
      "sports gear",
      // Adding some common misspellings and variations

      "equipment",
      "sportsequipment",
      "equiment",
      "sports equiment",
      "equippment",
      "sports equippment",
    ]
  },
  210402: {
    displayName: "Club Fees",
    keywords: [
      "membership club",
      "sports club",
      "joining fee",
      "team fee",
      // Adding some common misspellings and variations

      "clb",
      "cluub",
      "culb",
      "membeship",
      "memberrship",
    ]
  },
  210403: {
    displayName: "Outdoor Gear",
    keywords: [
      "hiking gear",
      "backpack",
      "tent accessories",
      "outdoor equipment",
      // Adding some common misspellings and variations

      "outdoorgear",
      "outoor",
      "outoor gear",
      "outddoor",
      "outddoor gear",
      "oudtoor",
    ]
  },
  210404: {
    displayName: "Camping Gear",
    keywords: [
      "tent",
      "sleeping bag",
      "camping stove",
      "camp chair",
      "campsite gear",
      // Adding some common misspellings and variations

      "campinggear",
      "caming",
      "caming gear",
      "campping",
      "campping gear",
      "capming",
    ]
  },
  210405: {
    displayName: "Fishing / Hunting",
    keywords: [
      "fishing rod",
      "bait",
      "tackle",
      "hunting gear",
      "licence outdoor",
      // Adding some common misspellings and variations

      "fishinghunting",
      "fising",
      "fising hunting",
      "fishhing",
      "fishhing hunting",
      "fihsing",
    ]
  },
  210406: {
    displayName: "Climbing",
    keywords: [
      "climbing",
      "bouldering",
      "harness",
      "chalk",
      "climbing gym",
      // Adding some common misspellings and variations

      "climing",
      "climbbing",
      "clibming",
    ]
  },
  210407: {
    displayName: "Swimming / Pool Fees",
    keywords: [
      "pool fee",
      "swimming lessons leisure",
      "swim pass",
      "public pool",
      // Adding some common misspellings and variations

      "swimmingpool",
      "swiming",
      "swiming pool fees",
      "swimmming",
      "swimmming pool fees",
      "swimmin",
    ]
  },
  210501: {
    displayName: "Books",
    keywords: [
      "books",
      "novel",
      "paperback",
      "hardback",
      "bookstore",
      // Adding some common misspellings and variations

      "boks",
      "boooks",
    ]
  },
  210502: {
    displayName: "eBooks / Audiobooks",
    keywords: [
      "ebook",
      "kindle book",
      "audiobook",
      "audible credit",
      "digital book",
      // Adding some common misspellings and variations

      "ebooks",
      "audiobooks",
      "ebooksaudiobooks",
      "audioooks",
      "ebooks audioooks",
      "audiobbooks",
    ]
  },
  210503: {
    displayName: "Magazines",
    keywords: [
      "magazine",
      "periodical",
      "print magazine",
      "subscription issue",
      // Adding some common misspellings and variations

      "magazines",
      "magaines",
      "magazzines",
      "magzaines",
      "magaine",
      "magazzine",
    ]
  },
  210504: {
    displayName: "Music Purchases",
    keywords: [
      "music",
      "album",
      "song purchase",
      "digital music",
      "vinyl",
      "CD",
      // Adding some common misspellings and variations

      "purchases",
      "musicpurchases",
      "purcases",
      "music purcases",
      "purchhases",
      "music purchhases",
    ]
  },
  210505: {
    displayName: "Instrument Purchases",
    keywords: [
      "guitar",
      "piano",
      "keyboard",
      "violin",
      "instrument",
      "music gear",
      // Adding some common misspellings and variations

      "purchases",
      "instrumentpurchases",
      "instrment",
      "instrment purchases",
      "instruument",
      "instruument purchases",
    ]
  },
  210506: {
    displayName: "Instrument Lessons",
    keywords: [
      "guitar lessons",
      "piano lessons",
      "violin lessons",
      "music teacher",
      // Adding some common misspellings and variations

      "instrument",
      "instrumentlessons",
      "instrment",
      "instrment lessons",
      "instruument",
      "instruument lessons",
    ]
  },
  210601: {
    displayName: "Trading Cards",
    keywords: [
      "pokemon cards",
      "tcg",
      "trading cards",
      "sports cards",
      "card packs",
      // Adding some common misspellings and variations

      "tradingcards",
      "traing",
      "traing cards",
      "tradding",
      "tradding cards",
      "trdaing",
    ]
  },
  210602: {
    displayName: "Comics",
    keywords: [
      "comics",
      "manga",
      "graphic novel",
      "comic books",
      // Adding some common misspellings and variations

      "comcs",
      "comiics",
      "coimcs",
    ]
  },
  210603: {
    displayName: "Memorabilia",
    keywords: [
      "memorabilia",
      "signed item",
      "collectible item",
      "fandom collectible",
      // Adding some common misspellings and variations

      "memorbilia",
      "memoraabilia",
      "memoarbilia",
    ]
  },
  210604: {
    displayName: "Model Building",
    keywords: [
      "model kit",
      "scale model",
      "miniature",
      "hobby model",
      // Adding some common misspellings and variations

      "building",
      "modelbuilding",
      "builing",
      "model builing",
      "buildding",
      "model buildding",
    ]
  },
  210605: {
    displayName: "Collectibles",
    keywords: [
      "collectibles",
      "rare item",
      "collector piece",
      "hobby collectible",
      // Adding some common misspellings and variations

      "collecibles",
      "collecttibles",
      "colletcibles",
    ]
  },
  210701: {
    displayName: "Hobby Classes",
    keywords: [
      "hobby course",
      "workshop class",
      "interest class",
      "creative class",
      // Adding some common misspellings and variations

      "classes",
      "hobbyclasses",
      "clases",
      "hobby clases",
      "classses",
      "hobby classses",
    ]
  },
  210702: {
    displayName: "Workshops",
    keywords: [
      "workshop",
      "masterclass",
      "hobby workshop",
      "craft workshop",
      // Adding some common misspellings and variations

      "workshops",
      "workhops",
      "worksshops",
      "worskhops",
      "workhop",
      "worksshop",
    ]
  },
  210703: {
    displayName: "Other Leisure Spending",
    keywords: [
      "entertainment misc",
      "hobby other",
      "leisure misc",
      // Adding some common misspellings and variations

      "spending",
      "leisurespending",
      "spening",
      "other leisure spening",
      "spendding",
      "other leisure spendding",
    ]
  },
  220101: {
    displayName: "Flights",
    keywords: [
      "flight",
      "plane",
      "airfare",
      "airline booking",
      "airport",
      // Adding some common misspellings and variations

      "flights",
      "flihts",
      "fligghts",
      "flgihts",
      "fliht",
      "fligght",
    ]
  },
  220102: {
    displayName: "Trains",
    keywords: [
      "train trip",
      "rail ticket",
      "railway travel",
      "intercity train",
      // Adding some common misspellings and variations

      "trains",
      "trans",
      "traiins",
      "trians",
      "trin",
      "traain",
    ]
  },
  220103: {
    displayName: "Buses",
    keywords: [
      "bus trip",
      "coach travel",
      "long distance bus",
      "bus ticket",
      // Adding some common misspellings and variations

      "buses",
      "bues",
      "busses",
      "bsues",
      "trp",
      "triip",
    ]
  },
  220104: {
    displayName: "Car Rental",
    keywords: [
      "rental car",
      "hire car",
      "holiday car",
      "vacation car rental",
      // Adding some common misspellings and variations

      "renal",
      "renttal",
      "retnal",
    ]
  },
  220105: {
    displayName: "Ferries",
    keywords: [
      "ferry",
      "boat crossing",
      "ferry travel",
      "port ticket",
      // Adding some common misspellings and variations

      "ferries",
      "feries",
      "ferrries",
      "ferreis",
      "fery",
      "ferrry",
    ]
  },
  220106: {
    displayName: "Airport Transfers",
    keywords: [
      "airport shuttle",
      "transfer",
      "taxi airport",
      "airport pickup",
      // Adding some common misspellings and variations

      "transfers",
      "airporttransfers",
      "tranfers",
      "airport tranfers",
      "transsfers",
      "airport transsfers",
    ],
    aliases: [
      "airport taxi",
      "airport uber",
      "ride from airport",
      "airport cab",
      "airport pickup ride",
      "airport drop off ride",
    ],
    merchantHints: [
      "uber",
      "lyft",
      "bolt",
      "grab",
    ]
  },
  220107: {
    displayName: "Baggage Fees",
    keywords: [
      "baggage",
      "checked bag",
      "luggage fee",
      "extra bag",
      "airline baggage",
      // Adding some common misspellings and variations

      "bagage",
      "bagggage",
    ]
  },
  220201: {
    displayName: "Hotels",
    keywords: [
      "hotel",
      "hotel stay",
      "accommodation",
      "room booking",
      "inn",
      // Adding some common misspellings and variations

      "hotels",
      "hotls",
      "hoteels",
      "hoetls",
      "hoel",
      "hottel",
    ],
    merchantHints: [
      "maldron",
      "clayton hotel",
      "premier inn",
      "travelodge",
      "leonardo hotel",
      "hilton",
      "radisson blu",
    ]
  },
  220202: {
    displayName: "Hostels",
    keywords: [
      "hostel",
      "backpacker hostel",
      "dorm bed",
      "youth hostel",
      // Adding some common misspellings and variations

      "hostels",
      "hosels",
      "hosttels",
      "hotsels",
      "hosel",
      "hosttel",
    ],
    merchantHints: [
      "generator hostel",
      "jacobs inn",
      "abbey court hostel",
    ]
  },
  220203: {
    displayName: "Vacation Rentals",
    keywords: [
      "Airbnb",
      "vacation rental",
      "short let",
      "holiday rental",
      "apartment stay",
      // Adding some common misspellings and variations

      "rentals",
      "vacationrentals",
      "vacaion",
      "vacaion rentals",
      "vacattion",
      "vacattion rentals",
    ],
    merchantHints: [
      "airbnb",
      "vrbo",
      "booking com",
    ]
  },
  220204: {
    displayName: "Camping Fees",
    keywords: [
      "campsite",
      "camping fee",
      "pitch fee",
      "camping booking",
      // Adding some common misspellings and variations

      "caming",
      "campping",
      "capming",
      "campite",
      "campssite",
    ]
  },
  220205: {
    displayName: "Resort Fees",
    keywords: [
      "resort fee",
      "hotel resort charge",
      "amenity fee",
      // Adding some common misspellings and variations

      "resrt",
      "resoort",
      "reosrt",
    ]
  },
  220206: {
    displayName: "Deposits",
    keywords: [
      "booking deposit",
      "accommodation deposit",
      "trip deposit",
      "security deposit travel",
      // Adding some common misspellings and variations

      "deposits",
      "depoits",
      "depossits",
      "depsoits",
      "booing",
      "bookking",
    ]
  },
  220301: {
    displayName: "Restaurants While Traveling",
    keywords: [
      "travel restaurant",
      "holiday dining",
      "vacation meals",
      "tourist dining",
      // Adding some common misspellings and variations

      "restaurants",
      "traveling",
      "restaurantstraveling",
      "restarants",
      "restarants while traveling",
      "restauurants",
    ]
  },
  220302: {
    displayName: "Snacks While Traveling",
    keywords: [
      "travel snacks",
      "airport snacks",
      "road trip snacks",
      "station snacks",
      // Adding some common misspellings and variations

      "traveling",
      "snackstraveling",
      "travling",
      "snacks while travling",
      "traveeling",
      "snacks while traveeling",
    ]
  },
  220303: {
    displayName: "Local Transport",
    keywords: [
      "metro abroad",
      "taxi abroad",
      "local bus holiday",
      "local travel",
      // Adding some common misspellings and variations

      "transport",
      "localtransport",
      "tranport",
      "local tranport",
      "transsport",
      "local transsport",
    ],
    aliases: [
      "metro abroad",
      "subway abroad",
      "tube abroad",
      "local train on holiday",
      "city transport while travelling",
      "getting around abroad",
    ],
    merchantHints: [
      "bart",
      "mta",
      "tube",
      "underground",
      "subway",
      "uber",
      "lyft",
      "bolt",
      "grab",
    ]
  },
  220304: {
    displayName: "Tips",
    keywords: [
      "travel tip",
      "hotel tip",
      "tour tip",
      "gratuity holiday",
      // Adding some common misspellings and variations

      "tips",
      "tis",
      "tipps",
      "tpis",
      "trael",
      "travvel",
    ]
  },
  220305: {
    displayName: "Currency Exchange Fees",
    keywords: [
      "fx fee",
      "currency exchange",
      "conversion fee",
      "exchange desk fee",
      // Adding some common misspellings and variations

      "currencyexchange",
      "currncy",
      "currncy exchange fees",
      "curreency",
      "curreency exchange fees",
      "curerncy",
    ]
  },
  220401: {
    displayName: "Passports",
    keywords: [
      "passport",
      "passport renewal",
      "passport application",
      "travel document",
      // Adding some common misspellings and variations

      "passports",
      "passorts",
      "passpports",
      "paspsorts",
      "passort",
      "passpport",
    ]
  },
  220402: {
    displayName: "Visas",
    keywords: [
      "visa",
      "travel visa",
      "entry permit",
      "visa application",
      // Adding some common misspellings and variations

      "visas",
      "vias",
      "vissas",
      "vsias",
      "via",
      "vissa",
    ]
  },
  220403: {
    displayName: "Travel Insurance",
    keywords: [
      "travel insurance",
      "trip cover",
      "holiday insurance",
      // Adding some common misspellings and variations

      "travelinsurance",
      "insuance",
      "travel insuance",
      "insurrance",
      "travel insurrance",
      "insruance",
    ]
  },
  220404: {
    displayName: "Tour Bookings",
    keywords: [
      "tour",
      "excursion",
      "guided tour",
      "activity booking",
      // Adding some common misspellings and variations

      "bookings",
      "tourbookings",
      "bookngs",
      "tour bookngs",
      "bookiings",
      "tour bookiings",
    ]
  },
  220405: {
    displayName: "Attraction Tickets",
    keywords: [
      "ticket",
      "attraction pass",
      "museum pass",
      "sightseeing pass",
      // Adding some common misspellings and variations

      "tickets",
      "attractiontickets",
      "attration",
      "attration tickets",
      "attracction",
      "attracction tickets",
    ]
  },
  220406: {
    displayName: "Travel SIM / Roaming",
    keywords: [
      "roaming",
      "travel sim",
      "eSIM travel",
      "roaming package",
      "data abroad",
      // Adding some common misspellings and variations

      "travelroaming",
      "roaing",
      "travel sim roaing",
      "roamming",
      "travel sim roamming",
      "romaing",
    ]
  },
  220501: {
    displayName: "Work Flights",
    keywords: [
      "business flight",
      "corporate travel",
      "work airfare",
      // Adding some common misspellings and variations

      "flights",
      "workflights",
      "flihts",
      "work flihts",
      "fligghts",
      "work fligghts",
    ]
  },
  220502: {
    displayName: "Work Lodging",
    keywords: [
      "work hotel",
      "corporate lodging",
      "business stay",
      // Adding some common misspellings and variations

      "worklodging",
      "loding",
      "work loding",
      "lodgging",
      "work lodgging",
      "logding",
    ]
  },
  220503: {
    displayName: "Meal Reimbursements Pending",
    keywords: [
      "reimbursable meal",
      "work meal",
      "claimable meal",
      "pending reimbursement",
      // Adding some common misspellings and variations

      "reimbursements",
      "mealreimbursementspending",
      "reimburements",
      "meal reimburements pending",
      "reimburssements",
      "meal reimburssements pending",
    ]
  },
  220504: {
    displayName: "Conference Travel",
    keywords: [
      "conference trip",
      "event travel",
      "seminar travel",
      "convention travel",
      // Adding some common misspellings and variations

      "conferencetravel",
      "confeence",
      "confeence travel",
      "conferrence",
      "conferrence travel",
      "confreence",
    ]
  },
  220505: {
    displayName: "Mileage",
    keywords: [
      "business mileage",
      "work travel distance",
      "mileage claim",
      "km claim",
      // Adding some common misspellings and variations

      "milage",
      "mileeage",
      "mielage",
      "busiess",
      "businness",
    ]
  },
  220601: {
    displayName: "Souvenirs",
    keywords: [
      "souvenir",
      "keepsake",
      "travel gift",
      "memento",
      // Adding some common misspellings and variations

      "souvenirs",
      "souvnirs",
      "souveenirs",
      "souevnirs",
      "souvnir",
      "souveenir",
    ]
  },
  220602: {
    displayName: "Travel Gear",
    keywords: [
      "suitcase",
      "travel pillow",
      "luggage",
      "adapter",
      "packing cubes",
      // Adding some common misspellings and variations

      "gear",
      "travelgear",
      "trael",
      "trael gear",
      "travvel",
      "travvel gear",
    ]
  },
  220603: {
    displayName: "Travel Laundry",
    keywords: [
      "laundry holiday",
      "hotel laundry",
      "laundromat abroad",
      "travel washing",
      // Adding some common misspellings and variations

      "travellaundry",
      "laudry",
      "travel laudry",
      "launndry",
      "travel launndry",
      "lanudry",
    ]
  },
  220604: {
    displayName: "Other Trip Costs",
    keywords: [
      "travel misc",
      "trip misc",
      "holiday other",
      "uncategorized travel",
      // Adding some common misspellings and variations

      "trp",
      "triip",
      "tirp",
      "trael",
      "travvel",
    ]
  },
  230101: {
    displayName: "Everyday Clothing",
    keywords: [
      "clothes",
      "tshirt",
      "jeans",
      "jumper",
      "everyday wear",
      "outfit",
      // Adding some common misspellings and variations

      "clothing",
      "everydayclothing",
      "cloting",
      "everyday cloting",
      "clothhing",
      "everyday clothhing",
    ]
  },
  230102: {
    displayName: "Formalwear",
    keywords: [
      "suit",
      "dress",
      "formal wear",
      "occasion wear",
      "black tie",
      "wedding outfit",
      // Adding some common misspellings and variations

      "formalwear",
      "formawear",
      "formallwear",
      "formlawear",
      "sut",
      "suiit",
    ]
  },
  230103: {
    displayName: "Workwear",
    keywords: [
      "office wear",
      "work clothes",
      "uniform",
      "business attire",
      // Adding some common misspellings and variations

      "workwear",
      "workear",
      "workwwear",
      "worwkear",
      "offce",
      "offiice",
    ],
    aliases: [
      "new clothes for work",
      "office clothes",
      "uniform for work",
      "work outfit",
      "clothes for the office",
      "work wardrobe",
    ]
  },
  230104: {
    displayName: "Sportswear",
    keywords: [
      "activewear",
      "gym clothes",
      "leggings",
      "shorts",
      "sports gear clothing",
      // Adding some common misspellings and variations

      "sportswear",
      "sportwear",
      "sportsswear",
      "sporstwear",
      "activwear",
      "activeewear",
    ]
  },
  230105: {
    displayName: "Underwear / Sleepwear",
    keywords: [
      "underwear",
      "socks",
      "pajamas",
      "pyjamas",
      "nightwear",
      "bras",
      // Adding some common misspellings and variations

      "sleepwear",
      "underwearsleepwear",
      "sleewear",
      "underwear sleewear",
      "sleeppwear",
      "underwear sleeppwear",
    ]
  },
  230106: {
    displayName: "Maternity Wear",
    keywords: [
      "maternity clothes",
      "pregnancy wear",
      "nursing wear",
      // Adding some common misspellings and variations

      "maternitywear",
      "matenity",
      "matenity wear",
      "materrnity",
      "materrnity wear",
      "matrenity",
    ]
  },
  230107: {
    displayName: "Children's Clothing",
    keywords: [
      "kids clothes",
      "baby clothes",
      "children wear",
      "school uniform",
      // Adding some common misspellings and variations

      "clothing",
      "childrenclothing",
      "chilren",
      "chilren s clothing",
      "childdren",
      "childdren s clothing",
    ]
  },
  230201: {
    displayName: "Shoes",
    keywords: [
      "shoes",
      "trainers",
      "boots",
      "heels",
      "sneakers",
      "sandals",
      // Adding some common misspellings and variations

      "shes",
      "shooes",
      "sohes",
    ],
    aliases: [
      "new shoes for work",
      "work shoes",
      "office shoes",
      "school shoes",
      "safety shoes",
      "trainers for work",
    ]
  },
  230202: {
    displayName: "Bags",
    keywords: [
      "bag",
      "handbag",
      "backpack",
      "purse",
      "tote",
      "school bag",
      // Adding some common misspellings and variations

      "bags",
      "bas",
      "baggs",
      "bgas",
    ]
  },
  230203: {
    displayName: "Belts",
    keywords: [
      "belt",
      "waist belt",
      "formal belt",
      // Adding some common misspellings and variations

      "belts",
      "bets",
      "bellts",
      "blets",
      "bet",
      "bellt",
    ]
  },
  230204: {
    displayName: "Wallets",
    keywords: [
      "wallet",
      "card holder",
      "purse",
      "money holder",
      // Adding some common misspellings and variations

      "wallets",
      "walets",
      "walllets",
      "walet",
      "walllet",
    ]
  },
  230205: {
    displayName: "Jewelry",
    keywords: [
      "jewelry",
      "necklace",
      "ring",
      "bracelet",
      "earrings",
      "pendant",
      // Adding some common misspellings and variations

      "jewlry",
      "jeweelry",
      "jeewlry",
    ]
  },
  230206: {
    displayName: "Watches",
    keywords: [
      "watch",
      "wristwatch",
      "smart watch non-tech shopping",
      "fashion watch",
      // Adding some common misspellings and variations

      "watches",
      "wathes",
      "watcches",
      "wacthes",
      "wach",
      "wattch",
    ]
  },
  230207: {
    displayName: "Sunglasses",
    keywords: [
      "sunglasses",
      "shades",
      "sun glasses",
      "fashion eyewear",
      // Adding some common misspellings and variations

      "sunglsses",
      "sunglaasses",
      "sungalsses",
    ]
  },
  230301: {
    displayName: "Phones",
    keywords: [
      "phone",
      "smartphone",
      "mobile device",
      "iPhone",
      "android phone",
      // Adding some common misspellings and variations

      "phones",
      "phoes",
      "phonnes",
      "phnoes",
      "phne",
      "phoone",
    ],
    merchantHints: [
      "apple",
      "currys",
      "did electrical",
      "harvey norman",
      "power city",
      "eir",
      "vodafone",
      "three",
    ]
  },
  230302: {
    displayName: "Tablets",
    keywords: [
      "tablet",
      "iPad",
      "android tablet",
      "portable screen",
      // Adding some common misspellings and variations

      "tablets",
      "tabets",
      "tabllets",
      "talbets",
      "tabet",
      "tabllet",
    ],
    merchantHints: [
      "apple",
      "currys",
      "did electrical",
      "harvey norman",
      "power city",
    ]
  },
  230303: {
    displayName: "Laptops",
    keywords: [
      "laptop",
      "notebook computer",
      "macbook",
      "ultrabook",
      // Adding some common misspellings and variations

      "laptops",
      "lapops",
      "lapttops",
      "latpops",
      "lapop",
      "lapttop",
    ],
    merchantHints: [
      "apple",
      "currys",
      "did electrical",
      "harvey norman",
      "power city",
    ]
  },
  230304: {
    displayName: "Accessories",
    keywords: [
      "charger",
      "phone case",
      "cable",
      "adapter",
      "keyboard",
      "mouse",
      // Adding some common misspellings and variations

      "accessories",
      "accesories",
      "accesssories",
      "accessoreis",
      "chager",
      "charrger",
    ],
    merchantHints: [
      "currys",
      "did electrical",
      "harvey norman",
      "power city",
      "apple",
    ]
  },
  230305: {
    displayName: "Smartwatches",
    keywords: [
      "smartwatch",
      "apple watch",
      "fitness watch",
      "wearable",
      // Adding some common misspellings and variations

      "smartwatches",
      "smartwtches",
      "smartwaatches",
      "smartawtches",
      "smartatch",
      "smartwwatch",
    ],
    merchantHints: [
      "apple",
      "currys",
      "harvey norman",
    ]
  },
  230306: {
    displayName: "Headphones",
    keywords: [
      "headphones",
      "earphones",
      "earbuds",
      "airpods",
      "headset",
      // Adding some common misspellings and variations

      "headpones",
      "headphhones",
      "headhpones",
    ],
    merchantHints: [
      "apple",
      "currys",
      "harvey norman",
      "did electrical",
      "power city",
    ]
  },
  230307: {
    displayName: "Smart Home Devices for Personal Use",
    keywords: [
      "smart speaker",
      "alexa",
      "google home",
      "smart plug",
      "home assistant device",
      // Adding some common misspellings and variations

      "devices",
      "smarthomedevicespersonal",
      "persnal",
      "smart home devices for persnal use",
      "persoonal",
      "smart home devices for persoonal use",
    ]
  },
  230401: {
    displayName: "Home Decor Retail",
    keywords: [
      "decor shop",
      "home accessories",
      "decorative retail",
      "household decor",
      // Adding some common misspellings and variations

      "homedecorretail",
      "retil",
      "home decor retil",
      "retaail",
      "home decor retaail",
      "reatil",
    ],
    merchantHints: [
      "ikea",
      "jysk",
      "home store and more",
      "homestore and more",
      "dunnes home",
      "harvey norman",
    ]
  },
  230402: {
    displayName: "Kitchenware",
    keywords: [
      "pots",
      "pans",
      "dishes",
      "utensils",
      "cookware",
      "cutlery",
      // Adding some common misspellings and variations

      "kitchenware",
      "kitchnware",
      "kitcheenware",
      "kitcehnware",
      "pos",
      "potts",
    ],
    merchantHints: [
      "ikea",
      "home store and more",
      "homestore and more",
      "dunnes home",
      "harvey norman",
    ]
  },
  230403: {
    displayName: "Bedding",
    keywords: [
      "duvet cover",
      "bedsheets",
      "pillowcases",
      "blanket",
      "comforter",
      // Adding some common misspellings and variations

      "bedding",
      "beding",
      "beddding",
      "beddin",
      "duet",
      "duvvet",
    ],
    merchantHints: [
      "ikea",
      "jysk",
      "home store and more",
      "homestore and more",
      "harvey norman",
    ]
  },
  230404: {
    displayName: "Storage",
    keywords: [
      "storage bins",
      "boxes",
      "shelves",
      "organizers",
      "drawer units",
      // Adding some common misspellings and variations

      "stoage",
      "storrage",
      "stroage",
      "bis",
      "binns",
    ],
    merchantHints: [
      "ikea",
      "jysk",
      "home store and more",
      "homestore and more",
    ]
  },
  230405: {
    displayName: "Seasonal Decorations",
    keywords: [
      "christmas decor",
      "festive decor",
      "halloween decor",
      "seasonal retail",
      // Adding some common misspellings and variations

      "decorations",
      "seasonaldecorations",
      "decortions",
      "seasonal decortions",
      "decoraations",
      "seasonal decoraations",
    ],
    merchantHints: [
      "home store and more",
      "homestore and more",
      "dunnes stores",
      "ikea",
    ]
  },
  230406: {
    displayName: "Office Supplies for Personal Use",
    keywords: [
      "notebooks",
      "pens",
      "printer paper",
      "stationery home",
      "desk supplies",
      // Adding some common misspellings and variations

      "office",
      "personal",
      "officepersonal",
      "persnal",
      "office supplies for persnal use",
      "persoonal",
    ]
  },
  230501: {
    displayName: "Designer Goods",
    keywords: [
      "designer",
      "luxury bag",
      "branded fashion",
      "premium goods",
      // Adding some common misspellings and variations

      "designergoods",
      "desiner",
      "desiner goods",
      "desiggner",
      "desiggner goods",
      "desginer",
    ]
  },
  230502: {
    displayName: "Premium Collectibles",
    keywords: [
      "premium collectible",
      "limited edition",
      "luxury collectible",
      // Adding some common misspellings and variations

      "collectibles",
      "premiumcollectibles",
      "collecibles",
      "premium collecibles",
      "collecttibles",
      "premium collecttibles",
    ]
  },
  230503: {
    displayName: "Specialty Boutique Purchases",
    keywords: [
      "boutique",
      "niche shop",
      "specialty store",
      "artisan retail",
      // Adding some common misspellings and variations

      "purchases",
      "specialtyboutiquepurchases",
      "purcases",
      "specialty boutique purcases",
      "purchhases",
      "specialty boutique purchhases",
    ]
  },
  230504: {
    displayName: "Limited Edition Goods",
    keywords: [
      "limited edition",
      "exclusive drop",
      "special release",
      "collectors edition",
      // Adding some common misspellings and variations

      "goods",
      "limitededitiongoods",
      "ediion",
      "limited ediion goods",
      "edittion",
      "limited edittion goods",
    ]
  },
  230601: {
    displayName: "Department Store",
    keywords: [
      "department store",
      "selfridges",
      "brown thomas",
      "major retail store",
      // Adding some common misspellings and variations

      "departmentstore",
      "deparment",
      "deparment store",
      "departtment",
      "departtment store",
      "depatrment",
    ]
  },
  230602: {
    displayName: "Marketplace Purchases",
    keywords: [
      "amazon",
      "ebay",
      "etsy",
      "online marketplace",
      "marketplace buy",
      // Adding some common misspellings and variations

      "purchases",
      "marketplacepurchases",
      "markeplace",
      "markeplace purchases",
      "markettplace",
      "markettplace purchases",
    ]
  },
  230603: {
    displayName: "Misc Retail",
    keywords: [
      "retail misc",
      "general shopping",
      "store purchase",
      "shopping other",
      // Adding some common misspellings and variations

      "retil",
      "retaail",
      "reatil",
    ]
  },
  230604: {
    displayName: "Impulse Purchases",
    keywords: [
      "impulse buy",
      "spontaneous purchase",
      "unplanned shopping",
      "random buy",
      // Adding some common misspellings and variations

      "purchases",
      "impulsepurchases",
      "purcases",
      "impulse purcases",
      "purchhases",
      "impulse purchhases",
    ]
  },
  240101: {
    displayName: "Birthday Gifts",
    keywords: [
      "birthday gift",
      "birthday present",
      "party present",
      // Adding some common misspellings and variations

      "gifts",
      "birthdaygifts",
      "birtday",
      "birtday gifts",
      "birthhday",
      "birthhday gifts",
    ]
  },
  240102: {
    displayName: "Wedding Gifts",
    keywords: [
      "wedding gift",
      "wedding present",
      "registry gift",
      // Adding some common misspellings and variations

      "gifts",
      "weddinggifts",
      "weding",
      "weding gifts",
      "weddding",
      "weddding gifts",
    ]
  },
  240103: {
    displayName: "Holiday Gifts",
    keywords: [
      "christmas gift",
      "holiday present",
      "festive gift",
      // Adding some common misspellings and variations

      "gifts",
      "holidaygifts",
      "holday",
      "holday gifts",
      "holiiday",
      "holiiday gifts",
    ]
  },
  240104: {
    displayName: "Baby Shower Gifts",
    keywords: [
      "baby shower",
      "newborn gift",
      "baby gift",
      // Adding some common misspellings and variations

      "gifts",
      "babyshowergifts",
      "shoer",
      "baby shoer gifts",
      "showwer",
      "baby showwer gifts",
    ]
  },
  240105: {
    displayName: "Graduation Gifts",
    keywords: [
      "graduation gift",
      "graduation present",
      "school finish gift",
      // Adding some common misspellings and variations

      "gifts",
      "graduationgifts",
      "gradution",
      "gradution gifts",
      "graduaation",
      "graduaation gifts",
    ]
  },
  240106: {
    displayName: "Anniversary Gifts",
    keywords: [
      "anniversary gift",
      "romantic gift",
      "relationship present",
      // Adding some common misspellings and variations

      "gifts",
      "anniversarygifts",
      "annivrsary",
      "annivrsary gifts",
      "anniveersary",
      "anniveersary gifts",
    ]
  },
  240107: {
    displayName: "Personal Occasion Gifts",
    keywords: [
      "occasion gift",
      "special occasion present",
      "celebration gift",
      // Adding some common misspellings and variations

      "personal",
      "gifts",
      "personaloccasiongifts",
      "occaion",
      "personal occaion gifts",
      "occassion",
    ]
  },
  240201: {
    displayName: "Party Supplies",
    keywords: [
      "balloons",
      "decorations",
      "cake supplies",
      "plates",
      "celebration supplies",
      // Adding some common misspellings and variations

      "party",
      "paty",
      "parrty",
      "praty",
      "ballons",
      "ballooons",
    ]
  },
  240202: {
    displayName: "Greeting Cards",
    keywords: [
      "card",
      "greeting card",
      "birthday card",
      "thank you card",
      "holiday card",
      // Adding some common misspellings and variations

      "cards",
      "greetingcards",
      "greeing",
      "greeing cards",
      "greetting",
      "greetting cards",
    ]
  },
  240203: {
    displayName: "Gift Wrap",
    keywords: [
      "wrapping paper",
      "gift bag",
      "ribbon",
      "wrapping supplies",
      // Adding some common misspellings and variations

      "wrap",
      "giftwrap",
      "git",
      "git wrap",
      "gifft",
      "gifft wrap",
    ]
  },
  240204: {
    displayName: "Event Contributions",
    keywords: [
      "contribution",
      "group contribution",
      "event fund",
      "shared celebration cost",
      // Adding some common misspellings and variations

      "contributions",
      "eventcontributions",
      "contriutions",
      "event contriutions",
      "contribbutions",
      "event contribbutions",
    ]
  },
  240205: {
    displayName: "Shared Group Gifts",
    keywords: [
      "group gift",
      "shared present",
      "pooled gift",
      "office collection",
      // Adding some common misspellings and variations

      "gifts",
      "sharedgroupgifts",
      "shaed",
      "shaed group gifts",
      "sharred",
      "sharred group gifts",
    ]
  },
  240301: {
    displayName: "Charitable Donations",
    keywords: [
      "charity",
      "donation",
      "nonprofit",
      "giving",
      "fundraiser",
      // Adding some common misspellings and variations

      "charitable",
      "donations",
      "charitabledonations",
      "chariable",
      "chariable donations",
      "charittable",
    ]
  },
  240302: {
    displayName: "Crowdfunding Support",
    keywords: [
      "gofundme",
      "crowdfunding",
      "fundraiser support",
      "campaign donation",
      // Adding some common misspellings and variations

      "crowdfnding",
      "crowdfuunding",
      "crowdufnding",
      "gofudme",
      "gofunndme",
    ]
  },
  240303: {
    displayName: "Nonprofit Contributions",
    keywords: [
      "nonprofit donation",
      "NGO support",
      "charity contribution",
      // Adding some common misspellings and variations

      "contributions",
      "nonprofitcontributions",
      "contriutions",
      "nonprofit contriutions",
      "contribbutions",
      "nonprofit contribbutions",
    ]
  },
  240304: {
    displayName: "Sponsored Events",
    keywords: [
      "sponsored walk",
      "sponsored run",
      "charity event",
      "pledge",
      // Adding some common misspellings and variations

      "events",
      "sponsoredevents",
      "sponored",
      "sponored events",
      "sponssored",
      "sponssored events",
    ]
  },
  240305: {
    displayName: "Volunteer-related Donations",
    keywords: [
      "volunteer donation",
      "giving in support",
      "supplies for volunteering",
      // Adding some common misspellings and variations

      "related",
      "donations",
      "volunteerrelateddonations",
      "donaions",
      "volunteer related donaions",
      "donattions",
    ]
  },
  240401: {
    displayName: "Community Support",
    keywords: [
      "community help",
      "local support",
      "neighbour support",
      "mutual aid",
      // Adding some common misspellings and variations

      "commnity",
      "commuunity",
      "comumnity",
      "hep",
      "hellp",
    ]
  },
  240402: {
    displayName: "Informal Financial Help",
    keywords: [
      "helped a friend",
      "gave money",
      "financial help",
      "personal support payment",
      // Adding some common misspellings and variations

      "informal",
      "informalfinancialhelp",
      "finacial",
      "informal finacial help",
      "finanncial",
      "informal finanncial help",
    ]
  },
  240403: {
    displayName: "Other Giving Expense",
    keywords: [
      "giving misc",
      "donation other",
      "gift misc",
      // Adding some common misspellings and variations

      "givng",
      "giviing",
      "giivng",
    ]
  },
  250101: {
    displayName: "Pet Food",
    keywords: [
      "pet food",
      "dog food",
      "cat food",
      "kibble",
      "wet food",
      // Adding some common misspellings and variations

      "fod",
      "foood",
    ],
    merchantHints: [
      "petmania",
      "petstop",
      "maxi zoo",
      "zooplus",
    ]
  },
  250102: {
    displayName: "Treats",
    keywords: [
      "treats",
      "pet snacks",
      "chew",
      "reward treat",
      // Adding some common misspellings and variations

      "trets",
      "treaats",
      "traets",
    ]
  },
  250103: {
    displayName: "Litter / Bedding",
    keywords: [
      "litter",
      "cat litter",
      "bedding",
      "cage bedding",
      "shavings",
      // Adding some common misspellings and variations

      "litterbedding",
      "beding",
      "litter beding",
      "beddding",
      "litter beddding",
      "beddin",
    ]
  },
  250104: {
    displayName: "Toys",
    keywords: [
      "pet toy",
      "chew toy",
      "ball",
      "scratching post",
      "enrichment toy",
      // Adding some common misspellings and variations

      "toys",
      "tos",
      "toyys",
      "tyos",
    ],
    merchantHints: [
      "petmania",
      "petstop",
      "maxi zoo",
    ]
  },
  250105: {
    displayName: "Grooming Supplies",
    keywords: [
      "shampoo pet",
      "brush",
      "nail clippers",
      "grooming tool",
      "pet wipes",
      // Adding some common misspellings and variations

      "grooing",
      "groomming",
      "gromoing",
      "shapoo",
      "shammpoo",
    ],
    merchantHints: [
      "petmania",
      "petstop",
      "maxi zoo",
    ]
  },
  250106: {
    displayName: "Cages / Tanks / Accessories",
    keywords: [
      "cage",
      "tank",
      "aquarium",
      "terrarium",
      "pet habitat",
      "pet accessories",
      // Adding some common misspellings and variations

      "cages",
      "tanks",
      "cagestanksaccessories",
      "accesories",
      "cages tanks accesories",
      "accesssories",
    ],
    merchantHints: [
      "petmania",
      "petstop",
      "maxi zoo",
    ]
  },
  250201: {
    displayName: "Routine Checkups",
    keywords: [
      "vet checkup",
      "annual vet",
      "routine pet appointment",
      "animal exam",
      // Adding some common misspellings and variations

      "checkups",
      "routinecheckups",
      "checups",
      "routine checups",
      "checkkups",
      "routine checkkups",
    ],
    merchantHints: [
      "village vets",
      "vet care",
      "vetcare",
      "irish blue cross",
    ]
  },
  250202: {
    displayName: "Vaccinations",
    keywords: [
      "pet vaccine",
      "booster",
      "shots",
      "rabies vaccine",
      "pet vaccination",
      // Adding some common misspellings and variations

      "vaccinations",
      "vaccintions",
      "vaccinaations",
      "vacciantions",
      "vacine",
      "vacccine",
    ],
    merchantHints: [
      "village vets",
      "vet care",
      "vetcare",
      "irish blue cross",
    ]
  },
  250203: {
    displayName: "Medication",
    keywords: [
      "pet medication",
      "flea treatment",
      "worming",
      "prescription pet meds",
      // Adding some common misspellings and variations

      "mediction",
      "medicaation",
      "mediaction",
    ],
    merchantHints: [
      "village vets",
      "vet care",
      "vetcare",
      "petmania",
    ]
  },
  250204: {
    displayName: "Surgery",
    keywords: [
      "pet surgery",
      "spay",
      "neuter",
      "operation",
      "surgical procedure pet",
      // Adding some common misspellings and variations

      "surery",
      "surggery",
      "sugrery",
    ]
  },
  250205: {
    displayName: "Emergency Vet",
    keywords: [
      "emergency vet",
      "urgent vet",
      "after hours vet",
      "animal emergency",
      // Adding some common misspellings and variations

      "emerency",
      "emerggency",
      "emegrency",
    ],
    merchantHints: [
      "village vets",
      "vet care",
      "vetcare",
      "pet emergency hospital",
    ]
  },
  250206: {
    displayName: "Dental for Pets",
    keywords: [
      "pet dental",
      "teeth cleaning pet",
      "oral care animal",
      // Adding some common misspellings and variations

      "pets",
      "dentalpets",
      "denal",
      "denal for pets",
      "denttal",
      "denttal for pets",
    ]
  },
  250301: {
    displayName: "Grooming",
    keywords: [
      "grooming",
      "dog grooming",
      "trim",
      "pet wash",
      "grooming salon",
      // Adding some common misspellings and variations

      "grooing",
      "groomming",
      "gromoing",
    ]
  },
  250302: {
    displayName: "Boarding",
    keywords: [
      "boarding",
      "kennel",
      "cattery",
      "overnight pet stay",
      // Adding some common misspellings and variations

      "boaring",
      "boardding",
      "boadring",
    ]
  },
  250303: {
    displayName: "Pet Sitting",
    keywords: [
      "pet sitting",
      "sitter",
      "house visit pet",
      "drop-in visit",
      // Adding some common misspellings and variations

      "siting",
      "sittting",
      "sittin",
    ]
  },
  250304: {
    displayName: "Dog Walking",
    keywords: [
      "dog walking",
      "walker",
      "pet exercise service",
      // Adding some common misspellings and variations

      "waling",
      "walkking",
      "wakling",
    ]
  },
  250305: {
    displayName: "Daycare",
    keywords: [
      "dog daycare",
      "pet daycare",
      "daytime care animal",
      // Adding some common misspellings and variations

      "dayare",
      "dayccare",
      "dacyare",
    ]
  },
  250401: {
    displayName: "Adoption Fees",
    keywords: [
      "adoption fee",
      "rescue fee",
      "shelter fee",
      "adoption cost",
      // Adding some common misspellings and variations

      "adopion",
      "adopttion",
      "adotpion",
    ]
  },
  250402: {
    displayName: "Purchase Costs",
    keywords: [
      "breeder",
      "pet purchase",
      "puppy cost",
      "kitten cost",
      // Adding some common misspellings and variations

      "purcase",
      "purchhase",
      "purhcase",
      "breder",
      "breeeder",
    ]
  },
  250403: {
    displayName: "Licensing / Registration",
    keywords: [
      "pet licence",
      "dog licence",
      "registration",
      "microchip registration",
      // Adding some common misspellings and variations

      "licensing",
      "licensingregistration",
      "registation",
      "licensing registation",
      "registrration",
      "licensing registrration",
    ]
  },
  250404: {
    displayName: "Training Classes",
    keywords: [
      "obedience class",
      "pet training",
      "puppy class",
      "dog school",
      // Adding some common misspellings and variations

      "classes",
      "trainingclasses",
      "traiing",
      "traiing classes",
      "trainning",
      "trainning classes",
    ]
  },
  250501: {
    displayName: "Travel with Pets",
    keywords: [
      "pet travel",
      "carrier fee",
      "airline pet fee",
      "pet passport",
      // Adding some common misspellings and variations

      "pets",
      "travelpets",
      "trael",
      "trael with pets",
      "travvel",
      "travvel with pets",
    ]
  },
  250502: {
    displayName: "Memorial Costs",
    keywords: [
      "cremation pet",
      "memorial",
      "urn",
      "pet loss expense",
      // Adding some common misspellings and variations

      "memoial",
      "memorrial",
      "memroial",
      "cremtion",
      "cremaation",
    ]
  },
  250503: {
    displayName: "Other Pet Expense",
    keywords: [
      "pet misc",
      "animal misc",
      "uncategorized pet cost",
    ]
  },
  260101: {
    displayName: "School Tuition",
    keywords: [
      "school tuition",
      "private school fee",
      "term fee",
      "school payment",
      // Adding some common misspellings and variations

      "schooltuition",
      "tuiion",
      "school tuiion",
      "tuittion",
      "school tuittion",
      "tutiion",
    ]
  },
  260102: {
    displayName: "University Tuition",
    keywords: [
      "university tuition",
      "college fee",
      "semester fee",
      "student tuition",
      // Adding some common misspellings and variations

      "universitytuition",
      "univesity",
      "univesity tuition",
      "univerrsity",
      "univerrsity tuition",
      "univresity",
    ]
  },
  260103: {
    displayName: "Registration Fees",
    keywords: [
      "registration fee",
      "enrolment fee",
      "sign-up fee school",
      // Adding some common misspellings and variations

      "registation",
      "registrration",
      "regisrtation",
    ]
  },
  260104: {
    displayName: "Lab Fees",
    keywords: [
      "lab fee",
      "science lab charge",
      "practical fee",
    ]
  },
  260105: {
    displayName: "Exam Fees",
    keywords: [
      "exam fee",
      "testing fee",
      "certification exam",
      "assessment fee",
      // Adding some common misspellings and variations

      "exm",
      "exaam",
      "eaxm",
    ]
  },
  260106: {
    displayName: "Graduation Fees",
    keywords: [
      "graduation fee",
      "ceremony fee",
      "robe rental",
      "diploma charge",
      // Adding some common misspellings and variations

      "gradution",
      "graduaation",
      "gradaution",
    ]
  },
  260201: {
    displayName: "Textbooks",
    keywords: [
      "textbook",
      "school book",
      "college book",
      "learning book",
      // Adding some common misspellings and variations

      "textbooks",
      "textooks",
      "textbbooks",
      "texbtooks",
      "textook",
      "textbbook",
    ]
  },
  260202: {
    displayName: "Stationery",
    keywords: [
      "stationery",
      "pencils",
      "pens",
      "notebooks",
      "folders",
      "supplies",
      // Adding some common misspellings and variations

      "statinery",
      "statioonery",
      "statoinery",
    ]
  },
  260203: {
    displayName: "Online Courses",
    keywords: [
      "online course",
      "Udemy",
      "Coursera",
      "e-learning",
      "digital class",
      // Adding some common misspellings and variations

      "courses",
      "onlinecourses",
      "couses",
      "online couses",
      "courrses",
      "online courrses",
    ],
    merchantHints: [
      "udemy",
      "coursera",
      "skillshare",
      "linkedin learning",
      "masterclass",
    ]
  },
  260204: {
    displayName: "Software for Study",
    keywords: [
      "study software",
      "student software",
      "educational app",
      "coding software",
      // Adding some common misspellings and variations

      "softwarestudy",
      "softare",
      "softare for study",
      "softwware",
      "softwware for study",
      "sofwtare",
    ],
    merchantHints: [
      "microsoft",
      "microsoft 365",
      "adobe",
      "notion",
      "grammarly",
    ]
  },
  260205: {
    displayName: "School Uniforms",
    keywords: [
      "school uniform",
      "blazer",
      "school shoes",
      "uniform pieces",
      // Adding some common misspellings and variations

      "uniforms",
      "schooluniforms",
      "unifrms",
      "school unifrms",
      "unifoorms",
      "school unifoorms",
    ]
  },
  260206: {
    displayName: "Supplies",
    keywords: [
      "school supplies",
      "study supplies",
      "art class materials",
      "classroom supplies",
      // Adding some common misspellings and variations

      "schol",
      "schoool",
    ]
  },
  260301: {
    displayName: "Language Classes",
    keywords: [
      "language course",
      "English lessons",
      "Spanish class",
      "language tuition",
      // Adding some common misspellings and variations

      "classes",
      "languageclasses",
      "langage",
      "langage classes",
      "languuage",
      "languuage classes",
    ]
  },
  260302: {
    displayName: "Music Lessons",
    keywords: [
      "piano lessons",
      "guitar lessons",
      "violin class",
      "music teacher",
      // Adding some common misspellings and variations

      "musiclessons",
      "lesons",
      "music lesons",
      "lesssons",
      "music lesssons",
      "muic",
    ]
  },
  260303: {
    displayName: "Coding Courses",
    keywords: [
      "coding course",
      "programming class",
      "bootcamp",
      "software course",
      // Adding some common misspellings and variations

      "courses",
      "codingcourses",
      "couses",
      "coding couses",
      "courrses",
      "coding courrses",
    ]
  },
  260304: {
    displayName: "Professional Certifications",
    keywords: [
      "certification",
      "exam prep",
      "professional credential",
      "qualification fee",
      // Adding some common misspellings and variations

      "certifications",
      "professionalcertifications",
      "certifiations",
      "professional certifiations",
      "certificcations",
      "professional certificcations",
    ]
  },
  260305: {
    displayName: "Trade Training",
    keywords: [
      "apprenticeship training",
      "trade school",
      "practical course",
      "vocational training",
      // Adding some common misspellings and variations

      "tradetraining",
      "traiing",
      "trade traiing",
      "trainning",
      "trade trainning",
      "traniing",
    ]
  },
  260401: {
    displayName: "Dorm Fees",
    keywords: [
      "dorm",
      "residence hall",
      "campus housing",
      "student accommodation",
      // Adding some common misspellings and variations

      "dom",
      "dorrm",
      "drom",
    ]
  },
  260402: {
    displayName: "Meal Plan",
    keywords: [
      "meal plan",
      "campus meal card",
      "canteen plan",
      "food plan",
      // Adding some common misspellings and variations

      "mel",
      "meaal",
      "mael",
    ]
  },
  260403: {
    displayName: "Student Travel",
    keywords: [
      "student bus",
      "commute to campus",
      "school transport",
      "student fare",
      // Adding some common misspellings and variations

      "travel",
      "studenttravel",
      "stuent",
      "stuent travel",
      "studdent",
      "studdent travel",
    ]
  },
  260404: {
    displayName: "Campus Fees",
    keywords: [
      "campus fee",
      "student services fee",
      "activity fee",
      "union fee",
      // Adding some common misspellings and variations

      "camus",
      "camppus",
      "capmus",
    ]
  },
  260501: {
    displayName: "Tutoring",
    keywords: [
      "tutor",
      "tuition",
      "extra lessons",
      "academic support",
      // Adding some common misspellings and variations

      "tutoring",
      "tutoing",
      "tutorring",
      "tutroing",
      "tuor",
      "tuttor",
    ]
  },
  260502: {
    displayName: "Test Prep",
    keywords: [
      "test prep",
      "exam preparation",
      "SAT prep",
      "leaving cert prep",
      "revision class",
      // Adding some common misspellings and variations

      "testprep",
      "prp",
      "test prp",
      "preep",
      "test preep",
      "perp",
    ]
  },
  260503: {
    displayName: "Study Abroad Costs",
    keywords: [
      "exchange program",
      "Erasmus",
      "study abroad",
      "overseas study cost",
      // Adding some common misspellings and variations

      "studyabroad",
      "abrad",
      "study abrad costs",
      "abrooad",
      "study abrooad costs",
      "aborad",
    ]
  },
  260504: {
    displayName: "Other Learning Expenses",
    keywords: [
      "education misc",
      "study other",
      "uncategorized learning expense",
      // Adding some common misspellings and variations

      "learing",
      "learnning",
      "leanring",
      "eduction",
      "educaation",
    ]
  },
  270101: {
    displayName: "Income Tax Payment",
    keywords: [
      "income tax",
      "tax bill",
      "revenue payment",
      "tax owed",
      "self assessment tax",
      // Adding some common misspellings and variations

      "incme",
      "incoome",
      "inocme",
    ]
  },
  270102: {
    displayName: "Estimated Tax Payment",
    keywords: [
      "estimated tax",
      "quarterly tax",
      "advance tax",
      "preliminary tax",
      // Adding some common misspellings and variations

      "estiated",
      "estimmated",
      "estmiated",
    ]
  },
  270103: {
    displayName: "Underpayment Penalties",
    keywords: [
      "underpayment",
      "penalty",
      "late tax interest",
      "tax surcharge",
      // Adding some common misspellings and variations

      "penalties",
      "underpaymentpenalties",
      "underpyment",
      "underpyment penalties",
      "underpaayment",
      "underpaayment penalties",
    ]
  },
  270104: {
    displayName: "Tax Settlement",
    keywords: [
      "settlement",
      "final tax bill",
      "tax balancing payment",
      "revenue settlement",
      // Adding some common misspellings and variations

      "settlment",
      "settleement",
      "settelment",
    ]
  },
  270201: {
    displayName: "Property Tax",
    keywords: [
      "property tax",
      "house tax",
      "annual property charge",
      // Adding some common misspellings and variations

      "proprty",
      "propeerty",
      "proeprty",
    ]
  },
  270202: {
    displayName: "Local Tax",
    keywords: [
      "local tax",
      "municipal tax",
      "local levy",
      // Adding some common misspellings and variations

      "loal",
      "loccal",
      "lcoal",
    ]
  },
  270203: {
    displayName: "Council Tax",
    keywords: [
      "council tax",
      "council bill",
      "local authority bill",
      // Adding some common misspellings and variations

      "coucil",
      "counncil",
      "conucil",
    ]
  },
  270204: {
    displayName: "Stamp Duty / Transfer Tax",
    keywords: [
      "stamp duty",
      "transfer tax",
      "property transfer tax",
      "deed tax",
      // Adding some common misspellings and variations

      "stampdutytransfer",
      "tranfer",
      "stamp duty tranfer tax",
      "transsfer",
      "stamp duty transsfer tax",
      "trasnfer",
    ]
  },
  270301: {
    displayName: "Motor Tax",
    keywords: [
      "motor tax",
      "road tax",
      "car tax",
      "vehicle tax",
      // Adding some common misspellings and variations

      "moor",
      "mottor",
      "mtoor",
    ]
  },
  270302: {
    displayName: "Registration Tax",
    keywords: [
      "registration tax",
      "VRT",
      "registration duty",
      "vehicle registration tax",
      // Adding some common misspellings and variations

      "registation",
      "registrration",
      "regisrtation",
    ]
  },
  270303: {
    displayName: "Road Use Tax",
    keywords: [
      "road use charge",
      "use tax",
      "highway use tax",
      // Adding some common misspellings and variations

      "rod",
      "roaad",
      "raod",
      "chage",
      "charrge",
    ]
  },
  270401: {
    displayName: "VAT Payment",
    keywords: [
      "VAT",
      "value added tax",
      "VAT return",
      "tax remittance",
    ]
  },
  270402: {
    displayName: "Sales Tax Payment",
    keywords: [
      "sales tax",
      "tax collected",
      "remittance tax",
      "state tax sale",
      // Adding some common misspellings and variations

      "saes",
      "salles",
      "slaes",
    ]
  },
  270403: {
    displayName: "Business Tax Payment",
    keywords: [
      "business tax",
      "corporation tax",
      "company tax",
      "business payment",
      // Adding some common misspellings and variations

      "busiess",
      "businness",
      "busniess",
    ]
  },
  270404: {
    displayName: "Payroll Tax",
    keywords: [
      "payroll tax",
      "employer tax",
      "wage tax",
      "payroll remittance",
      // Adding some common misspellings and variations

      "payoll",
      "payrroll",
      "paryoll",
    ]
  },
  270501: {
    displayName: "Tax Filing Software",
    keywords: [
      "tax software",
      "filing software",
      "tax app",
      "return software",
      // Adding some common misspellings and variations

      "filingsoftware",
      "softare",
      "tax filing softare",
      "softwware",
      "tax filing softwware",
      "sofwtare",
    ]
  },
  270502: {
    displayName: "Accountant / Tax Preparer",
    keywords: [
      "accountant",
      "tax preparer",
      "CPA",
      "tax help",
      "filing service",
      // Adding some common misspellings and variations

      "accountantpreparer",
      "accoutant",
      "accoutant tax preparer",
      "accounntant",
      "accounntant tax preparer",
      "acconutant",
    ]
  },
  270503: {
    displayName: "Filing Fees",
    keywords: [
      "filing fee",
      "submission fee",
      "tax filing charge",
      // Adding some common misspellings and variations

      "filng",
      "filiing",
      "fiilng",
    ]
  },
  270504: {
    displayName: "Audit Support",
    keywords: [
      "audit support",
      "tax audit",
      "representation",
      "audit help",
      // Adding some common misspellings and variations

      "auit",
      "auddit",
      "aduit",
    ]
  },
  270601: {
    displayName: "Import Duties",
    keywords: [
      "import duty",
      "customs duty",
      "duty charge",
      "import tax",
      // Adding some common misspellings and variations

      "duties",
      "importduties",
      "dutes",
      "import dutes",
      "dutiies",
      "import dutiies",
    ]
  },
  270602: {
    displayName: "Customs Charges",
    keywords: [
      "customs",
      "border fee",
      "import fee",
      "customs handling charge",
      // Adding some common misspellings and variations

      "charges",
      "customscharges",
      "chages",
      "customs chages",
      "charrges",
      "customs charrges",
    ]
  },
  270603: {
    displayName: "Tax Penalties",
    keywords: [
      "tax penalty",
      "late filing penalty",
      "tax fine",
      "interest penalty",
      // Adding some common misspellings and variations

      "penalties",
      "penaties",
      "penallties",
      "penlaties",
      "penlty",
      "penaalty",
    ]
  },
  270604: {
    displayName: "Other Government Tax Charges",
    keywords: [
      "government tax misc",
      "tax other",
      "statutory tax charge",
      // Adding some common misspellings and variations

      "charges",
      "governmentcharges",
      "goverment",
      "other goverment tax charges",
      "governnment",
      "other governnment tax charges",
    ]
  },
  280101: {
    displayName: "Netflix",
    keywords: [
      "netflix",
      "streaming",
      "video subscription",
      "TV subscription",
      // Adding some common misspellings and variations

      "netlix",
      "netfflix",
      "neftlix",
    ]
  },
  280102: {
    displayName: "Spotify",
    keywords: [
      "spotify",
      "music subscription",
      "streaming music",
      // Adding some common misspellings and variations

      "spoify",
      "spottify",
      "sptoify",
    ]
  },
  280103: {
    displayName: "YouTube Premium",
    keywords: [
      "youtube premium",
      "youtube subscription",
      "ad free youtube",
      // Adding some common misspellings and variations

      "youtubepremium",
      "preium",
      "youtube preium",
      "premmium",
      "youtube premmium",
      "prmeium",
    ]
  },
  280104: {
    displayName: "Disney+",
    keywords: [
      "disney plus",
      "disney+",
      "streaming disney",
      // Adding some common misspellings and variations

      "disneyplus",
      "disey",
      "disey plus",
      "disnney",
      "disnney plus",
      "dinsey",
    ]
  },
  280105: {
    displayName: "Audible",
    keywords: [
      "audible",
      "audiobook subscription",
      "audio books",
      // Adding some common misspellings and variations

      "audble",
      "audiible",
      "auidble",
    ]
  },
  280106: {
    displayName: "Other Streaming Services",
    keywords: [
      "hulu",
      "max",
      "prime video",
      "streaming app",
      "media subscription",
      // Adding some common misspellings and variations

      "streming",
      "streaaming",
      "straeming",
      "huu",
      "hullu",
    ]
  },
  280201: {
    displayName: "Microsoft 365",
    keywords: [
      "office 365",
      "microsoft 365",
      "outlook subscription",
      "office subscription",
      // Adding some common misspellings and variations

      "micrsoft",
      "microosoft",
      "micorsoft",
      "offce",
      "offiice",
    ]
  },
  280202: {
    displayName: "Google One",
    keywords: [
      "google one",
      "drive storage",
      "google storage",
      "cloud backup",
      // Adding some common misspellings and variations

      "goole",
      "googgle",
      "gogole",
    ]
  },
  280203: {
    displayName: "Adobe",
    keywords: [
      "adobe",
      "creative cloud",
      "photoshop subscription",
      "PDF pro",
      // Adding some common misspellings and variations

      "adbe",
      "adoobe",
      "aodbe",
    ]
  },
  280204: {
    displayName: "Cloud Storage",
    keywords: [
      "icloud",
      "dropbox",
      "cloud storage",
      "storage plan",
      "backup plan",
      // Adding some common misspellings and variations

      "cloudstorage",
      "stoage",
      "cloud stoage",
      "storrage",
      "cloud storrage",
      "stroage",
    ]
  },
  280205: {
    displayName: "Antivirus",
    keywords: [
      "antivirus",
      "security software",
      "malware protection",
      "norton",
      "mcafee",
      // Adding some common misspellings and variations

      "antiirus",
      "antivvirus",
      "antviirus",
    ]
  },
  280206: {
    displayName: "Productivity Apps",
    keywords: [
      "notion",
      "todoist",
      "evernote",
      "productivity subscription",
      "task app",
      // Adding some common misspellings and variations

      "apps",
      "productivityapps",
      "producivity",
      "producivity apps",
      "producttivity",
      "producttivity apps",
    ]
  },
  280301: {
    displayName: "Gym Membership",
    keywords: [
      "gym membership",
      "monthly gym",
      "fitness club fee",
      // Adding some common misspellings and variations

      "membeship",
      "memberrship",
      "membreship",
    ]
  },
  280302: {
    displayName: "Warehouse Club Membership",
    keywords: [
      "costco membership",
      "wholesale club",
      "warehouse fee",
      // Adding some common misspellings and variations

      "warehouseclubmembership",
      "membeship",
      "warehouse club membeship",
      "memberrship",
      "warehouse club memberrship",
      "membreship",
    ]
  },
  280303: {
    displayName: "Professional Association",
    keywords: [
      "association membership",
      "annual member fee",
      "professional body fee",
      // Adding some common misspellings and variations

      "professionalassociation",
      "profesional",
      "profesional association",
      "professsional",
      "professsional association",
      "professionel",
    ]
  },
  280304: {
    displayName: "Loyalty / Premium Membership",
    keywords: [
      "premium membership",
      "loyalty plus",
      "VIP membership",
      "prime membership",
      // Adding some common misspellings and variations

      "loyaltypremiummembership",
      "membeship",
      "loyalty premium membeship",
      "memberrship",
      "loyalty premium memberrship",
      "membreship",
    ]
  },
  280305: {
    displayName: "Club Membership",
    keywords: [
      "club fee",
      "association dues",
      "member dues",
      "social club",
      // Adding some common misspellings and variations

      "membership",
      "clubmembership",
      "membeship",
      "club membeship",
      "memberrship",
      "club memberrship",
    ]
  },
  280401: {
    displayName: "Meal Subscription",
    keywords: [
      "meal subscription",
      "food plan",
      "recurring meal delivery",
      // Adding some common misspellings and variations

      "mealsubscription",
      "subscrption",
      "meal subscrption",
      "subscriiption",
      "meal subscriiption",
      "subscirption",
    ]
  },
  280402: {
    displayName: "Regular Delivery Boxes",
    keywords: [
      "monthly box",
      "subscription box",
      "beauty box",
      "snack box",
      "book box",
      // Adding some common misspellings and variations

      "regular",
      "delivery",
      "boxes",
      "regulardeliveryboxes",
      "deliery",
      "regular deliery boxes",
    ]
  },
  280403: {
    displayName: "App Subscription",
    keywords: [
      "app subscription",
      "premium app",
      "in-app premium",
      "pro app",
      // Adding some common misspellings and variations

      "subscrption",
      "subscriiption",
      "subscirption",
    ]
  },
  280404: {
    displayName: "Gaming Subscription",
    keywords: [
      "xbox game pass",
      "ps plus",
      "gaming membership",
      "game subscription",
      // Adding some common misspellings and variations

      "gamingsubscription",
      "subscrption",
      "gaming subscrption",
      "subscriiption",
      "gaming subscriiption",
      "subscirption",
    ]
  },
  280405: {
    displayName: "Dating App Subscription",
    keywords: [
      "tinder gold",
      "bumble premium",
      "hinge subscription",
      "dating premium",
      // Adding some common misspellings and variations

      "datingsubscription",
      "subscrption",
      "dating app subscrption",
      "subscriiption",
      "dating app subscriiption",
      "subscirption",
    ]
  },
  280501: {
    displayName: "Premium News",
    keywords: [
      "news subscription",
      "paywall",
      "premium journalism",
      "digital newspaper",
      // Adding some common misspellings and variations

      "premiumnews",
      "preium",
      "preium news",
      "premmium",
      "premmium news",
      "prmeium",
    ]
  },
  280502: {
    displayName: "Subscription Boxes",
    keywords: [
      "subscription box",
      "monthly box",
      "curated box",
      "recurring box",
      // Adding some common misspellings and variations

      "boxes",
      "subscriptionboxes",
      "subscrption",
      "subscrption boxes",
      "subscriiption",
      "subscriiption boxes",
    ]
  },
  280503: {
    displayName: "Other Recurring Service Subscription",
    keywords: [
      "recurring service",
      "monthly service",
      "annual plan",
      "subscription other",
      // Adding some common misspellings and variations

      "recurringsubscription",
      "subscrption",
      "other recurring service subscrption",
      "subscriiption",
      "other recurring service subscriiption",
      "subscirption",
    ]
  },
  290101: {
    displayName: "Office Supplies",
    keywords: [
      "office supplies",
      "pens",
      "notebooks",
      "admin supplies",
      "printer paper",
      // Adding some common misspellings and variations

      "offce",
      "offiice",
      "ofifce",
    ],
    merchantHints: [
      "viking direct",
      "eason",
      "office depot",
    ]
  },
  290102: {
    displayName: "Printing",
    keywords: [
      "printing",
      "copies",
      "business prints",
      "posters",
      "flyers print",
      // Adding some common misspellings and variations

      "prining",
      "printting",
      "pritning",
    ]
  },
  290103: {
    displayName: "Stationery",
    keywords: [
      "stationery",
      "envelopes",
      "labels",
      "folders",
      "letterhead",
      // Adding some common misspellings and variations

      "statinery",
      "statioonery",
      "statoinery",
    ]
  },
  290104: {
    displayName: "Coworking Space",
    keywords: [
      "coworking",
      "hot desk",
      "shared office",
      "desk rental",
      // Adding some common misspellings and variations

      "space",
      "coworkingspace",
      "cowoking",
      "cowoking space",
      "coworrking",
      "coworrking space",
    ],
    merchantHints: [
      "wework",
      "iconic offices",
      "dogpatch labs",
      "pembroke hall",
    ]
  },
  290105: {
    displayName: "Office Rent",
    keywords: [
      "office rent",
      "workspace rent",
      "studio rent",
      "business premises rent",
      // Adding some common misspellings and variations

      "officerent",
      "offce",
      "offce rent",
      "offiice",
      "offiice rent",
      "ofifce",
    ]
  },
  290106: {
    displayName: "PO Box",
    keywords: [
      "PO box",
      "mailbox rental",
      "postal box",
      "mailing address fee",
    ]
  },
  290201: {
    displayName: "Desk / Chair",
    keywords: [
      "office desk",
      "ergonomic chair",
      "home office desk",
      "work chair",
      // Adding some common misspellings and variations

      "deskchair",
      "chir",
      "desk chir",
      "chaair",
      "desk chaair",
      "cahir",
    ]
  },
  290202: {
    displayName: "Monitors / Peripherals",
    keywords: [
      "monitor",
      "dock",
      "webcam",
      "keyboard",
      "mouse",
      "USB hub",
      // Adding some common misspellings and variations

      "monitors",
      "peripherals",
      "monitorsperipherals",
      "periperals",
      "monitors periperals",
      "periphherals",
    ]
  },
  290203: {
    displayName: "Printer / Ink / Paper",
    keywords: [
      "printer",
      "ink",
      "toner",
      "printer paper",
      "print supplies",
      // Adding some common misspellings and variations

      "printerpaper",
      "priter",
      "priter ink paper",
      "prinnter",
      "prinnter ink paper",
      "prniter",
    ]
  },
  290204: {
    displayName: "Workspace Furnishing",
    keywords: [
      "shelving",
      "desk lamp",
      "cable management",
      "office furnishing",
      // Adding some common misspellings and variations

      "workspace",
      "workspacefurnishing",
      "furnihing",
      "workspace furnihing",
      "furnisshing",
      "workspace furnisshing",
    ]
  },
  290205: {
    displayName: "Internet / Utility Allocation",
    keywords: [
      "business internet share",
      "home office utility",
      "work-from-home internet",
      "utility allocation",
      // Adding some common misspellings and variations

      "internetutilityallocation",
      "alloction",
      "internet utility alloction",
      "allocaation",
      "internet utility allocaation",
      "alloaction",
    ]
  },
  290206: {
    displayName: "Home Office Supplies",
    keywords: [
      "notebooks",
      "pens",
      "folders",
      "stapler",
      "desk supplies",
      "WFH supplies",
      // Adding some common misspellings and variations

      "home",
      "office",
      "homeoffice",
      "offce",
      "home offce supplies",
      "offiice",
    ]
  },
  290301: {
    displayName: "Accounting Software",
    keywords: [
      "xero",
      "quickbooks",
      "freshbooks",
      "accounting software",
      // Adding some common misspellings and variations

      "accountingsoftware",
      "accouting",
      "accouting software",
      "accounnting",
      "accounnting software",
      "acconuting",
    ]
  },
  290302: {
    displayName: "CRM",
    keywords: [
      "CRM",
      "client management",
      "sales pipeline software",
      "contact manager",
    ]
  },
  290303: {
    displayName: "Design Tools",
    keywords: [
      "canva",
      "adobe",
      "figma",
      "design software",
      "creative tools",
      // Adding some common misspellings and variations

      "designtools",
      "desgn",
      "desgn tools",
      "desiign",
      "desiign tools",
      "deisgn",
    ]
  },
  290304: {
    displayName: "Cloud Services",
    keywords: [
      "aws",
      "azure",
      "hosting",
      "storage",
      "compute",
      "cloud service",
      // Adding some common misspellings and variations

      "clud",
      "clooud",
      "colud",
    ],
    merchantHints: [
      "aws",
      "amazon web services",
      "azure",
      "google cloud",
      "digitalocean",
    ]
  },
  290305: {
    displayName: "Website Hosting",
    keywords: [
      "hosting",
      "web hosting",
      "server hosting",
      "site hosting",
      // Adding some common misspellings and variations

      "website",
      "websitehosting",
      "hosing",
      "website hosing",
      "hostting",
      "website hostting",
    ],
    merchantHints: [
      "godaddy",
      "namecheap",
      "siteground",
      "bluehost",
    ]
  },
  290306: {
    displayName: "Email Services",
    keywords: [
      "business email",
      "mail hosting",
      "email service",
      "workspace email",
      // Adding some common misspellings and variations

      "emil",
      "emaail",
      "eamil",
      "busiess",
      "businness",
    ],
    merchantHints: [
      "google workspace",
      "microsoft 365",
      "zoho mail",
    ]
  },
  290307: {
    displayName: "Domain Names",
    keywords: [
      "domain",
      "domain renewal",
      "website name",
      "DNS domain",
      // Adding some common misspellings and variations

      "names",
      "domainnames",
      "domin",
      "domin names",
      "domaain",
      "domaain names",
    ],
    merchantHints: [
      "godaddy",
      "namecheap",
      "cloudflare",
      "blacknight",
    ]
  },
  290401: {
    displayName: "Advertising",
    keywords: [
      "ad spend",
      "ads",
      "facebook ads",
      "google ads",
      "paid ads",
      // Adding some common misspellings and variations

      "advertising",
      "adverising",
      "adverttising",
      "advetrising",
      "spnd",
      "speend",
    ]
  },
  290402: {
    displayName: "Social Media Ads",
    keywords: [
      "instagram ads",
      "tiktok ads",
      "social ads",
      "paid social",
      // Adding some common misspellings and variations

      "media",
      "socialmedia",
      "socal",
      "socal media ads",
      "sociial",
      "sociial media ads",
    ]
  },
  290403: {
    displayName: "Branding",
    keywords: [
      "logo design",
      "brand kit",
      "rebrand",
      "visual identity",
      "packaging design",
      // Adding some common misspellings and variations

      "branding",
      "braning",
      "brandding",
      "bradning",
      "loo",
      "loggo",
    ]
  },
  290404: {
    displayName: "Business Cards",
    keywords: [
      "business cards",
      "cards printing",
      "networking cards",
      // Adding some common misspellings and variations

      "businesscards",
      "busiess",
      "busiess cards",
      "businness",
      "businness cards",
      "busniess",
    ]
  },
  290405: {
    displayName: "Events / Trade Shows",
    keywords: [
      "expo",
      "booth fee",
      "exhibition",
      "trade show",
      "conference stand",
      // Adding some common misspellings and variations

      "events",
      "shows",
      "eventstradeshows",
      "evets",
      "evets trade shows",
      "evennts",
    ]
  },
  290406: {
    displayName: "Client Gifts",
    keywords: [
      "client gift",
      "corporate gift",
      "thank you gift client",
      // Adding some common misspellings and variations

      "gifts",
      "clientgifts",
      "clint",
      "clint gifts",
      "clieent",
      "clieent gifts",
    ]
  },
  290407: {
    displayName: "Lead Generation",
    keywords: [
      "lead gen",
      "prospecting tool",
      "contact database",
      "lead service",
      // Adding some common misspellings and variations

      "generation",
      "leadgeneration",
      "genertion",
      "lead genertion",
      "generaation",
      "lead generaation",
    ]
  },
  290501: {
    displayName: "Accountant",
    keywords: [
      "accountant",
      "bookkeeping help",
      "tax accountant",
      "financial statements",
      // Adding some common misspellings and variations

      "accoutant",
      "accounntant",
      "acconutant",
    ]
  },
  290502: {
    displayName: "Bookkeeper",
    keywords: [
      "bookkeeper",
      "bookkeeping",
      "ledger support",
      "reconciliations",
      // Adding some common misspellings and variations

      "bookkeper",
      "bookkeeeper",
      "bookekeper",
    ]
  },
  290503: {
    displayName: "Lawyer",
    keywords: [
      "lawyer",
      "solicitor business",
      "attorney business",
      "legal support",
      // Adding some common misspellings and variations

      "lawer",
      "lawyyer",
      "laywer",
    ]
  },
  290504: {
    displayName: "Consultant",
    keywords: [
      "consultant",
      "advisor",
      "business consultant",
      "strategy consultant",
      // Adding some common misspellings and variations

      "consutant",
      "consulltant",
      "conslutant",
    ]
  },
  290505: {
    displayName: "Virtual Assistant",
    keywords: [
      "VA",
      "assistant",
      "admin support",
      "virtual admin",
      // Adding some common misspellings and variations

      "virtualassistant",
      "assitant",
      "virtual assitant",
      "assisstant",
      "virtual assisstant",
      "asssitant",
    ]
  },
  290506: {
    displayName: "Contractor Payments",
    keywords: [
      "freelancer payment",
      "contractor invoice",
      "subcontractor",
      "outsourced work",
      // Adding some common misspellings and variations

      "contrctor",
      "contraactor",
      "contarctor",
      "freelncer",
      "freelaancer",
    ]
  },
  290601: {
    displayName: "Computer Equipment",
    keywords: [
      "laptop",
      "desktop",
      "monitor",
      "IT equipment",
      "computer hardware",
      // Adding some common misspellings and variations

      "computerequipment",
      "equiment",
      "computer equiment",
      "equippment",
      "computer equippment",
      "equpiment",
    ]
  },
  290602: {
    displayName: "Camera / Audio Equipment",
    keywords: [
      "camera",
      "microphone",
      "lens",
      "tripod",
      "audio gear",
      "recording gear",
      // Adding some common misspellings and variations

      "equipment",
      "cameraaudioequipment",
      "equiment",
      "camera audio equiment",
      "equippment",
      "camera audio equippment",
    ]
  },
  290603: {
    displayName: "Office Furniture",
    keywords: [
      "office furniture",
      "shelving",
      "meeting table",
      "filing cabinet",
      // Adding some common misspellings and variations

      "officefurniture",
      "furnture",
      "office furnture",
      "furniiture",
      "office furniiture",
      "furinture",
    ]
  },
  290604: {
    displayName: "Tools / Machinery",
    keywords: [
      "machinery",
      "trade tools",
      "equipment",
      "workshop machine",
      "site tools",
      // Adding some common misspellings and variations

      "toolsmachinery",
      "machnery",
      "tools machnery",
      "machiinery",
      "tools machiinery",
      "macihnery",
    ]
  },
  290605: {
    displayName: "Repairs & Maintenance",
    keywords: [
      "maintenance",
      "repair",
      "servicing",
      "equipment repair",
      "machine service",
      // Adding some common misspellings and variations

      "repairs",
      "repairsmaintenance",
      "maintnance",
      "repairs and maintnance",
      "mainteenance",
      "repairs and mainteenance",
    ]
  },
  290606: {
    displayName: "Shipping & Postage",
    keywords: [
      "postage",
      "courier",
      "shipping",
      "parcel",
      "mail cost",
      "dispatch",
      // Adding some common misspellings and variations

      "shippingpostage",
      "shiping",
      "shiping and postage",
      "shippping",
      "shippping and postage",
      "shippin",
    ]
  },
  290701: {
    displayName: "Client Meals",
    keywords: [
      "client lunch",
      "client dinner",
      "business meal",
      "networking meal",
      // Adding some common misspellings and variations

      "meals",
      "clientmeals",
      "clint",
      "clint meals",
      "clieent",
      "clieent meals",
    ]
  },
  290702: {
    displayName: "Business Travel",
    keywords: [
      "business trip",
      "work travel",
      "corporate travel",
      // Adding some common misspellings and variations

      "businesstravel",
      "busiess",
      "busiess travel",
      "businness",
      "businness travel",
      "busniess",
    ]
  },
  290703: {
    displayName: "Mileage",
    keywords: [
      "mileage claim",
      "business miles",
      "work mileage",
      "km allowance",
      // Adding some common misspellings and variations

      "milage",
      "mileeage",
      "mielage",
      "clim",
      "claaim",
    ],
    merchantHints: [
      "free now",
      "uber",
      "bolt",
      "lyft",
    ]
  },
  290704: {
    displayName: "Conferences",
    keywords: [
      "conference",
      "seminar",
      "summit",
      "professional event",
      "business conference",
      // Adding some common misspellings and variations

      "conferences",
      "confeences",
      "conferrences",
      "confreences",
      "confeence",
      "conferrence",
    ]
  },
  290705: {
    displayName: "Lodging",
    keywords: [
      "hotel business",
      "accommodation work",
      "business stay",
      // Adding some common misspellings and variations

      "lodging",
      "loding",
      "lodgging",
      "logding",
      "hoel",
      "hottel",
    ]
  },
  290706: {
    displayName: "Taxis",
    keywords: [
      "taxi work",
      "uber business",
      "transport client meeting",
      // Adding some common misspellings and variations

      "taxis",
      "tais",
      "taxxis",
      "txais",
      "tai",
      "taxxi",
    ],
    aliases: [
      "taxi to client meeting",
      "uber for work",
      "cab for work",
      "business rideshare",
      "ride to meeting",
      "work taxi fare",
    ],
    merchantHints: [
      "uber",
      "lyft",
      "bolt",
      "grab",
    ]
  },
  290801: {
    displayName: "Business Registration Fees",
    keywords: [
      "company registration",
      "setup fee",
      "business formation",
      "filing fee",
      // Adding some common misspellings and variations

      "businessregistration",
      "registation",
      "business registation fees",
      "registrration",
      "business registrration fees",
      "regisrtation",
    ]
  },
  290802: {
    displayName: "Licenses & Permits",
    keywords: [
      "business licence",
      "permit",
      "compliance permit",
      "operating licence",
      // Adding some common misspellings and variations

      "licenses",
      "permits",
      "licensespermits",
      "liceses",
      "liceses and permits",
      "licennses",
    ]
  },
  290803: {
    displayName: "VAT / Sales Tax",
    keywords: [
      "VAT",
      "sales tax",
      "remittance",
      "tax return business",
      // Adding some common misspellings and variations

      "saes",
      "salles",
      "slaes",
    ]
  },
  290804: {
    displayName: "Payroll Services",
    keywords: [
      "payroll provider",
      "payroll software",
      "salary processing",
      // Adding some common misspellings and variations

      "payoll",
      "payrroll",
      "paryoll",
      "provder",
      "proviider",
    ]
  },
  290805: {
    displayName: "Insurance",
    keywords: [
      "business insurance",
      "professional indemnity",
      "liability insurance",
      // Adding some common misspellings and variations

      "insuance",
      "insurrance",
      "insruance",
      "busiess",
      "businness",
    ]
  },
  290901: {
    displayName: "Bank Fees",
    keywords: [
      "bank fee",
      "monthly fee",
      "account charge",
      "transfer fee business",
      // Adding some common misspellings and variations

      "bak",
      "bannk",
      "bnak",
    ]
  },
  290902: {
    displayName: "Merchant Fees",
    keywords: [
      "stripe fee",
      "card fee",
      "merchant fee",
      "processing fee",
      "terminal fee",
      // Adding some common misspellings and variations

      "mercant",
      "merchhant",
      "merhcant",
      "strpe",
      "striipe",
    ]
  },
  290903: {
    displayName: "Chargebacks / Refund Losses",
    keywords: [
      "chargeback",
      "refund loss",
      "disputed payment",
      "lost payment",
      // Adding some common misspellings and variations

      "chargebacks",
      "losses",
      "chargebacksrefundlosses",
      "chargbacks",
      "chargbacks refund losses",
      "chargeebacks",
    ]
  },
  290904: {
    displayName: "Miscellaneous Business Costs",
    keywords: [
      "business misc",
      "other business",
      "uncategorized business expense",
      // Adding some common misspellings and variations

      "busiess",
      "businness",
      "busniess",
    ]
  },
  300101: {
    displayName: "Tithes",
    keywords: [
      "tithe",
      "tithing",
      "regular giving",
      "church giving",
      // Adding some common misspellings and variations

      "tithes",
      "tites",
      "tithhes",
      "tihtes",
      "tihe",
      "titthe",
    ]
  },
  300102: {
    displayName: "Weekly / Monthly Offerings",
    keywords: [
      "offering",
      "weekly offering",
      "monthly offering",
      "donation regular",
      // Adding some common misspellings and variations

      "offerings",
      "weeklymonthlyofferings",
      "offeings",
      "weekly monthly offeings",
      "offerrings",
      "weekly monthly offerrings",
    ]
  },
  300103: {
    displayName: "Temple / Mosque / Church Contributions",
    keywords: [
      "church donation",
      "mosque donation",
      "temple donation",
      "contribution faith",
      // Adding some common misspellings and variations

      "contriutions",
      "temple mosque church contriutions",
      "contribbutions",
      "temple mosque church contribbutions",
      "contrbiutions",
      "temple mosque church contrbiutions",
    ]
  },
  300104: {
    displayName: "Community Support",
    keywords: [
      "faith community support",
      "spiritual community giving",
      "parish support",
      // Adding some common misspellings and variations

      "commnity",
      "commuunity",
      "comumnity",
      "fath",
      "faiith",
    ]
  },
  300201: {
    displayName: "Weddings",
    keywords: [
      "wedding ceremony faith",
      "church wedding",
      "temple wedding",
      "spiritual wedding",
      // Adding some common misspellings and variations

      "weddings",
      "weddngs",
      "weddiings",
      "wedidngs",
      "weding",
      "weddding",
    ]
  },
  300202: {
    displayName: "Baptisms / Christenings",
    keywords: [
      "baptism",
      "christening",
      "naming ceremony",
      "church baptism",
      // Adding some common misspellings and variations

      "baptisms",
      "christenings",
      "baptismschristenings",
      "christnings",
      "baptisms christnings",
      "christeenings",
    ]
  },
  300203: {
    displayName: "Funerals",
    keywords: [
      "funeral faith",
      "memorial service",
      "burial ceremony",
      "religious funeral",
      // Adding some common misspellings and variations

      "funerals",
      "funeals",
      "funerrals",
      "funreals",
      "funral",
      "funeeral",
    ]
  },
  300204: {
    displayName: "Festivals / Holy Days",
    keywords: [
      "religious festival",
      "holy day",
      "feast day",
      "spiritual festival",
      // Adding some common misspellings and variations

      "festivals",
      "days",
      "festivalsholydays",
      "festvals",
      "festvals holy days",
      "festiivals",
    ]
  },
  300205: {
    displayName: "Pilgrimage Costs",
    keywords: [
      "pilgrimage",
      "holy journey",
      "religious travel",
      "spiritual trip",
      // Adding some common misspellings and variations

      "pilgrmage",
      "pilgriimage",
      "pilgirmage",
    ]
  },
  300301: {
    displayName: "Religious Education",
    keywords: [
      "sunday school",
      "catechism",
      "spiritual class",
      "religious studies",
      // Adding some common misspellings and variations

      "education",
      "religiouseducation",
      "eduction",
      "religious eduction",
      "educaation",
      "religious educaation",
    ]
  },
  300302: {
    displayName: "Retreats",
    keywords: [
      "retreat",
      "spiritual retreat",
      "meditation retreat",
      "faith retreat",
      // Adding some common misspellings and variations

      "retreats",
      "retrats",
      "retreeats",
      "reterats",
      "reteat",
      "retrreat",
    ]
  },
  300303: {
    displayName: "Youth Programs",
    keywords: [
      "youth group",
      "church youth",
      "spiritual youth program",
      "camp faith",
      // Adding some common misspellings and variations

      "programs",
      "youthprograms",
      "progams",
      "youth progams",
      "progrrams",
      "youth progrrams",
    ]
  },
  300304: {
    displayName: "Mission Support",
    keywords: [
      "mission donation",
      "mission trip support",
      "outreach support",
      // Adding some common misspellings and variations

      "mision",
      "misssion",
      "missoin",
      "donaion",
      "donattion",
    ]
  },
  300305: {
    displayName: "Spiritual Counseling",
    keywords: [
      "pastoral counseling",
      "spiritual advice",
      "faith support session",
      // Adding some common misspellings and variations

      "spiritualcounseling",
      "counsling",
      "spiritual counsling",
      "counseeling",
      "spiritual counseeling",
      "counesling",
    ]
  },
  300401: {
    displayName: "Books / Materials",
    keywords: [
      "religious books",
      "prayer book",
      "spiritual materials",
      "faith reading",
      // Adding some common misspellings and variations

      "booksmaterials",
      "mateials",
      "books mateials",
      "materrials",
      "books materrials",
      "matreials",
    ]
  },
  300402: {
    displayName: "Ritual Supplies",
    keywords: [
      "candles church",
      "incense",
      "altar supplies",
      "prayer items",
      "ritual materials",
      // Adding some common misspellings and variations

      "rital",
      "rituual",
      "riutal",
      "canles",
      "canddles",
      "chuch",
    ]
  },
  300403: {
    displayName: "Donations to Spiritual Organizations",
    keywords: [
      "spiritual org donation",
      "ministry donation",
      "monastery support",
      "temple support",
      // Adding some common misspellings and variations

      "donations",
      "organizations",
      "organiations",
      "donations to spiritual organiations",
      "organizzations",
      "donations to spiritual organizzations",
    ]
  },
  310101: {
    displayName: "Solicitor / Attorney Fees",
    keywords: [
      "solicitor",
      "attorney",
      "lawyer fee",
      "legal consultation",
      // Adding some common misspellings and variations

      "solicitorattorney",
      "soliitor",
      "soliitor attorney fees",
      "soliccitor",
      "soliccitor attorney fees",
      "solciitor",
    ]
  },
  310102: {
    displayName: "Court Fees",
    keywords: [
      "court fee",
      "filing fee",
      "hearing fee",
      "legal court charge",
      // Adding some common misspellings and variations

      "cort",
      "couurt",
      "cuort",
    ]
  },
  310103: {
    displayName: "Notary Fees",
    keywords: [
      "notary",
      "notarization",
      "certified signature",
      "notarial fee",
      // Adding some common misspellings and variations

      "notry",
      "notaary",
      "noatry",
    ]
  },
  310104: {
    displayName: "Document Filing Fees",
    keywords: [
      "filing fee",
      "registration filing",
      "submission fee",
      "legal document fee",
      // Adding some common misspellings and variations

      "documentfiling",
      "docuent",
      "docuent filing fees",
      "documment",
      "documment filing fees",
      "docmuent",
    ]
  },
  310105: {
    displayName: "Mediation",
    keywords: [
      "mediation",
      "dispute resolution",
      "mediator",
      "settlement discussion",
      // Adding some common misspellings and variations

      "medition",
      "mediaation",
      "medaition",
    ]
  },
  310106: {
    displayName: "Immigration Services",
    keywords: [
      "immigration lawyer",
      "visa help",
      "immigration filing",
      "residence permit help",
      // Adding some common misspellings and variations

      "immigation",
      "immigrration",
      "immirgation",
      "lawer",
      "lawyyer",
    ]
  },
  310107: {
    displayName: "Estate / Probate Services",
    keywords: [
      "probate",
      "estate administration",
      "will service",
      "inheritance legal help",
      // Adding some common misspellings and variations

      "estateprobate",
      "proate",
      "estate proate services",
      "probbate",
      "estate probbate services",
      "prboate",
    ]
  },
  310201: {
    displayName: "Financial Planner",
    keywords: [
      "financial planner",
      "money advisor",
      "financial planning session",
      // Adding some common misspellings and variations

      "financialplanner",
      "finacial",
      "finacial planner",
      "finanncial",
      "finanncial planner",
      "finnacial",
    ]
  },
  310202: {
    displayName: "Investment Advisor",
    keywords: [
      "investment advisor",
      "wealth manager",
      "portfolio advice",
      // Adding some common misspellings and variations

      "investmentadvisor",
      "invesment",
      "invesment advisor",
      "investtment",
      "investtment advisor",
      "invetsment",
    ]
  },
  310203: {
    displayName: "Tax Advisor",
    keywords: [
      "tax advisor",
      "tax consultant",
      "revenue advice",
      "planning taxes",
      // Adding some common misspellings and variations

      "advsor",
      "adviisor",
      "adivsor",
    ]
  },
  310204: {
    displayName: "Insurance Broker",
    keywords: [
      "insurance broker",
      "policy advisor",
      "cover broker",
      // Adding some common misspellings and variations

      "insurancebroker",
      "insuance",
      "insuance broker",
      "insurrance",
      "insurrance broker",
      "insruance",
    ]
  },
  310205: {
    displayName: "Mortgage Broker",
    keywords: [
      "mortgage broker",
      "loan broker",
      "remortgage advisor",
      // Adding some common misspellings and variations

      "mortgagebroker",
      "mortage",
      "mortage broker",
      "mortggage",
      "mortggage broker",
      "morgtage",
    ]
  },
  310301: {
    displayName: "Passport Fees",
    keywords: [
      "passport fee",
      "passport renewal",
      "new passport",
      // Adding some common misspellings and variations

      "passort",
      "passpport",
      "paspsort",
    ]
  },
  310302: {
    displayName: "ID Renewal",
    keywords: [
      "ID renewal",
      "identity card",
      "driving ID renewal",
      "official ID",
      // Adding some common misspellings and variations

      "renwal",
      "reneewal",
      "reenwal",
    ]
  },
  310303: {
    displayName: "Background Checks",
    keywords: [
      "background check",
      "police cert",
      "vetting",
      "verification fee",
      // Adding some common misspellings and variations

      "checks",
      "backgroundchecks",
      "backgound",
      "backgound checks",
      "backgrround",
      "backgrround checks",
    ]
  },
  310304: {
    displayName: "Certification / Authentication Fees",
    keywords: [
      "apostille",
      "authentication",
      "certified copy",
      "official certification",
      // Adding some common misspellings and variations

      "authentcation",
      "certification authentcation fees",
      "authentiication",
      "certification authentiication fees",
      "authenitcation",
      "certification authenitcation fees",
    ]
  },
  310401: {
    displayName: "Translation Services",
    keywords: [
      "translation",
      "certified translation",
      "interpreter",
      "document translation",
      // Adding some common misspellings and variations

      "transation",
      "transllation",
      "tranlsation",
    ]
  },
  310402: {
    displayName: "Resume / Career Coaching",
    keywords: [
      "CV writing",
      "resume service",
      "career coach",
      "interview prep service",
      // Adding some common misspellings and variations

      "coaching",
      "resumecareercoaching",
      "coacing",
      "resume career coacing",
      "coachhing",
      "resume career coachhing",
    ]
  },
  310403: {
    displayName: "Licensing Help",
    keywords: [
      "application help",
      "licensing consultant",
      "registration consultant",
      // Adding some common misspellings and variations

      "licensinghelp",
      "licesing",
      "licesing help",
      "licennsing",
      "licennsing help",
      "licnesing",
    ]
  },
  310404: {
    displayName: "Expert Consultations",
    keywords: [
      "expert advice",
      "specialist consultation",
      "paid consultation",
      "advisor fee",
      // Adding some common misspellings and variations

      "consultations",
      "expertconsultations",
      "consulations",
      "expert consulations",
      "consulttations",
      "expert consulttations",
    ]
  },
};

export function getExpenseTaxonomyKeywordEntry(subcategoryId: number) {
  return expenseTaxonomyKeywordPack[subcategoryId];
}

