# Iris — Pitch Deck

---

## Hook

A cozy apartment sim where you date strangers, tend your space, and slowly realize some of
them never leave.

---

## Concept

Iris is a contemplative life sim about preparing for dates and living with the consequences.
You play as Nema, alone in a small apartment. Each morning a newspaper arrives with personal
ads. You choose someone to invite over, then spend the day making the apartment worthy of them
— wiping stains, arranging objects, choosing music, watering plants, pouring drinks. Every
choice is physical, unhurried, and tactile.

When your date arrives, they notice everything. They judge your outfit at the door, react to
the drink you pour, wander the living room reading your shelves and reacting to your plants.
Their affection — rendered as a growing flower — is the sum of your preparation. Afterward,
you trim that flower. How well you cut determines how long it lives in your apartment.

Over seven days, the calendar fills. Dates accumulate. Some of them go very well. Those are
the ones who stop calling back.

---

## Core Loop

```
MORNING
  └─ Newspaper arrives — read personal ads, choose tonight's date

AFTERNOON (Preparation Phase — timed)
  └─ Clean stains · arrange objects · water plants
  └─ Select vinyl · spray perfume · prepare drinks
  └─ Set the apartment exactly how you want it seen

EVENING
  └─ Phone rings — date arrives
  └─ Phase 1: Entrance — outfit, perfume, greeting, cleanliness judged
  └─ Phase 2: Kitchen — make their drink from memory or guesswork
  └─ Phase 3: Living Room — they investigate everything you placed

NIGHT
  └─ Date ends · flower left behind
  └─ Flower trimming scene (cut stems, score, spawn living plant)
  └─ Sleep → next day
        │
        └─ [after the third date — the one with the strange flower]
              Souvenir appears. They don't call back.
```

---

## Key Mechanics

### 1. Physical Apartment Preparation
Every object in the apartment is a physics prop. You pick things up, carry them, place them.
The watering can needs to be filled and tilted. The vinyl record has to be extracted from its
sleeve and walked to the turntable. The drink bottles pour in real time with layered liquid
physics. The preparation phase is not a menu — it is the game.

### 2. The Date's Gaze
Your date notices specific things in a specific order. Phase 3 is an item-by-item reveal:
the date walks to each ReactableTag in the apartment, pauses, reacts. A thought bubble
appears — heart, neutral face, or frown — while a multiplier popup shows how much the item
mattered. Items the date likes boost affection. Items they dislike subtract it. Items placed
in prominent spots are worth more. The camera auto-pushes in on each discovery.

### 3. The Flower as Score
Every date leaves a flower. You trim it in a standalone scene using virtual scissors —
cutting angle, stem length, and part condition all feed into a score. That score determines
how many days the trimmed flower survives as a living plant in your apartment. Healthy plants
improve the apartment's air quality and affect the mood system. Each person brings three
flowers across three dates. The first two are beautiful. The third one is... different.

### 4. The Keyword System
Every character has preferences that surface as highlighted keywords in the newspaper,
dialogue, and item descriptions. Liked things glow pink; hobbies shimmer gold; dislikes
pulse grey-blue. The system rewards close reading. Attention paid to a personal ad on day one
pays off in affection on day three.

### 5. The Mood Machine
The apartment has a living atmosphere. Weather, time of day, the music on the record player,
plant health, and air quality all feed into a single 0–1 mood value. This drives the
directional light color, ambient tone, fog density, rain particles, and audio mix in real
time. A stormy evening with a dead plant and no music feels different from a clear sunset
with jazz and a healthy fern. The date notices.

### 6. The Horror Layer (Implicit)
Each person visits three times. You learn their preferences, refine your preparation, deepen
the relationship. Their third flower arrives wrong — wilted, dark, thorned, off in a way the
first two weren't. After that final date, they vanish. They stop appearing in the newspaper.
Calls go unanswered. A personal item of theirs appears in the apartment: a necklace on the
counter, a ring in the kitchen drawer. These souvenirs accumulate. Future dates notice them.
The game never explains Nema. The player connects the dots.

---

## Aesthetic and Tone

**Visual style:** PS2-era low-poly with warm URP rendering. Soft vertex snapping, affine
texture warping, and 2D dithering give the game a distinctly retro texture while the
lighting and color grading remain modern and intentional. Time-of-day shifts from bright
morning blue-white to golden late-afternoon to cool night blue through a single shader-driven
sky system.

**Palette:** Muted, warm. Cream walls, terracotta pots, soft greens, amber lamplight.
The PSX aesthetic keeps the game feeling hand-made rather than procedural. Every surface reads
as a deliberate choice.

**Tone:** 60% cozy, 25% mystery, 15% creepy. The apartment sim is genuine and unhurried —
methodical preparation, small domestic rituals, the satisfaction of a well-poured drink and
a clean apartment. The observation layer runs constantly: keywords, preferences, details that
reward attention across encounters. The creepy moments are rare but land hard precisely
because the rest of the game earned the player's trust. A strange third flower. An empty
slot in the newspaper. A ring in the kitchen drawer that wasn't there yesterday.

**Visual references:** Sorry We're Closed (primary — PSX aesthetic, warm domestic horror),
Killer7 (mundane horror, unreliable perspective), Spiritfarer (cozy with emotional weight).

**Mechanical references:** Unpacking (spatial arrangement as storytelling), The Case of the
Golden Idol (point-and-click observation and deduction), Grim Fandango (point-and-click with
physical world interaction).

---

## Target Audience

**Primary:** Cozy game players who want more mechanical depth. Fans of apartment sims,
slice-of-life games, and games where preparation and ritual are the gameplay.

**Secondary:** Horror-adjacent players drawn to psychological dread and unreliable-narrator
storytelling. Players who discovered games like Yume Nikki, LSD: Dream Emulator, or
Omori through word-of-mouth.

**Comparable titles:**

| Title | Relevance |
|-------|-----------|
| Sorry We're Closed | PSX aesthetic, warm domestic horror — primary visual reference |
| Unpacking | Spatial arrangement as emotional storytelling |
| The Case of the Golden Idol | Point-and-click observation, deduction through details |
| Grim Fandango | Point-and-click with physical world interaction, tone |
| Coffee Talk | Date prep loop, character-driven relationship building |
| Spiritfarer | Cozy management with meaningful loss |
| Venba | Small kitchen mechanics with emotional weight |

---

## Unique Selling Points

- **The preparation IS the game.** Not a menu between story beats — a tactile, physical
  ritual you perform before every date.
- **A date who sees everything.** The judgment phase rewards players who paid attention to
  keyword hints. Every item in the apartment has a potential meaning.
- **Cozy and unsettling in the same scene.** The horror is never delivered by a cutscene
  or a jump scare. It emerges from the same systems the cozy gameplay runs on.
- **The flower as a living consequence.** How you trim your date's flower becomes part of
  the apartment for days. A bad cut changes the smell, the mood, the mess.
- **Fully physical UI.** Drinks pour in layered cross-sections. Watering shows a 2D
  soil-and-water simulation. The record player requires physical handling. Every mechanic
  has a visual body.

---

## Current State

**30+ systems built across 20,000+ lines of production-quality code (8/10 technical audit).**

**Built and functional:**

- Full apartment hub: 3 areas, physics interaction, grid-snap placement, ghost preview
- Complete dating loop: 7-day calendar, newspaper ads, 3-phase dates, affection scoring
- 7 fully defined date characters with preferences, reactions, and dialogue hooks
- Entrance judgment sequence: outfit, perfume, greeting, cleanliness (4 sequential evaluations)
- Physical drink making with bottle pour, magnetic snap, and delivery to date
- Record player: physical vinyl workflow (extract, carry, snap, play), album art
- Book collection puzzle with paired-item snap and celebration reward
- Physical watering: 2D cross-section, 4 vase shapes, weather-affected drying, day-to-day persistence
- Tidiness system with per-area scoring, stain wiping, mess spawning, surface multipliers
- MoodMachine with weather timelines, time-of-day, music, plants, air quality inputs
- Flower trimming: virtual cutting, scoring, grading, music, 5x zoom, living plant persistence
- Keyword highlighting: per-category shimmer (like/dislike/hobby/personality) across all UI
- Moment Camera for automatic cinematic framing of key events
- Phase 3 scoring with multiplier popups, particle reactions, paired item bonuses
- PSX rendering suite with per-object overrides, volumetric light shafts, tilt-shift
- Full accessibility suite (15 settings, 5 categories, tabbed panel, text theme)
- Save system: auto-saves day/date history/plant records/apartment layout
- Main menu (parallax Nema head), tutorial card, Earthbound-style name entry, outfit selection
- Item pairing (shoes side-by-side, dishes stacked), visibility eye indicators
- Calendar UI with date history, flower grades, preference tracking
- Playtest feedback (F8/F9) with Discord webhook integration, screenshot capture

**In development:**

- Nema character: phase models exist, completing contextual behaviors
- Phase 3 conversation and reaction polish
- Date disappearance mechanic + souvenir accumulation
- Player knowledge / dating journal (per-phase preference unlocks)
- Couch win scene (post-successful date)
- Narrative content (Nema bible, mess narratives)

**Remaining for vertical slice:**

- Full 7-day scripted narrative arc (3–4 polished date characters)
- Horror payload delivery (souvenir accumulation, disappearances, environmental dread)
- Convention demo mode (7-minute curated slice)
- Photo intro sequence
- Art and audio asset integration pass

---

## Team and Timeline

**Team:** Raspberry Rum — two-person studio. One programmer, one artist, shared creative
direction. 30+ systems designed and built collaboratively.

**Engine:** Unity 6 with URP

**Current milestone:** Vertical slice — full loop with 3-4 characters, 3 flowers each,
and horror layer.

**Estimated milestones:**

| Milestone | Target | Deliverable |
|-----------|--------|-------------|
| Vertical Slice | Q3 2026 | Full loop, 3-4 characters, horror layer, 3 flowers each |
| Playable Demo | Q4 2026 | Convention-ready build, 7-minute curated slice |
| Content Complete | Q2 2027 | All 7 characters (21 dates), souvenirs, journal, full art + audio |
| Polish & Beta | Summer 2027 | Playtesting, accessibility, localization, platform builds |
| Release | Oct 2027 | Halloween launch window — Steam (PC), itch.io |

---

## Business Model

**Distribution:** Steam (primary), itch.io (simultaneous)

**Model:** Premium, one-time purchase.

**Price point:** $15–$20 USD (4–6 hour core experience, replayable for date variety and
horror discovery layer)

**DLC potential:** Date packs — 3 new characters per pack with new furniture, items, and
seasonal events. Each pack adds 9 new dates and fresh content to discover. The apartment
grows. The souvenir collection grows.

**Platforms:** PC (Windows, Mac). Console ports considered post-launch based on reception.

**Wishlist / community:** Discord server active. Playtest feedback system built into the
game (F8/F9 Discord webhooks). Dev journal maintained for community.

**Why it's replayable without new content:** The game never forces the horror on the player.
Stains, sad notes, relics of Nema's past, and the strange third flower from each date are
all there — but only players who look closely will piece it together. A first playthrough
where you ignore the mess is a cozy apartment sim. A second where you read everything is
a different game entirely. The observation layer drives replayability, not content volume.
