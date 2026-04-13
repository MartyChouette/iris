# Iris

A contemplative apartment life sim about dating strangers, tending your space, and the things
left behind. Built in Unity 6.0.3 with URP.

## Overview

You play as Nema, living alone in a small apartment. Each morning a newspaper arrives with
personal ads. You choose a date for the evening, spend the day preparing — cleaning stains,
arranging objects, choosing music, watering plants, making drinks — and then host them for a
three-phase visit. Their judgment is shaped by everything you did and didn't do. Afterward,
you trim the flowers they left behind.

The game runs on a 7-day calendar. Some dates stop calling back.

## System Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                        Apartment Hub                             │
│  3 Areas: Kitchen · Living Room · Entrance                       │
│  Camera: CinemachineSplineDolly browse + area lerp               │
│  ObjectGrabber: spring-damper grab, grid snap, ghost preview     │
└───────────────────────────────┬──────────────────────────────────┘
                                │
          ┌─────────────────────┼──────────────────────┐
          ▼                     ▼                      ▼
    ┌──────────┐         ┌──────────────┐       ┌──────────────┐
    │ Kitchen  │         │ Living Room  │       │  Entrance    │
    │ Drinks   │         │ Record Player│       │ Outfit/Greet │
    │ Fridge   │         │ Books        │       │ Shoe/Coat    │
    │ Newspaper│         │ Perfume      │       │ DropZones    │
    │ Watering │         │ Coffee Table │       │              │
    └────┬─────┘         └──────┬───────┘       └──────┬───────┘
         │                      │                      │
         └──────────────────────┼──────────────────────┘
                                │
                                ▼
               ┌────────────────────────────┐
               │        Dating Loop         │
               │  GameClock (7-day)         │
               │  Newspaper → Schedule Date │
               │  Phone → Date Arrives      │
               └────────────┬───────────────┘
                            │
               ┌────────────┼────────────┐
               ▼            ▼            ▼
           Phase 1      Phase 2      Phase 3
          Entrance      Kitchen     Living Room
         (Arrival &    (Drink       (Apartment
          Judgment)    Making)      Judging)
                            │
                            ▼
               ┌────────────────────────────┐
               │       Date End Screen      │
               │  Grade · Affection · Score │
               └────────────┬───────────────┘
                            │
                            ▼
               ┌────────────────────────────┐
               │   Flower Trimming Scene    │
               │  (loaded additively)       │
               │  VirtualCut → FlowerBrain  │
               │  → Score → LivingPlant     │
               └────────────────────────────┘
```

## Key Subsystems

| Subsystem | Description |
|-----------|-------------|
| **Apartment Hub** | 3-area browse (Kitchen, Living Room, Entrance). CinemachineSplineDolly + direct pos/rot/FOV lerp per area. All stations always active. |
| **Object Interaction** | Spring-damper grab with render-on-top when held. Grid snap to PlacementSurfaces. Ghost preview (green/red). Occlusion blocking. Barrier cubes prevent placement inside furniture. Per-object rotation controls. |
| **Item Pairing** | PairableItem component. Shoes snap side-by-side (SpecificPartner). Dishes stack (AnyOfCategory). ObjectGrabber intercepts click-while-holding. |
| **Tidiness System** | Per-area scoring: stains (wipe with sponge), mess (DailyMessSpawner each morning), smell (accumulates, fought by plants), floor clutter. Surface multipliers 1x–5x for prominent spots. |
| **Record Player** | Physical vinyl workflow: hover sleeve to peek, extract, carry to turntable, magnetic snap, platter spin, tone arm animation, needle drop/lift SFX. Album art tooltip on hover. |
| **Book Collection** | 3 books pair side-by-side in any order. Either-side snap. Order validation triggers celebration + secret reward item arcing to coffee table. |
| **Watering System** | Physical watering can. Magnetic snap to plant rim. 2D pot cross-section with 4 vase shapes. Bubbly soil obscures water line. Target fill line. Pour persists day-to-day. Weather affects overnight drying. Under/overwater consequences: leaf trash, browning, water spills. |
| **Drink Making** | Physical bottle pour. Magnetic snap + tilt. 2D glass cutaway with weighted colored layers. 4 bottles, 2 glass types. Pour order matters. Scoring: order + layer accuracy + fill + garnish + overflow. |
| **Dating Loop** | 7-day calendar, newspaper personal ads, 3-phase dates (Arrival / Kitchen / Living Room), affection flower meter. Time jumps to sunset on date arrival. |
| **Date Characters** | 7 characters: Paris, Livii, Clover, Lily, Sage, Psychic, Sterling. Per-character liked/disliked tags, mood preferences, drink preferences, outfit preferences, reaction strength. |
| **Phase 3 Scoring** | Item-by-item apartment judgment. Multiplier popups (scale bigger + redder at higher values). Particles on liked/disliked items. Per-item highlight wave. Paired items earn bonus multiplier. Moment Camera auto-frames each reveal. |
| **MoodMachine** | Aggregates Weather, TimeOfDay, Music, AirQuality, LivingPlants into a 0–1 mood value. Drives directional light color/intensity/angle, ambient light, fog, rain particles, audio volumes. |
| **Weather System** | 6 states: Clear, Overcast, Rainy, Stormy, Snowy, FallingLeaves. Affects plant drying rate, mood, lighting, atmosphere. |
| **Flower Trimming** | Additive scene load. Virtual stem cutting (non-destructive). FlowerAutoSetup editor wizard. Scoring: stem length + cut angle + part condition vs. IdealFlowerDefinition. |
| **Living Plants** | Trimmed flowers spawn LivingFlowerPlant in apartment. Health decays over days (green → yellow → brown). Plant score affects air quality + MoodMachine. Flower grade shown on calendar. |
| **Keyword Highlighting** | Global keyword database. Per-category shimmer animation on TMP text (Like=pink, Dislike=grey-blue, Hobby=gold, Personality=purple, Special=teal). Active in newspaper, dialogue, date end screen, item descriptions. |
| **Moment Camera** | Auto-frames key events (book collection, item discovery, date arrival, Phase 3 reveals). Smooth push/hold/return with easing curves. |
| **PSX Rendering** | Retro shader suite: vertex snap, affine textures, pixelation, dithering. NatureBox sky shader with time-of-day palette. Volumetric light shafts at windows. Per-object PSX override via PSXObjectSettings (no material instances). Toggle via accessibility settings. |
| **Nema (companion cat)** | Per-phase models (newspaper, exploration, arrival, kitchen, couch, dancing secret). Head tracking + bored glance animations. |
| **Context Cursors** | 9 context types (watering, fridge, phone, drawer, drink, sponge, grab, scissors, interact). Pre-baked 16-step alpha bank. Fade in/out on hover. Hover-fade to 45% after 2s idle. |
| **Visibility Eyes** | World-space open/closed eye icons per item. Flash at exploration start. AnimationCurve-driven fade. Closed drawers show red-slashed eye. |
| **Save System** | IrisSaveData: calendar day, date history, plant records, apartment layout. Auto-saves on quit and end of date. Slot system. |
| **Accessibility** | 15 settings across 5 categories. Colorblind modes (4), text scale, reduce motion, timer multiplier (Normal / Relaxed 1.5x / Extended 2x / No Timer), captions, PSX toggle. Tabbed SettingsPanel UI. |
| **Audio** | 6-channel AudioManager (SFX, Music, Ambience, Weather, Environment, UI). MoodMachine-driven mix. SFX auto-cutoff. Caption support. |
| **Text Theme** | IrisTextTheme ScriptableObject loaded from Resources. Controls font, colors, size multipliers, spacing — applied globally to all TMP text. |
| **Feedback Tools** | PlaytestFeedbackForm (F8) + BugReportForm (F9) with Discord webhooks and local JSON/screenshot backup. |

## Dating Loop

```
GameClock (7-day calendar)
    │
    ▼
NewspaperManager — personal ads appear each morning
    │
    └─ Player selects ad → DateSessionManager.ScheduleDate()
                                │
                                ▼
                       PhoneController rings
                       Player answers → date arrives
                                │
                    ┌───────────┼───────────┐
                    ▼           ▼           ▼
                Phase 1     Phase 2     Phase 3
               Entrance     Kitchen    Living Room
              4 judgments  Drink eval  Item-by-item
             (outfit,      pour score  apartment
              perfume,         │       judging
              welcome,         │           │
              cleanliness)     └─────┬─────┘
                                     ▼
                              DateEndScreen
                             (S/A/B/C/D grade)
                                     │
                                     ▼
                           FlowerTrimmingBridge
                          (additive scene load)
                                     │
                                     ▼
                         LivingFlowerPlant spawned
                         in apartment (days alive
                         = flower trim score)
```

## Apartment Hub

The apartment is the central hub. A CinemachineSplineDolly browse camera traverses a 4-knot
closed-loop spline. Pressing A/D or clicking nav arrows moves between areas. All station
managers are always active — no gating.

**Kitchen** — DrinkMaking, Fridge, Newspaper, Watering (plants always accessible)
**Living Room** — Record Player, Bookcase (books + perfume + drawers), Coffee Table
**Entrance** — Shoe Rack, Coat Rack, Door, DropZones for entrance tidiness

Stations with their own Cinemachine cameras skip directly from Browsing to InStation. Clicking
an object during Browsing enters Selected state first (clean pick-and-place). Press Esc to
return to Browsing.

## Project Structure

```
Assets/
├── Editor/
│   ├── FlowerAutoSetup.cs          # Auto-wiring wizard for new flower levels
│   ├── ApartmentSceneBuilder.cs    # Generates full apartment hub scene
│   ├── BookcaseSceneBuilder.cs     # Standalone bookcase scene builder
│   ├── SettingsPanelBuilder.cs     # Generates settings panel prefab
│   ├── SceneValidator.cs           # Singleton/component/hierarchy validation
│   └── Tests/
│       └── FlowerGameBrainTests.cs # 24 NUnit tests for scoring logic
├── Scripts/
│   ├── Framework/                  # TimeScaleManager, AudioManager, GameClock,
│   │                               #   AccessibilitySettings, LocalizationManager
│   ├── GameLogic/                  # FlowerGameBrain, FlowerSessionController, scoring
│   ├── InteractionAndFeel/         # XYTetherJoint, SquishMove, GrabPull
│   ├── DynamicMeshCutter/          # Mesh cutting engine + plane behaviors
│   ├── Fluids/                     # Sap particle system, decal pooling
│   ├── UI/                         # HUD, grading, SettingsPanel, CaptionDisplay,
│   │                               #   IrisTextTheme, VisibilityEyeIndicator,
│   │                               #   PourCursorOverlay, KeywordHighlighter
│   ├── Apartment/                  # ApartmentManager, ObjectGrabber, MoodMachine,
│   │                               #   TidyScorer, CursorWorldShadow, PlacementSurface
│   ├── Bookcase/                   # BookVolume, PerfumeBottle, DrawerController
│   ├── Dating/                     # DateSessionManager, GameClock, PhoneController,
│   │                               #   DateCharacterController, ReactableTag,
│   │                               #   ReactionEvaluator, DayPhaseManager
│   ├── Mechanics/                  # DrinkMaking, Cleaning, Watering, RecordPlayer,
│   │                               #   PourDragHelper, WateringManager
│   ├── Rendering/                  # PSXRenderController, PSXPostProcessFeature,
│   │                               #   VolumetricLightShaft, AtmosphereController
│   └── Camera/                     # CameraPresetDefinition, CameraTestController
├── Shaders/                        # PSXLit, PSXPost, RimLight, VolumetricShaft,
│                                   #   NatureBox sky
├── ScriptableObjects/              # Flower defs, area defs, book/perfume/drink/date
│                                   #   defs, camera presets, quality presets, mood profiles
├── Resources/                      # IrisTextTheme SO (auto-loaded), DiscordWebhookConfig
└── Scenes/
    ├── apartment.unity             # Main game scene
    ├── mainmenu.unity              # Title screen
    └── [mechanic prototypes]       # 10 standalone test scenes
```

## Creating a New Flower Level

1. Import your flower model into the scene
2. Open **Window > Iris > Flower Auto Setup**
3. Select the flower root in the hierarchy
4. Verify detected parts (stem, crown, leaves, petals)
5. Click **Setup Flower** — all runtime components are auto-wired
6. Adjust the generated `IdealFlowerDefinition` ScriptableObject for scoring rules
7. Wire UI events (OnGameOver, OnResult) to your HUD prefabs
8. Validate with **Window > Iris > Scene Validator**

## Accessibility

Full settings suite at ESC > Settings with 6 tabs:

| Tab | Controls |
|-----|----------|
| Visual | Colorblind mode (4 options), high contrast, text scale |
| Audio | Master, Music, SFX, Ambience, UI volume sliders + captions toggle |
| Motion | Reduce Motion (disables parallax, vertex snap, text morphing), screen shake |
| Timing | Timer multiplier: Normal / Relaxed 1.5x / Extended 2x / No Timer |
| Controls | Input rebinding (JSON override persistence) |
| Performance | Resolution scale, quality preset, PSX effect toggle |

## Text Theme System

Create an `IrisTextTheme` ScriptableObject at **Create > Iris > Text Theme**, place in
`Assets/Resources/` named `IrisTextTheme`. Controls primary font, header font,
body/header/subtitle/accent colors, size multipliers, and spacing — applied globally to
every TMP text component in the scene via `IrisTextThemeApplier` on scene startup.

## Development

See [LONGTERM_PLAN.md](docs/LONGTERM_PLAN.md) for the full development roadmap and
remaining vertical slice work items.

See [DEV_JOURNAL.md](docs/DEV_JOURNAL.md) for session-by-session development notes.

See [CODEBASE_QUALITY_ASSESSMENT.md](docs/CODEBASE_QUALITY_ASSESSMENT.md) for the
technical audit.

See [DESIGN_NEMA_LIFE.md](docs/DESIGN_NEMA_LIFE.md) for the narrative design document
covering Nema's life systems, date disappearance mechanics, souvenir accumulation, and
the daily rhythm.
