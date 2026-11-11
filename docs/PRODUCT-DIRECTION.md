# Cozy living workshop

## Current gallery

The crystal world tree is the only gallery presenter ([GALLERY-WORLDS](GALLERY-WORLDS.md) describes what ships). Standing decisions from the iteration log:

- Every world is one normalized crystal sphere that keeps its identifying features (environment, weather, behavior, objects); one shared glass shell, genuine alpha, no inner sphere. Occlusion keeps rear worlds from showing through.
- The top empty place invites creation and is never swappable. A drop onto an occupied place swaps the two worlds; other places stay fixed; only the swap pair stays clear while held.
- All product interaction is 3D; only Settings is a 2D panel. No new labels, furniture or collection trees. Layout tuning lives in a Unity ScriptableObject (GalleryLayoutProfile); effective state is native Flecs.
- Faded or hidden previews never intercept input. Exit is a gentle elastic edge; return keeps the player's saved world and camera context. Archived worlds stay stored but are not shown in ordinary gallery space.
- Use Feel for interaction feedback where it fits. Respect reduced motion; idle drift pauses while pointed at, manipulated or inspected.
- Archive/bin UI, screen-glass controls, crossing distortion, three collection trees, sharing and networking are future work, not implemented. Permanent deletion needs its own deliberate design.
- Entry is a continuous handoff: keep revision, camera pose and target, choose a target once per visit, keep the prior view recoverable, never merge worlds on drop. Resuming the current world restores its camera and selection.

Full iteration log, including superseded furniture/table/drawer sketches: [history/PRODUCT-DIRECTION-gallery-log-2026-11.md](history/PRODUCT-DIRECTION-gallery-log-2026-11.md).

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
