# First saved-world loop

The approved 2026-11-09 milestone replaces the new-creation mock path with a small native experience. Existing sample worlds remain disposable mocks. This is a first end-to-end path, not a general content catalog or complete capability snapshot system.

## Try it

Enter the top seed. The first deliberate click creates a native world with a ground and a spinning cube. Inside it, left-drag the object to move it; click empty space to place another primitive. Right-drag orbits, middle-drag pans, wheel zooms. Escape or the established outward boundary returns to the gallery. The gallery sphere shows a snapshot of the saved features. Re-enter to resume camera, object selection and authored placement; the current hidden world remains resident and paused until another world is entered.

## Ownership

- `GallerySavedWorld` is a reflected native record on the gallery entry: source, serialized native object components, selection and revision. It is persisted in the optional `worlds` section of the existing local layered arrangement. Legacy saves without that section remain readable.
- `GalleryContent_<id>` owns the private native source. `<id>Content` is its private root; `ChildOf` supplies hierarchy and `(GalleryContentOf, galleryEntry)` links it to its catalog entry. Objects instantiate the common `GallerySharedShape` prefab through `IsA`.
- Native `Position`, `Rotation`, `Scale`, `Color`, `Primitive` and `Spin` provide this first vocabulary. The existing C++ Spin system runs behavior. Unity's existing FlecsWorld presents those entities. No second ECS, physics model, native ABI change or new per-frame AI algorithm was introduced.
- The presentation adapter leases the root pose only to map world-local coordinates into the isolated preview stage. Object poses and behavior remain native. Picking uses transformed mesh bounds because the existing primitive renderer deliberately removes PhysX colliders. This is preview/object input geometry, not a new physics simulation.
- Returning saves native component state and sets native Spin rates to zero while retaining the authored rates in the definition. The root is hidden. Re-entry resumes those rates and native state. Entering a different world clears the prior private source, removes its private hierarchy and releases its pose lease. The shared prefab and global schemas remain.
- GalleryExperienceStore is lifecycle orchestration and disposable Unity-instance bookkeeping. Definitions and selection are read from Flecs. A generation cache avoids re-reading serialized definitions every rendered frame. GalleryPreviewStage rebinds targets only after instance reconstruction.

World documents validate names, component members, finite bounded numbers and supported primitive kinds. Restore rejects definitions without a matching catalog entry. Camera/focus/portal state remains in the existing reflected gallery components; source/state reconstruction uses native float tolerance.

## Reuse and limits

`CrystalWorldGallery.ReuseWorldObject(from, name, to)` demonstrates reusing a saved object's definition in another world. Instances have independent placement/state and reference the common native primitive prefab. It is a developer operation at this stage; a final drag-between-worlds affordance and linked object-library editing are not implemented.

Persistence covers the six primitive components above, camera/selection, the gallery context and the optional `GallerySavedWorld.graph`: capability bindings, `CapDependsOn` relations, NativeTree definitions and adapter state under `<id>Content`, plus behavior sources named `GalleryWorld_<id>.*` (see [RUNTIME-CAPABILITIES](../Packages/com.runtime.capabilities/Documentation~/RUNTIME-CAPABILITIES.md#persistence)). Textures, audio recordings, shared-user assets and world-scoped AI tool routing are not yet included; AI tools do not yet author into a world scope. Do not call RuntimeExperience.Load to load a gallery world: that legacy loader replaces the whole native session, including gallery state. Keep new world loading scoped and preserve shared dependencies.

Archive remains disabled. First-run learning, multiplayer/online worlds, export/printing and the bin remain deferred. No provider requests or new services are needed for this milestone.

## Verification

GalleryScenarioSuite covers native creation and Spin output, production drag/placement, save/reconstruction, camera/selection, retained hidden pause, private scope unloading, shared-prefab survival and definition reuse. It resets a disposable thirty-world fixture and restores the original in-memory arrangement. The command runner restores original save bytes after Play-mode shutdown/autosave. Captures include the actual native world and its saved gallery preview. These are Editor checks; player restart/device coverage and full capability persistence remain future work.
