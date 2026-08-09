# FastView 1.3.0

FastView 1.3.0 adds fast FBX and OBJ workflows plus native preview-video creation.

## Highlights

- Opens GLB, glTF, FBX, and OBJ from the File menu, command line, drag-and-drop, and Windows Explorer.
- Shows large models quickly with 512 px initial textures, then refines detailed textures in the background.
- Generates an eight-second 1920 x 1080 H.264 MP4 with a smooth 360-degree camera orbit.
- Saves preview videos beside the selected model as `<model-name>_preview.mp4`.
- Adds visible loading progress, safe cancellation, and early drag-format feedback.
- Extends Explorer thumbnail generation to FBX and OBJ.

## Validation

- Release solution builds with zero warnings and zero errors.
- GLB, OBJ, and FBX thumbnail commands return exit code 0.
- A 334.66 MB FBX with 35 embedded textures opens responsively and exports 240 Full-HD frames.
- The Full-HD stress preview is valid H.264/YUV420p at 30 FPS and exactly eight seconds long.

FastView remains an x64 Windows application. The viewer is self-contained; the Explorer thumbnail provider requires the Microsoft .NET 10 x64 runtime.
