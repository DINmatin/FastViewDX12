# FastView 1.5.0

FastView 1.5.0 turns the lightweight viewer into a cleaner, recoverable scene editor while keeping large-model loading fast.

## New

- Replaced the floating icon toolbar with one unified Inspector for transforms, camera, view, lighting, shadows, bloom, and detailed rendering values.
- Added editor-style transform fields: drag horizontally to scrub, double-click for keyboard entry, hold `Shift` for faster changes, or `Ctrl` for precision.
- Added `Alt+Enter` borderless fullscreen mode.
- Added automatic recovery of all loaded files, model order, selection, camera, file transforms, and mesh-part transforms.
- Added hierarchical file and mesh-part selection. Click geometry to edit one part, or select the file root to transform the complete imported file.
- Exported scene GLBs remain split into independently editable mesh parts when reopened in FastView.
- Added a Yes/No/Cancel close prompt for transform changes that have not been exported as a scene GLB.

## Drag and drop

- Drop normally to open a model and replace the current scene.
- Hold `Ctrl` while dropping to add the model to the current scene.
- The cursor and large viewport overlay show the active mode before release.

## Recovery and export

- Choosing **No** in the close prompt exits without exporting but keeps the editable state in FastView's automatic recovery session.
- Choosing **Yes** opens the GLB scene-export dialog; canceling the dialog keeps FastView open.
