using System;
using System.Collections.Generic;
using System.Numerics;

namespace FastViewDX12;

// Optional editor-only ground grid. It is appended only to the temporary
// renderer scene, so it never becomes part of source models or scene exports.
public sealed partial class MainForm
{
    private const float VisibleGridAlpha =
        0.34f;

    private bool _gridVisible;

    private readonly MaterialData _gridMaterial =
        new()
        {
            Name =
                "FastView Editor Grid",

            // The grid geometry is kept resident even while hidden. G only
            // changes this alpha value, avoiding a complete scene/texture reload.
            BaseColorFactor =
                new Vector4(
                    0.38f,
                    0.40f,
                    0.46f,
                    0.0f),

            EmissiveFactor =
                new Vector4(
                    0.045f,
                    0.05f,
                    0.065f,
                    1.0f),

            MetallicFactor =
                0.0f,

            RoughnessFactor =
                1.0f,

            AlphaMode =
                MeshAlphaMode.Blend,

            DoubleSided =
                true,

            Unlit =
                true
        };

    /// <summary>
    /// Adds editor-only helpers to a freshly flattened render scene.
    /// </summary>
    private SceneData BuildEditorRenderScene()
    {
        SceneData scene =
            _sceneDocument.BuildRenderScene();

        // Keep the small editor grid mesh and material resident. Hiding it
        // is then just an alpha change instead of a synchronous GPU rebuild.
        AppendGroundGrid(
            scene);

        return scene;
    }

    /// <summary>
    /// Toggles the XZ ground grid without changing the camera framing.
    /// </summary>
    private void ToggleGroundGrid()
    {
        _gridVisible =
            !_gridVisible;

        Vector4 currentColor =
            _gridMaterial.BaseColorFactor;

        _gridMaterial.BaseColorFactor =
            new Vector4(
                currentColor.X,
                currentColor.Y,
                currentColor.Z,
                _gridVisible
                    ? VisibleGridAlpha
                    : 0.0f);

        // Shader constants read MaterialData every frame, so no geometry,
        // texture, descriptor-heap, or scene rebuild is required here.
        _renderer.Render();
    }

    private void AppendGroundGrid(
        SceneData scene)
    {
        if (!TryCalculateSceneXZBounds(
                scene,
                out float minX,
                out float maxX,
                out float minZ,
                out float maxZ))
        {
            return;
        }

        // The grid is centered on the world origin and extends equally in
        // positive and negative X/Z. Use the farthest scene extent so the
        // complete model remains covered even when it is offset from zero.
        float halfExtent =
            MathF.Max(
                MathF.Max(
                    MathF.Abs(minX),
                    MathF.Abs(maxX)),
                MathF.Max(
                    MathF.Abs(minZ),
                    MathF.Abs(maxZ)));

        if (halfExtent <
            0.001f)
        {
            halfExtent =
                1.0f;
        }

        float largestSpan =
            halfExtent *
            2.0f;

        float spacing =
            CalculateNiceGridSpacing(
                largestSpan /
                12.0f);

        float roundedHalfExtent =
            MathF.Ceiling(
                halfExtent /
                spacing) *
            spacing +
            spacing;

        float gridMinX =
            -roundedHalfExtent;

        float gridMaxX =
            roundedHalfExtent;

        float gridMinZ =
            -roundedHalfExtent;

        float gridMaxZ =
            roundedHalfExtent;

        while ((gridMaxX - gridMinX) /
                   spacing +
               (gridMaxZ - gridMinZ) /
                   spacing >
               160.0f)
        {
            spacing *=
                2.0f;
        }

        float lineWidth =
            MathF.Max(
                spacing *
                0.018f,
                largestSpan *
                0.00035f);

        // A tiny downward offset prevents z-fighting with models resting on Y=0
        // while remaining visually located at the world-origin ground plane.
        float gridY =
            -MathF.Max(
                spacing *
                0.0005f,
                0.000001f);

        var positions =
            new List<Vector3>();

        var normals =
            new List<Vector3>();

        var tangents =
            new List<Vector4>();

        var texCoords0 =
            new List<Vector2>();

        var texCoords1 =
            new List<Vector2>();

        var indices =
            new List<int>();

        for (float x = gridMinX;
             x <= gridMaxX +
                 spacing *
                 0.25f;
             x += spacing)
        {
            bool originLine =
                MathF.Abs(x) <=
                spacing *
                0.1f;

            AddGridStrip(
                positions,
                normals,
                tangents,
                texCoords0,
                texCoords1,
                indices,
                new Vector3(
                    x,
                    gridY,
                    gridMinZ),
                new Vector3(
                    x,
                    gridY,
                    gridMaxZ),
                originLine
                    ? lineWidth *
                      2.4f
                    : lineWidth);
        }

        for (float z = gridMinZ;
             z <= gridMaxZ +
                 spacing *
                 0.25f;
             z += spacing)
        {
            bool originLine =
                MathF.Abs(z) <=
                spacing *
                0.1f;

            AddGridStrip(
                positions,
                normals,
                tangents,
                texCoords0,
                texCoords1,
                indices,
                new Vector3(
                    gridMinX,
                    gridY,
                    z),
                new Vector3(
                    gridMaxX,
                    gridY,
                    z),
                originLine
                    ? lineWidth *
                      2.4f
                    : lineWidth);
        }

        int materialIndex =
            scene.Materials.Count;

        scene.Materials.Add(
            _gridMaterial);

        scene.Meshes.Add(
            new MeshData
            {
                Name =
                    "FastView Editor Grid",

                MaterialIndex =
                    materialIndex,

                Positions =
                    positions.ToArray(),

                Normals =
                    normals.ToArray(),

                Tangents =
                    tangents.ToArray(),

                TexCoords0 =
                    texCoords0.ToArray(),

                TexCoords1 =
                    texCoords1.ToArray(),

                Indices =
                    indices.ToArray(),

                ResolvedAlphaMode =
                    MeshAlphaMode.Blend
            });
    }

    private static bool TryCalculateSceneXZBounds(
        SceneData scene,
        out float minX,
        out float maxX,
        out float minZ,
        out float maxZ)
    {
        minX =
            float.MaxValue;

        maxX =
            float.MinValue;

        minZ =
            float.MaxValue;

        maxZ =
            float.MinValue;

        bool foundPosition =
            false;

        foreach (MeshData mesh in
                 scene.Meshes)
        {
            foreach (Vector3 position in
                     mesh.Positions)
            {
                minX =
                    MathF.Min(
                        minX,
                        position.X);

                maxX =
                    MathF.Max(
                        maxX,
                        position.X);

                minZ =
                    MathF.Min(
                        minZ,
                        position.Z);

                maxZ =
                    MathF.Max(
                        maxZ,
                        position.Z);

                foundPosition =
                    true;
            }
        }

        return foundPosition;
    }

    private static float CalculateNiceGridSpacing(
        float requestedSpacing)
    {
        requestedSpacing =
            MathF.Max(
                requestedSpacing,
                0.000001f);

        float power =
            MathF.Pow(
                10.0f,
                MathF.Floor(
                    MathF.Log10(
                        requestedSpacing)));

        float normalized =
            requestedSpacing /
            power;

        float multiplier =
            normalized <= 1.0f
                ? 1.0f
                : normalized <= 2.0f
                    ? 2.0f
                    : normalized <= 5.0f
                        ? 5.0f
                        : 10.0f;

        return multiplier *
               power;
    }

    private static void AddGridStrip(
        List<Vector3> positions,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Vector2> texCoords0,
        List<Vector2> texCoords1,
        List<int> indices,
        Vector3 start,
        Vector3 end,
        float width)
    {
        Vector2 direction =
            new(
                end.X -
                start.X,
                end.Z -
                start.Z);

        float length =
            direction.Length();

        if (length <=
            0.000001f)
        {
            return;
        }

        direction /=
            length;

        Vector2 perpendicular =
            new(
                -direction.Y,
                direction.X);

        perpendicular *=
            width *
            0.5f;

        int firstVertex =
            positions.Count;

        positions.Add(
            new Vector3(
                start.X +
                perpendicular.X,
                start.Y,
                start.Z +
                perpendicular.Y));

        positions.Add(
            new Vector3(
                start.X -
                perpendicular.X,
                start.Y,
                start.Z -
                perpendicular.Y));

        positions.Add(
            new Vector3(
                end.X -
                perpendicular.X,
                end.Y,
                end.Z -
                perpendicular.Y));

        positions.Add(
            new Vector3(
                end.X +
                perpendicular.X,
                end.Y,
                end.Z +
                perpendicular.Y));

        for (int index = 0;
             index < 4;
             index++)
        {
            normals.Add(
                Vector3.UnitY);

            tangents.Add(
                new Vector4(
                    Vector3.UnitX,
                    1.0f));

            texCoords0.Add(
                Vector2.Zero);

            texCoords1.Add(
                Vector2.Zero);
        }

        indices.Add(
            firstVertex);

        indices.Add(
            firstVertex +
            1);

        indices.Add(
            firstVertex +
            2);

        indices.Add(
            firstVertex);

        indices.Add(
            firstVertex +
            2);

        indices.Add(
            firstVertex +
            3);
    }
}
