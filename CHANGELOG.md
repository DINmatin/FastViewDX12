# Changelog

## Unreleased

No unreleased changes yet.

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
