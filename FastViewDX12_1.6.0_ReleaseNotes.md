# FastView 1.6.0

FastView 1.6.0 adds STL support throughout the viewer and Windows Explorer integration while retaining the existing FBX workflow.

## STL support

- Open `.stl` files from the File menu, command line, **Open with**, or drag-and-drop.
- Add STL models to multi-model scenes and restore them with the automatic session recovery.
- Render STL thumbnails in Windows Explorer through the file-based FastView preview provider.
- Display a FastView fallback icon for STL files when no thumbnail is available.

## FBX and Explorer previews

- FBX thumbnails continue to use the original file path so embedded and adjacent resources remain available.
- The shared glTF, FBX, STL, and OBJ provider now reports format-specific diagnostics.
- Installer registrations include GLB, glTF, FBX, STL, and OBJ for Explorer thumbnails and **Open with FastView**.
