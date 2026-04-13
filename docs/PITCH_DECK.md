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
        └─ [if the date went very well]
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
Every successful date leaves a flower. You trim it in a standalone scene using virtual
scissors — cutting angle, stem length, and part condition all feed into a score. That score
determines how many days the trimmed flower survives as a living plant in your apartment.
Healthy plants improve the apartment's air quality and affect the mood system. A bad trim
leaves clippings on the floor and a plant that browns within days.

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
Dates who score very high affection — the great ones, the ones you prepared perfectly for —
vanish after that night. They stop appearing in the newspaper. Calls go unanswered. A
personal item of theirs appears in the apartment the next morning: a necklace on the counter,
a ring in the kitchen drawer. These souvenirs accumulate. Future dates notice them. The game
never explains Nema. The player connects the dots.

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

**Tone:** Cozy on the surface. Methodical preparation, small domestic rituals, the
satisfaction of a well-poured drink and a clean apartment. Beneath that — a slow accumulation
of dread. The horror is environmental and never explicit. Players who pay attention will
understand what Nema is. Players who don't will have a pleasant apartment sim.

**References:** Shenmue (domestic physicality), Spiritfarer (cozy with weight), Disco
Elysium (character surfaces through behavior), Killer7 (mundane horror, unreliable
perspective).

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
| Unpacking | Spatial arrangement as emotional storytelling |
| Coffee Talk | Date prep loop, character-driven relationship building |
| Spiritfarer | Cozy management with meaningful loss |
| Shenmue | Domestic physicality, daily rhythm |
| Venba | Small kitchen mechanics with emotional weight |
| Yume Nikki | Quiet horror beneath a domestic surface |

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

**Built and functional:**

- Full apartment hub: 3 areas, physics interaction, placement system, ghost preview
- Complete dating loop: 7-day calendar, newspaper ads, 3-phase dates, affection scoring
- 7 fully defined date characters with preferences, reactions, and dialogue hooks
- Record player, book collection puzzle, physical watering, physical drink making
- Tidiness system with per-area scoring, stain wiping, mess spawning
- MoodMachine with weather, time-of-day, music, plants, air quality inputs
- Flower trimming: virtual cutting, scoring, living plant persistence in apartment
- Keyword highlighting system active in newspaper, dialogue, and item descriptions
- Moment Camera for automatic cinematic framing of key events
- Phase 3 scoring with multiplier popups, particle reactions, paired item bonuses
- PSX rendering suite with per-object overrides
- Full accessibility suite (15 settings, 5 categories)
- Save system: auto-saves day/date history/plant records/apartment layout
- Main menu, tutorial card, name entry, playtest feedback tools

**In development:**

- Date disappearance mechanic + souvenir accumulation
- Phase 2 and Phase 3 dialogue and NPC flow polish
- Half-folded newspaper visual rework
- Player knowledge / dating journal (per-phase preference unlocks)
- Couch win scene (post-successful date)
- Nema visible character in apartment

**Remaining for vertical slice:**

- Full 7-day scripted narrative arc (3–4 polished date characters)
- Photo intro sequence
- Horror payload delivery (souvenir accumulation, mail system, disappearances)
- Convention demo mode (7-minute curated slice)

---

## Team and Timeline

**Team:** Solo developer with AI-assisted development (design documentation, code review,
iteration support via Claude Code). All art, code, design, and systems by one person.

**Engine:** Unity 6.0.3 with URP

**Current milestone:** Vertical slice — full 7-day loop with polished Phase 3 and horror
layer delivering by end of Q2 2026.

**Estimated milestones:**

| Milestone | Target | Deliverable |
|-----------|--------|-------------|
| Vertical Slice | Q2 2026 | Full 7-day loop, 3-4 characters, horror layer |
| Convention Demo | Q3 2026 | 7-minute curated slice, playable at events |
| Content Alpha | Q4 2026 | All 7 characters, full souvenir system, journal |
| Beta | Q1 2027 | Full platform builds, accessibility pass, localization |
| Release | Q2 2027 | Steam (PC), itch.io |

---

## Business Model

**Distribution:** Steam (primary), itch.io (simultaneous)

**Model:** Premium, one-time purchase. No DLC planned.

**Price point:** $12–$15 USD (4–6 hour core experience, replayable for date variety and
horror discovery layer)

**Platforms:** PC (Windows, Mac). Console ports considered post-launch based on reception.

**Wishlist / community:** Discord server active. Playtest feedback system built into the
game (F8/F9 Discord webhooks). Dev journal maintained for community.

**Why premium:** The game has a defined arc and a deliberate ending. A subscription or
live-service model would undermine the tone. The horror layer only works once per player
in its intended form.
