using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Reflection;

namespace FastViewDX12;

// Viewport ray picking and selection-aware camera framing.
public sealed partial class MainForm
{
    private readonly struct SceneModelMeshRange
    {
        public SceneModelMeshRange(
            SceneModel model,
            int firstMeshIndex,
            int meshCount)
        {
            Model =
                model;

            FirstMeshIndex =
                firstMeshIndex;

            MeshCount =
                meshCount;
        }

        public SceneModel Model { get; }

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
                out SceneModel? hitModel)
                ? hitModel
                : null;

        if (!TrySetSceneDocumentSelection(
                model))
        {
            Debug.WriteLine(
                "Viewport selection could not update SceneDocument. " +
                "No compatible selection member was found.");

            return;
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
        out SceneModel? model)
    {
        model =
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
            new List<SceneModelMeshRange>(
                _sceneDocument.Models.Count);

        int firstMeshIndex =
            0;

        for (int modelIndex = 0;
             modelIndex < _sceneDocument.Models.Count;
             modelIndex++)
        {
            SceneModel model =
                _sceneDocument.Models[
                    modelIndex];

            if (!_sourceMeshCountByModel.TryGetValue(
                    model,
                    out int meshCount))
            {
                // One-model scenes are unambiguous, including scenes that
                // existed before this cache was introduced.
                if (_sceneDocument.Models.Count == 1)
                {
                    meshCount =
                        renderScene.Meshes.Count;
                }
                else
                {
                    return false;
                }
            }

            int remainingMeshCount =
                Math.Max(
                    0,
                    renderScene.Meshes.Count -
                    firstMeshIndex);

            meshCount =
                Math.Clamp(
                    meshCount,
                    0,
                    remainingMeshCount);

            ranges.Add(
                new SceneModelMeshRange(
                    model,
                    firstMeshIndex,
                    meshCount));

            firstMeshIndex +=
                meshCount;
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
        out SceneModelMeshRange result)
    {
        foreach (SceneModelMeshRange range in
                 ranges)
        {
            if (ReferenceEquals(
                    range.Model,
                    model))
            {
                result =
                    range;

                return true;
            }
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

    /// <summary>
    /// SceneDocument already owns selection for the sidebar and gizmo. This
    /// compatibility adapter calls its existing selection API without adding a
    /// second, competing selection state.
    /// </summary>
    private bool TrySetSceneDocumentSelection(
        SceneModel? model)
    {
        object document =
            _sceneDocument;

        Type documentType =
            document.GetType();

        const BindingFlags bindingFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        if (model == null)
        {
            string[] clearMethodNames =
            [
                "ClearSelection",
                "ClearSelectedModel",
                "Deselect",
                "DeselectAll"
            ];

            foreach (string methodName in
                     clearMethodNames)
            {
                MethodInfo? clearMethod =
                    documentType.GetMethod(
                        methodName,
                        bindingFlags,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null);

                if (clearMethod == null)
                {
                    continue;
                }

                clearMethod.Invoke(
                    document,
                    null);

                return true;
            }
        }

        string[] selectionMethodNames =
        [
            "Select",
            "SelectModel",
            "SetSelection",
            "SetSelected",
            "SetSelectedModel",
            "SetSelectedModelId",
            "SetSelectedId",
            "SelectSceneModel",
            "SelectById"
        ];

        foreach (string methodName in
                 selectionMethodNames)
        {
            MethodInfo[] methods =
                documentType.GetMethods(
                    bindingFlags);

            foreach (MethodInfo method in
                     methods)
            {
                if (!method.Name.Equals(
                        methodName,
                        StringComparison.Ordinal) ||
                    method.GetParameters().Length !=
                    1)
                {
                    continue;
                }

                ParameterInfo parameter =
                    method.GetParameters()[0];

                if (!TryCreateSelectionValue(
                        parameter.ParameterType,
                        model,
                        out object? value))
                {
                    continue;
                }

                method.Invoke(
                    document,
                    [value]);

                return true;
            }
        }

        string[] propertyNames =
        [
            "SelectedModel",
            "SelectedModelId",
            "SelectedId"
        ];

        foreach (string propertyName in
                 propertyNames)
        {
            PropertyInfo? property =
                documentType.GetProperty(
                    propertyName,
                    bindingFlags);

            if (property?.SetMethod == null ||
                !TryCreateSelectionValue(
                    property.PropertyType,
                    model,
                    out object? value))
            {
                continue;
            }

            property.SetValue(
                document,
                value);

            return true;
        }

        FieldInfo[] fields =
            documentType.GetFields(
                bindingFlags);

        foreach (FieldInfo field in
                 fields)
        {
            if (!field.Name.Contains(
                    "selected",
                    StringComparison.OrdinalIgnoreCase) ||
                !TryCreateSelectionValue(
                    field.FieldType,
                    model,
                    out object? value))
            {
                continue;
            }

            field.SetValue(
                document,
                value);

            return true;
        }

        return false;
    }

    private static bool TryCreateSelectionValue(
        Type targetType,
        SceneModel? model,
        out object? value)
    {
        if (model == null)
        {
            if (!targetType.IsValueType ||
                Nullable.GetUnderlyingType(
                    targetType) != null)
            {
                value =
                    null;

                return true;
            }

            if (targetType ==
                typeof(Guid))
            {
                value =
                    Guid.Empty;

                return true;
            }

            value =
                null;

            return false;
        }

        if (targetType.IsInstanceOfType(
                model))
        {
            value =
                model;

            return true;
        }

        object modelId =
            model.Id;

        if (targetType.IsInstanceOfType(
                modelId))
        {
            value =
                modelId;

            return true;
        }

        Type? nullableType =
            Nullable.GetUnderlyingType(
                targetType);

        if (nullableType != null &&
            nullableType.IsInstanceOfType(
                modelId))
        {
            value =
                modelId;

            return true;
        }

        value =
            null;

        return false;
    }
}
