# NSFinTech Design System

## Purpose
This file is the source of truth for NSFinTech mobile visual language and layout behavior.
It defines the baseline for design decisions, engineering implementation, and future Codex work.

## Product Design Philosophy
- Premium, calm, and content-first.
- Banking-grade trust and readability over visual novelty.
- Progressive disclosure: primary screens show only high-value information.
- Planner-friendly structure: overview first, detail one tap away.
- AI-assistive: guidance and suggestions, not noisy chatbot-first UI.

## Information Architecture Principles
- Primary tabs: Home, Accounts, Activity, Planner.
- One screen = one job.
- Avoid duplicated information between top-level screens.
- Move advanced controls to drill-down pages or sheets.
- Keep top-level pages scannable within 5-10 seconds.

## Color System
### Core palette intent
- Dark ink background.
- One primary blue family.
- Controlled cyan accent.
- Restrained status colors.

### Color roles
- `appBackground`: global canvas.
- `elevatedBackground`: stacked container background.
- `cardSurface` / `glassSurface`: card-level surfaces.
- `tabBarSurface`: floating tab container.
- `textPrimary` / `textSecondary`: hierarchy text colors.
- `primary`: primary CTA and active emphasis.
- `success` / `negative` / `caution`: status and amount semantics only.

## Typography Hierarchy
Use these semantic levels consistently:
- Display XL: hero balance only.
- Display L: large balance on account-level detail.
- Title 1: page title.
- Title 2: section title and key card title.
- Body 1: primary row/card content.
- Body 2: secondary metadata and support text.
- Caption: chips, helper text, tiny metadata.

Rules:
- Use tabular numbers for monetary values.
- Keep metadata one step lighter than primary content.
- Do not mix arbitrary font sizes between screens.

## Spacing System
Base spacing tokens:
- 4, 8, 12, 16, 20, 24, 32, 40.

Layout rhythm:
- Horizontal page padding: 20.
- Standard section gap: 20.
- Standard list row gap: 12.
- Card internal padding: 16.

Rules:
- Never collapse major sections tighter than 16.
- Keep filter rows separated from list containers by at least 12.
- Ensure vertical rhythm is predictable across all tabs.

## Radius System
- Small: 12 (inputs, small chips).
- Medium: 18 (cards, rows, standard buttons).
- Large: 24 (floating tab bar, larger cards).
- Hero: 28 (hero balance surfaces).

## Elevation and Shadow Rules
- Use soft elevation for standard cards.
- Use one stronger floating shadow for tab bar and FAB only.
- Avoid heavy glow shadows except very restrained hero emphasis.
- Remove shadows that look like visual artifacts.

## Surface System
Every block must use a defined surface level:
- App surface: full screen background.
- Section surface: low-emphasis container.
- Card surface: primary content cards.
- Floating surface: FAB and elevated floating controls.
- Sheet surface: modals/bottom sheets.
- Tab surface: floating bottom navigation.

Rules:
- Do not make every block the same card style.
- Border + background + depth must match the chosen surface role.

## Bottom Navigation Rules
- Floating, inset from left/right, and visually elevated.
- Keep visible on drill-down views where it improves continuity.
- Active state must be clear, restrained, and high-contrast.
- Separators must be subtle and non-distracting.

## Safe-Area and Bottom-Bar Layout Rules
This is mandatory for all scrollable/fixed content screens.

Rules:
- Content must stop above floating tab bar footprint.
- Use real container/list bottom inset, not decorative fade hacks.
- Keep 8-16px breathing room above tab bar region.
- No titles, rows, or buttons may appear partially under the bar.
- FAB and list endings must account for both tab bar and safe area.

Implementation guidance:
- Compute inset from safe-area bottom + tab height + tab offset + breathing room.
- For list screens with FAB, use additional bottom inset for FAB clearance.
- Use the shared inset helpers from theme utilities.

## Button System
Supported variants:
- Primary: high-emphasis CTA.
- Secondary: framed supporting action.
- Tertiary/Text: low-emphasis inline action.
- Icon-only: compact utility action.

Rules:
- Standard button height: 50.
- Standard radius: medium.
- Pressed state: small scale and subtle opacity shift.
- Do not create one-off button styles per screen.

## List/Feed System
Transaction and planner list rows should follow one pattern:
- Leading icon/avatar.
- Primary title.
- Compact metadata line.
- Value/action to the right.

Rules:
- Row backgrounds use section/card surface, not random colors.
- Maintain clear spacing between rows (12).
- Keep metadata concise and single-line when possible.

## Modal and Sheet System
- Use dark premium surfaces for all sheets/modals.
- Avoid default platform white backgrounds.
- Ensure close/open transitions preserve dark context.
- Keep consistent top spacing, radius, and border treatment.

Rules:
- Add Account, Add Transaction, and Transaction Context must feel like one family.
- No white flash on open/close/back transitions.

## Iconography Rules
- Use one icon family across the app.
- Keep icon sizes consistent by context:
  - Tab icons ~18.
  - Row icons ~16.
  - Utility icon buttons ~16-18.
- Avoid decorative icon clutter.

## Avatar and Initial Badges
- Use circular shape by default.
- Keep initials high-contrast and short (1-2 letters).
- Background should use section/card surface with subtle border.
- Avoid saturated colors unless used as a semantic status marker.

## Motion System
Motion intent:
- Soft, fast, restrained, continuity-focused.

Timing:
- Quick: 140ms.
- Standard: 220ms.
- Slow: 320ms.

Rules:
- No abrupt flashes or mismatched backgrounds.
- Use subtle press feedback on interactive controls.
- Keep list/section reveal motion restrained.

## Page Templates
### Home
- Greeting/time.
- Total balance hero.
- Month trend.
- 1-2 insights.
- Recent activity preview.
- One clear next action area.

### Accounts
- Title.
- Account list with balances.
- Clear drill-down entry.

### Activity
- Title.
- Simple filter row.
- Transaction feed.

### Planner
- This month summary.
- Necessities summary.
- Category health preview.
- Suggestions preview.
- Companion entry.

### Companion
- Prompt starters.
- Conversation area.
- Input and send action.

## Do and Don't
### Do
- Prioritize clarity and hierarchy.
- Keep top-level screens concise.
- Use shared tokens and shared component patterns.
- Respect safe-area and floating bar geometry in all layouts.

### Don't
- Patch layout problems with heavy fades.
- Introduce one-off visual styles per screen.
- Let content slip under floating controls.
- Add decorative effects that reduce trust/readability.
