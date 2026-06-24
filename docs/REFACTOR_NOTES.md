# Refactor notes

This cleanup intentionally changes source organization rather than rendering behavior.

## Structural changes

- `Dx12Renderer` is now split by responsibility:
  - `Dx12Renderer.cs` — shared state, constant-buffer layouts, construction, and disposal
  - `Dx12Renderer.Device.cs` — device, pipeline, swap-chain, resize, and synchronization
  - `Dx12Renderer.Scene.cs` — materials, textures, mesh upload, and scene ownership
  - `Dx12Renderer.Rendering.cs` — frame recording, queues, draw calls, and shader constants
  - `Dx12Renderer.Background.cs` — EXR upload, environment lighting, and background rendering
  - `Dx12Renderer.Capture.cs` — GPU readback, alpha conversion, crop, scaling, and PNG output
  - `Dx12Renderer.Interaction.cs` — camera and light input facade
- `MainForm` is split into lifecycle, model loading, menu commands, and input routing.
- `ThumbnailProviderCore` is split into orchestration, process execution, and bitmap conversion.
- Small data and interop files were normalized to conventional C# formatting.
- English XML documentation and algorithm comments were added throughout the renderer, loader, provider, controllers, build flow, and shaders.

## Removed code

Only members with no references anywhere in the solution were removed:

- `CameraController.FitToMesh`
- `DecodedTexture.CreateDebugUvTexture`

No other method was removed merely because it looked obsolete.

## Validation performed

- Every C# file was parsed after the refactor; no syntax-error or missing-syntax nodes remain.
- The declaration inventory was compared with the pre-refactor source. It is identical except for the two intentionally removed methods.
- The repository was scanned for private absolute paths, local test models, user names, email addresses, PDBs, executable binaries, and runtime diagnostic files.
- Comments in C# and HLSL source were checked for consistent English language.

A final Windows build and runtime smoke test are still required because Direct3D 12, WinForms, COM registration, and Inno Setup cannot be executed in the source-cleanup environment.
