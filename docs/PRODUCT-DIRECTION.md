# Cozy living workshop

## Current gallery presentation — 2026-11-09

The crystal tree is the only gallery presenter; the earlier flat table/drawer is retired. World exit aligns to the entry portal, rises/pulls back, reveals previews by distance and briefly marks the returned world with a distinct themed rim. GalaxyDust is the current soft 3D ring study. Keep the player's saved world/camera context and hidden inactive records. Archive/bin, screen-glass controls and crossing distortion are future work; older furniture sketches below are design history, not another runtime path to maintain.


## Current input refinement — 2026-11-08

Archive UI is disabled and its browsing/restoration design postponed; retain stored worlds. Preview size is unchanged. Keep the current Cinemachine Follow setup, using constant orbit radius and true top-down access. Hover highlighting clears smoothly on pointer exit. Faded previews do not intercept input, including previously focused ones. Fade from actual preview overlap, with matching interaction eligibility, instead of dimming every rear world indiscriminately. Exiting should feel like a gentle elastic edge and one more push, supported by optional vignette, not a long repeated wheel lock. Return through the original entry view while keeping the edited world camera for resume. Prefer Feel for interaction feedback wherever it fits.

## Archive visibility and depth refinement - 2026-11-08

Latest refinement: far-side rings and traveling light fade with their worlds. Rings should taper softly as they approach a sphere, avoiding sharp circular holes, while remaining outside its volume.

Latest preference: keep the accepted spacing/material/opacity tuning and traveling ring movement, but evaluate its final visual effect. Use a rounded 3D ring with adjustable width, rather than a flat textured band. A separate Rear world fade control makes the far side quieter while the swap pair stays clear. Use one original-style glass shell around each preview; remove the nested inner sphere. These corrections supersede the double-surface descriptions below.

Latest correction supersedes removing the rings: retain them and compare a feathered ribbon, spaced dashes, particle trail and traveling glow at sphere-center height with sphere-volume gaps. Actual sphere opacity is separately adjustable from 0 to 1, without fading the world contents; rim strength remains independent. Gentle perspective stays. The previous solid backdrop was making the spheres opaque despite lighter shading and has been replaced by genuine alpha.

Latest feedback: gentle perspective is liked; sphere opacity is too strong and rings add little. The next review removes guides by default, softens sphere fill/rims and relies on staggered placement, wider vertical gaps and shared layer rotation. Keep occlusion to prevent rear-world features showing through. Treat tier readability as open.

Do not show archived worlds or their layer in ordinary gallery space. Open storage from the minimal archive screen icon; its eventual browsing/restoration interaction remains todo. Dropping onto the icon stores without opening it. Layer guides belong between spheres at their center height, with clearance around the complete sphere volume. To improve depth, the study tries gentle perspective, slightly hazier distant colors and solid shaded inner backs so foreground worlds occlude those behind, retaining glass edges. This is a review treatment, not settled final art; a near-parallel comparison is available. No new labels, furniture or collection trees are added.

## Latest crystal gallery direction - 2026-11-08

Supersedes the earlier orbit/dial/top-slot proposals below. Worlds appear inside normalized crystal spheres, retaining identifying environment, weather, behavior and object features. The top empty invitation is reserved for creating: enter, make the first thing, then the resulting world settles onto a layer. It is never swappable. Active layers drift continuously during browsing; a held placement freezes their layout/camera. Use subtle particle bands to imply the tree, with configurable world-width ribbon/orbit alternatives. During a swap preview only the two candidates remain clear; all others fade, with no connector.

Settings returns to a minimal 2D screen icon. A matching archive icon is explicitly being tried at the user's request, alongside the broad lowest-layer storage drop target. It works even when storage is empty. Drag archived worlds back onto the active tree to restore; archive features remain static and focusing storage fades the tree without a camera jump. Cross-world preview interactions may be deferred with enclosed spheres. Material references, spacing, opacity and idle tuning now have a disconnected Unity GalleryLayoutProfile asset; the layered runtime is not yet ported. The web study remains the review surface. Three neighboring collection trees and label-free shared/discovery recognition remain open.

## Current orbit study and open collection design - 2026-11-08

Use thin orbit tracks to clarify tiers without solid circular shelves. Active layers/worlds may move very slowly while idle; pause while pointed at, manipulated, inspected or in archive focus, and honor reduced motion. A drop onto an occupied place swaps the two worlds; every other place stays fixed. Highlight both, connect them subtly and preview the destination while held. Archive membership changes still compact as needed. The archive is one lower orbit, not a literal drawer: keep cached previews static, dim the active tree when it is focused, and return without auto-zoom. A small base adjustment dial opens the only 2D product Settings.

Designer tuning belongs in a future Unity layout ScriptableObject; effective runtime state remains native Flecs. The current web study has host design controls for layer/world spacing, orbit visibility, tree fade and idle speed. Those are not player-facing layout-editor controls. The prototype removes visible in-scene labels while retaining accessibility names and external study controls.

Collection proposal to review: an empty creation place atop the personal tree; three neighboring trees for personal, shared and discoverable content, seen together on pullback with continuous camera navigation. The latest user preference is to try WITHOUT text names. Own recognizable creations, actual collaborator presence and changing unfamiliar community creations are potential cues to test, not a proven solution. No three-tree view, top-creation implementation, real sharing or networking exists yet. Do not restore the previously suggested short base labels without user direction. Preserve direct resume of the last editing context.

## Layered world-tree candidate - sketch first, 2026-11-08

Reference inspected: https://www.instagram.com/reel/C6tUJxeLmg9/ . Take its expanding segmented structure as inspiration. Candidate: a single world at the top; descending rings of individual world-sized platforms, staggered so upper layers leave viewing gaps; each layer rotates independently; camera has full orbit; archive is a single lower layer with static previews and bounded front visibility. Platform material/shape remains undecided: glass, softly opaque or no visible support are candidates, not decisions. Avoid solid stacked shelves which obscure worlds. Drag/drop remains reversible, with insertion and compaction; Flecs would own layer membership/order/rotation and camera intent, Flexalon would place cells. The user chose to see a simple sketch first; do not implement the tiers yet.

The current flat revision removes the background and world-responsive tabletop treatment, decorative rim marks and insertion circle. It retains world-sized archive previews with no automatic zoom and uses an integrated small adjustment knob for Settings. This supersedes the older responsive-finish notes below.

Design direction, not a claim that this UX/theme system is already implemented. See BACKLOG for implementation status.

## Authorized structural workbench and responsive finish - 2026-11-08

Latest user correction: every world occupies exactly one normalized cell, both on the table and in the drawer. Arrange table cells in concentric circular rows following the tabletop, compacting inner rings before adding outer rings. Preserve each world's natural orientation relative to the table; do not rotate previews to face the camera. Mock recipe width/depth still describe the original feature geometry, not occupied cells. GalleryCell.units is always one; surveyed feature bounds normalize the presentation into a bounded circular cell without changing the entered world's geometry or scale. This supersedes the earlier mixed-footprint rectangular-grid decision.

The user authorized implementation after discussing background previews and asked whether the table could take on the previewed world's appearance. Keep the furniture's recognizable shape, thick turning edge, curved drawer and cog; let the selected world subtly influence its finish, sheen and surface accents. The current disposable implementation uses palette-derived tint plus water ripple, space speckle, rain or wave accents. These are transient presentation responses, not edits to the world's authored appearance. Avoid changing table geometry or moving controls on each hover.

The structural prototype now includes natural unframed feature mocks, hidden initial placement, a front-facing camera with limited zoom-out, side-drag tabletop rotation, a small pull-handle drawer, a five-slot Flexalon carousel with static archived geometry, horizontal browse versus upward lift gestures, ghost insertion/restoration and a physical cog for Settings. A background projection prepares one disposable world context; entry promotes that same context and camera viewpoint, then approaches a target selected once. Returning pauses and retains the resident mock and its camera; switching to another releases the old private mock context. Furniture/focus and selected mock state persist in native reflected components and a local versioned arrangement document.

Entry must wait until the approached preview fills/overscans the view. The implemented orthographic route uses a cheap threshold derived from normalized projected bounds and measured camera size, with the entry decision in a separate reactive Flecs rule. Zoom does not physically move this camera into a collider. Saved table framing returns behind the handoff fade; a resumed arrangement does not automatically re-arm entry.

The test fixtures remain disposable. This does not implement real saved-world preview extraction, a reusable content/dependency catalog, arbitrary plugin pause/checkpoint support, multiplayer, general streaming or permanent deletion. The eight fixture previews are prebuilt; the drawer activates only its visible bounded window. One temporary preview plus one retained mock world share a single reusable projection texture. Production style isolation, cache revision handling, unsupported targets and broader platform/render performance still require work. General requirements below remain the design contract for that future integration.

## Background projection and preview-to-entry proposal - 2026-11-08

The user wants to focus on structure/functionality before final table visuals. Keep the permanent-deletion bin in the todo list without implementing it now. Another art mockup is optional; a disposable Unity interaction prototype can reuse the existing fixtures to test table/drawer/navigation structure without building the full content lifecycle first.

Proposed treatment for the currently empty background: when a world is deliberately previewed by pointer dwell, inspection or approach/zoom, a quiet background projection shows that world's viewpoint behind the table. Support the drawer as well, retaining static cached archive representations rather than starting archived simulations. Only the selected/settled preview should control the backdrop; fast pointer travel, a held drag or a canceled approach should not continually switch targets or load full worlds. Scope pointer/wheel input to the table, drawer carousel or entry approach so their gestures do not compete. Previewing/restoring an archived world does not itself silently activate its full simulation or merge it with another world.

Entry is a continuous handoff: retain the preview record's stable world/content revision, world-space camera pose, projection/lens/aspect, reference time/state and eligible target ID/region. Choose an object, behavior group, environmental region or suitable viewpoint once for the visit, with a fallback if it disappears. Start the entered world's camera at the matching preview viewpoint, then gently move toward/follow the chosen target. Do not reroll on each hover frame or when switching from preview to live presentation. Resuming the already-resident current world preserves its previous camera, selection and editing context; a new random entry must not replace that context.

A simplified/static preview cannot guarantee a pixel-identical live scene, especially if the world was edited or a multiplayer target moved. Validate revisions and target availability, prepare the destination, match the live camera/projection, and use a controlled blend/occlusion handoff for differences in geometry, lighting, animation time or scale. Keep the prior view/world recoverable during load/cancel/failure. Multiplayer authority cannot be paused to match a cached image. Unity can render one isolated camera to a reusable RenderTexture/backdrop; this is a possible presentation implementation, not a new 2D product panel. Use caching, bounded resolution/update cadence and scoped effects; avoid one continuously rendering full-world camera per preview. Reference: [Unity camera output to a Render Texture](https://docs.unity.com/en-us/engine/6000.0/manual/materials-and-shaders/textures/render-texture/output-to-render-texture).

The user permits using Flecs modules and additional Flecs worlds when helpful without breaking Unity integration. Start with modules/scoped instances in the existing authoritative session; assess extra isolated worlds for preview/staging only when they solve a demonstrated isolation or scheduling need. Flecs worlds are independent stores, not shared-entity namespaces: catalog IDs, source/asset sharing, state transfer and Unity/adapter ownership must explicitly bridge contexts. Extra worlds do not by themselves isolate Unity global lighting, weather, physics, audio or plugin singletons. There is no multi-world migration in this design update. Reference: [Flecs world isolation](https://www.flecs.dev/flecs/group__core__types.html).

## Requested world lifecycle and direct manipulation - 2026-11-08

The user requested these items in the todo list while continuing interaction design. They define the intended runtime and interaction direction, not completed streaming, sharing, networking or deletion functionality.

Keep one recognizable workshop object: a rotating round tabletop with a thick grippable side, a small front-facing curved drawer and an embedded settings cog. Drag the thick side sideways to turn the tabletop; the drawer/cog base and gallery camera stay front-facing. Limit zoom-out rather than shrinking the whole collection. Table dimensions follow occupied normalized preview footprints. Previews first become visible only after layout has settled; use a brief in-place fade or small emergence. Remove uniform rectangular preview holders. A preview may stand naturally, float or include organic terrain, weather, choreography or other identifying world aspects; a fish world can be represented by fish without an aquarium. The furniture shares one visual language while worlds retain their own styles. All product interactions stay 3D; only Settings is a 2D panel.

Collection actions use direct manipulation. Pull the drawer handle to open it. Bringing a held world ghost to the drawer opens it; only a release inside commits archiving, after which the tabletop grid compacts. Originals remain fixed while the ghost is held. One curved row/carousel of static world previews is visible inside the drawer. Drag the row or use the wheel over it to browse, keeping preview size readable, pooling only visible/nearby views, and remembering the browsing position. A closer inspection view can frame the contents without physically enlarging the drawer. Restore by dragging a world ghost out onto the tabletop: show insertion, then commit membership/order and compact on release; cancellation leaves it archived. Restoring a world to the table is distinct from entering it. Zoom into its preview to enter, or into the retained current world's preview to resume its camera, selection and editing context. Dropping an entire world onto another world must not silently merge their contents; cross-world object/feature reuse needs an explicit source/instance policy and an agreed spatial transfer gesture.

Flecs should maintain a queryable catalog of known accessible creations and their reusable feature definitions, stable IDs, versions, provenance and dependencies, even when their full instances are not resident. This covers user-authored objects, materials, rigs, behavior, relationships, environment, weather and other aspects, not just geometry. Shared definitions/assets and independently mutable world instances have distinct ownership. Future shared/player-created/multiplayer content uses the same model with appropriate permissions and authority; this does not authorize networking work now. Do not interpret the catalog as loading every asset/simulation, discovering unknown remote content automatically, or generating unsupported algorithms from Script.

Requested context policy:

| Context | Resident/active content |
|---|---|
| Table | Lightweight identifying feature instances and supported cross-preview interactions. Retain the current full world and its state, but hide its full Unity presentation and suspend its private single-player simulation, physics, audio and timers. Preview state is separate so gallery play does not silently mutate the saved full world. |
| Archive | Static cached world representations with virtualized/poolable carousel presentation; no archive world simulations or cross-preview reactions. Retain the hidden current world as above while browsing. |
| Inside a world | Activate that world's features and required common/shared dependencies. Suspend unrelated gallery/other-world instances and their effects. |
| Entering a different world | Validate/prepare the destination and preserve the prior world if switching fails. Save selected old-world state, release old private instances and obsolete adapter resources, retain still-needed shared definitions/dependencies, and activate the destination. |

Authored definitions must survive context-driven activation/removal of disposable instances. Unity visibility alone does not stop native systems or plugin physics/audio; the native context policy must gate both Flecs execution and presentation adapters. A live multiplayer world may need to keep simulating under session authority while this player's presentation is hidden. Local gallery navigation must not pause another player's session. Shared does not mean every world globally runs the same mutable instance or inherits another world's weather/state.

Deferred permanent deletion: a separate bin under the table, visually and gesturally distinct from the storage drawer. The user explicitly requested todo only, with no implementation now. The exact deliberate gesture and irreversible boundary remain design work. Deleting an owned world would remove its saved root and private content, then collect only dependencies unreferenced by other saved/archived worlds, reusable library entries or live sessions. Keep shared definitions outside recursively deleted private ownership hierarchies. Removing a local reference to another player's creation does not delete their original. Evicting unused runtime memory is separate from deleting saved content, so optimization does not force the player to discard creations.

Existing building blocks: independently managed Script modules, reflected schemas/queries, CapBinding/CapDependsOn, disposable adapters and selected-state saves. Upstream Flecs supplies prefabs/inheritance, relationships, component toggling and cleanup traits; Ecaty must implement the catalog, context policy, scope/dependency ownership and persistent-content collector. The current RuntimeExperience.Load recreates the native session rather than retaining common instances across world switches. Sources: [Flecs prefabs](https://www.flecs.dev/flecs/PrefabsManual.html), [component toggling and cleanup traits](https://www.flecs.dev/flecs/ComponentTraits.html). Full arbitrary VM/plugin checkpoints remain unsupported.

## Authorized gallery prototype - 2026-11-08

The first implementation now tests a single-plane Flexalon grid with fully 3D mock previews, simple drag/drop insertion, mixed cell footprints and compact repacking after changes. Normalize preview scale; shape determines occupied cells. Identifying features can include objects, environment, weather, choreography, behavior and abstract systems. The mocks are disposable; do not constrain the product to miniature scenery or object-only thumbnails. All product interaction is 3D except Settings. The gallery is meant to feel calm and manageable, without requiring a workshop room. See STATUS for actual verification and remaining limits.

Follow-up interaction decisions: during a held drag, all original previews stay fixed and only a translucent ghost follows the pointer. An outline previews the insertion; releasing commits the new order. Use gentler drag, arrangement and camera movement. Worlds the player no longer wants on display go into an archive instead of being deleted. The prototype represents this as a reversible 3D box/collection, with independent gallery and archive camera intent.

## Retained gallery idea - 2026-11-08

The user asked to retain this for later: while the player is in Living Diorama Gallery mode deciding which world to enter, randomly selected creations may occasionally try to attract the player's attention through small in-world actions. This is a gallery discovery/idle idea, not an implemented feature. Frequency, sound, eligibility and preview-simulation rules remain undecided.

Geppetto's workshop is a visual/feel reference now under discussion: a personal maker's space that can accommodate multiple worlds. The gallery's visual form is not settled. This note does not authorize runtime implementation.

## Gallery constraints and retained organizer idea - 2026-11-08

Confirmed constraints: use Flexalon for gallery layout/presentation, retaining Flecs authority over membership and relationships. Gallery worlds remain lower-resolution/simplified representations of the actual worlds; do not replace this decision with separately curated identifying scenes. Continuous zoom connects gallery, worlds, objects and details without mandatory focus selection.

Retain a very easy, directly manipulable folder-like world organizer for later design. The player should organize worlds through the gallery itself. Its visual form is undecided.

Candidate under discussion, not approved implementation: a constellation of worlds without shelves or containers, with optional construction-toy/puzzle-like arrangement. Whether and how selected aspects of neighboring worlds interact is unresolved; arbitrary world contents do not imply universal simulation compatibility. Calm means relaxed and manageable interaction, not a required room, lighting style or miniature-only product.

## Gallery grouping and preview reactions - 2026-11-08

The user likes groups gathering loosely at a distance and spreading apart on approach, and likes small interactions between compatible world previews. If this direction is adopted, prepare the preview interaction aspects on world save/exit. Flecs and optional AI should compose supported simple cross-world interactions, without assuming all arbitrary simulations interoperate.

The supplied floating-island reference illustrates clustered spatial depth, not a required shared art style, terrain base or miniature-only world format. An aquarium and a space scene must coexist while retaining their own visual styles. The user prefers exploring a 3D grid if previews can remain readable without hiding/colliding; a flat arrangement is also possible. Gallery layout, occlusion handling, preview bounds and the save/exit preparation contract remain design questions, not authorized runtime implementation.

## Feel

Small deliberate actions, immediate visible response, gentle procedural correction, reversible play, quiet sound and unhurried discovery. Keep AI waiting separate from simulation: already-created things remain alive while the next creation is prepared.

[Tiny Glade](https://store.steampowered.com/app/2198150/Tiny_Glade/) demonstrates relaxed gridless building with procedural adornment and no required goals. [Brushland](https://store.steampowered.com/app/5163450/Brushland/) (listed as coming soon when checked 2026-11-06) describes painted terrain/buildings responding to their neighbours and a growing living village. Borrow the responsive feel and delightful consequences, not their art, layouts or exact mechanics. Our distinguishing action is bringing a person's own creation to life.

## A real miniature-maker's bench

Physical workflow: choose base/scale -> block out foam/clay -> shape terrain and paths -> paint surfaces -> place vegetation/props -> add water/light -> inspect from different angles. [Woodland Scenics](https://woodlandscenics.woodlandscenics.com/show/category/TerrainAndLandscaping) is a useful source for actual terrain, water, vegetation and workshop materials.

| Real tool | Digital interaction candidate |
|---|---|
| Baseboard, tray, display case | World seed/holder; add or open an experience |
| Sculpting loop, clay tool | Reshape selected geometry/control points |
| Tweezers, hand | Place, pick up, drag and arrange |
| Brush, paint swatch | Try a theme/material palette on the selection |
| Pipette | Sample a colour, finish or style |
| Flocking shaker | Scatter plants or small details over an area |
| Turntable | Orbit and inspect the whole world |
| Drawers/sample trays | Browse saved objects/recipes through previews |
| Winding key (toy metaphor) | Give life/motion, pause or change an activity |
| Magnifier | Reveal properties/behaviours; same selection in browser inspector |

Do not show all tools at once. Start with add, shape, style and life around the selected object. Use familiar silhouettes and animated previews; no explanatory text in the normal view, but retain accessible names and optional help. Flexalon arranges the affordances; Flecs recipes describe selection, availability and effects. Unity input/presentation adapters still need implementation.

## Themes are recipes, not one forced clay look

Proposed resolution: product defaults -> world theme -> object theme -> explicit property override. A theme supplies semantic channels: palette/finish, edge softness, motion timing/exaggeration, sound/instrument palette, lighting/VFX and interaction feedback. Existing geometry/behaviour identity stays intact. A change to "paper theatre" or "polished toy" adjusts supported channels without asking AI to regenerate every object.

Adapters advertise which theme channels they support and use sensible fallbacks. Explicit user edits win; a theme can be previewed, reverted, saved and reused offline. Theme choices need compatibility/version metadata like other recipes. Physics settings must not silently change merely because visual motion becomes more exaggerated.


## The first complete workshop visit

The current approved opening shows only a floating seed against a quiet background. Clicking it reveals 3D choices for creating or reopening a world and trying examples. A world hides the seed; recalling it brings it into close view. Workshop benches, drawers and turntables remain visual inspiration for future tools. The player sees their miniature from outside, with room to orbit and inspect it.

1. **Open the seed.** It settles into a shallow diorama base. An add token appears at its edge. Picking a form from the drawer previews where it will land before the player places it.
2. **Touch the creation.** A few physical tools slide into an arc around the selected object. The sculpting loop reveals a handle, the brush previews a theme swatch, and the winding key attaches a motion recipe. The selected object stays visible between the tools.
3. **Make it respond.** Place a bell near the creation and connect a simple reaction. Ringing the bell makes the creation respond immediately. The winding key starts or pauses the behaviour. Dragging an object previews its landing position and uses the same movement rules as the simulation.
4. **Keep the world.** A small thumbnail returns to the drawer. Opening it restores the forms, placement, theme and behaviours. The full loop works with local recipes. An image or voice request is available from the selected tray or object when the player chooses AI.

Use consistent gestures on every object. Tap selects, drag moves or previews, and release applies. Hold reveals the attached recipes and a remove affordance. A quick undo makes experimentation safe. Screen-space hit areas stay comfortably sized as the 3D tokens change apparent size. The token appears on the camera-facing side so the object remains legible. Keyboard/controller paths and accessible labels are part of the same interactions.

## Visual and motion references

| Reference | What to study | Ecaty application |
|---|---|---|
| [Potion Craft](https://store.steampowered.com/app/1210320/Potion_Craft_Alchemist_Simulator/) | 2D physical silhouettes and gestures that match the tool | An identifiable brush, pipette and sculpting loop, with a short animated preview on selection |
| [Assemble with Care](https://store.steampowered.com/app/1202900/Assemble_with_Care/) | Close framing of individual objects and the rhythm of their manipulation | Zoom only enough to understand the selected object. Use soft contact, settling and a small confirmation sound |
| [Tiny Glade](https://store.steampowered.com/app/2198150/Tiny_Glade/) | Procedural details and the relationship between a stroke and the visible result | Preview changes as the tool moves. Give the miniature a quiet idle animation and keep the camera steady |
| [Brushland](https://store.steampowered.com/app/5163450/Brushland/) | Neighbouring choices can visibly change how the little world behaves | Let the existing diorama react to a placed object or recipe, such as a bell that wakes a creature |
| [Figmin XR controls](https://www.figmin.com/learn.html) | Contextual controls for placing, grabbing and resizing objects in space | Keep the tools attached to the selected object. Reuse their placement and gestures across mouse, touch and future XR |
| [Woodland Scenics](https://woodlandscenics.woodlandscenics.com/show/category/TerrainAndLandscaping) | Real scenery materials, ground layers, plants and water | A tray with readable strata, a swatch drawer, a scatter shaker and a pipette. Show texture at miniature scale |

Start with a neutral workshop and two contrasting world themes. **Paper garden** uses layered edges, matte fibre, delicate movement and rustling. **Glazed toy** uses rounded ceramic surfaces, subtle highlights, springier motion and soft chimes. Theme changes preview on the same creation. Tool silhouettes and meanings stay recognizable across themes. A wind-up key turns, the object anticipates movement and then settles. The saved thumbnail shows the resulting look. Honour reduced-motion settings and provide a quiet sound option.

## Implementation order

1. Sketch three matching shots of the same tray, first arrival, object selection and a visible reaction. Use the references above and a small number of concept images to compare material and camera choices.
2. Build one scene in Unity with orbit, tap and drag, object-anchored Flexalon tools, one existing model recipe, a theme preview, one bell reaction, undo and save/reopen. Use the existing Flecs sources and capability adapters for each edit. Verify that the loop works offline with AI disabled.
3. Attach optional photo, microphone and text actions to the same selection. Show a small local progress affordance while the model works. Give failed creation requests a useful retry path and keep the existing world usable.
4. Expand the drawers, recipes and available tools after this loop feels good. Add further adapters when a specific creative action needs one. A browser inspector can follow the selected object later.

The first milestone passes when someone can open a tray, place a creation, give it a visible reaction, change its theme and reopen the result without reading instructions or signing in.

2026-11-09: the approved first native seed-world loop is documented in GALLERY-WORLDS. Keep direct 3D placement and reversible movement; expand real capability content within owned native world scopes. The initial cube/ground are structural fixtures, not a final art direction.
