# FastView 1.7.0

FastView 1.7.0 adds playback and standards-compliant export of skeletal animations embedded in GLB/glTF models.

## Animation playback

- Automatically play the first embedded animation after loading an animated GLB.
- Evaluate node translation, rotation, and scale tracks together with joint-weight skinning.
- Select clips and control playback from the Inspector with separate **Play** and **Pause** buttons, Restart, Loop, timeline scrubbing, and a live time display.
- Update only existing GPU vertex buffers during playback so material and texture resources remain resident.

## Animation-preserving export

- Preserve clips, keyframes, skeletons, skins, joints, weights, and clip names through **Export scene as...** and **Save compliant GLB...**.
- Apply the FastView model transform to the exported animated scene.
- Write repaired normals back into the retained glTF document before validation and export.

Animation-preserving export currently accepts one animated model per document. FastView reports a clear error instead of silently discarding animation data when multiple animated models are combined.
