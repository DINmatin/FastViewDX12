# Testing checklist

## Viewer

1. Build `FastView.slnx` in `Release | x64`.
2. Open one embedded `.glb`.
3. Open one `.gltf` with an external `.bin` and at least one external texture.
4. Confirm orbit, pan, zoom, fit-to-scene, direct light, and environment lighting.
5. Under **View → Background**:
   - choose a solid color,
   - load an EXR,
   - move EXR opacity from 0 to 100 percent,
   - switch back to solid color.
6. Export a normal preview PNG and confirm it uses the selected visible background.

## Thumbnail command

```bat
start "" /wait "C:\path\FastViewDX12.exe" --thumbnail "C:\path\model.glb" "C:\Temp\FastView-glb.png" 768 768
```

Repeat with a `.gltf` that references external files. Confirm:

- exit code is `0`,
- PNG exists,
- PNG has transparency,
- model is shown from a 45-degree three-quarter angle,
- model fills most of the image without clipping.

## Explorer provider

After installing:

1. Restart Explorer when prompted.
2. Open a folder containing GLB and glTF files in large-icon view.
3. Confirm transparent thumbnails for both formats.
4. Confirm a glTF with adjacent `.bin` and texture files renders correctly.
5. Rename or temporarily remove a referenced texture and confirm the provider fails cleanly rather than hanging Explorer.
6. Uninstall FastView and confirm both thumbnail registrations are removed.

Diagnostic files, when needed:

```text
%TEMP%\FastView_ThumbnailProvider_LastRun.txt
%TEMP%\FastView_ThumbnailProvider_Error.txt
```
