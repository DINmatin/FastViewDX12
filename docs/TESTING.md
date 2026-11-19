# Testing checklist

## Viewer

1. Build `FastView.slnx` in `Release | x64`.
2. Open one embedded `.glb`.
3. Open one `.gltf` with an external `.bin` and at least one external texture.
4. Open one FBX with embedded textures, one STL, and one OBJ with an adjacent MTL file.
5. Confirm orbit, pan, zoom, fit-to-scene, direct light, and environment lighting.
6. Drag a supported and unsupported file over the viewport and verify `DROP HERE` / `WRONG FORMAT`.
7. Create a preview video with `Ctrl+Shift+V`; verify 1920 x 1080 H.264, a complete orbit, and camera restoration.
8. Under **View → Background**:
   - choose a solid color,
   - load an EXR,
   - move EXR opacity from 0 to 100 percent,
   - switch back to solid color.
9. Export a normal preview PNG and confirm it uses the selected visible background.

## Thumbnail command

```bat
start "" /wait "C:\path\FastViewDX12.exe" --thumbnail "C:\path\model.glb" "C:\Temp\FastView-glb.png" 768 768
```

Repeat with `.gltf`, `.fbx`, `.stl`, and `.obj` files. Confirm:

- exit code is `0`,
- PNG exists,
- PNG has transparency,
- model is shown from a 45-degree three-quarter angle,
- model fills most of the image without clipping.

## Explorer provider

After installing:

1. Restart Explorer when prompted.
2. Open a folder containing GLB, glTF, FBX, STL, and OBJ files in large-icon view.
3. Confirm transparent thumbnails for all five formats.
4. Confirm a glTF with adjacent `.bin` and texture files renders correctly.
5. Rename or temporarily remove a referenced texture and confirm the provider fails cleanly rather than hanging Explorer.

## Viewport statistics and six-axis input

1. Press `F3` and confirm FPS, VRAM, polygons, and vertices update without flicker.
2. Load a model containing 8K textures and confirm the first frame uses the fast 512 px stage before the window-title refinement counter completes at full resolution.
3. With a SpaceExplorer or SpaceMouse connected, enable **View > SpaceExplorer camera mode** and confirm pan, dolly, yaw, and pitch respond while FastView is focused.
4. Disable the camera-mode menu item and confirm six-axis motion no longer changes the view.
6. Uninstall FastView and confirm all thumbnail registrations are removed.

Diagnostic files, when needed:

```text
%TEMP%\FastView_ThumbnailProvider_LastRun.txt
%TEMP%\FastView_ThumbnailProvider_Error.txt
```
