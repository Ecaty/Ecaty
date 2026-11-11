# Owned assets and runtime references

Configs are siblings of source under their owner, not nested inside Resources:

| Owner | Config |
|---|---|
| Gallery | Assets/_Features/Gallery/Gallery.asset |
| World | Assets/_Features/World/World.asset; Environment/Environment.asset |
| Project adapters | Assets/_Adapters/Adapters.asset |
| ThemeConfig | Assets/__Common/Theme/ThemeConfig.flecs (generated from the Editor-only Theme.asset) |
| Agent host | Assets/__Common/Agent/Agent.asset; Presentation/PanelSettings.asset |
| Project Settings | Assets/_Settings/Project/Rendering/ |

Assets/__Common/Bootstrap/Resources/RuntimeComposition.asset holds typed asset references only. It makes feature-owned configs available in a player without copying their values into a central settings object. Packages receive host configuration through their existing explicit APIs. Shared agent code does not depend on Flecs or this composition class.

Runtime Script/schema/behavior definitions, shaders, fixtures and prompts retain Resources folders inside their owners. Gallery uses Resources/Gallery; World uses Resources/Runtime and world-look rules in Resources/Workshop; Creation owns seed/art/example resources. Settings owns the dock style. Package schemas stay in com.runtime.capabilities. Vendor imports are untouched.

Use Runtime > Gallery > Layout and ring controls or Theme colours. Ordinary scalar tuning applies live through the existing native input path; restart Play after changing material templates. GalleryLayout supplies designer values, Flecs inputs/rules own effective runtime data and dependencies, and neither asset is a player-save store. A universal typed SO-to-Flecs binding remains work to audit/extend; this migration does not rewrite Script per slider change.

Tests/Fixtures/resource-load-manifest.json covers Resources and explicit composition references (the composition field distinguishes the latter), preserving each asset's GUID. Run-Resources also verifies the 217-asset modular migration inventory and package direction. Run-Validator audits owned configs and startup wiring with Odin. Run-All includes both, native/PlayMode/gallery checks and Windows player-script compilation. See TESTING and PROJECT-STRUCTURE.

GalleryCatalog replaces the old prototype-named resource, preserving its GUID. GallerySurface is retired; it is not an active shader/reference fixture. The global InputSystem_Actions asset lives in _Settings/Project/Input and remains assigned in project settings. Return timing/config lives with Gallery, and recall colour stays in Theme.
