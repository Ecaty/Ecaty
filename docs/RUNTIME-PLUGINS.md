# Runtime plugin map

## Gallery implementation inspection - 2026-11-08

The approved layered gallery is implemented with project-owned URP GalleryOrbit/GalleryCrystal shaders and Flexalon layout. No vendor code, package installs or renderer-feature settings were changed. The orbit is a rounded mesh with per-sphere clearance, silhouette feathering, traveling phase and camera-relative rear fade. It reads native intent/time and uses bounded per-layer sphere uniforms; final art and device profiling remain open.

Local sources inspected: Lumen 2 LumenEffectPlayer exposes runtime profiles/fades and instantiated layer renderers; StaticRay/DynamicRay layers specialize in rays rather than this closed, live-clearance loop. Cavity's URP SSCCRendererFeature has RecordRenderGraph and depth/normals paths; it is a shape-readability option, not a fix for transparent ring boundaries, and was not enabled globally. Artistic OilPaint/TiltShift are present but would change the whole camera treatment; the approved look was retained. VFX Graph 17.6.0 is installed with the prepared PeacefulParticles quad-output sample and existing CapVfx adapter, but that prepared graph has no gallery sphere-clearance/closed-loop contract. Authoring another graph was unnecessary for this bounded effect; a GPU trail remains a possible later visual variant, not an implemented feature. Zephyr/Artificer, gaze and audio tools address different responsibilities and were not added to gallery state/input.

The custom effects are presentation assets, not new simulation algorithms or a replacement ECS. Existing optional plugin-adapter import gates remain intact. See STATUS for Editor render/interaction/reconstruction evidence; no player/device graphics certification is implied.

Current implementation/status: [RUNTIME-CAPABILITIES](RUNTIME-CAPABILITIES.md) and [RUNTIME-STACK](RUNTIME-STACK.md). The source-review table below records earlier evaluation; the capability milestone supersedes its "no adapter yet" notes. MicroSplat/TVE are excluded.

Checked against the local Ecaty checkout on 2026-11-06. Proposed expansion and source references: [UNITY-CAPABILITY-ROADMAP](UNITY-CAPABILITY-ROADMAP.md). Integration means configuring the purchased plugin through its runtime API, not reproducing or redistributing vendor source. Vendor files stay out of the public repositories.

| Category | Asset / engine | Local state and runtime use | Recommendation |
|---|---|---|---|
| Organic modeling | MudBun | Imported; existing recipe adapter creates brushes and pivots during play. RuntimeMudRenderer computes after animation. | Keep for soft, connected shapes; art direction is not fixed to clay. |
| Curves / areas / structures | Spline Mesher Pro 1.0.16 | Imported as `xyz.staggart-creations.splinemesher-pro`. Runtime source exposes SplineCurveMesher, SplineFillMesher, SplineMesher.Rebuild, settings, generated mesh containers and rebuild events. No Flecs adapter yet. | Best next geometry adapter: Script-defined open curves or closed area boundaries, profile/material references -> Unity Splines -> curve extrusion or filled-area mesh generation. It supports both line/curve and area workflows. Rebuild on edits, measure cost before continuous deformation. |
| Modular buildings | Cozy Builder 0.5.6 | Imported as com.projectdawn.cozy-builder. Runtime CozyRenderer.Build/Clear and generated mesh/GameObject paths inspected. No Flecs adapter or Ecaty player test yet. | Keep for buildings using prepared construction kits and runtime configuration. |
| Vegetation | Broccoli Tree Creator | Imported in Assets/Waldemarst. Runtime example loads a pipeline through TreeFactory.LoadPipeline and generates trees with Spawn. No Flecs adapter or Ecaty player test yet. | Keep for runtime variations using configurable tree pipelines. |
| Camera | Cinemachine 6.6.0 | Installed; no Ecaty adapter. | Runtime target/framing/blending capability. |
| Presentation sequences | Timeline 6.6.0 and Unity Playables | Installed; runtime track/clip creation and graph APIs available. No Ecaty sequence adapter. | Flecs controls sequence intent; Unity coordinates audio/animation/cameras. Save reconstruction data, not Editor asset operations. |
| Ground locomotion / collision | Unity CharacterController | Runtime adapter now consumes MoveIntent and publishes actual ActorMotion. Independent actor capsules and static slab collider; no general rigid-body/joint adapter yet. | Keep one solver. User, goal-follow and Script use the same motor. Vehicles/flight require different motor capabilities. |
| Creature locomotion | FImpossible Legs Animator | Imported; runtime AddComponent, explicit step-curve initialization, actual hips/three-pivot leg chains, procedural idle-glue stepping, ground mask and movement/grounded signals. No imported animation clip or skin required. | Reuse, with scale/foot/gait tuning and valid generated chains. Runtime setup now assigns leg sides/opposite pairs before initialization, enabling alternating steps. Animator binding alone does not prove stepping quality. |
| Body response | FImpossible Leaning Animator | Imported; runtime base/spine references. | Reuse on a torso branch; avoid competing writers on the same pivot. |
| Secondary motion | Boing Kit | Imported; runtime BoingBones on separate chains, visible brush nodes excluded. | Reuse for ears/tails/spring branches. Do not let it drive the leg chain or physics root. |
| Feedback / audio / camera | Feel | Imported. MMF_Player supports AddFeedback, Initialization and PlayFeedbacks at runtime; feedbacks are not limited to Inspector authoring. No Flecs adapter yet. | Next event adapter: Flecs event/sequence plus registered feedback type/assets -> a runtime player. Audio feedback needs actual clips or a synthesizer; Feel does not invent sounds. |
| Navigation | Unity AI Navigation | Installed; no runtime adapter. | Add only when direct goal steering needs obstacle avoidance/pathfinding. Runtime surface builds have costs. |
| Reach / aim / constraints | Unity Animation Rigging 6.6.0 | Installed; local RigBuilder runtime Build/Clear/Evaluate APIs verified. No Ecaty adapter or use yet. | Generate constraints from pivot roles and targets; keep one pose writer per branch alongside FImpossible and Boing. |
| Spatial tools / layouts | Flexalon 4.4.2 | Imported; runtime layout/constraints/interactions inspected. No Ecaty adapter yet. | Use for world-space tools. Route camera/input explicitly and avoid two transform writers. |
| GPU / particle effects | Unity VFX Graph 17.6.0 | Installed; no Ecaty effect adapter. ParticleSystem also available. | Runtime parameters/events/mesh/texture bindings to prepared effect graphs; evaluate device support. |
| Touch / controller input | Unity Input System | Installed; WASD, gamepad and ground-pointer commands feed the same runtime schema. | Keep input separate from the motor and behavior rules. |

## Other owned assets

The supplied audit lists Dreamteck Splines, Prefab World Builder, MicroSplat Runtime Procedural Texturing, Traversal Pro, Malbers Animal Controller, Vehicle Physics Pro Community, Playmaker, Love/Hate, Lean Touch and Dialogue System. These are inventory candidates, not verified installed runtime dependencies. Clayxels is excluded as requested. Digger and Archimatix are excluded from the current direction; destruction is out of scope.

Prioritize one capability at a time: Spline Mesher for paths/structures; Feel for event feedback; navigation only when needed. Specialized controllers can be adapters if they accept runtime-generated geometry/rigs and expose runtime configuration. A controller expecting preauthored animations/avatars is not automatically a universal creature motor. Avoid multiple spline/IK/controller frameworks for the same responsibility.

## Boing EntityId compatibility

The locally imported version already uses UnityEngine.EntityId for Params.InstanceID and lookup keys. In `Assets/Boing Kit/Script/BoingBehavior.cs`, the requested assignment is now `Params.InstanceID = GetEntityId();`. Do not hash to int then implicitly convert back. That loses the typed identity and triggers the obsolete conversion. Reapply this one-line patch after a vendor reimport if upstream has not fixed it; vendor source is ignored by Git. Other preexisting EntityId migration edits are preserved.

## Adapter contract

Store versioned configuration, relationships and reusable source in Flecs. An adapter validates a changed recipe, resolves stable entity/part and asset references, creates/configures the plugin at runtime, and reports realized state/errors back. One owner writes each transform per stage: motor root -> body/leg procedural pose -> separate secondary chains -> MudBun geometry. No model call is required each frame. Versioned local experience save/resume now restores managed sources and selected model/rig/motor/brush/Persistent state. Pending awaits restart; this is not a full VM/physics-solver checkpoint.

## User-owned candidates reviewed, 2026-11-07

Public publisher documentation and listings were reviewed. These are recommendations for inspection, with no new imports or runtime adapter implementation in this review. A documented runtime API does not establish compatibility with live MudBun, Unity 6.6, arbitrary cameras or mobile. The existing implementations remain described in RUNTIME-CAPABILITIES.

| Candidate | Assessment for Ecaty |
|---|---|
| [Windinator](https://github.com/BlenMiner/Windinator) | Code-driven uGUI/window framework; publisher describes SDF UI. Consider world-space Canvas tools if useful, after settling interactions. Flexalon currently arranges the 3D controls. Importing a UI framework alone does not settle their meaning. |
| [Master Audio 2024](https://dtdevtools.com/docs/masteraudio/TOC.htm) | Runtime audio groups, variations, ambience and playlist APIs are documented. Candidate playback/mixing adapter when sound becomes a product feature. Keep native synthesis and each playing voice under an explicit owner. |
| [Sound Filters and Effects](https://assetstore.unity.com/packages/tools/audio/sound-filters-and-effects-19328) | Publisher documents runtime-controllable EQ, delay, tremolo, reverb and filters. Useful for future recorded foley processing; evaluate against existing Unity mixer filters and CPU budgets. |
| [Recolor Tool](https://assetstore.unity.com/packages/tools/painting/recolor-tool-uv-editor-by-ithappy-374860) | Advertised workflow edits UVs and saves assets in the Editor. Useful for preparing palette-based catalog assets. A shipping runtime recoloring API has not been established. MudBun brush color already has a runtime path. |
| [Love/Hate](https://forum.pixelcrushers.com/post/creating-factionmanager-and-factionmember-programmatically-13713840) | Publisher confirms runtime faction/member creation. Candidate social-emotion solver whose measured relationships can feed Flecs goals. It adds social algorithms beyond storing ECS relationships. Defer until interactions need personality, reputation or social memory. |
| [Zephyr](https://distant-lands.gitbook.io/zephyr) | Documented global settings, wind zones, CPU queries and shader integration. Strong candidate for consistent wind. Each vegetation shader, particle system and physics/motor consumer still needs a compatible integration. |
| [Mantis LOD Pro](https://www.mesh-online.net/faq.html) | Author documents runtime simplification in the included source. Candidate for optimizing frozen/baked creations and generating LODs. Dynamic MudBun remeshing needs separate scheduling and validation. |
| [Lumen 2](https://distant-lands.gitbook.io/lumen-stylized-light-fx/lumen-effect) | Documented effect player has runtime profiles, color/brightness/scale and fades. Strong candidate for cozy light shafts, glows and creation feedback. Prepare/clone profiles and pool effect instances. |
| [Artificer](https://assetstore.unity.com/packages/tools/animation/artificer-341008) | Publisher documents runtime assembly/dismantling, splitting, scripted control and events. Candidate for peaceful objects assembling themselves on placement. Inspect how it handles live versus baked MudBun before using it on generated characters. |
| [Eyes Animator](https://assetstore.unity.com/packages/3d/animations/eyes-animator-137246) | Useful next FImpossible candidate for gaze/expression. Only Legs Animator and Leaning Animator were found in the imported animation folder. Inspect runtime initialization and pivot/blendshape requirements after import. Unity Animation Rigging can provide basic aim constraints, with custom behavior for richer gaze. |
| [TopDown Engine](https://topdown-engine-docs.moremountains.com/) | Full gameplay framework with input, controllers, AI and managers. Defer a framework integration; it would overlap existing motor/input/behavior ownership. Individual capabilities need deliberate boundaries if reused. |
| [Screen Space Cavity & Curvature](https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/screen-space-cavity-curvature-built-in-urp-hdrp-216995) | High-priority appearance trial to improve edge and shape readability. Verify Unity 6.6 Render Graph support and the depth/normals written by live MudBun. |
| [Illustrate](https://assetstore.unity.com/packages/vfx/shaders/illustrate-stylized-toon-shader-256762) | Candidate prepared toon material family. Live MudBun uses its own rendering path; shader compatibility and brush colors require inspection. Baked catalog meshes are a separate integration path. |
| [Artistic Bundle](https://fronkongames.github.io/store/artistic/) | High-priority theme trial, especially Tilt Shift and Oil Paint. The bundle unlocks separately downloaded effects. Author documents Unity 6/URP Render Graph and flags a built-in Bloom interaction; test ordering with our CapPostProcessing adapter. |
| [Dynamic Radial Masks](https://amazing-assets.gitbook.io/dynamic-radial-masks) | Shader extension with runtime controller-driven masks. Candidate for surface previews, selection ripples and reveals. Compatible shader variants are authored/compiled before shipping; runtime parameters choose their behavior. |
| [Stylized Weather System](https://chemicalbliss.com/wordpress/portfolio-items/stylized-weather-system/) | Unluck Software's particle/fog/wind system, distinct from Distant Lands COZY. Publisher documents weather presets, triggers and transitions. Consider its weather visual style while retaining explicit owners for global sky/fog/wind already touched by UniStorm/Zephyr. |
| [All In 1 Shader Nodes](https://seasidestudios.gitbook.io/seaside-studios/shader-nodes) | Authoring accelerator for Shader Graph/Amplify. Include selected compiled shaders in the player and expose their properties to Flecs; the node editor itself is not established as a shipping runtime shader compiler. |
| [VoluSmokeFX](https://assetstore.unity.com/packages/vfx/shaders/volusmokefx-343844) | Candidate for gentle mist and smoke. Publisher documents stacked-plane faux volumetrics and warns about extreme/close camera angles. Check orbit/AR cameras before choosing it as the shared atmosphere renderer. |
| [Screenspace VFX](https://assetstore.unity.com/packages/vfx/particles/spells/screenspace-vfx-316287) | Optional effect-content library. Inspect renderer/prefab API and suitability for world-bound feedback after import. |
| [Anime Powers Pack](https://assetstore.unity.com/packages/vfx/particles/spells/anime-powers-pack-305728) | Optional themed effect-content library. Pick effects appropriate for the peaceful experience and bind their lifetime/targets through the existing capability model. |
| [All In 1 Sprite Shader](https://assetstore.unity.com/packages/vfx/shaders/all-in-1-sprite-shader-156513) | Candidate for drawings, cutouts, stickers and flat world-space affordances. Control prepared materials at runtime; evaluate material/keyword choices for the supported themes. |

For the next appearance/expression inspection, import Cavity & Curvature, Artistic Tilt Shift/Oil Paint, Lumen and Eyes Animator. Zephyr and Artificer are strong subsequent candidates for coordinated wind and assembly feedback. Master Audio plus the filter pack are useful when the recorded-sound loop is implemented. These priorities are provisional; UX review comes first.

Each adopted plugin uses the same boundary. Flecs stores configuration/targets and issues commands; a disposable adapter calls the actual runtime API; measured results/events return to Flecs; source/configuration and selected plugin state rebuild after reload. A plugin owns its algorithm while active. Imported authoring tools can prepare reusable assets without being part of the shipping runtime.

## Primary references

- [Spline Mesher Pro, author listing](https://assetstore.unity.com/packages/tools/modeling/spline-mesher-pro-338468) and the imported package's Runtime API.
- [Cozy Builder documentation](https://lukaschod.github.io/cozy-builder-docs/manual/index.html); imported runtime source inspected, integration/build verification still needed.
- [Feel runtime player creation](https://feel-docs.moremountains.com/mmf-player.html).
- [Boing Kit manual](https://longbunnylabs.com/boing-kit-manual/).
- [Legs Animator author manual](https://www.filipmoeglich.pl/download/Legs%20Animator%20-%20User%20Manual.pdf).
- [CharacterController.Move](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.Move.html).
- [Dreamteck author manual](https://dreamteck.io/page/dreamteck_splines/user_manual.pdf).
- [Digger runtime documentation](https://github.com/ofux/Digger-Documentation/blob/master/Runtime.md): runtime editing is the Pro feature; ownership of Digger alone does not establish that tier.

## Gallery refinement and shader authoring — 2026-11-08

Amplify Shader Editor is useful for reusable functions/templates/custom nodes and visually editable material authoring. Publisher reference: https://amplify.pt/unity/amplify-shader-editor/ . The user's ownership and Zephyr's Amplify-compatible functions do not establish that the authoring editor is imported. No import or vendor modification was needed for this refinement. GalleryOrbit uses a project-owned URP HLSL shader: continuous view-normal falloff softens the entire 3D tube silhouette, while world-clearance and camera-relative rear fades remain independent. A profile softness control and shared EcatyTheme supply authorable values. The seed uses the existing baked workshop mesh with a small project-owned fading vertex-colour material. VFX Graph remains available for future content; it was not necessary for this analytic ring effect.


## Current Feel / Amplify use — 2026-11-08

User preference: use Feel for interaction feedback wherever appropriate. GalleryFeedbackSpring is a small disposable presentation helper backed by MoreMountains.Feedbacks.MMSpringFloat when ECATY_CAP_FEEL is imported. It drives hover/swap opacity and tint, ghost/seed fades, exit pull and vignette strength. Native Flecs still owns the goals/navigation; this is not a new simulation system. The vignette image is drawn by a lightweight project-owned shader instead of enabling global URP post-processing or a full MMF_Vignette_URP stack. No vendor code was modified.

The user has now imported Amplify Shader Editor 1.9.9.14. Its source folder/root meta are ignored, as are other vendor assets. The visible C# error during import was in our test (duplicate local angle), not in Amplify, and was fixed. The gallery remains directly maintained shaders with compatible material override slots; no new Amplify graph is required to customize the exposed ring parameters. A pre-existing Cavity Lit + URP shader signature diagnostic resurfaced during import; assess that material before future use.
