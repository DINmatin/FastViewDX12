cbuffer PostProcessBuffer : register(b0)
{
    // x/y = inverse source dimensions
    // z = bloom threshold
    // w = bloom intensity
    float4 PostProcessSettings;

    // x = bloom blur radius for the current blur pass
    float4 BloomSettings;
};

Texture2D SourceTexture : register(t0);
Texture2D BloomTexture : register(t1);
SamplerState LinearClampSampler : register(s0);

struct FullscreenOutput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
};

FullscreenOutput VSMain(uint vertexId : SV_VertexID)
{
    FullscreenOutput output;

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

float4 PSBright(FullscreenOutput input) : SV_Target
{
    float4 scene = SourceTexture.Sample(
        LinearClampSampler,
        input.TexCoord);

    float brightness = max(
        scene.r,
        max(scene.g, scene.b));

    float threshold = saturate(
        PostProcessSettings.z);

    // Soft transition prevents hard halos around the threshold.
    float bloomMask = smoothstep(
        threshold,
        threshold + 0.18f,
        brightness);

    return float4(
        scene.rgb * bloomMask,
        1.0f);
}

float3 SampleGaussian(float2 uv, float2 direction)
{
    float2 texel = PostProcessSettings.xy;
    float radius = max(BloomSettings.x, 0.0f);

    float3 color = SourceTexture.Sample(
        LinearClampSampler,
        uv).rgb * 0.2270270270f;

    color += SourceTexture.Sample(
        LinearClampSampler,
        uv + direction * texel * 1.3846153846f * radius).rgb * 0.3162162162f;

    color += SourceTexture.Sample(
        LinearClampSampler,
        uv - direction * texel * 1.3846153846f * radius).rgb * 0.3162162162f;

    color += SourceTexture.Sample(
        LinearClampSampler,
        uv + direction * texel * 3.2307692308f * radius).rgb * 0.0702702703f;

    color += SourceTexture.Sample(
        LinearClampSampler,
        uv - direction * texel * 3.2307692308f * radius).rgb * 0.0702702703f;

    return color;
}

float4 PSBlurHorizontal(FullscreenOutput input) : SV_Target
{
    return float4(
        SampleGaussian(
            input.TexCoord,
            float2(1.0f, 0.0f)),
        1.0f);
}

float4 PSBlurVertical(FullscreenOutput input) : SV_Target
{
    return float4(
        SampleGaussian(
            input.TexCoord,
            float2(0.0f, 1.0f)),
        1.0f);
}

float4 PSComposite(FullscreenOutput input) : SV_Target
{
    float4 scene = SourceTexture.Sample(
        LinearClampSampler,
        input.TexCoord);

    float3 bloom = BloomTexture.Sample(
        LinearClampSampler,
        input.TexCoord).rgb;

    float3 combined = scene.rgb +
        bloom * max(PostProcessSettings.w, 0.0f);

    return float4(
        saturate(combined),
        scene.a);
}
