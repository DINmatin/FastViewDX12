# Changelog


## Unreleased

### Changed

- Split the renderer, WinForms shell, and thumbnail-provider core into focused partial-class files.
- Reordered members by lifecycle and responsibility and documented the render, capture, COM, and resource-ownership flows in English.
- Added repository-wide C# style settings and an architecture guide.

### Removed

- Removed the unused `CameraController.FitToMesh` overload.
- Removed the unused debug UV texture generator.

## 1.1.0 - pre-release

- Added transparent Explorer thumbnails.
- Added a 45-degree thumbnail camera angle and tighter automatic crop.
- Added solid-color or visible EXR backgrounds in the viewer.
- Added EXR background opacity control.
- Added file-based Explorer thumbnails for `.gltf` with external resources.
- Added a 16–256 px multi-resolution GLB/glTF fallback icon.
- Added repository-relative release and Inno Setup build scripts.
- Added public-repository hygiene, release checklist, and third-party notices.
