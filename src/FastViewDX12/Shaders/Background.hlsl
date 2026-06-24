cbuffer BackgroundBuffer : register(b0)
{
    // xyz = camera basis direction, w = additional scalar
    float4 CameraForwardAndTanHalfFov;
    float4 CameraRightAndAspect;
    float4 CameraUpAndEnvironmentRotation;

    // rgb = solid background colour in sRGB, a = EXR opacity
    float4 SolidColorAndOpacity;
};

Texture2D EnvironmentTexture : register(t0);
SamplerState TextureSampler : register(s0);

struct BackgroundVSOutput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
};

static const float PI = 3.14159265359f;

// Generates a full-screen triangle without a vertex buffer. Three vertices cover
// the complete target and avoid the diagonal interpolation seam of a quad.
BackgroundVSOutput VSMain(uint vertexId : SV_VertexID)
{
    BackgroundVSOutput output;

    float2 texCoord = float2(
        (vertexId << 1) & 2,
        vertexId & 2);

    output.TexCoord = texCoord;
    output.Position = float4(
        texCoord.x * 2.0f - 1.0f,
        1.0f - texCoord.y * 2.0f,
        0.0f,
        1.0f);

    return output;
}

float3 RotateDirectionAroundY(float3 direction, float angle)
{
    float sine;
    float cosine;
    sincos(angle, sine, cosine);

    return float3(
        cosine * direction.x + sine * direction.z,
        direction.y,
        -sine * direction.x + cosine * direction.z);
}

// Converts a normalized world-space direction to equirectangular longitude/latitude UVs.
float2 DirectionToEnvironmentUv(float3 direction)
{
    direction = normalize(direction);

    float longitude = atan2(direction.z, direction.x);
    float latitude = acos(clamp(direction.y, -1.0f, 1.0f));

    return float2(
        frac(longitude / (2.0f * PI) + 0.5f),
        saturate(latitude / PI));
}

float3 LinearToSRGB(float3 color)
{
    color = max(color, 0.0f);

    float3 lowPart = color * 12.92f;
    float3 highPart = 1.055f * pow(color, 1.0f / 2.4f) - 0.055f;

    return lerp(
        lowPart,
        highPart,
        step(0.0031308f, color));
}

// Applies a small photographic exposure curve before converting linear EXR radiance to sRGB.
float3 ToneMapAndEncode(float3 linearColor)
{
    float3 mapped = 1.0f - exp(-linearColor * 1.15f);
    return saturate(LinearToSRGB(mapped));
}

// Reconstructs the view ray for this pixel, samples the EXR environment, and blends
// it over the selected solid color using the UI-controlled background opacity.
float4 PSMain(BackgroundVSOutput input) : SV_Target
{
    float2 ndc = float2(
        input.TexCoord.x * 2.0f - 1.0f,
        1.0f - input.TexCoord.y * 2.0f);

    float tanHalfFov = CameraForwardAndTanHalfFov.w;
    float aspect = CameraRightAndAspect.w;

    float3 rayDirection = normalize(
        CameraForwardAndTanHalfFov.xyz +
        CameraRightAndAspect.xyz * (ndc.x * aspect * tanHalfFov) +
        CameraUpAndEnvironmentRotation.xyz * (ndc.y * tanHalfFov));

    rayDirection = RotateDirectionAroundY(
        rayDirection,
        CameraUpAndEnvironmentRotation.w);

    float2 environmentUv = DirectionToEnvironmentUv(rayDirection);

    float3 environmentColor = EnvironmentTexture.SampleLevel(
        TextureSampler,
        environmentUv,
        0.0f).rgb;

    environmentColor = ToneMapAndEncode(environmentColor);

    float opacity = saturate(SolidColorAndOpacity.a);
    float3 finalColor = lerp(
        SolidColorAndOpacity.rgb,
        environmentColor,
        opacity);

    return float4(finalColor, 1.0f);
}
