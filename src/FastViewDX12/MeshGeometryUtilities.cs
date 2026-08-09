using System.Numerics;

namespace FastViewDX12;

internal static class MeshGeometryUtilities
{
    public static Vector3[] CalculateNormals(
        Vector3[] positions,
        int[] indices)
    {
        var normals = new Vector3[positions.Length];

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            if (!IsValid(i0, positions.Length) ||
                !IsValid(i1, positions.Length) ||
                !IsValid(i2, positions.Length))
            {
                continue;
            }

            Vector3 faceNormal = Vector3.Cross(
                positions[i1] - positions[i0],
                positions[i2] - positions[i0]);

            if (faceNormal.LengthSquared() <= 0.0000000001f)
            {
                continue;
            }

            normals[i0] += faceNormal;
            normals[i1] += faceNormal;
            normals[i2] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = NormalizeOrFallback(normals[i], Vector3.UnitY);
        }

        return normals;
    }

    public static Vector4[] CalculateTangents(
        Vector3[] positions,
        Vector3[] normals,
        Vector2[] texCoords,
        int[] indices)
    {
        var result = new Vector4[positions.Length];

        if (normals.Length != positions.Length ||
            texCoords.Length != positions.Length)
        {
            FillFallbackTangents(result, normals);
            return result;
        }

        var tangentSums = new Vector3[positions.Length];
        var bitangentSums = new Vector3[positions.Length];

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            if (!IsValid(i0, positions.Length) ||
                !IsValid(i1, positions.Length) ||
                !IsValid(i2, positions.Length))
            {
                continue;
            }

            Vector3 edge1 = positions[i1] - positions[i0];
            Vector3 edge2 = positions[i2] - positions[i0];
            Vector2 deltaUv1 = texCoords[i1] - texCoords[i0];
            Vector2 deltaUv2 = texCoords[i2] - texCoords[i0];
            float determinant =
                deltaUv1.X * deltaUv2.Y -
                deltaUv1.Y * deltaUv2.X;

            if (MathF.Abs(determinant) <= 0.00000001f)
            {
                continue;
            }

            float inverse = 1.0f / determinant;
            Vector3 tangent =
                (edge1 * deltaUv2.Y - edge2 * deltaUv1.Y) * inverse;
            Vector3 bitangent =
                (edge2 * deltaUv1.X - edge1 * deltaUv2.X) * inverse;

            tangentSums[i0] += tangent;
            tangentSums[i1] += tangent;
            tangentSums[i2] += tangent;
            bitangentSums[i0] += bitangent;
            bitangentSums[i1] += bitangent;
            bitangentSums[i2] += bitangent;
        }

        for (int i = 0; i < result.Length; i++)
        {
            Vector3 normal = NormalizeOrFallback(normals[i], Vector3.UnitY);
            Vector3 tangent = tangentSums[i] -
                normal * Vector3.Dot(normal, tangentSums[i]);

            tangent = tangent.LengthSquared() <= 0.00000001f
                ? CreateFallbackTangent(normal)
                : Vector3.Normalize(tangent);

            float handedness = Vector3.Dot(
                Vector3.Cross(normal, tangent),
                bitangentSums[i]) < 0.0f
                    ? -1.0f
                    : 1.0f;

            result[i] = new Vector4(tangent, handedness);
        }

        return result;
    }

    private static void FillFallbackTangents(
        Vector4[] tangents,
        Vector3[] normals)
    {
        for (int i = 0; i < tangents.Length; i++)
        {
            Vector3 normal = normals.Length == tangents.Length
                ? NormalizeOrFallback(normals[i], Vector3.UnitY)
                : Vector3.UnitY;

            tangents[i] = new Vector4(
                CreateFallbackTangent(normal),
                1.0f);
        }
    }

    private static Vector3 CreateFallbackTangent(Vector3 normal)
    {
        Vector3 reference = MathF.Abs(normal.Y) < 0.999f
            ? Vector3.UnitY
            : Vector3.UnitX;

        return Vector3.Normalize(Vector3.Cross(reference, normal));
    }

    private static Vector3 NormalizeOrFallback(
        Vector3 value,
        Vector3 fallback)
    {
        return value.LengthSquared() > 0.00000001f
            ? Vector3.Normalize(value)
            : fallback;
    }

    private static bool IsValid(int index, int count) =>
        index >= 0 && index < count;
}

