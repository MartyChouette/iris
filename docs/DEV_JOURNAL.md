# Dev Journal — Iris v3.0

---

## 2026-04-06 to 2026-04-13 — Massive Systems Sprint, Drink Delivery, Polish

### Session Summary

Extended multi-session sprint: record player rework, book collection puzzle, watering system overhaul, full drink system rework with phase 2 gating and physical delivery to date, keyword highlighting, affection flower, Nema phase models + wellbeing/personality charts, weather timelines, dialogue portraits, fly system, tilt-shift camera, analog clock, item pairing improvements, surface multipliers, PSX glitch, ghost preview improvements, sink zone + dirty glass, edge scroll, fridge rework, barrier placement blocking, shoe pairing fixes, flower trimming enhancements (music, 5x zoom, phase title, leaf release), and extensive bug fixes. ~19 commits.

---

### 1. Drink System Rework (Major Feature)

Complete rework of the Phase 2 dating experience:
- **Phase 2 gating:** Bottle pour + magnetic snap only active during BackgroundJudging date phase
- **Bottle placement:** `alreadyPoured` check lets player put bottle down near glass after pouring
- **Drink delivery:** New `WaitingForDelivery` state — glass becomes pickable, click date to hand over drink
- **Proximity delivery:** Primary: proximity check (within 1.5m of date = deliver). Fallback: RaycastAll through all objects to find DateCharacterController. Auto-adds CapsuleCollider to date if missing.
- **HUD prompts** at every step: "Click glass to finish" → "Pick up and hand to date" → "Click on [name]!"
- **DrinkCutawayUI:** Removed GraphicRaycaster (was blocking all clicks), scene-change cleanup, wine glass silhouette with Mask
- **DrinkCartController:** Skips SimpleDrinkManager when DrinkPourManager exists

**Files:** `DrinkPourManager.cs`, `DrinkCutawayUI.cs`, `DrinkCartController.cs`, `ObjectGrabber.cs`

---

### 2. Record Player, Books, Watering Reworks

- **Record player:** Physical vinyl workflow — hover sleeve to peek, extract, carry to turntable, magnetic snap, platter spin, tone arm animation, album art tooltip
- **Book collection puzzle:** 3 books pair side-by-side in any order, either-side snap, order validation triggers celebration + secret reward item
- **Watering system:** Physical watering can with magnetic snap to plant rim, 2D pot cross-section with 4 vase shapes, bubbly soil, target fill line, day-to-day persistence, weather-affected drying

---

### 3. Nema & Atmosphere Systems

- **Nema phase models:** Per-phase models (newspaper, exploration, arrival, kitchen, couch, dancing secret). Head tracking + bored glance animations
- **NemaWellbeing:** Personality/wellbeing charts tracking emotional state from DateHistory
- **Voice lines:** Nema voice line system
- **Dialogue portraits:** Visual novel style portrait system
- **Weather timelines:** Timeline-based weather progression
- **Fly system:** FlyController environmental detail (volume tuned down)
- **Tilt-shift camera:** Post-processing depth effect
- **Analog clock:** Physical apartment clock with pivot hands

---

### 4. Interaction & Placement Polish

- **Keyword highlighting:** Global database, per-category shimmer (Like=pink, Dislike=grey-blue, Hobby=gold, Personality=purple, Special=teal) across newspaper, dialogue, date end screen, item descriptions
- **Pair hover snap:** Partner shoe highlights when picking up one shoe
- **Surface multipliers:** Per-surface scoring multipliers for prominent placement spots
- **Ghost/shadow suppression:** Hidden during bottle→glass, can→plant, shoe→partner snaps. Red/invalid over barrier geometry
- **Barrier blocking:** Larger sphere (0.08m) + downward probe (0.15m) catches barriers inside furniture
- **Shoe pairing fix:** Shoes straighten to flat (zero tilt), partner Y position matches exactly, InteractableHighlight auto-added
- **Sink zone + dirty glass:** Dishwashing area with dirty glass mechanics
- **Edge scroll:** Screen-edge scrolling for apartment browsing
- **Blanket item:** Placeable/reactable blanket in apartment

---

### 5. Flower Trimming Enhancements

- **Music:** Background music during trimming scene
- **5x zoom UI:** Detail zoom for precise cuts
- **Epic phase title drop:** Dramatic reveal animation
- **Leaf release:** Leaves below cut plane released when stem is virtually cut
- **Held items render on top:** Items render above scene geometry while held

---

### 6. Misc Fixes & Polish

- **Cursor:** Force visible during pause menus (SimplePauseMenu.IsPaused + timeScale check)
- **PickupDescriptionHUD:** Smooth 0.3s fade-out via CanvasGroup
- **MomentCamera:** Pure lateral slide (no rotation), slower push/return (1.0s/0.8s), optional FOV
- **Fly buzz volume:** 0.15 → 0.04
- **PSX glitch dishevel:** Aesthetic glitch effect
- **Fridge rework:** Improved controller with date scene model integration

---

### Commits (~19)

| Hash | Description |
|------|-------------|
| `80da8f2` | massive session: flies, weather timelines, dialogue portraits, flower snapshot fixes, tilt-shift, cursor system |
| `00f7a84` | perf fixes + Nema personality/wellbeing charts + voice lines + weather dialogue |
| `2a8c5d7` | analog clock with pivot hands + disable VN dialogue box |
| `1f0485a` | huge session: pair hover snap, surface multipliers, PSX glitch, ghost preview, sink zone, Nema phase models, edge scroll |
| `01632f2` | massive bugfix + fridge rework + date scene models + drink cart |
| `ecd714e` | massive session: record player rework, book collections, watering, drink rework, keyword highlighting, affection flower, blanket item |
| `2d66e09` | drink demo scene, water persistence fix, barrier placement, default material fix |
| `be12987` | updated README + PITCH_DECK.md |
| `42900fb` | add elevated HTML pitch deck with scroll animations |
| `f9eea98` | drink system rework: phase 2 gating, bottle placement, delivery to date, UI fixes, camera smoothing |
| `f9a83a0` | fix drink delivery: proximity-based click detection + date character collider |
| `4f64546` | fix barrier placement blocking: larger sphere + downward probe + ghost feedback |
| `7453780` | fix shoe pairing: alignment + partner highlight |

---

## 2026-04-05 — Bug/Polish Pass, New Mechanics, UX Overhaul

### Session Summary

Large polish session: fixed audio loss after flower trimming, added drag-to-pour mechanic, visibility eye indicators, smooth cursor fading, fridge rejection text, quit-to-menu transitions, album art for records, and cubby capacity enforcement. 22 commits.

---

### 1. Audio Loss After Flower Trimming (Bug Fix)

**Problem:** All SFX went silent on Day 2 after flower trimming.

**Root causes (two):**
- `AudioManager.SFXCutoffRoutine` had a volume ratchet-down bug: interrupted fades captured partially-faded volume as restore target. After many SFX calls, `sfxSource.volume` approached 0.
- Music was ducked to 10% during flower trimming, then `_currentPhase = DayPhase.Evening` was set directly (bypassing `SetPhase`), so `UnduckMusic` never fired.

**Fix:** Store a clean baseline volume for SFX cutoff. Explicitly restore audio levels in the flower trimming transition.

**Files:** `AudioManager.cs`, `DayPhaseManager.cs`

---

### 2. Drag-to-Pour Mechanic (New Feature)

Replaced hold-to-pour with drag-to-pour: click to start, pull mouse downward to control pour speed (quadratic ease-in for fine control at low rates), release to score.

- `PourDragHelper` — static helper tracking drag distance to pour rate (0-1) and tilt angle (0-90 degrees)
- `PourCursorOverlay` — screen-space UI cursor that rotates to show tilt, replaces hardware cursor during pour
- Both `WateringManager` and `SimpleDrinkManager` updated

**Files:** `PourDragHelper.cs` (NEW), `PourCursorOverlay.cs` (NEW), `WateringManager.cs`, `SimpleDrinkManager.cs`

---

### 3. Visibility Eye Indicators (New Feature)

World-space eye icons showing whether the date can see each item:
- Open eye = visible to date (public)
- Closed eye with red slash = hidden (private, in closed cubby)
- Flash all items at exploration start for a quick visibility snapshot
- Icons billboard toward camera, render on top of geometry, hold then fade out
- Inspector AnimationCurves for fade-in and fade-out easing

**Files:** `VisibilityEyeIndicator.cs` (NEW), `ReactableTag.cs` (OnPrivacyChanged event), `DrawerController.cs` (OnDrawerPrivacyChanged event), `ObjectGrabber.cs` (OnObjectPlaced now passes PlaceableObject), `DayPhaseManager.cs`

---

### 4. Smooth Context Cursor Fading (New Feature)

Context cursors (watering, fridge, sponge, etc.) now fade in/out smoothly:
- Pre-baked alpha bank: 16 textures per cursor at discrete alpha steps
- Fade in (~0.14s) when entering hover, fade out (~0.20s) when leaving
- After 2s sustained hover, fades to 45% opacity so player can see underneath
- `CursorFadeSettings` ScriptableObject for Inspector-editable animation curves
- Cursor shadow syncs with fade alpha
- RaycastAll hits sorted by distance so nearest interactable wins

**Files:** `GlobalCursorManager.cs`, `CursorFadeSettings.cs` (NEW), `CursorWorldShadow.cs`

---

### 5. Cursor Hidden While Holding Items

Cursor fully hidden (`Cursor.visible = false`) when holding an item. The held object following the mouse IS the feedback — no grab cursor, no sponge, nothing.

**Files:** `GlobalCursorManager.cs`

---

### 6. Fridge Rejection Text

Clicking the fridge outside drink-making phase shows "I don't want anything right now." via PickupDescriptionHUD. 5-second cooldown prevents spam. Wall occlusion respected.

**Files:** `FridgeController.cs`

---

### 7. Quit-to-Menu Fade Transition

Fade screen to black + fade out music/ambience/weather over 0.8s, start menu song while still black, then load menu scene. Both `SimplePauseMenu` and `PauseMenuController` paths covered. `AudioManager.FadeOutMusic` now uses `unscaledDeltaTime`.

**Files:** `SimplePauseMenu.cs`, `PauseMenuController.cs`, `AudioManager.cs`

---

### 8. Album Art for Records

`RecordDefinition` gets an `albumArt` Texture2D field. `RecordItem.Awake` creates an instance material and applies the art to the record mesh.

**Files:** `RecordDefinition.cs`, `RecordItem.cs`

---

### 9. Cubby Capacity Enforcement

`DrawerController.HasInteriorCapacity` property counts items on the interior surface. `ObjectGrabber` checks capacity before placement — shows "No room in here." when full.

**Files:** `DrawerController.cs`, `ObjectGrabber.cs`

---

### 10. Misc Fixes

- `DateSessionManager`: unsubscribe `OnReaction` before destroying date character (event leak)
- `HorrorCameraManager`: cache `FindObjectsByType<CinemachineCamera>` at startup
- `CurvedWorldGrid`: silently disable self when no Renderer (was logging warning every play)
- Debug.Log spam: wrapped 13 info-level logs with `#if UNITY_EDITOR`
- `CursorWorldShadow`: shadow scales with camera distance instead of shrinking when zoomed out
- Eye accuracy: items placed on non-cubby surfaces explicitly cleared to `IsPrivate = false`

---

### Commits (22)

| Hash | Description |
|------|-------------|
| `3840ce4` | fix audio loss after flower trimming + bug/perf/polish pass |
| `5fcd66a` | add fade-to-black + music crossfade on quit to main menu |
| `b48bb5f` | fridge rejection text |
| `30965d8` | fix compile error in fridge reject block |
| `24e186b` | context cursors fade to half-transparent after 2s hover |
| `a92a51b` | silence CurvedWorldGrid warning |
| `d8bab10` | smooth cursor fade + shadow sync |
| `bb77c8a` | fix cursor shadow scaling |
| `5020026` | add visibility eye indicator |
| `5cf240d` | cursor fade: pre-bake alpha bank |
| `ae0a492`–`b307b65` | cursor texture sizing fixes |
| `ca42fba` | fix context cursor picking — sort RaycastAll by distance |
| `1e220bf` | add album art support to records |
| `69b9b95` | fix visibility eye auto-spawn |
| `7289081` | drag-to-pour mechanic |
| `4f19688` | fix eyes — pass placed object in event |
| `85839db` | fix eye spam + make closed eye visible |
| `45a997d` | hide cursor while holding + filter non-reactable eyes |
| `6a58315` | fix eye accuracy + enforce cubby capacity |
| `6a1860e` | flash visibility eyes at exploration start |
| `63abaaf` | Inspector animation curves for eye + cursor fades |

---

## 2026-02-25 — Scene Sync, Transition Fix, DrawerController Setup

### Session Summary

Rolled back to commit 07b8097 (ObjectGrabber click-to-open/close DrawerController cubby doors) to recover stable state. Fixed a white screen hang during the name entry → newspaper transition, and resolved DrawerController click detection on new furniture.

---

### 1. DayPhaseManager Morning Transition Fix

**Problem:** After confirming name in NameEntryScreen, the screen stayed solid white and never faded to reveal the newspaper.

**Root cause:** `DayPhaseManager._currentPhase` is a `[SerializeField]` field defaulting to `Evening`. The scene file had it saved as `Morning` from a previous play session. When `SetPhase(DayPhase.Morning)` was called, the early-return guard (`if (_currentPhase == phase) return;`) skipped the entire `MorningTransition` coroutine, including the `ScreenFade.FadeIn()` call.

**Diagnosis:** Added FLAG debug logs at 8 points through the chain (NameEntryScreen → DayManager → DayPhaseManager → ScreenFade). FLAG 5a revealed `_currentPhase` was already `Morning` on entry.

**Fix:** Reset `_currentPhase` to `Evening` in the scene Inspector and saved.

**Files:** `DayPhaseManager.cs` (debug flags added then removed), `apartment.unity` (scene resaved)

---

### 2. DrawerController Click Detection

**Problem:** DrawerController on cabinet doors didn't respond to clicks despite being configured with 90-degree open angle.

**Root cause:** ObjectGrabber's click raycast (line 212) uses `placeableLayer` mask. The cabinet door was on `Default` layer, invisible to the raycast.

**Fix:** Set the door GameObject's Layer to `Placeables` in the Inspector.

---

### 3. Scene & Editor State Sync

Synced editor state after rollback: updated FBX models, scene layout, area SOs (removed unused BookNook/CozyCorner/WateringNook areas), added missing .meta files for BookItem, RecordItem, RecordSlot, and book definition SOs.

---

### Files Changed (Summary)

| File | Change |
|------|--------|
| `DayPhaseManager.cs` | Debug flags added and removed (code unchanged from 07b8097) |
| `apartment.unity` | Scene resaved with _currentPhase=Evening, layout updates |
| `PlaceableLayout.json` | Updated positions |
| Area SOs | Removed BookNook, CozyCorner, WateringNook; updated Kitchen/LivingRoom/Entrance |
| Book SO .metas | Added missing meta files |
| FBX files | Synced model state |

---

## 2026-02-21 — Performance Optimization, Text Theme, Accessibility Suite

### Session Summary

Three-part session: (1) performance audit and optimization pass eliminating hot-path `FindObjectsByType` calls and material leaks, (2) centralized text theme system for global font/color control, (3) full accessibility & settings suite with 15 settings across 5 categories.

---

### 1. Accessibility & Settings Suite

Expanded `AccessibilitySettings` from colorblind-only into a full 15-setting hub:
- **Visual**: Colorblind mode (4 palettes), High Contrast, Text Scale (0.8-1.5)
- **Motion**: Reduce Motion (disables parallax, PSX vertex snap, text morphing, jitter), Screen Shake Scale
- **Audio**: Master + Music + SFX + Ambience + UI volume sliders, Captions toggle
- **Timing**: Timer multiplier (1x / 1.5x / 2x / unlimited)
- **Performance**: Resolution Scale, Quality Preset, PSX Effect toggle

Built `SettingsPanel` with 6 tabs (Visual/Audio/Motion/Timing/Controls/Performance), `SettingsPanelBuilder` editor tool, and wired into `SimplePauseMenu` (ESC → Settings → Back to Pause).

**New files:** `SettingsPanel.cs`, `SettingsTabButton.cs`, `CaptionDisplay.cs`, `SettingsPanelBuilder.cs`
**Modified:** `AccessibilitySettings.cs` (rewrite), `AudioManager.cs` (volume integration + caption param), `PSXRenderController.cs`, `ApartmentManager.cs`, `TMP_FocusBlur.cs`, `MotionJitter.cs`, `DayPhaseManager.cs`, `DateSessionManager.cs`, `SimplePauseMenu.cs`, `ApartmentSceneBuilder.cs`

---

### 2. Performance Optimization Pass

Audited codebase for bottlenecks. Key findings and fixes:

| Issue | Location | Fix |
|-------|----------|-----|
| `FindObjectsByType<PlaceableObject>` every Update frame | DishDropZone, DropZone | Added `PlaceableObject.All` static registry + `ObjectGrabber.HeldObject` static accessor |
| `FindObjectsByType<PlaceableObject>` in getters | TidyScorer (3 methods) | Rewrote to use `PlaceableObject.All` with indexed for-loops |
| `FindObjectsByType<PlacementSurface>` in FindNearest | PlacementSurface | Added `PlacementSurface.All` static registry |
| `FindObjectsByType<PlaceableObject>` in labels overlay | ItemLabelOverlay | Switched to `PlaceableObject.All` |
| `FindObjectsByType<PlaceableObject>` in save/load | AutoSaveController | Switched to `PlaceableObject.All` |
| `Camera.main` called every frame without caching | ApartmentManager, ItemLabelOverlay | Added `_cachedMainCamera`/`_cachedCamera` fields |
| Material leak in drink delivery | CoffeeTableDelivery | Track `_drinkMat`, destroy in `ClearDrink()` |

---

### 3. Centralized Text Theme System

Created `IrisTextTheme` ScriptableObject for one-place font/color/spacing control. Place in `Assets/Resources/IrisTextTheme`. Controls:
- Primary font + header font
- Body/header/subtitle/accent colors
- Global and header size multipliers
- Character and line spacing

Expanded `AccessibleText` to support TextRole (Body/Header/Subtitle/Accent), font overrides, and color application from theme. Created `IrisTextThemeApplier` component that auto-adds `AccessibleText` to all TMP_Text in scene at startup.

**New files:** `IrisTextTheme.cs`, `IrisTextThemeApplier.cs`
**Modified:** `AccessibleText.cs` (expanded with theme support), `CaptionDisplay.cs`, `ItemLabelOverlay.cs`, `ApartmentSceneBuilder.cs`

---

### Files Changed (Summary)

| File | Change |
|------|--------|
| `AccessibilitySettings.cs` | Full rewrite — 15 settings, 5 categories, PlayerPrefs, events |
| `AudioManager.cs` | Volume integration, caption param |
| `PSXRenderController.cs` | ReduceMotion + PSXEnabled checks |
| `ApartmentManager.cs` | Parallax guard, Camera.main cache |
| `TMP_FocusBlur.cs` | ReduceMotion guard |
| `MotionJitter.cs` | ReduceMotion guard |
| `DayPhaseManager.cs` | Timer multiplier |
| `DateSessionManager.cs` | Timer multiplier |
| `SimplePauseMenu.cs` | Settings panel integration |
| `PlaceableObject.cs` | Static registry (s_all) |
| `PlacementSurface.cs` | Static registry (s_all) |
| `TidyScorer.cs` | Use PlaceableObject.All |
| `DishDropZone.cs` | Use ObjectGrabber.HeldObject |
| `DropZone.cs` | Use ObjectGrabber.HeldObject, remove FindHeldPlaceable |
| `ObjectGrabber.cs` | HeldObject static property |
| `ItemLabelOverlay.cs` | PlaceableObject.All + Camera.main cache |
| `AutoSaveController.cs` | PlaceableObject.All |
| `CoffeeTableDelivery.cs` | Material leak fix |
| `SettingsPanel.cs` | NEW — Tabbed settings UI |
| `SettingsTabButton.cs` | NEW — Tab button helper |
| `CaptionDisplay.cs` | NEW — Audio caption overlay |
| `SettingsPanelBuilder.cs` | NEW — Editor panel builder |
| `IrisTextTheme.cs` | NEW — Centralized text theme SO |
| `IrisTextThemeApplier.cs` | NEW — Auto-applies theme to all text |
| `AccessibleText.cs` | Expanded with theme roles + font/color |
| `ApartmentSceneBuilder.cs` | Settings panel + caption + theme applier |

---

## 2026-02-19 — Audio Hookups & SFX Infrastructure

### Session Summary

Wired audio across four core systems: MoodMachine ambience/weather, ObjectGrabber pickup/place, ApartmentManager area transitions, and DayPhaseManager phase-specific ambience. All hooks are null-guarded and work silently with no clips assigned — activate when clips are added in Inspector.

---

### 1. MoodMachine — Ambience & Weather Audio Channels

Added `_ambienceClip` and `_weatherClip` fields to `MoodMachine`. On Start, both loops begin playing at volume 0 via `AudioManager.PlaySFX`. `ApplyMood()` now reads `ambienceVolume` and `weatherVolume` AnimationCurves from `MoodMachineProfile` and drives loop volume based on current mood value. `OnDestroy` stops both loops.

- New `MoodMachineProfile` fields: `ambienceVolume` and `weatherVolume` AnimationCurves
- Volume driven continuously by mood lerp, not triggered on/off

**Files:** `MoodMachine.cs`, `MoodMachineProfile.cs`

---

### 2. ObjectGrabber — Pickup & Place SFX

Added `_pickupSFX` and `_placeSFX` AudioClip fields. `PlaySFX` calls fire on grab and on successful placement. Null-guarded on both the clip and `AudioManager.Instance`.

**Files:** `ObjectGrabber.cs`

---

### 3. ApartmentManager — Area Transition SFX

Added `_areaTransitionSFX` field. Plays via `AudioManager.PlaySFX` inside `CycleArea()` when the player navigates between apartment areas.

**Files:** `ApartmentManager.cs`

---

### 4. DayPhaseManager — Phase-Specific Ambience

Added `_morningAmbienceClip` and `_explorationAmbienceClip` fields. Ambience swaps when transitioning between Morning and Exploration phases. Each clip is played through `AudioManager` and the previous one stops on transition.

**Files:** `DayPhaseManager.cs`

---

### Follow-Up Work Identified

| Task | Notes |
|------|-------|
| Per-stain sparkle VFX + completion SFX | Stain quad should deactivate or sparkle after cleaning |
| Already-clean stain gating | Clean stain spots should not trigger sponge interaction |
| Tighten tidiness scoring thresholds | Currently too lenient at 35% weight for stains |
| Visual indicator for public vs private items | Items left out for date vs stored in drawers |
| Polish highlight shaders | Tone down bright green/red placement shadow, warmer rim colors |
| Complete SFX/music asset list | ~35-40 assets needed across all systems |

---

### Commits

| Hash | Description |
|------|-------------|
| `d9f37df` | MoodMachine ambience/weather audio channels |
| `a75627f` | ObjectGrabber pickup/place SFX |
| `8f90293` | ApartmentManager area transition SFX |
| `ffa6c84` | DayPhaseManager phase-specific ambience |

---

### Files Changed (Summary)

| File | Change |
|------|--------|
| `MoodMachine.cs` | Ambience/weather clip fields, volume from profile curves, OnDestroy cleanup |
| `MoodMachineProfile.cs` | Added ambienceVolume, weatherVolume AnimationCurves |
| `ObjectGrabber.cs` | Pickup/place SFX fields with PlaySFX calls |
| `ApartmentManager.cs` | Area transition SFX field in CycleArea |
| `DayPhaseManager.cs` | Morning/exploration ambience clip fields with swap logic |

---

## 2026-02-18 — Bookcase Refactor, Cleaning Simplification & Bug Fixes

### Session Summary

Major bookcase overhaul: removed trinkets, deterministic 15-book layout on one row, expanded coffee table books to 5 with upright shelf placement, and multiple bug fixes across cleaning, perfumes, and wall placement.

---

### 1. Bookcase Refactor — Remove Trinkets

**Before:** Trinkets (small display items) on bookcase with double-click inspection, dedicated layer, separate SOs.
**After:** Trinket system removed entirely. Bookcase focuses on books, coffee table books, perfumes, and drawers.

- Deleted `TrinketVolume.cs`, `TrinketDefinition.cs`
- Removed trinket mask, hover/click logic, and Inspecting state paths from `BookInteractionManager.cs`
- Removed `TrinketNames` array, `BuildTrinketDisplaySlots()`, `"Trinkets"` layer from `BookcaseSceneBuilder.cs`

---

### 2. Deterministic 15 Books on One Row

**Before:** Random book count per row (3-6), spread across 4 rows with random colors/sizes.
**After:** Exactly 15 books, all on row 1, with varying heights/thickness but deterministic titles.

- All 15 `BookTitles` entries packed onto a single shelf row
- Rewrote `BuildBookSpineTitle()` using PPM (2000 pixels-per-meter) technique — canvas `sizeDelta = worldSize * PPM`, `localScale = 1/PPM` for crisp text on narrow spine faces
- Auto-sizing TMP text fills the spine proportionally

**Files:** `BookcaseSceneBuilder.cs`

---

### 3. Fix Pages Rendering Behind Book

**Problem:** When opening a book to read, the world-space pages canvas was occluded by the opaque book mesh.

**Fix:** In `BookVolume.EnterReading()`, disable `MeshRenderer` and hide `SpineTitle` child. Re-enable both in `PutBackRoutine()`.

**Files:** `BookVolume.cs`

---

### 4. Coffee Table Books — Upright on Shelf, 5 Books

**Before:** 2 small flat coffee table books stacked on shelf.
**After:** 5 upright coffee table books on row 2 with varying sizes. Click to toggle between bookcase (upright, side-by-side) and coffee table (flat, stacked). One book starts on the coffee table.

- Varying sizes per book: different thicknesses (0.035-0.06), heights, depths
- `RecalculateCoffeeTableStack()` — only repositions books on the coffee table; bookcase books stay at saved positions
- Hover: slides forward on shelf, up on table
- Spine titles on coffee table books too
- **Static field domain reload fix**: replaced `static BookcaseStackBase/CoffeeTableStackBase` (wiped on domain reload) with per-instance serialized `coffeeTableBase`/`coffeeTableRotation` fields

**Files:** `CoffeeTableBook.cs`, `BookcaseSceneBuilder.cs`, `ApartmentSceneBuilder.cs`

---

### 5. ReactableTag Cleanup

**Before:** Station-level ReactableTag on bookcase group, individual ReactableTags toggled on coffee table books when moved.
**After:** Only coffee table books get ReactableTags (always active/public). No station-level tag. Drawers remain private via DrawerController privacy toggle.

**Files:** `ApartmentSceneBuilder.cs`, `CoffeeTableBook.cs`

---

### 6. Cleaning — Sponge Only

**Before:** Cleaning had sponge + spray tools with Tab toggle, stubbornness gating (some stains needed spray first).
**After:** Sponge only. Sponge is always 100% effective. Spray mechanic removed entirely (may return later for harder stains).

- Removed: `Tool` enum, `_sprayVisual`, `SelectSponge()`/`SelectSpray()`, Tab toggle from `CleaningManager.cs`
- Removed stubbornness gating from `CleanableSurface.Wipe()` — sponge now clears stains directly
- Simplified `CleaningHUD.cs` — removed tool name label and tool buttons
- Updated `CleaningSceneBuilder.cs` and `ApartmentSceneBuilder.cs` — removed spray visual, tool buttons, spray wiring

**Files:** `CleaningManager.cs`, `CleanableSurface.cs`, `CleaningHUD.cs`, `CleaningSceneBuilder.cs`, `ApartmentSceneBuilder.cs`

---

### 7. Perfume Fixes — Lower Shelf + Re-Spraying

**Problem 1:** Perfumes were one-shot locked — `SprayOnce()` had a `if (SprayComplete) return;` guard.
**Problem 2:** Perfumes on top shelf (row 4) were hard to notice.

**Fix:** Removed one-shot guard. `SprayOnce()` now deactivates all other perfumes first (via `Deactivate()` method), then activates the sprayed one. Allows switching between perfumes freely. Moved perfumes from row 4 to row 3.

**Files:** `PerfumeBottle.cs`, `BookcaseSceneBuilder.cs`

---

### 8. Wall Pictures — Wrong Side Fix

**Problem:** Pictures picked up and re-placed on walls snapped to the wrong side of the wall surface.

**Root cause:** `PlacementSurface.ProjectOntoSurface()` always projected to `center[n] + extents[n]` (the front face), regardless of which side the player was interacting from. For walls whose `transform.forward` didn't face the room, pictures ended up on the outside.

**Fix:** For vertical (wall) surfaces, `ProjectOntoSurface()`, `SnapToGrid()`, and `ClampToSurface()` now detect which face the point is nearest to and project to that face, flipping the surface normal accordingly. Horizontal surfaces (tables/shelves) still always use the top face.

**Files:** `PlacementSurface.cs`

---

### Bookcase Layout (After Refactor)

| Row | Contents |
|-----|----------|
| 0 | Empty |
| 1 | 15 normal books (upright, varying heights) |
| 2 | 5 coffee table books (upright, varying sizes) |
| 3 | 3 perfume bottles |
| 4 | Empty |

---

### Files Changed (Summary)

| File | Change |
|------|--------|
| `TrinketVolume.cs` | DELETED |
| `TrinketDefinition.cs` | DELETED |
| `BookInteractionManager.cs` | Removed trinket logic |
| `BookVolume.cs` | Hide mesh/spine when reading |
| `CoffeeTableBook.cs` | Upright shelf, stacking, serialized fields |
| `CoffeeTableBookDefinition.cs` | Widened thickness range |
| `PerfumeBottle.cs` | Re-spraying, deactivate others |
| `BookcaseSceneBuilder.cs` | 15 books on row 1, 5 coffee books row 2, perfumes row 3, PPM spine titles |
| `ApartmentSceneBuilder.cs` | Coffee table target wiring, ReactableTag cleanup, cleaning simplification |
| `CleaningManager.cs` | Sponge only, removed spray/tool switching |
| `CleanableSurface.cs` | Removed stubbornness gating |
| `CleaningHUD.cs` | Removed tool UI |
| `CleaningSceneBuilder.cs` | Removed spray wiring |
| `PlacementSurface.cs` | Two-sided wall projection |

---

## 2026-02-14 — Apartment Polish Batch + Camera Preset System

### Session Summary

Major polish pass on the apartment scene and a full camera preset system for visual direction exploration.

---

### 1. Perfume — One-Click Puff

**Before:** Pick up perfume → hold LMB → wait 0.5s → release → escape. Too many steps.
**After:** Single click on perfume bottle → big particle burst → mood set → done. Bottle stays on shelf.

- Rewrote `PerfumeBottle.cs` — new `SprayOnce()` method replaces the multi-step pick-up/hold/spray flow
- Removed `HoldingPerfume` and `Spraying` states from `BookInteractionManager.cs`
- Click perfume → spray → stay in Browsing state

**Files:** `PerfumeBottle.cs`, `BookInteractionManager.cs`

---

### 2. Picture Position Drift Fix

**Problem:** Wall placeables shift slightly on every scene rebuild because `ApplyCrookedOffset()` applied a new random rotation before `RestoreLayout()` overwrote it.

**Fix:** Skip `ApplyCrookedOffset()` when `PlaceableLayout.json` already exists. Only apply crook on first-ever build.

**Files:** `ApartmentSceneBuilder.cs` (`CreateWallPlaceable`)

---

### 3. UI — Spread to Edges

**Problem:** All HUD elements anchored around screen center, causing overlap.

**Fix:** Repositioned apartment browse UI:
- Area name panel → top-center (anchor 0.5, 1)
- Browse hints → bottom-center (anchor 0.5, 0)
- Nav left arrow → left edge (anchor 0, 0.5)
- Nav right arrow → right edge (anchor 1, 0.5)

**Files:** `ApartmentSceneBuilder.cs` (`CreateUIPanel`, `CreateNavButton`)

---

### 4. Prep Timer "Watch" Panel

**Problem:** `DayPhaseManager` had `_prepTimerText` and `_prepTimerPanel` fields but the scene builder never created UI for them.

**Fix:** Built a small panel (top-right corner, 180x50) with clock icon + MM:SS countdown text. Wired to DayPhaseManager's serialized fields. Auto-shows/hides when prep starts/ends.

**Files:** `ApartmentSceneBuilder.cs` (`BuildDayPhaseManager`)

---

### 5. Hover Highlight (Rim Light on Mouse-Over)

**Before:** `InteractableHighlight` was always-on (static rim light overlay on every interactable).
**After:** Highlight starts OFF. Appears only when the mouse hovers over an interactable object.

- Rewrote `InteractableHighlight.cs` — caches base and highlighted material arrays, `SetHighlighted(bool)` swaps them
- Added hover raycast in `ApartmentManager.Update()` — tracks `_hoveredHighlight`, calls `SetHighlighted(true/false)` on enter/exit

**Files:** `InteractableHighlight.cs`, `ApartmentManager.cs`

---

### 6. NavMesh Crash Fix

**Problem:** `SetDestination can only be called on an active agent placed on NavMesh` when date character arrives.

**Fix:** In `DateCharacterController.Initialize()`:
1. Disable agent, teleport, re-enable
2. `NavMesh.SamplePosition()` to find nearest valid point
3. `NavMeshAgent.Warp()` before any `SetDestination` call
4. All `SetDestination` calls guarded with `_agent.isOnNavMesh`

**Files:** `DateCharacterController.cs`

---

### 7. Camera Preset System (Full Feature)

Built a complete camera preset comparison system for exploring different visual directions per apartment area.

#### 7a. Core Preset Infrastructure
- `CameraPresetDefinition` ScriptableObject (namespace `Iris.Apartment`) with per-area configs
- `CameraTestController` — applies presets, smooth lerp transitions between areas
- Keyboard shortcuts: `1`/`2`/`3` to apply presets, backtick to clear
- UI buttons (bottom-left) for mouse access
- Scene builder creates 3 default presets: V1 High Angle, V2 Low & Wide, V3 Isometric Ortho

#### 7b. Mouse Parallax in Preset Mode
- Presets feed position through `ApartmentManager.SetPresetBase()` so mouse parallax works on top of preset cameras
- `ApartmentManager` applies parallax offset to base position without feedback loop

#### 7c. SO Preservation on Rebuild
- `CreateAreaDef()` and `CreateCameraPreset()` only write default values on first creation (`isNew`)
- Subsequent rebuilds preserve user edits to SO fields

#### 7d. Full Cinemachine LensSettings Per Area
- Replaced `float fovOrOrthoSize` with `LensSettings lens` on `AreaCameraConfig`
- Full control: FOV, near/far clip, dutch angle, ortho size, mode override
- Physical camera support: aperture, focus distance, ISO, shutter speed, sensor size, anamorphism, barrel clipping
- All properties lerp smoothly during transitions

#### 7e. Editor Gizmos + Capture Button
- `CameraPresetDefinitionEditor.cs` — custom editor for `CameraPresetDefinition` SOs
- Draws frustum wireframes in Scene View for every area config (yellow = perspective, cyan = ortho)
- Sphere + label at each camera position
- "Capture Scene View → [Area]" button per area — grabs position, rotation, and full lens settings from Scene View camera

#### 7f. VolumeProfile + Light Overrides Per Area
- Each `AreaCameraConfig` has a `VolumeProfile` reference for per-camera post-processing (color grading, bloom, vignette, DoF)
- Light intensity multiplier and color tint fields — applied on top of MoodMachine's base values
- `CameraTestController` captures baseline light values on Start, restores on clear
- `PresetVolume` (global Volume, priority 10) created by scene builder, profile swapped instantly on preset change

**Architecture (two-layer lighting):**
```
Player actions → MoodMachine → base light / ambient / fog / rain
                                    ↓
Camera preset  → VolumeProfile  → color grade, bloom, DoF, vignette
               → light multipliers → intensity / color tint on top of mood
```

**Files:**
- `CameraPresetDefinition.cs` — SO + AreaCameraConfig struct
- `CameraTestController.cs` — runtime preset application
- `CameraPresetDefinitionEditor.cs` — editor gizmos + capture
- `ApartmentSceneBuilder.cs` — preset SO creation, Volume + light wiring
- `ApartmentManager.cs` — preset base + parallax integration

---

### 8. Namespace Fix

**Problem:** `CameraPresetDefinition` was in global namespace, causing SO gear icon issues.
**Attempted fix:** Wrapped in `namespace Iris` — but this conflicted with existing `Iris.Camera` namespace, making `Camera` ambiguous across the whole project.
**Final fix:** Used `namespace Iris.Apartment` — no conflict with `Iris.Camera`. Consumers use `using Iris.Apartment;`.

---

### Files Changed (Summary)

| File | Change |
|------|--------|
| `PerfumeBottle.cs` | One-click spray, removed hold states |
| `BookInteractionManager.cs` | Removed HoldingPerfume/Spraying states |
| `InteractableHighlight.cs` | Toggle-based highlight (off by default) |
| `ApartmentManager.cs` | Hover raycast, preset base + parallax |
| `CameraPresetDefinition.cs` | NEW — SO with LensSettings + VolumeProfile + light overrides |
| `CameraTestController.cs` | NEW — preset application with keyboard shortcuts |
| `CameraPresetDefinitionEditor.cs` | NEW — editor gizmos + scene capture |
| `ApartmentSceneBuilder.cs` | UI layout, drift fix, prep timer, Volume wiring |
| `DateCharacterController.cs` | NavMesh warp fix |
