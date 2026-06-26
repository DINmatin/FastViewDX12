using System;
using System.Collections.Generic;
using System.Numerics;

namespace FastViewDX12;

/// <summary>
/// Defines how a mesh primitive participates in the renderer's alpha pipeline.
/// </summary>
public enum MeshAlphaMode
{
    /// <summary>The primitive writes opaque color and depth.</summary>
    Opaque,

    /// <summary>The primitive discards pixels below an alpha cutoff.</summary>
    Mask,

    /// <summary>The primitive is rendered in the transparent back-to-front queue.</summary>
    Blend
}

/// <summary>
/// Renderer-neutral scene container produced by the glTF loader.
/// </summary>
public sealed class SceneData
{
    /// <summary>Gets the unique material definitions referenced by scene meshes.</summary>
    public List<MaterialData> Materials { get; } = new();

    /// <summary>Gets the flattened glTF mesh primitives ready for GPU upload.</summary>
    public List<MeshData> Meshes { get; } = new();
}

/// <summary>
/// Texture addressing modes supported by the glTF 2.0 sampler model.
/// The numeric values are intentionally compact because one S/T pair is
/// packed into a single shader constant.
/// </summary>
public enum TextureWrapModeData
{
    /// <summary>Tiles the texture for coordinates outside the zero-to-one range.</summary>
    Repeat = 0,

    /// <summary>Extends the nearest edge texel beyond the zero-to-one range.</summary>
    ClampToEdge = 1,

    /// <summary>Tiles the texture while mirroring every second repetition.</summary>
    MirroredRepeat = 2
}

/// <summary>
/// Describes which glTF texture-coordinate set a material channel uses,
/// its optional KHR_texture_transform matrix, and its sampler addressing modes.
/// </summary>
public sealed class TextureMappingData
{
    /// <summary>
    /// Gets or sets the TEXCOORD_n set selected by the texture channel.
    /// </summary>
    public int TextureCoordinate { get; set; }

    /// <summary>
    /// Gets or sets the affine UV transform. Identity means no transform.
    /// </summary>
    public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;

    /// <summary>Gets or sets the sampler addressing mode for the U/S axis.</summary>
    public TextureWrapModeData WrapS { get; set; } = TextureWrapModeData.Repeat;

    /// <summary>Gets or sets the sampler addressing mode for the V/T axis.</summary>
    public TextureWrapModeData WrapT { get; set; } = TextureWrapModeData.Repeat;

    /// <summary>
    /// Packs the U and V addressing modes into one value for the shader.
    /// </summary>
    public int GetSamplerIndex()
    {
        return (int)WrapS +
               (int)WrapT * 3;
    }

    /// <summary>
    /// Applies the channel's KHR_texture_transform matrix to one UV coordinate.
    /// </summary>
    public Vector2 Apply(Vector2 textureCoordinate)
    {
        return Vector2.Transform(
            textureCoordinate,
            Transform);
    }
}

/// <summary>
/// Renderer-neutral glTF material data, including encoded texture payloads and physically based factors.
/// </summary>
public sealed class MaterialData
{
    /// <summary>Gets or sets the display name copied from the glTF material.</summary>
    public string Name { get; set; } = "Material";

    /// <summary>Gets or sets encoded base-color image bytes, or null when no texture is assigned.</summary>
    public byte[]? BaseColorTextureBytes { get; set; }

    /// <summary>Gets or sets encoded tangent-space normal image bytes.</summary>
    public byte[]? NormalTextureBytes { get; set; }

    /// <summary>Gets or sets encoded glTF metallic-roughness image bytes.</summary>
    public byte[]? MetallicRoughnessTextureBytes { get; set; }

    /// <summary>Gets or sets encoded emissive image bytes.</summary>
    public byte[]? EmissiveTextureBytes { get; set; }

    /// <summary>Gets the UV set and transform used by the base-color texture.</summary>
    public TextureMappingData BaseColorTextureMapping { get; } = new();

    /// <summary>Gets the UV set and transform used by the normal texture.</summary>
    public TextureMappingData NormalTextureMapping { get; } = new();

    /// <summary>Gets the UV set and transform used by the metallic-roughness texture.</summary>
    public TextureMappingData MetallicRoughnessTextureMapping { get; } = new();

    /// <summary>Gets the UV set and transform used by the emissive texture.</summary>
    public TextureMappingData EmissiveTextureMapping { get; } = new();

    /// <summary>Gets or sets the linear RGBA base-color multiplier.</summary>
    public Vector4 BaseColorFactor { get; set; } = Vector4.One;

    /// <summary>Gets or sets the linear RGB emissive multiplier. The W component is reserved.</summary>
    public Vector4 EmissiveFactor { get; set; } = new(0.0f, 0.0f, 0.0f, 1.0f);

    /// <summary>Gets or sets the metallic factor defined by the glTF material.</summary>
    public float MetallicFactor { get; set; } = 1.0f;

    /// <summary>Gets or sets the roughness factor defined by the glTF material.</summary>
    public float RoughnessFactor { get; set; } = 1.0f;

    /// <summary>Gets or sets the tangent-space normal-map strength.</summary>
    public float NormalScale { get; set; } = 1.0f;

    /// <summary>Gets or sets the alpha mode declared by the glTF material.</summary>
    public MeshAlphaMode AlphaMode { get; set; } = MeshAlphaMode.Opaque;

    /// <summary>Gets or sets the alpha-test threshold used by masked materials.</summary>
    public float AlphaCutoff { get; set; } = 0.5f;

    /// <summary>Gets or sets whether back-face culling is disabled.</summary>
    public bool DoubleSided { get; set; }

    /// <summary>Gets or sets whether the material bypasses environment and direct lighting.</summary>
    public bool Unlit { get; set; }
}

/// <summary>
/// One flattened glTF primitive with world-space vertex attributes and resolved material state.
/// </summary>
public sealed class MeshData
{
    /// <summary>Gets or sets the node or primitive name shown in diagnostics.</summary>
    public string Name { get; set; } = "Mesh";

    /// <summary>Gets or sets the index into <see cref="SceneData.Materials"/>.</summary>
    public int MaterialIndex { get; set; }

    /// <summary>Gets or sets world-space vertex positions.</summary>
    public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();

    /// <summary>Gets or sets normalized world-space vertex normals.</summary>
    public Vector3[] Normals { get; set; } = Array.Empty<Vector3>();

    /// <summary>Gets or sets tangent vectors with handedness stored in W.</summary>
    public Vector4[] Tangents { get; set; } = Array.Empty<Vector4>();

    /// <summary>Gets or sets the primary texture-coordinate channel.</summary>
    public Vector2[] TexCoords0 { get; set; } = Array.Empty<Vector2>();

    /// <summary>Gets or sets the secondary texture-coordinate channel.</summary>
    public Vector2[] TexCoords1 { get; set; } = Array.Empty<Vector2>();

    /// <summary>Gets or sets triangle-list indices. An empty array represents non-indexed geometry.</summary>
    public int[] Indices { get; set; } = Array.Empty<int>();

    /// <summary>Gets or sets the alpha mode chosen after material and texture inspection.</summary>
    public MeshAlphaMode ResolvedAlphaMode { get; set; } = MeshAlphaMode.Opaque;
}
