# Design Document — Nema's Life Systems

**Created:** February 14, 2026

---

## Core Concept

Nema is a potential serial killer who dates people and some of them... disappear. The player slowly realizes this through environmental storytelling — souvenirs left behind, empty chairs at the table, repeat dates that stop calling back.

### Tone Ratio: 60 / 25 / 15

| Pillar | Weight | What It Means |
|--------|:------:|---------------|
| **Cozy** | 60% | The apartment sim is genuine. Preparation, rituals, the satisfaction of a clean space and a good drink. This is the majority of playtime. |
| **Mystery** | 25% | The observation/deduction layer runs constantly. Keywords, preferences, details that reward close reading across encounters. Golden Idol–inspired attention mechanics. |
| **Creepy** | 15% | Rare but devastating. The strange third flower. The empty newspaper slot. The souvenir in the drawer. These moments land hard because the cozy earned trust first. |

Creepy moments are **never jump scares, never gore, never explicit**. The dread is environmental and inferential.

---

## Game Modes

| Mode | Duration | Purpose |
|------|----------|---------|
| Convention Demo | 7 minutes | Quick playable showcase at events |
| Downloadable Demo / Vertical Slice | 7 in-game days | Full loop of Nema's life, polished |
| Full Game | TBD (depends on 7-day feel) | Extended calendar, more dates, more content |

The full game has **no calendar end** — it runs until we decide the right stopping point based on how 7 days feels and how much date content is needed.

---

## Time & Calendar System

### Clock
- Real-time clock ticking through the day (GameClock already exists, needs expansion)
- Key time gates:
  - **4:00 PM** — Mail arrives daily
  - **8:00 PM** — Dates can begin (not before)
  - Night → sleep → next day

### Calendar
- Physical calendar object in the apartment (wall or desk)
- Player can click to check the date, see upcoming events
- Tracks: current day, dates scheduled, dates completed, dates who disappeared
- Visual markers on days (date faces? red X's for disappeared?)
- Asset-swappable: calendar is a mesh + UI overlay, easily replaced

---

## Nema (Player Character Placeholder)

Nema should be **visible in the apartment** — not just a floating camera. She's present in rooms doing contextual things.

### Kitchen Behaviors
- Idle: leaning on counter, looking out window
- During cooking/drink making: at the counter working
- After mail: reading mail at kitchen table
- Eating: at table (morning routine)

### Living Room Behaviors
- Idle: sitting on couch, flipping through book
- With record player: swaying/bobbing to music
- After perfume: adjusting hair in reflection
- With date: sitting across from date on couch

### Implementation Notes
- Placeholder: simple humanoid mesh with idle animation
- Position anchors per room (kitchen spot, living room spot)
- Contextual animation swaps based on current activity
- **Must be easily swappable** — Nema model, animations, materials all replaceable without code changes
- Consider: `NemaController` component with state machine, room-aware positioning

---

## Disco Ball

- Clickable object in the apartment (living room ceiling?)
- On click: Nema starts dancing at her current location
- Fun/personality moment — shows Nema's character
- Particle effects (mirror ball reflections on walls)
- Music change or overlay?
- **Asset-swappable:** disco ball mesh, dance animation, particle effect

---

## Mail System

- **4:00 PM daily** — mail arrives (sound effect, visual cue)
- Mail location: front door slot, mailbox, or kitchen counter
- Contents could include:
  - Newspaper (already exists — morning delivery, could shift to mail)
  - Letters from dates (post-date follow-up)
  - Bills, flyers (world-building flavor)
  - Missing person notices (for disappeared dates — escalating horror)
- Player clicks mail to read/interact
- **Asset-swappable:** mail objects, letter textures, envelope mesh

---

## Date Schedule Rework

### Timing
- Dates happen at **8:00 PM** (not whenever newspaper is read)
- Flow: Morning (newspaper) → Afternoon (prep + mail at 4pm) → Evening (date at 8pm)
- This gives the player a clear daily rhythm

### Date Outcomes — The Horror Layer

Dates fall into categories based on how they go:

| Outcome | What Happens | After Effect |
|---------|-------------|-------------|
| Goes poorly | Date leaves early, texts "not a match" | Nothing sinister |
| Goes okay | Date leaves normally, might text again | Can become repeat date |
| Goes very well | Date stays late... then **disappears** | Souvenir left behind |
| Repeat date | Same person comes back | Deeper relationship, or... |

### Three Dates, Three Flowers

Each date character visits **three times**. The player learns their preferences over repeated encounters, refining their preparation each visit.

- **Date 1** — First meeting. Player is guessing. Flower left behind is normal and beautiful.
- **Date 2** — Player applies knowledge from date 1. Better score. Flower is normal.
- **Date 3** — Player knows them well. The flower left behind is **wrong** — wilted, dark, thorned, discolored, or off in some way the first two weren't. This is the horror signal.

After the third date, the person **disappears**.

### Disappearances
- After the third date, the person vanishes
- They stop appearing in the newspaper personals
- They don't respond to calls
- The strange third flower is the only warning
- **No explicit violence shown** — the horror is in the implication
- The player begins to question: did I cause this? Did Nema do this?

### Souvenirs (Serial Killer Trophies)
- After a date disappears, one of their personal items appears in the apartment:
  - Necklace on the bathroom counter
  - Ring in the kitchen drawer
  - Hat on the coat rack
  - Sweater draped over the couch
  - Watch on the nightstand
  - Scarf hanging by the door
- These are **persistent** — they accumulate over days
- New dates might notice and comment on them (ReactableTag system)
- Player can pick up and place souvenirs (ObjectGrabber system)
- **Asset-swappable:** each souvenir is a mesh + material, defined via ScriptableObject

### Souvenir ScriptableObject (proposed)
```
SouvenirDefinition:
  - souvenirName (string)
  - linkedDate (DatePersonalDefinition)
  - mesh (Mesh)
  - material (Material)
  - spawnLocation (Vector3)
  - reactableTag (string) — for date reactions
  - description (string) — player's internal monologue?
```

---

## Date Content Scaling

Each person visits **3 times** (3 flowers each). The amount of unique date characters determines game length:

| Characters | Total Dates | Calendar Days | Notes |
|:----------:|:-----------:|:-------------:|-------|
| 4 (current) | 12 | 7-12 days | Vertical slice — tight, focused |
| 7 | 21 | 14-21 days | Full game — good variety |
| 10+ | 30+ | 21-30 days | Extended content territory |

### Three-Date Arc Per Character
- **Date 1** — First meeting, player guessing, normal flower
- **Date 2** — Player applies learned preferences, better score, normal flower
- **Date 3** — Player knows them well, strange/creepy flower, person disappears after
- They remember previous visits (liked items, drinks)
- Relationship deepens across encounters → more personal details revealed
- Third date is always the last

---

## Daily Rhythm (Target Flow)

```
MORNING (7am - 12pm)
  └─ Wake up
  └─ Newspaper arrives (read personals, pick date for tonight)
  └─ Morning routine (kitchen, bathroom)

AFTERNOON (12pm - 8pm)
  └─ Preparation (clean apartment, arrange items, make drinks)
  └─ 4:00 PM — Mail arrives
  └─ Continue prep (water plants, play records, spray perfume)
  └─ Nema does idle things around apartment

EVENING (8pm - late)
  └─ 8:00 PM — Date arrives (doorbell)
  └─ Date phases (arrival → drinks → apartment judging)
  └─ Date leaves (or doesn't...)

NIGHT
  └─ Post-date (souvenir appears if date "went well")
  └─ Go to bed → next day
```

---

## Save Game System

The game needs a full save/load system that persists across sessions. The existing `SaveManager` only tracks flower trimming session results — it doesn't save game progress.

### What Needs Saving

| Category | Data | Current Holder |
|----------|------|---------------|
| Calendar | Current day, time of day | GameClock |
| Player | Name, outfit (future) | PlayerData |
| Date history | Who was dated, when, outcome, grade | DateHistory (static, lost on reload) |
| Player knowledge | What was learned about each date per phase | NEW — DateKnowledgeRegistry |
| Item states | Which perfumes/trinkets/books are on display | ItemStateRegistry (static, lost on reload) |
| Apartment state | Object positions (PlaceableLayout.json already exists) | JSON file (partial) |
| Souvenirs | Which souvenirs are in apartment, positions | NEW — SouvenirRegistry |
| Disappeared dates | Which dates have vanished | NEW — tracked in DateHistory |
| MoodMachine | Current active sources | MoodMachine singleton |
| Newspaper | Today's ad lineup, which was selected | DayManager |

### Save File Structure (proposed)

```
GameSaveData:
  ├─ version (int) — save format version for migration
  ├─ timestamp (string) — when saved
  ├─ currentDay (int)
  ├─ currentHour (float)
  ├─ playerName (string)
  ├─ dateHistory[] — DateHistoryEntry per completed date
  ├─ dateKnowledge[] — per-date, per-phase knowledge unlocks
  ├─ itemStates{} — dictionary of item ID → display state
  ├─ souvenirs[] — which souvenirs have appeared + positions
  ├─ disappearedDates[] — date IDs that vanished
  ├─ apartmentLayout — object positions (merge with PlaceableLayout.json)
  └─ settings — accessibility, language, volume (already in PlayerPrefs)
```

### Save Triggers
- **Auto-save:** End of each day (going to bed)
- **Manual save:** Pause menu (if we add one)
- **Convention demo:** No save needed (7-min session)

### Load Flow
1. Main menu → "Continue" or "New Game"
2. On load: deserialize JSON → inject into GameClock, DayManager, DateHistory, ItemStateRegistry, etc.
3. Scene rebuilds apartment from saved state (object positions, souvenirs, stains)

---

## Player Knowledge System (Dating Journal)

The player accumulates knowledge about each date character across encounters. Knowledge is **unlocked per phase** — even if a date goes badly and they leave at phase 2, the player keeps everything learned in phases 1 and 2.

### How Knowledge Unlocks

```
Date Encounter:
  Phase 1 (Arrival) → Learn: appearance reaction, perfume opinion, greeting style
      ↓ pass
  Phase 2 (Kitchen) → Learn: drink preferences, cooking reactions, kitchen conversation
      ↓ pass
  Phase 3 (Living Room) → Learn: music taste, book opinions, decor preferences, deep topics
      ↓ date ends
  Result: Player keeps ALL knowledge from phases reached, even if rejected
```

### Knowledge Types

| Phase | What You Learn | How It Helps Next Time |
|-------|---------------|----------------------|
| Phase 1 | Outfit preference, perfume reaction, greeting style | Dress right, spray right perfume, greet correctly |
| Phase 2 | Liked/disliked drinks, recipe preferences | Make the right drink next time |
| Phase 3 | Liked/disliked music, books, trinkets, cleanliness standard | Set up apartment with their favorites |

### Knowledge Data (proposed)

```
DateKnowledgeEntry:
  ├─ dateId (string) — links to DatePersonalDefinition
  ├─ highestPhaseReached (int) — 1, 2, or 3
  ├─ timesEncountered (int) — repeat date tracking
  ├─ phaseInsights[]
  │     ├─ phase (int)
  │     ├─ insightKey (string) — e.g. "likes_jazz", "hates_gin", "prefers_clean"
  │     └─ insightText (string) — human-readable for journal UI
  ├─ bestAffection (float) — highest score achieved
  └─ disappeared (bool) — did they vanish?
```

### Journal UI

- Accessible from apartment (maybe a notebook on the kitchen counter, or a phone app)
- Shows each date character with:
  - Portrait / silhouette
  - Name (or "???" if never dated)
  - Times dated
  - Known preferences (unlocked per phase)
  - Status: "Available" / "Dating" / "Disappeared" / "???"
- **Red flags accumulate:** each disappeared date gets a red X or ominous marking
- Player can review before choosing tomorrow's date from newspaper
- **Asset-swappable:** journal UI, character portraits, icons

### Integration with Existing Systems

- `DatePreferences` on `DatePersonalDefinition` already defines liked/disliked tags, drinks, mood — knowledge system reveals these to the player progressively
- `ReactionEvaluator` already checks preferences → can flag "you just learned: Sterling hates jazz" when a negative reaction fires
- `DateEndScreen` already shows grade → can add "New insights unlocked" section
- `DateHistory` already tracks outcomes → extend with knowledge entries

### Gameplay Loop Impact

```
Day 1: Date Sterling → reach Phase 2, rejected at drinks
  → Learn: Sterling likes clean apartments, hates floral perfume, prefers gin

Day 3: Date Sterling again → apply knowledge
  → Clean apartment, skip perfume, make gin drink
  → Reach Phase 3 this time → learn music + book preferences
  → Date goes "very well"...

Day 4: Sterling doesn't appear in newspaper. Necklace on bathroom counter.
  → Player opens journal: Sterling — "Disappeared"
```

---



1. **All art swappable** — meshes, materials, animations defined in ScriptableObjects or prefab references
2. **Data-driven dates** — DatePersonalDefinition already handles preferences; extend with conversation, souvenir links
3. **Calendar as data** — `CalendarData` tracks days, events, disappearances. UI reads from data.
4. **Nema as component** — `NemaController` with room-aware state machine, animation swaps, position anchors
5. **Mail as event** — `MailSystem` fires at 4pm via GameClock, spawns mail objects from SO pool
6. **Souvenirs as ReactableTags** — new dates react to previous dates' left-behind items

---

## Implementation Priority (Suggested)

### Must-Have for Vertical Slice
1. Save game system (auto-save on sleep, load on continue)
2. Player knowledge system / dating journal (per-phase insight unlocks)
3. Calendar system (physical object + data tracking)
4. Time gates (mail at 4pm, dates at 8pm)
5. Nema placeholder in rooms (idle + contextual)
6. Date disappearance mechanic + 1-2 souvenirs
7. Convention demo timer mode (7 min)
8. Feedback overlay on time limit expiry

### Nice-to-Have
9. Disco ball + dance
10. Mail with flavor content (letters, missing person notices)
11. Repeat date conversations with memory
12. Souvenir accumulation over many days
13. Date reactions to souvenirs ("whose ring is that?")
14. Journal UI with portraits and red flags

---

## Feedback System

When any timed session ends (convention demo 7-min, vertical slice day 7, or any future endpoint), show a **feedback overlay**:

- Simple overlay screen after the "end" trigger
- Fields: rating (1-5 stars or emoji scale), free-text comment, optional email/contact
- Data saved locally to JSON (or posted to a simple endpoint if online)
- Must be **non-intrusive** — player can skip it
- Convention demo: show automatically when 7-min timer expires
- Vertical slice: show after day 7 evening
- Full game: show on quit or at milestone days
- **Asset-swappable:** overlay UI, rating icons, text prompts all replaceable

---

## Open Questions

- [ ] Does the convention demo play the full loop compressed, or a curated slice?
- [ ] How explicit should the horror be? Newspaper headlines? Police reports in mail?
- [ ] Should Nema have voice lines / internal monologue, or purely environmental?
- [ ] Do ALL "very well" dates disappear, or is there a chance they survive?
- [ ] Can the player choose NOT to date (skip days)? What happens?
- [ ] Is there a "game over" condition, or does Nema's life just continue?
