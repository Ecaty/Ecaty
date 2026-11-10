# Workshop chain and navigation

In a fresh Play session click the seed, choose the small **bell** in its 3D ring, and click the gold bell in the world. The blue character steps off the green pressure plate to investigate. The orange gate lifts and the yellow character crosses. Double-click a character to focus the camera. The bell is the trigger for the chain.

To build the sample yourself, choose the **plus** around the seed. Hold the left button over empty ground and release to open the 3D tray. Pick a miniature, then click to place it. Select a source and drag its pale port to a target. The pale line appears during that drag. Connect bell to blue character, blue character to plate, plate to gate, and gate to yellow character. Set the blue character on the plate and the bell nearby. Tap a connected port to remove its link and any links that depend on it. The characters can swap roles.

| Input | Result |
| --- | --- |
| Double left-click | Center and fit the object under the cursor, including the world base. The first click retains its ordinary action. |
| Left-drag | Orbit even when the drag starts over an object. A wire drag or an already-grabbed prop takes priority. |
| Hold left on a movable prop, then drag | Grab and move the prop. |
| Right-drag up or down | Zoom while keeping the picked point at its initial screen position. |
| Scroll the wheel | Zoom while keeping the picked point under the mouse. Moving the cursor can select a new point. |
| Press and drag the wheel | Pan the camera and its pivot at the scale of the visible world. |
| Escape | Dismiss open tools. Otherwise recall and frame the seed, or hide it and return to the overview. |

The single bottom-center button opens **Settings** for AI connection. Sound stays on. In development builds with ALINE installed, **F8** toggles the camera pivot overlay. The workshop functions offline. Future AI actions belong with the tools that use them.

Startup shows only the seed. Its 3D ring has a plus for a blank world, a folder for the local save when available, a bell for Bell & Gate and paired figures for Creature Playground. The seed hides when a world opens. Escape brings it back and frames it closely. A disk saves the current world and the sun/cloud opens world appearance. Switching worlds shows the chosen miniature and a checkmark; confirm with the checkmark or dismiss by clicking outside. The current save is one local slot.

World appearance offers paper, sun, moon and reset icons for soft, warm, twilight and original rendering. These apply normal Flecs Script capability definitions. Exposure, grading, white balance, bloom, vignette and tonemapping execute through the URP adapter. Weather controls will join the world controls after the UniStorm setup is verified.

Flecs Script owns the seed’s bob, tilt, slow rotation and activation request. Unity picks the seed, draws the baked mesh, handles hover feedback and presents the chooser. The stock seed uses one 436-triangle mesh baked with **Runtime > Workshop > Bake default MudBun seed**.

Open **Runtime > Workshop > Icon Studio**, choose an icon slot and use **Create editable sculpt** to adjust its MudBun brushes. **Bake into slot** replaces the mesh/prefab while preserving references. You can also supply any selected MudBun renderer or regular mesh. For the seed alone, **Runtime > Workshop > Bake selected model as seed** remains available. The menu exports a centered, build-ready mesh and prefab to `Assets/__Common/Theme/Resources`. The original source remains editable. A new bake replaces the seed art and keeps the asset references stable.

Flecs templates own the conditions and emit command components on the wire entities. A Unity bridge applies commands to the existing targets without transferring entity ownership to a wire. The plate checks its configured actor's distance. The gate is a visual marker and the chime uses the existing synthesizer. The 3D tray and wire tools currently cover the starter kit and desktop mouse controls.

Run **Runtime > Runtime engine > Verify workshop chain** outside Play Mode. It checks the cursor-pivot selection and stability during zoom, gesture ownership, wire preview cleanup, the production world-picker with synthetic coordinates, offline placement and wiring, real Unity motors, audio binding, save/reopen and rewiring. The result is written to `Verification/workshop-chain-unity-checks.json`. In Unity batch mode use `-executeMethod Ecaty.Editor.WorkshopChainVerification.StartVerification -ecatyWorkshopChainVerify` and omit `-quit` so the test can finish.

Run `python Verification/workshop_chain_smoke.py` for the independent native check. It covers wrong-target input, hearing range, live placement, reacting to measured occupancy, cross-entity BehaviorTree.CPP success and failure and retargeting a link during play.

