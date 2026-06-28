cbuffer ShadowBuffer : register(b0)
{
    float4x4 LightViewProjection;
};

struct VSInput
{
    float3 Position : POSITION;
};

float4 VSMain(
    VSInput input) : SV_Position
{
    return mul(
        float4(
            input.Position,
            1.0f),
        LightViewProjection);
}
