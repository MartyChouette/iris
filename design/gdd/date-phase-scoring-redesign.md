# Date Phase Scoring Redesign

**Status**: Draft
**Depends on**: DateSessionManager, EntranceJudgmentSequence, DateInspectSystem, AffectionBar, ReactableTag

---

## Overview

Redesign the date scoring presentation so each phase has a deliberate, player-felt reveal
of points, multipliers, and flower growth. Phase 1 judgments play one-at-a-time with
flower feedback per beat. Phase 2 makes the drink tasting a dramatic centerpiece moment.
Phase 3 switches from automatic room scanning to player-driven item clicking. All phases
end with a "Continue" button so the player controls pacing.

---

## Player Fantasy

Every judgment feels like a mini-verdict — the flower is the emotional barometer you're
watching while the date reacts. You feel the tension of each reveal, the satisfaction of
a good pick, and the dread of a bad one. In Phase 3, you're a curator showing off your
apartment, choosing what to highlight and in what order.

---

## Detailed Rules

### Phase 1 — Impressions (Sequential Reveal)

**Current behavior**: `EntranceJudgmentSequence.RunJudgments()` already evaluates music,
perfume, outfit, and cleanliness one-at-a-time with 3.2s gaps. Each fires
`ApplyReaction()` which updates the flower via `OnAffectionChanged`.

**Changes needed**:

1. **Flower beat per judgment** — After each judgment's reaction bubble appears, hold
   for a beat (~0.8s) so the player can watch the flower pulse (Like) or wilt (Dislike).
   The flower already animates on `ApplyReaction`, but the current 3.2s inter-judgment
   gap doesn't draw the player's eye to it. Add a brief camera nudge or UI flash on the
   flower area to direct attention.

2. **Multiplier popups on flower** — `AffectionBar.ShowPopup()` already fires per
   judgment. Currently shows `"Your Music ♥"` etc. Add the multiplier suffix when
   applicable (e.g. `"Your Music ♥ ×2"`). `EntranceJudgmentSequence.ShowJudgmentJuice()`
   needs to pass the multiplier from `GetTagEffectMultiplier` if the judgment maps to a
   ReactableTag.

3. **No auto-advance** — After all 4 judgments complete, show the "Continue" button
   instead of immediately starting Phase 2 transition. Player clicks when ready.

**What stays the same**: Judgment order (music → perfume → outfit → cleanliness),
evaluation logic, personality pre-comments, SFX, reaction particles.

### Phase 2 — Drinks (Big Moment)

**Current behavior**: `ReceiveDrink(recipe, score)` calls `ApplyReaction()` once and
immediately starts the Phase 3 transition coroutine.

**Changes needed**:

1. **Tasting beat** — After the player delivers the drink, insert a dramatic pause
   sequence:
   - Date character picks up drink (~0.5s)
   - Suspenseful hold / sip beat (~1.5s) — could be a thinking-face reaction bubble
   - Verdict reaction: big labeled bubble (`"Your Drink ♥"` or `"Your Drink ☹"`) with
     particles + SFX
   - Flower pulse/wilt with popup showing drink score contribution
   - If multiplier applies, world-space multiplier popup at NPC

2. **Drink score granularity** — Currently the drink score maps to a single
   Like/Neutral/Dislike via `ReactionEvaluator.EvaluateDrink()`. Consider showing the
   raw score as flavor text on the popup (e.g. `"Great mix! +8 ♥"`) to make the drink
   crafting feel more consequential. *(Tuning knob — can revert to simple reaction.)*

3. **No auto-advance** — After the drink verdict plays out, show "Continue" button.

**What stays the same**: `DrinkPourManager` flow, `ReactionEvaluator.EvaluateDrink()`
logic, pour mechanics, bottle handling.

### Phase 3 — Warming Up (Player-Driven)

**Current behavior**: `RevealAllReactions()` auto-scans all active ReactableTags, sorts
by multiplier, and plays them as a wave with 0.6s stagger. Player is passive.

**Changes needed**:

1. **Player clicks items** — Use the existing `DateInspectSystem` (already handles
   click → wiggle → reaction bubble → particles → affection change). During Phase 3,
   DateInspectSystem is the primary interaction. Each click on a ReactableTag item:
   - Wiggle animation on the item (already exists: squash-stretch, 0.4s)
   - Date reacts with labeled bubble + icon (already exists)
   - Points + multiplier popup on flower (already exists)
   - World-space multiplier popup if applicable (need to add — currently only in
     `RevealAllReactions`)
   - Flower grows/shrinks (already exists via `ApplyReaction`)

2. **Multiplier popup on inspect** — `DateInspectSystem.TryInspect()` currently does
   NOT spawn the world-space `SpawnMultiplierPopup()`. Add it when the inspected tag
   has a multiplier > 1.

3. **"Continue" button visible throughout Phase 3** — Player clicks it when they've
   shown everything they want.

4. **Remaining items sweep** — When the player clicks "Continue", any active
   non-private ReactableTags that were NOT inspected get the existing wave treatment
   from `RevealAllReactions()` — particles, highlights, multiplier popups, staggered
   0.6s per item. This is the safety net so nothing is missed. After the sweep, proceed
   to date ending.

5. **Disable auto-scan** — `RevealAllReactions()` is no longer called at the start of
   Phase 3. It only runs for the un-inspected remainder when "Continue" is clicked.

**What stays the same**: `ReactableTag` system, `DateInspectSystem` hover tooltips,
`ReactionEvaluator` logic, multiplier calculation, highlight system.

### Continue Button (All Phases)

A simple UI button in screen space, bottom-center or bottom-right.

- **Appearance**: Text label `"Continue →"` on a minimal panel. Playtest-quality — no
  final art needed.
- **Visibility**: Hidden during phase transitions and active sequences (entrance
  judgments playing, drink tasting beat). Shown after the phase's scoring is complete.
- **Phase 1**: Shown after all 4 entrance judgments finish.
- **Phase 2**: Shown after the drink verdict animation completes.
- **Phase 3**: Shown immediately when Phase 3 starts (player controls the pace).
- **On click**: Triggers the phase transition. Phase 3 also runs the remaining-items
  sweep before transitioning.
- **Disabled during sweep**: Button hides once clicked; re-enabled is not needed since
  the sweep leads to date ending.

---

## Formulas

No new formulas. All scoring math stays the same:

- **Affection delta** = `reactionValue × multiplier × moodModifier`
  - `reactionValue`: Like = +5, Neutral = +0.5, Dislike = -4 (from DateSessionManager)
  - `multiplier`: 1×–5× from `GetTagEffectMultiplier(tag)`
  - `moodModifier`: 1.5× match, 0.5× mismatch (from DateSessionManager config)
- **Fail thresholds**: Arrival < 25, Drinks < 20, Reveal < 30, Bail-out < 10
- **Flower threshold**: >= 90 for flower gift

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| Player clicks "Continue" in Phase 3 without inspecting anything | Full wave sweep plays for all items (identical to current auto-scan behavior) |
| Player inspects every item before clicking "Continue" | Sweep finds nothing to reveal, proceeds directly to date ending |
| Bail-out threshold hit mid-Phase 1 judgment | Existing `CheckBailOut()` fires `FailDate()` immediately, overrides the sequence |
| Player delivers drink before fridge bottles are grabbed | Not possible — drink glass requires ingredients to be poured first |
| No active ReactableTags in Phase 3 | "Continue" button still shown; sweep is empty; proceed to ending |
| Player clicks "Continue" during sweep animation | Button is hidden once clicked; sweep plays to completion |
| DateInspectSystem click on an item during Phase 1 or 2 | Already blocked — `DateInspectSystem.TryInspect()` only processes during BackgroundJudging and Reveal phases. Need to restrict to Reveal only, OR allow in all phases (design choice — recommend Reveal only for now). |

---

## Dependencies

| System | Role | Changes needed |
|---|---|---|
| `DateSessionManager` | Orchestrator | Add continue-button logic, modify phase transition triggers, add remaining-items sweep call |
| `EntranceJudgmentSequence` | Phase 1 scoring | Add multiplier suffix to popups, signal completion for continue button |
| `DateInspectSystem` | Phase 3 clicking | Add world-space multiplier popup on inspect, restrict to Phase 3 only during dates |
| `AffectionBar` | Flower + popups | No changes needed (already works) |
| `ReactableTag` | Item tagging | No changes needed |
| `DrinkPourManager` | Drink delivery | No changes — trigger point stays the same |
| `SpawnMultiplierPopup` | World-space ×N text | Already exists in DateSessionManager, just needs to be called from DateInspectSystem path too |
| New: `PhaseContinueButton` | UI button | New MonoBehaviour — simple button that fires an event |

---

## Tuning Knobs

| Knob | Location | Default | Purpose |
|---|---|---|---|
| `_interJudgmentPause` | EntranceJudgmentSequence | 3.2s | Gap between Phase 1 judgments (increase for more drama) |
| `_drinkTastingHold` | DateSessionManager (new) | 1.5s | Suspense pause before drink verdict |
| `_sweepStagger` | DateSessionManager | 0.6s | Delay between items in the remaining-items wave |
| `_continueButtonDelay` | PhaseContinueButton (new) | 0.5s | Delay before button appears after scoring ends |
| `_phase3SweepEnabled` | DateSessionManager (new) | true | Toggle remaining-items sweep on/off |
| `_showDrinkScoreNumber` | DateSessionManager (new) | false | Show raw drink score vs simple reaction |

---

## Acceptance Criteria

- [ ] Phase 1: Each of the 4 entrance judgments shows reaction + flower pulse/wilt + popup one at a time
- [ ] Phase 1: Multiplier suffix appears on popup when item has multiplier > 1
- [ ] Phase 1: "Continue" button appears after all 4 judgments, does NOT auto-advance
- [ ] Phase 2: Drink delivery triggers a dramatic pause → reaction → flower → popup sequence
- [ ] Phase 2: "Continue" button appears after drink verdict, does NOT auto-advance
- [ ] Phase 3: Player clicks ReactableTag items to show them to the date (no auto-scan)
- [ ] Phase 3: Each click produces wiggle + reaction + flower + popup + multiplier popup
- [ ] Phase 3: "Continue" button is visible from Phase 3 start
- [ ] Phase 3: Clicking "Continue" sweeps remaining un-inspected items as a wave
- [ ] Phase 3: If all items inspected, sweep is empty and date proceeds to ending
- [ ] Bail-out threshold still works mid-phase (date fails immediately)
- [ ] Flower grows/shrinks visibly in response to each individual judgment
