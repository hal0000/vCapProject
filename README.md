# Multi-Catalog Addressables Loader for Archviz Scenes

## Problem & Constraints

We needed a **runtime loader** that can:

* pull **two archviz environments** (A/B) from the cloud,
* switch **texture quality** (512/1024/2048) across **diffuse, normal, lightmaps** and other texture packs,
* let users load the **entire scene** or **pick modules** (Furniture, Curtains, Reflections, Props),
* be **stable in VR/AR** and
* run on a **zero-budget CDN** (no CCD usage, no surprise costs).

Time was limited; polish and robustness mattered more than complex tooling.

---

## Architecture: Why Additive Scenes?

**Always-active Loader scene + Additive content scenes.**

**Why:**

* **Lighting & skybox stability.** We keep cameras, input, volumes/skybox in a tiny **Loader scene** that never changes. Environments (A/B) are loaded **additively**. This prevents “active scene” fights that often break skybox/lighting and avoids reconfiguring URP volumes each switch.
* **Iteration speed.** Loading/unloading **modules** additively means no prefab surgery and no monolithic “god scene”. It’s fast to add/remove content without touching the core scene.
* **Runtime safety.** We don’t rely on `SetActiveScene` for the environment. The **Loader stays active**, eliminating edge cases where activating the environment scene nukes post-processing or skybox bindings.
* **Task fit.** For a timed test, additive composition gives maximum feature coverage with minimum risk.

**Trade-offs:**

* You must ensure each loaded environment has local volumes/lightmaps that **don’t override globals** unexpectedly. By keeping the Loader active, we control the final frame consistently.

---

## Multi-Catalog & Quality Switching : The Model

* **Catalog per (Environment × Quality)**:
  `A_512`, `A_1024`, `A_2048`, `B_512`, `B_1024`, `B_2048`.
* **Core labels**: `A`, `B` (each is the base environment).
* **Module labels**: `Furniture`, `Curtains`, `Reflections`, `Props`.

**Why separate catalogs by quality?**

* Guarantees that **only one tier** of **diffuse + normal + lightmap** (and related maps) is in memory at a time.
* Switching quality = **unload everything -> swap catalog -> reload**. This avoids duplicate texture residency and keeps memory predictable.

**Result:** predictable RAM footprint, clean swaps, minimal coupling between tiers.

---

## Hosting Strategy: Why GitHub Releases?

**Constraint:** no CCD budget; must be public & free.

* GitHub Release **tags** act as “buckets” (`A_1024`, etc.).
* Catalog filename is **deterministic**: `catalog_A_1024.bin`, `catalog_B_2048.bin`, etc.
* At runtime we build the final URL:
  `.../releases/download/<TAG>/catalog_<CORE>_<SIZE>.bin`
* We attach **all bundles** referenced by that catalog to the **same tag**.

**Why this works well:**

* Zero infra cost, permanent links, versioned by tag.
* Deterministic URLs = no directory listing needed (GitHub doesn’t expose it).
* Collision-free: each tag isolates a build; identical bundle names across tiers never clash.

**Trade-offs:**

* Manual upload and you must be precise with filenames. This is acceptable for a test and reproducible for reviews.

---

## Runtime Flow: Why This UX?

1. **Choose Environment (Archviz 1/Archviz 2)** -> **Popup: Full vs Partial**
2. **Preflight**: estimate size (`GetDownloadSizeAsync`) and **free-space check**

   * Fails early with a friendly reason (**No space**, **No internet**, **Catalog error**).
3. **Loader** opens, shows **total MB** and **throttled progress** (see optimizations).
4. **Core** loads additively; if **Full**, modules follow; if **Partial**, user toggles them later.
5. **After load**:

   * **Asset Manager** popup to **toggle modules** on/off (unload frees memory).
   * **Texture Quality buttons**: 512/1024/2048 -> triggers **safe swap** (unload -> catalog switch -> reload).

**Why preflight?**
No surprises: users see size before committing; we avoid half-downloads that fail for storage.

---

## Stability Fixes & Edge Cases Solved

* **“Core label not found” after Unload All**
  Root cause: re-using state/order after clearing.
  **Fix:** When switching catalogs (or reloading after `UnloadAll`), we **rebuild the plan** with the **core label first** every time, then optional labels. We **validate** the core exists in the target catalog **before** we drop the current locator; otherwise we fail gracefully and keep the current working state.

* **Skybox/lighting glitches when a content scene is active**
  **Policy:** **Never** activate environment scenes. Loader stays active.
  Environments carry local volumes/lightmaps and render content; the Loader controls the final skybox/camera stack.

* **Progress bar “snaps” back to 50%**
  Caused by naive mapping of Download (0–0.5) vs Load (0.5–1.0).
  **Fix:** track separate `_uiPDownload` and `_uiPLoad` values and only **increase monotonically**. Progress events are **throttled**; Scene-loading phase starts **from the current value**, not a hard reset to 0.5.

* **Janky UI updates under heavy I/O**
  **Fix:** throttle progress and bytes text (`MB/Total`) by time and step, reducing main-thread churn.

---

## Performance & Memory Strategy

* **UniTask everywhere**

  * Tight integration with Addressables (`ToUniTask`), **cancellation tokens**, **frame-accurate** `ActivateAsync` awaits.
  * **Low GC** vs. classic `Task`/coroutines; easier to reason about lifetimes.

* **PrimeTween for UI**

  * Smooth, **allocation-free** tweens for loader bar and popups.
  * Better perceived performance without impacting main-thread budget.

* **UIEffect**

  * High-quality **URP-friendly** effects (blur/gradient/outline) tuned for **world-space**.
  * Visual polish without shader spaghetti; minimal maintenance.

* **Download preflight** (`GetDownloadSizeAsync`)

  * Ask once, then only download what’s needed; prevents waste on slow connections.

* **Memory hygiene**

  * On unload: call Addressables unload, then `Resources.UnloadUnusedAssets()`; optionally `GC.Collect()` after heavy ops.
  * Catalog swaps: **always unload first** to ensure **no duplicate tiers** of textures or lightmaps.

* **Handle discipline**

  * Every async handle is **checked** and **released**; locators removed before replacement; catalog handles are **safely** disposed.

---

## Error Handling & User Feedback

* **Clear status states:** Starting, Connecting, Downloading, Retrying, No Space, No Internet, Error, Completed.
* **Detailed progress:** MB/Total, phase (Downloading vs Scene Loading).
* **Graceful cancellation:** operations tied to a token; UI returns to **Idle**.

**Why this matters:** reviewers see resilience, not just a happy path demo.

---

## Trade-offs & Justifications

* **Additive instead of Single scene switches:**

  * Stability (lighting/skybox), modularity, faster iteration.
    * Requires discipline in volumes/lightmaps per environment.

* **GitHub Releases vs CCD:**

  * Zero cost, deterministic URLs, tag-based versioning.
    * Manual release packaging; strict on filenames.

* **Separate catalogs per tier:**

  * Clean memory story; no accidental mixed tiers.
    * More build artifacts (6 releases for A/B × 3 tiers).

* **Keep Loader active:**

  * Eliminates “active-scene” pitfalls; camera/input/UI never break.
    * Environment scenes must not assume they are active.

---

## Outcomes

* **Robust multi-catalog loader** with **safe quality switching** (diffuse/normal/lightmaps included).
* **Stable visuals** in VR/AR thanks to the **always-active Loader** policy.
* **Predictable memory** due to unload-before-swap discipline.
* **Good UX under real networks**: preflight sizing + throttled progress + clear errors.
* **Low-GC, responsive code** via UniTask + PrimeTween; clean UI polish via UIEffect.

---

## What I’d Do Next (If time allowed)

* **Bundle pre-validation** step (HEAD requests) to detect missing release assets before attempting a switch.
* **Persisted module sets** per catalog so re-loading after a quality swap restores the user’s previous module choices.
* **Optional delta streaming** for extremely large scenes (progressive reveal).
* **And ofcourse** a better UI/UX.

---

## Why this design fits the task

It delivers **everything the brief asks** multi-catalog browsing, **full vs partial** loading, **texture quality swapping** (including normal/lightmaps) and **careful memory management** while staying **production-minded**: resilient error handling, deterministic hosting and a scene strategy that **avoids classic XR pitfalls**. It’s small, focused and shows the kind of engineering choices you want on a real team: **simple where it can be, deliberate where it must be**.

### Bonus: Theme Support & UI SFX (implemented)

**Dynamic theme switching.** I added an opt-in, event-driven theme system so only the widgets that should recolor will do so, no hierarchy scans.

* **Data:** `ThemeSwitch` (ScriptableObject) holds palette fields (`TextColor`, `UIColor`). We ship **Light** and **Dark** assets.
* **Opt-in tagging:** Any UI object that should react gets a tiny `ThemeTag` MonoBehaviour with a **slot** (Text / UI) and a *Preserve Alpha* toggle. This avoids changing backgrounds, icons, etc., unless explicitly tagged.
* **Registry:** `ThemeRegistry` keeps a live list of active `ThemeTag`s. `OnEnable`/`OnDisable` auto-register/unregister.
* **Switcher:** `ThemeManager.ApplyTheme(ThemeDefinition theme, bool animated=true)` pushes the new colors to the registry in a single pass **O(n)**; no GC spikes. Color transitions tween via **PrimeTween**; we only lerp RGB (alpha preserved), so ongoing fade/hover animations aren’t disturbed.
* **UX hook:** A “Theme: Light/Dark” toggle/button simply calls `ThemeManager.ApplyTheme(...)`. Current theme can be persisted if desired.

**UI sound effects.** Requirements were minimal by design: a **click** and **dialog open/close**.

* **Runtime:** `SFXManager` (lightweight singleton) with one `AudioSource` and three clips: `Click`, `DialogOpen`, `DialogClose` and `NotificationPopUp`. Playback uses `PlayOneShot` (no pooling needed), routed to a `UI` mixer group for a global volume/mute.
* **Integration:**

  * ButtonBehaviour.OnPointerClick -> SFXManager.PlayClick().
  * Popup/panel show/hide points -> SFXManager.PlayDialogOpen() / PlayDialogClose().
  * NotificationController.Show -> SFXManager.PlayClick().

* **Why this shape:** One-shot non-spatial UI sounds keep CPU/GC near zero, are consistent across screens and don’t compete with world audio. The single manager also centralizes volume, allowing a simple settings slider to control UI SFX.

## UI Effects Implemented what & why (developer notes)

**Window Effects**

* Open/Close Animations
  * Every modal/panel uses the same animation contract: scale (pivoted) + fade with PrimeTween, ≤200 ms, easing tuned for “snappy but not jumpy.” It keeps layout stable (no reflow during tween) and gates raycastTarget so you can’t click through while animating. One path means fewer bugs and no per-panel animation scripts.

* Background Blur
  * Panels that steal focus (catalog picker, asset manager) draw over a blurred scrim. I use my own Blur shader it has some camera perspektive bugs to be honest but It’s cheap, predictable, URP-friendly. And that bug can be fixed in anytime.

**System Effects**

* Popup Notifications
  * Non-blocking toasts for “Module loaded”, “No space”, etc. They slide in + fade, auto-dismiss and are capped (ring buffer) to avoid spam. Implemented as a single prefab with PrimeTween sequences; no GameObject churn. SFX hook plays a soft tick; all routed to the UI mixer.

**Visual Effects**

* UI Blur Effect (local)
  *  Used sparingly on headers/backs of heavy panels to separate them from the environment.

* Hover Effect
  * Instant feedback on interactables. ButtonBehaviour handles hover/press states with GC-free scale/color tweens; disabled state short-circuits everything. For subtle polish, I also flip UIEffect.samplingScale to 1 on hover (cheap highlight), back to 0 on exit.

* Gradient Effects (as assets, not animation)
  * Gradients are authored once (sprite/material) and applied statically as Sliced Image in Panel's Container. No runtime gradient math.

**Animation Effects**

* UI Popup Animations
  * Used in NotificationController for showing player a good feedback

* Image Fading
  * Used for every panels show/hide animations and also changing the theme

* Spinner Animation on Edge of buttons as Shiny on Hover
  * Used for every button's hover animations.


## UI Element Effects

Button System (state + hover + press)
One ButtonBehaviour drives hover/press/disabled visuals, theme color adoption and SFX. It exposes NoAnim/NoEffect flags for heavy UIs and uses UIEffect for a lightweight hover gleam. Because it’s the same script everywhere, we get consistent timing, disabled-state gating and easy theme/SFX wiring.

Notes on how this scales:

* World-space first. All effects are designed to run on world-space canvases (VR/AR). No reliance on camera-stack trickery; each panel is self-contained.

* Performance hygiene. No hierarchy scans on theme change or effect toggles—targets opt in via ThemeTag and register once. Blur/shine components are only enabled while visible; samplingScale is flipped instead of swapping materials.

* No GC spikes. PrimeTween for all tweens, pooled toasts, no per-frame new during hover/press.

* Theme-safe. Theme switches lerp RGB only and preserve alpha when requested, so fades/hover effects aren’t overridden mid-animation.