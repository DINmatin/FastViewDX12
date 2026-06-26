cbuffer SceneBuffer : register(b0)
{
    float4x4 ViewProjection;

    float4 CameraPosition;
    float4 LightDirection;
    // x = Environment rotation in radians
    // y = Environment intensity
    // z = Direct light intensity
    // w = highest environment mip level
    float4 EnvironmentSettings;
    float4 BaseColorFactor;
    float4 EmissiveFactor;

    // x = MetallicFactor
    // y = RoughnessFactor
    // z = NormalScale
    // w = AlphaCutoff
    float4 MaterialFactors;

    // x = AlphaMode: 0 Opaque, 1 Mask, 2 Blend
    // y = Unlit: 0 false, 1 true
    float4 MaterialFlags;

    // Packed sampler addressing pairs for BaseColor, Normal,
    // MetallicRoughness, and Emissive textures.
    float4 TextureSamplerIndices;
};

Texture2D BaseColorTexture : register(t0);
Texture2D NormalTexture : register(t1);
Texture2D MetallicRoughnessTexture : register(t2);
Texture2D EmissiveTexture : register(t3);
Texture2D EnvironmentTexture : register(t4);

SamplerState TextureSampler : register(s0);

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float4 Tangent : TANGENT;
    float2 BaseColorTexCoord : TEXCOORD0;
    float2 NormalTexCoord : TEXCOORD1;
    float2 MetallicRoughnessTexCoord : TEXCOORD2;
    float2 EmissiveTexCoord : TEXCOORD3;
};

struct VSOutput
{
    float4 Position : SV_Position;

    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float4 Tangent : TEXCOORD2;
    float2 BaseColorTexCoord : TEXCOORD3;
    float2 NormalTexCoord : TEXCOORD4;
    float2 MetallicRoughnessTexCoord : TEXCOORD5;
    float2 EmissiveTexCoord : TEXCOORD6;
};

static const float PI =
    3.14159265359f;

static const int TEXTURE_WRAP_REPEAT = 0;
static const int TEXTURE_WRAP_CLAMP_TO_EDGE = 1;
static const int TEXTURE_WRAP_MIRRORED_REPEAT = 2;

float MirrorTextureCoordinate(
    float coordinate)
{
    return 1.0f -
        abs(
            frac(
                coordinate *
                0.5f) *
            2.0f -
            1.0f);
}

float ApplyTextureWrapMode(
    float coordinate,
    int wrapMode,
    float halfTexel)
{
    if (wrapMode ==
        TEXTURE_WRAP_CLAMP_TO_EDGE)
    {
        return clamp(
            coordinate,
            halfTexel,
            1.0f - halfTexel);
    }

    if (wrapMode ==
        TEXTURE_WRAP_MIRRORED_REPEAT)
    {
        return clamp(
            MirrorTextureCoordinate(
                coordinate),
            halfTexel,
            1.0f - halfTexel);
    }

    return coordinate;
}

float2 ApplyTextureWrapModes(
    float2 textureCoordinate,
    int samplerIndex,
    uint textureWidth,
    uint textureHeight)
{
    int wrapS =
        samplerIndex % 3;

    int wrapT =
        samplerIndex / 3;

    float2 halfTexel =
        0.5f /
        max(
            float2(
                textureWidth,
                textureHeight),
            1.0f);

    return float2(
        ApplyTextureWrapMode(
            textureCoordinate.x,
            wrapS,
            halfTexel.x),

        ApplyTextureWrapMode(
            textureCoordinate.y,
            wrapT,
            halfTexel.y));
}

float4 SampleBaseColorTexture(
    float2 textureCoordinate)
{
    uint width;
    uint height;

    BaseColorTexture.GetDimensions(
        width,
        height);

    int samplerIndex =
        (int)(
            TextureSamplerIndices.x +
            0.5f);

    return BaseColorTexture.Sample(
        TextureSampler,
        ApplyTextureWrapModes(
            textureCoordinate,
            samplerIndex,
            width,
            height));
}

float4 SampleNormalTexture(
    float2 textureCoordinate)
{
    uint width;
    uint height;

    NormalTexture.GetDimensions(
        width,
        height);

    int samplerIndex =
        (int)(
            TextureSamplerIndices.y +
            0.5f);

    return NormalTexture.Sample(
        TextureSampler,
        ApplyTextureWrapModes(
            textureCoordinate,
            samplerIndex,
            width,
            height));
}

float4 SampleMetallicRoughnessTexture(
    float2 textureCoordinate)
{
    uint width;
    uint height;

    MetallicRoughnessTexture.GetDimensions(
        width,
        height);

    int samplerIndex =
        (int)(
            TextureSamplerIndices.z +
            0.5f);

    return MetallicRoughnessTexture.Sample(
        TextureSampler,
        ApplyTextureWrapModes(
            textureCoordinate,
            samplerIndex,
            width,
            height));
}

float4 SampleEmissiveTexture(
    float2 textureCoordinate)
{
    uint width;
    uint height;

    EmissiveTexture.GetDimensions(
        width,
        height);

    int samplerIndex =
        (int)(
            TextureSamplerIndices.w +
            0.5f);

    return EmissiveTexture.Sample(
        TextureSampler,
        ApplyTextureWrapModes(
            textureCoordinate,
            samplerIndex,
            width,
            height));
}

float3 RotateDirectionAroundY(
    float3 direction,
    float angle)
{
    float sine;
    float cosine;

    sincos(
        angle,
        sine,
        cosine);

    return float3(
        cosine * direction.x +
        sine * direction.z,

        direction.y,

        -sine * direction.x +
        cosine * direction.z);
}

float2 DirectionToEnvironmentUv(
    float3 direction)
{
    direction =
        normalize(
            direction);

    float longitude =
        atan2(
            direction.z,
            direction.x);

    float latitude =
        acos(
            clamp(
                direction.y,
                -1.0f,
                1.0f));

    float u =
        longitude /
        (2.0f * PI) +
        0.5f;

    float v =
        latitude /
        PI;

    return float2(
        frac(u),
        saturate(v));
}

float3 SRGBToLinear(
    float3 color)
{
    float3 lowPart =
        color / 12.92f;

    float3 highPart =
        pow(
            (color + 0.055f) / 1.055f,
            2.4f);

    return lerp(
        lowPart,
        highPart,
        step(0.04045f, color));
}

float3 LinearToSRGB(
    float3 color)
{
    color =
        max(
            color,
            0.0f);

    float3 lowPart =
        color * 12.92f;

    float3 highPart =
        1.055f *
        pow(
            color,
            1.0f / 2.4f) -
        0.055f;

    return lerp(
        lowPart,
        highPart,
        step(0.0031308f, color));
}

float DistributionGGX(
    float normalHalfway,
    float roughness)
{
    float alpha =
        roughness *
        roughness;

    float alphaSquared =
        alpha *
        alpha;

    float denominator =
        normalHalfway *
        normalHalfway *
        (alphaSquared - 1.0f) +
        1.0f;

    return alphaSquared /
        max(
            PI *
            denominator *
            denominator,
            0.000001f);
}

float GeometrySchlickGGX(
    float normalDirection,
    float roughness)
{
    float value =
        roughness +
        1.0f;

    float k =
        value *
        value /
        8.0f;

    return normalDirection /
        max(
            normalDirection *
            (1.0f - k) +
            k,
            0.000001f);
}

float GeometrySmith(
    float normalView,
    float normalLight,
    float roughness)
{
    return
        GeometrySchlickGGX(
            normalView,
            roughness) *
        GeometrySchlickGGX(
            normalLight,
            roughness);
}

float3 FresnelSchlick(
    float cosine,
    float3 baseReflectance)
{
    return baseReflectance +
        (1.0f - baseReflectance) *
        pow(
            1.0f - cosine,
            5.0f);
}

float3 FresnelSchlickRoughness(
    float cosine,
    float3 baseReflectance,
    float roughness)
{
    float3 maximumReflectance =
        max(
            float3(
                1.0f - roughness,
                1.0f - roughness,
                1.0f - roughness),
            baseReflectance);

    return baseReflectance +
        (maximumReflectance -
         baseReflectance) *
        pow(
            1.0f - cosine,
            5.0f);
}

float3 ToneMapAndEncode(
    float3 linearColor)
{
    float3 mapped =
        1.0f -
        exp(
            -linearColor *
            1.15f);

    return saturate(
        LinearToSRGB(
            mapped));
}

VSOutput VSMain(
    VSInput input)
{
    VSOutput output;

    output.Position =
        mul(
            float4(
                input.Position,
                1.0f),
            ViewProjection);

    output.WorldPosition =
        input.Position;

    output.Normal =
        normalize(
            input.Normal);

    output.Tangent =
        input.Tangent;

    output.BaseColorTexCoord =
        input.BaseColorTexCoord;

    output.NormalTexCoord =
        input.NormalTexCoord;

    output.MetallicRoughnessTexCoord =
        input.MetallicRoughnessTexCoord;

    output.EmissiveTexCoord =
        input.EmissiveTexCoord;

    return output;
}

float4 PSMain(
    VSOutput input) : SV_Target
{
    float4 sampledBaseColor =
        SampleBaseColorTexture(
            input.BaseColorTexCoord);

    float3 baseColorLinear =
        SRGBToLinear(
            sampledBaseColor.rgb);

    baseColorLinear *=
        BaseColorFactor.rgb;

    float alpha =
        sampledBaseColor.a *
        BaseColorFactor.a;

    int alphaMode =
        (int)(
            MaterialFlags.x +
            0.5f);

    if (alphaMode == 0)
    {
        alpha =
            1.0f;
    }
    else if (alphaMode == 1)
    {
        clip(
            alpha -
            MaterialFactors.w);
    }
    else
    {
        clip(
            alpha -
            0.001f);
    }

    float3 emissiveLinear =
        SRGBToLinear(
            SampleEmissiveTexture(
                input.EmissiveTexCoord).rgb);

    emissiveLinear *=
        EmissiveFactor.rgb;

    if (MaterialFlags.y > 0.5f)
    {
        return float4(
            ToneMapAndEncode(
                baseColorLinear +
                emissiveLinear),
            alpha);
    }

    float3 geometryNormal =
        normalize(
            input.Normal);

    float3 tangent =
        input.Tangent.xyz;

    tangent -=
        geometryNormal *
        dot(
            geometryNormal,
            tangent);

    tangent =
        normalize(
            tangent);

    float3 bitangent =
        normalize(
            cross(
                geometryNormal,
                tangent) *
            input.Tangent.w);

    float3 tangentNormal =
        SampleNormalTexture(
            input.NormalTexCoord).xyz;

    tangentNormal =
        tangentNormal *
        2.0f -
        1.0f;

    tangentNormal.xy *=
        MaterialFactors.z;

    tangentNormal =
        normalize(
            tangentNormal);

    float3 normal =
        normalize(
            tangent *
            tangentNormal.x +
            bitangent *
            tangentNormal.y +
            geometryNormal *
            tangentNormal.z);

    float4 metallicRoughnessSample =
        SampleMetallicRoughnessTexture(
            input.MetallicRoughnessTexCoord);

    float roughness =
        metallicRoughnessSample.g *
        MaterialFactors.y;

    roughness =
        clamp(
            roughness,
            0.055f,
            1.0f);

    float metallic =
        metallicRoughnessSample.b *
        MaterialFactors.x;

    metallic =
        saturate(
            metallic);

    float3 viewDirection =
        normalize(
            CameraPosition.xyz -
            input.WorldPosition);

    float3 directionToLight =
    normalize(
        LightDirection.xyz);

    float3 halfwayDirection =
        normalize(
            viewDirection +
            directionToLight);

    float normalLight =
        saturate(
            dot(
                normal,
                directionToLight));

    float normalView =
        saturate(
            dot(
                normal,
                viewDirection));

    float normalHalfway =
        saturate(
            dot(
                normal,
                halfwayDirection));

    float halfwayView =
        saturate(
            dot(
                halfwayDirection,
                viewDirection));

    float3 baseReflectance =
        lerp(
            float3(
                0.04f,
                0.04f,
                0.04f),
            baseColorLinear,
            metallic);

    float distribution =
        DistributionGGX(
            normalHalfway,
            roughness);

    float geometry =
        GeometrySmith(
            normalView,
            normalLight,
            roughness);

    float3 fresnel =
        FresnelSchlick(
            halfwayView,
            baseReflectance);

    float3 specular =
        distribution *
        geometry *
        fresnel;

    specular /=
        max(
            4.0f *
            normalView *
            normalLight,
            0.0001f);

    float3 diffuseWeight =
        (1.0f - fresnel) *
        (1.0f - metallic);

    float3 diffuse =
        diffuseWeight *
        baseColorLinear /
        PI;

    float3 sunColor =
        float3(
            1.0f,
            0.96f,
            0.88f);

    float sunIntensity =
    3.0f *
    EnvironmentSettings.z;

    float3 directLighting =
        (diffuse + specular) *
        sunColor *
        sunIntensity *
        normalLight;

    float upwardFactor =
        saturate(
            normal.y *
            0.5f +
            0.5f);

    float3 groundLight =
        float3(
            0.10f,
            0.085f,
            0.07f);

    float3 skyLight =
        float3(
            0.48f,
            0.56f,
            0.72f);

    float3 hemisphereLight =
        lerp(
            groundLight,
            skyLight,
            upwardFactor);

    float3 ambientDiffuse =
    baseColorLinear *
    hemisphereLight *
    (1.0f - metallic) *
    0.9f *
    EnvironmentSettings.y;

    float3 reflectionDirection =
    reflect(
        -viewDirection,
        normal);

    reflectionDirection =
    RotateDirectionAroundY(
        reflectionDirection,
        EnvironmentSettings.x);

    float2 environmentUv =
    DirectionToEnvironmentUv(
        reflectionDirection);

    float environmentLod =
    roughness *
    EnvironmentSettings.w;

    float3 reflectedEnvironment =
    EnvironmentTexture.SampleLevel(
        TextureSampler,
        environmentUv,
        environmentLod).rgb;

    reflectedEnvironment *=
    EnvironmentSettings.y;

    float3 environmentFresnel =
    FresnelSchlickRoughness(
        normalView,
        baseReflectance,
        roughness);
    
    float reflectionStrength =
    lerp(
        1.0f,
        0.18f,
        roughness);

    float3 ambientSpecular =
    reflectedEnvironment *
    environmentFresnel *
    reflectionStrength;
    
    float3 finalColor =
        directLighting +
        ambientDiffuse +
        ambientSpecular +
        emissiveLinear;

    return float4(
        ToneMapAndEncode(
            finalColor),
        alpha);
}
