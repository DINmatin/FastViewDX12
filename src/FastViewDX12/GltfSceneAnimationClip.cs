using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace FastViewDX12;

/// <summary>
/// Samples SharpGLTF node curves and performs glTF linear-blend skinning into
/// the viewer's flattened world-space mesh representation.
/// </summary>
internal sealed class GltfSceneAnimationClip : SceneAnimationClip
{
    private sealed record Binding(
        MeshData Mesh,
        Node Node,
        Vector3[] Positions,
        Vector3[] Normals,
        Vector4[] Tangents,
        Vector4[] Joints,
        Vector4[] Weights);

    private static readonly ConditionalWeakTable<MeshData, Binding> PendingBindings = new();

    private readonly Animation _animation;
    private readonly Binding[] _bindings;

    public GltfSceneAnimationClip(
        Animation animation,
        IReadOnlyList<MeshData> meshes)
        : base(animation.Name ?? "Animation", animation.Duration)
    {
        _animation = animation;
        var bindings = new List<Binding>(meshes.Count);

        foreach (MeshData mesh in meshes)
        {
            if (PendingBindings.TryGetValue(mesh, out Binding? binding))
            {
                bindings.Add(binding);
            }
        }

        _bindings = bindings.ToArray();
    }

    internal static void RegisterBinding(
        MeshData mesh,
        Node node,
        Vector3[] positions,
        Vector3[] normals,
        Vector4[] tangents,
        Vector4[] joints,
        Vector4[] weights)
    {
        PendingBindings.Add(
            mesh,
            new Binding(
                mesh,
                node,
                positions,
                normals,
                tangents,
                joints,
                weights));
    }

    public override void Apply(float timeSeconds)
    {
        float time = Duration > 0.0f
            ? Math.Clamp(timeSeconds, 0.0f, Duration)
            : 0.0f;

        foreach (Binding binding in _bindings)
        {
            if (binding.Node.Skin != null &&
                binding.Joints.Length == binding.Positions.Length &&
                binding.Weights.Length == binding.Positions.Length)
            {
                ApplySkin(binding, time);
            }
            else
            {
                ApplyNodeTransform(binding, time);
            }
        }
    }

    private void ApplyNodeTransform(Binding binding, float time)
    {
        Matrix4x4 world = binding.Node.GetWorldMatrix(_animation, time);
        Matrix4x4 normalMatrix = CreateNormalMatrix(world);

        for (int index = 0; index < binding.Positions.Length; index++)
        {
            binding.Mesh.Positions[index] = Vector3.Transform(binding.Positions[index], world);
            binding.Mesh.Normals[index] = Normalize(
                Vector3.TransformNormal(binding.Normals[index], normalMatrix),
                Vector3.UnitY);
            binding.Mesh.Tangents[index] = TransformTangent(
                binding.Tangents[index],
                world,
                binding.Mesh.Normals[index]);
        }
    }

    private void ApplySkin(Binding binding, float time)
    {
        Skin skin = binding.Node.Skin!;
        int jointCount = Math.Min(skin.Joints.Count, skin.InverseBindMatrices.Count);
        var jointMatrices = new Matrix4x4[jointCount];
        var normalMatrices = new Matrix4x4[jointCount];

        for (int index = 0; index < jointCount; index++)
        {
            jointMatrices[index] =
                skin.InverseBindMatrices[index] *
                skin.Joints[index].GetWorldMatrix(_animation, time);
            normalMatrices[index] = CreateNormalMatrix(jointMatrices[index]);
        }

        for (int vertex = 0; vertex < binding.Positions.Length; vertex++)
        {
            Vector4 jointSet = binding.Joints[vertex];
            Vector4 weightSet = binding.Weights[vertex];
            float weightSum = weightSet.X + weightSet.Y + weightSet.Z + weightSet.W;
            if (weightSum <= 0.000001f)
            {
                binding.Mesh.Positions[vertex] = binding.Positions[vertex];
                binding.Mesh.Normals[vertex] = binding.Normals[vertex];
                binding.Mesh.Tangents[vertex] = binding.Tangents[vertex];
                continue;
            }

            Vector3 position = Vector3.Zero;
            Vector3 normal = Vector3.Zero;
            Vector3 tangent = Vector3.Zero;
            Vector3 sourceTangent = new(
                binding.Tangents[vertex].X,
                binding.Tangents[vertex].Y,
                binding.Tangents[vertex].Z);

            for (int component = 0; component < 4; component++)
            {
                int jointIndex = (int)GetComponent(jointSet, component);
                float weight = GetComponent(weightSet, component) / weightSum;
                if (weight <= 0.0f || jointIndex < 0 || jointIndex >= jointCount)
                {
                    continue;
                }

                position += Vector3.Transform(binding.Positions[vertex], jointMatrices[jointIndex]) * weight;
                normal += Vector3.TransformNormal(binding.Normals[vertex], normalMatrices[jointIndex]) * weight;
                tangent += Vector3.TransformNormal(sourceTangent, jointMatrices[jointIndex]) * weight;
            }

            normal = Normalize(normal, Vector3.UnitY);
            tangent -= normal * Vector3.Dot(normal, tangent);
            tangent = Normalize(tangent, Vector3.UnitX);

            binding.Mesh.Positions[vertex] = position;
            binding.Mesh.Normals[vertex] = normal;
            binding.Mesh.Tangents[vertex] = new Vector4(tangent, binding.Tangents[vertex].W);
        }
    }

    private static Matrix4x4 CreateNormalMatrix(Matrix4x4 matrix)
    {
        return Matrix4x4.Invert(matrix, out Matrix4x4 inverse)
            ? Matrix4x4.Transpose(inverse)
            : matrix;
    }

    private static Vector4 TransformTangent(Vector4 tangent, Matrix4x4 matrix, Vector3 normal)
    {
        Vector3 transformed = Vector3.TransformNormal(new Vector3(tangent.X, tangent.Y, tangent.Z), matrix);
        transformed -= normal * Vector3.Dot(normal, transformed);
        return new Vector4(Normalize(transformed, Vector3.UnitX), tangent.W);
    }

    private static Vector3 Normalize(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() > 0.00000001f &&
               float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z)
            ? Vector3.Normalize(value)
            : fallback;
    }

    private static float GetComponent(Vector4 value, int index)
    {
        return index switch
        {
            0 => value.X,
            1 => value.Y,
            2 => value.Z,
            _ => value.W
        };
    }
}
