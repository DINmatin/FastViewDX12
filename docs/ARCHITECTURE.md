# Architecture

FastView is intentionally split into two processes. The interactive viewer owns Direct3D 12 and model loading, while the Explorer extension is a small COM bridge that launches the viewer in a headless thumbnail mode. Keeping Direct3D and WinForms out of the Explorer process reduces crash and compatibility risk.

## `FastViewDX12`

- `Program.cs` selects interactive or `--thumbnail` startup before WinForms visual-style initialization.
- `MainForm*.cs` contains only the WinForms shell: menus, dialogs, input routing, model selection, and the idle render loop.
- `ModelSceneLoader.cs` dispatches GLB/glTF, FBX, STL, and OBJ input into renderer-neutral `SceneData`.
- `GltfSceneLoader.cs`, `AssimpFbxSceneLoader.cs`, and `ObjSceneLoader.cs` own their respective parsing paths.
- `Dx12Renderer.cs` owns shared state and disposal. Focused partial files contain device setup, scene upload, background rendering, frame rendering, interaction, and PNG capture.
- `CameraController.cs` and `LightController.cs` contain interaction math without Direct3D resource ownership.
- `MediaFoundationVideoEncoder.cs` writes native Windows H.264/MP4 preview videos without shipping FFmpeg.
- `GpuTextureUploader.cs` and `GpuHdrTextureUploader.cs` stage decoded images into GPU resources.

## `FastView.ThumbnailProvider`

- `FastViewThumbnailProvider` accepts GLB data through `IInitializeWithStream`.
- `FastViewGltfThumbnailProvider` accepts an original path through `IInitializeWithFile` for glTF, FBX, STL, and OBJ, preserving access to adjacent resources.
- `ThumbnailProviderCore*.cs` launches `FastViewDX12.exe --thumbnail`, converts the resulting PNG into a premultiplied ARGB HBITMAP, and performs best-effort diagnostics and cleanup.

## Ownership rules

- Every COM object returned to Explorer transfers ownership of its HBITMAP to Explorer.
- Every Direct3D/DXGI object created by `Dx12Renderer` is released by `Dispose`.
- Scene resources are destroyed before a replacement scene is uploaded.
- Thumbnail temporary files are deleted in provider `finally` blocks.
- Diagnostic logging must never throw into Explorer.

## Render flow

1. WinForms creates a native host handle.
2. `Dx12Renderer.Initialize()` creates device and swap-chain resources.
3. `ModelSceneLoader.LoadFromFile()` creates CPU scene data.
4. `Dx12Renderer.LoadScene()` uploads materials and mesh buffers.
5. `Render()` draws the background, opaque queue, and transparent queue.
6. A pending preview request copies the backbuffer into a readback buffer and writes a cropped PNG.
