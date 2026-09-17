# FastView 1.5.1

FastView 1.5.1 adds a repair workflow for GLB files with malformed vertex normals.

## GLB repair

- Files rejected because of invalid, zero-length, or non-finite vertex normals now load through a targeted repair pass.
- Invalid normals are regenerated from the mesh geometry for viewing, Explorer thumbnails, and export.
- Other malformed glTF structures remain subject to validation.

## Save compliant

- Use **File → Save compliant GLB...** to write the current editable scene as a self-contained repaired GLB.
- FastView immediately reloads the written file with strict glTF validation before reporting success.
- The suggested output name ends in `_compliant.glb`.
