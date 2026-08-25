using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace FastViewDX12;

// Viewport ray picking and selection-aware camera framing.
public sealed partial class MainForm
{
    private readonly struct SceneModelMeshRange
    {
        public SceneModelMeshRange(
            SceneModel model,
            SceneMeshPart? part,
            int firstMeshIndex,
            int meshCount)
        {
            Model =
                model;

            Part =
                part;

            FirstMeshIndex =
                firstMeshIndex;

            MeshCount =
                meshCount;
        }

        public SceneModel Model { get; }

        public SceneMeshPart? Part { get; }

        public int FirstMeshIndex { get; }

        public int MeshCount { get; }
    }

    /// <summary>
    /// Selects the closest model under a normal left click. Clicking empty
    /// viewport space clears the selection.
    /// </summary>
    private void SelectSceneModelFromViewport(
        Point mousePosition)
    {
        SceneModel? model =
            TryPickSceneModel(
                mousePosition,
                out SceneModel? hitModel,
                out SceneMeshPart? hitPart)
                ? hitModel
                : null;

        if (model == null)
        {
            _sceneDocument.Select(null);
        }
        else if (hitPart != null)
        {
            _sceneDocument.SelectPart(model, hitPart);
        }
        else
        {
            _sceneDocument.Select(model);
        }

        RefreshSceneSidebar();
        UpdateMoveGizmoOverlay();

        _renderer.Render();
        _moveGizmoOverlayForm?.Update();
    }

    /// <summary>
    /// Frames the selected model, or the complete scene when no model is selected.
    /// </summary>
    private void FocusSelectedModelOrScene()
    {
        SceneModel? selectedModel =
            _sceneDocument.SelectedModel;

        SceneMeshPart? selectedPart =
            _sceneDocument.SelectedPart;

        if (selectedModel == null)
        {
            _renderer.FitCameraToScene();
            _renderer.Render();
            UpdateMoveGizmoOverlay();
            _moveGizmoOverlayForm?.Update();

            return;
        }

        SceneData renderScene =
            _sceneDocument.BuildRenderScene();

        if (!TryCreateSceneModelMeshRanges(
                renderScene,
                out List<SceneModelMeshRange> ranges) ||
            !TryFindModelRange(
                ranges,
                selectedModel,
                selectedPart,
                out SceneModelMeshRange selectedRange) ||
            !TryCalculateBounds(
                renderScene,
                selectedRange,
                out Vector3 min,
                out Vector3 max))
        {
            // A defensive fallback keeps F useful even when an externally
            // created SceneModel did not pass through MainForm.Models.
            _renderer.FitCameraToScene();
        }
        else
        {
            _renderer.FitCameraToBounds(
                min,
                max);
        }

        _renderer.Render();
        UpdateMoveGizmoOverlay();
        _moveGizmoOverlayForm?.Update();
    }

    private bool TryPickSceneModel(
        Point mousePosition,
        out SceneModel? model,
        out SceneMeshPart? part)
    {
        model =
            null;

        part =
            null;

        if (!_renderer.TryCreateWorldRay(
                mousePosition,
                out Vector3 rayOrigin,
                out Vector3 rayDirection))
        {
            return false;
        }

        SceneData renderScene =
            _sceneDocument.BuildRenderScene();

        if (!TryCreateSceneModelMeshRanges(
                renderScene,
                out List<SceneModelMeshRange> ranges))
        {
            return false;
        }

        float closestDistance =
            float.MaxValue;

        foreach (SceneModelMeshRange range in
                 ranges)
        {
            if (!TryCalculateBounds(
                    renderScene,
                    range,
                    out Vector3 min,
                    out Vector3 max) ||
                !TryRayIntersectBounds(
                    rayOrigin,
                    rayDirection,
                    min,
                    max,
                    out float boundsDistance) ||
                boundsDistance >
                closestDistance)
            {
                continue;
            }

            if (!TryRayIntersectModelTriangles(
                    renderScene,
                    range,
                    rayOrigin,
                    rayDirection,
                    out float modelDistance) ||
                modelDistance >=
                closestDistance)
            {
                continue;
            }

            closestDistance =
                modelDistance;

            model =
                range.Model;

            part =
                range.Part;
        }

        return model !=
            null;
    }

    /// <summary>
    /// BuildRenderScene currently appends each model's transformed meshes in
    /// document order. The source-mesh cache records how many flattened meshes
    /// belong to each model.
    /// </summary>
    private bool TryCreateSceneModelMeshRanges(
        SceneData renderScene,
        out List<SceneModelMeshRange> ranges)
    {
        ranges =
            new List<SceneModelMeshRange>();

        int firstMeshIndex =
            0;

        for (int modelIndex = 0;
             modelIndex < _sceneDocument.Models.Count;
             modelIndex++)
        {
            SceneModel model =
                _sceneDocument.Models[
                    modelIndex];

            for (int partIndex = 0;
                 partIndex < model.Parts.Count;
                 partIndex++)
            {
                ranges.Add(
                    new SceneModelMeshRange(
                        model,
                        model.Parts[partIndex],
                        firstMeshIndex,
                        1));

                firstMeshIndex++;
            }
        }

        if (ranges.Count > 0 &&
            firstMeshIndex <
            renderScene.Meshes.Count)
        {
            // Keep the final model usable if BuildRenderScene ever appends a
            // small number of helper meshes after its normal source meshes.
            SceneModelMeshRange previousLast =
                ranges[
                    ranges.Count - 1];

            ranges[
                ranges.Count - 1] =
                new SceneModelMeshRange(
                    previousLast.Model,
                    previousLast.Part,
                    previousLast.FirstMeshIndex,
                    previousLast.MeshCount +
                    renderScene.Meshes.Count -
                    firstMeshIndex);
        }

        return true;
    }

    private static bool TryFindModelRange(
        List<SceneModelMeshRange> ranges,
        SceneModel model,
        SceneMeshPart? part,
        out SceneModelMeshRange result)
    {
        int firstMeshIndex = -1;
        int meshCount = 0;

        foreach (SceneModelMeshRange range in
                 ranges)
        {
            if (ReferenceEquals(
                    range.Model,
                    model))
            {
                if (part != null)
                {
                    if (ReferenceEquals(range.Part, part))
                    {
                        result = range;
                        return true;
                    }

                    continue;
                }

                if (firstMeshIndex < 0)
                {
                    firstMeshIndex = range.FirstMeshIndex;
                }

                meshCount += range.MeshCount;
            }
        }

        if (firstMeshIndex >= 0)
        {
            result = new SceneModelMeshRange(
                model,
                part: null,
                firstMeshIndex,
                meshCount);
            return true;
        }

        result =
            default;

        return false;
    }

    private static bool TryCalculateBounds(
        SceneData scene,
        SceneModelMeshRange range,
        out Vector3 min,
        out Vector3 max)
    {
        min =
            new Vector3(
                float.MaxValue);

        max =
            new Vector3(
                float.MinValue);

        bool foundPosition =
            false;

        int endMeshIndex =
            Math.Min(
                scene.Meshes.Count,
                range.FirstMeshIndex +
                range.MeshCount);

        for (int meshIndex = range.FirstMeshIndex;
             meshIndex < endMeshIndex;
             meshIndex++)
        {
            Vector3[] positions =
                scene.Meshes[
                    meshIndex]
                .Positions;

            for (int positionIndex = 0;
                 positionIndex < positions.Length;
                 positionIndex++)
            {
                Vector3 position =
                    positions[
                        positionIndex];

                min =
                    Vector3.Min(
                        min,
                        position);

                max =
                    Vector3.Max(
                        max,
                        position);

                foundPosition =
                    true;
            }
        }

        return foundPosition;
    }

    private static bool TryRayIntersectModelTriangles(
        SceneData scene,
        SceneModelMeshRange range,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        out float closestDistance)
    {
        closestDistance =
            float.MaxValue;

        bool found =
            false;

        int endMeshIndex =
            Math.Min(
                scene.Meshes.Count,
                range.FirstMeshIndex +
                range.MeshCount);

        for (int meshIndex = range.FirstMeshIndex;
             meshIndex < endMeshIndex;
             meshIndex++)
        {
            MeshData mesh =
                scene.Meshes[
                    meshIndex];

            Vector3[] positions =
                mesh.Positions;

            int[] indices =
                mesh.Indices;

            if (indices.Length >= 3)
            {
                for (int index = 0;
                     index + 2 < indices.Length;
                     index += 3)
                {
                    int index0 =
                        indices[
                            index];

                    int index1 =
                        indices[
                            index + 1];

                    int index2 =
                        indices[
                            index + 2];

                    if ((uint)index0 >=
                            (uint)positions.Length ||
                        (uint)index1 >=
                            (uint)positions.Length ||
                        (uint)index2 >=
                            (uint)positions.Length)
                    {
                        continue;
                    }

                    if (TryRayIntersectTriangle(
                            rayOrigin,
                            rayDirection,
                            positions[index0],
                            positions[index1],
                            positions[index2],
                            out float distance) &&
                        distance <
                        closestDistance)
                    {
                        closestDistance =
                            distance;

                        found =
                            true;
                    }
                }
            }
            else
            {
                for (int positionIndex = 0;
                     positionIndex + 2 < positions.Length;
                     positionIndex += 3)
                {
                    if (TryRayIntersectTriangle(
                            rayOrigin,
                            rayDirection,
                            positions[positionIndex],
                            positions[positionIndex + 1],
                            positions[positionIndex + 2],
                            out float distance) &&
                        distance <
                        closestDistance)
                    {
                        closestDistance =
                            distance;

                        found =
                            true;
                    }
                }
            }
        }

        return found;
    }

    private static bool TryRayIntersectTriangle(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        Vector3 vertex0,
        Vector3 vertex1,
        Vector3 vertex2,
        out float distance)
    {
        distance =
            0.0f;

        const float epsilon =
            0.0000001f;

        Vector3 edge1 =
            vertex1 -
            vertex0;

        Vector3 edge2 =
            vertex2 -
            vertex0;

        Vector3 perpendicular =
            Vector3.Cross(
                rayDirection,
                edge2);

        float determinant =
            Vector3.Dot(
                edge1,
                perpendicular);

        if (MathF.Abs(
                determinant) <=
            epsilon)
        {
            return false;
        }

        float inverseDeterminant =
            1.0f /
            determinant;

        Vector3 fromVertex =
            rayOrigin -
            vertex0;

        float barycentricU =
            Vector3.Dot(
                fromVertex,
                perpendicular) *
            inverseDeterminant;

        if (barycentricU <
                0.0f ||
            barycentricU >
                1.0f)
        {
            return false;
        }

        Vector3 cross =
            Vector3.Cross(
                fromVertex,
                edge1);

        float barycentricV =
            Vector3.Dot(
                rayDirection,
                cross) *
            inverseDeterminant;

        if (barycentricV <
                0.0f ||
            barycentricU +
                barycentricV >
                1.0f)
        {
            return false;
        }

        float hitDistance =
            Vector3.Dot(
                edge2,
                cross) *
            inverseDeterminant;

        if (hitDistance <=
            epsilon)
        {
            return false;
        }

        distance =
            hitDistance;

        return true;
    }

    private static bool TryRayIntersectBounds(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        Vector3 min,
        Vector3 max,
        out float distance)
    {
        float minimumDistance =
            0.0f;

        float maximumDistance =
            float.MaxValue;

        if (!TryIntersectAxisSlab(
                rayOrigin.X,
                rayDirection.X,
                min.X,
                max.X,
                ref minimumDistance,
                ref maximumDistance) ||
            !TryIntersectAxisSlab(
                rayOrigin.Y,
                rayDirection.Y,
                min.Y,
                max.Y,
                ref minimumDistance,
                ref maximumDistance) ||
            !TryIntersectAxisSlab(
                rayOrigin.Z,
                rayDirection.Z,
                min.Z,
                max.Z,
                ref minimumDistance,
                ref maximumDistance))
        {
            distance =
                0.0f;

            return false;
        }

        distance =
            minimumDistance;

        return maximumDistance >=
            0.0f;
    }

    private static bool TryIntersectAxisSlab(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float minimumDistance,
        ref float maximumDistance)
    {
        const float epsilon =
            0.0000001f;

        if (MathF.Abs(
                direction) <=
            epsilon)
        {
            return origin >=
                    minimum &&
                origin <=
                    maximum;
        }

        float inverseDirection =
            1.0f /
            direction;

        float distance0 =
            (minimum -
             origin) *
            inverseDirection;

        float distance1 =
            (maximum -
             origin) *
            inverseDirection;

        if (distance0 >
            distance1)
        {
            (distance0, distance1) =
                (distance1, distance0);
        }

        minimumDistance =
            MathF.Max(
                minimumDistance,
                distance0);

        maximumDistance =
            MathF.Min(
                maximumDistance,
                distance1);

        return maximumDistance >=
            minimumDistance;
    }

}
