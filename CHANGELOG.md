# Changelog

## Unreleased

No unreleased changes yet.

## 1.2.0 - 2026-06-28

### Added

* Added multi-model scene assembly with additional model loading and viewport selection.
* Added a scene sidebar with per-model Position, Rotation, and Scale controls.
* Added Move, Rotate, and Scale gizmos with `W`, `E`, and `R` shortcuts.
* Added Local and Global transform orientations, local-axis scaling, and a central uniform-scale handle.
* Added per-row transform reset buttons and a full Transform reset.
* Added `Ctrl+Z` transform undo with one history entry per completed gizmo drag.
* Added an optional adaptive XZ ground grid at the world origin, toggled with `G`.
* Added perspective front, right, left, top, bottom, and back camera presets.
* Added selection-aware focus: `F` frames the selected model or the complete scene when nothing is selected.
* Added a collapsible viewport toolbar with vector-drawn icons and tooltips.
* Added persistent viewer settings for the camera, background, grid, lighting, active transform tool, and Local/Global orientation.
* Added a built-in EXR background option alongside custom EXR files and solid colors.
* Added bloom with adjustable threshold, intensity, and radius.
* Added `KHR_materials_emissive_strength` support for HDR emissive materials.
* Added directional-light shadow mapping with adjustable strength and softness.
* Added viewport toolbar toggles for bloom and shadows.
* Added export of the assembled scene as a self-contained GLB file.

### Fixed

* Fixed sluggish viewport input caused by continuously invalidating the transparent gizmo overlay.
* Fixed model and gizmo updates stalling during mouse drags when the WinForms idle loop was busy.
* Fixed local scale handles so their displayed axes match the scale components being edited.
* Fixed rotation-gizmo axis math and drag direction for already rotated models and opposite camera sides.
* Fixed the ground grid so it is symmetric around the world origin, visible from both sides, and toggles without rebuilding the complete GPU scene.
* Fixed emissive-factor-only materials by using a white fallback emissive texture instead of black.
* Fixed camera and model 90-degree snapping so pressing or releasing `Shift` during a drag does not jump back.

### Changed

* Local is now the default transform orientation for new settings.
* Viewport tools stay hidden until the `...` button opens the scene sidebar and toolbar.
* Transform updates retain material textures on the GPU instead of rebuilding the complete material scene.
* Viewer and camera settings are stored in `%LOCALAPPDATA%\FastViewDX12\settings.json`.
* The renderer now includes reusable post-processing and directional-shadow passes.

### Tested with

* `EmissiveStrengthTest.glb`
* Multi-model transform and GLB export round trips
* Local and Global Move, Rotate, and Scale workflows
* Bloom radius, directional shadows, built-in EXR, grid, and persistent camera settings

## 1.0.1 - 2026-06-26

### Added

* Added transparent Windows Explorer thumbnails for `.glb` files.
* Added a 45-degree thumbnail camera angle and tighter automatic model framing.
* Added a solid-color background option to the viewer.
* Added a visible EXR background option with an opacity control.
* Added file-based Explorer thumbnail support for `.gltf` files with external resources.
* Added a 16–256 px multi-resolution GLB/glTF fallback icon.
* Added repository-relative release and Inno Setup build scripts.
* Added public-repository documentation, release checks, and third-party notices.

### Fixed

* Added support for `TEXCOORD_1`.
* Added support for `KHR_texture_transform`.
* Added support for the glTF texture wrap modes `CLAMP_TO_EDGE` and `MIRRORED_REPEAT`.
* Correctly respects the glTF `doubleSided` material property.
* Added backface culling for single-sided materials.
* Added thin-surface support for `KHR_materials_transmission`.
* Fixed the opaque watch glass in `ChronographWatch.glb`.

### Changed

* Split the renderer, WinForms shell, and thumbnail-provider core into focused partial-class files.
* Reordered members by lifecycle and responsibility.
* Added detailed English documentation for rendering, capture, COM integration, and resource ownership.
* Added repository-wide C# style settings and an architecture guide.

### Removed

* Removed the unused `CameraController.FitToMesh` overload.
* Removed the unused debug UV texture generator.

### Tested with

* `TextureTransformMultiTest.glb`
* `TextureSettingsTest.glb`
* `ChronographWatch.glb`
* Don McCurdy's glTF Viewer as a visual reference
