import common.Core;
import common.compression;
import common.PushData;
import common.utils;
struct Parameters : IParameters
{
    [SrgbRead] DescriptorHandle<Sampler2D> g_tColor;
    DescriptorHandle<Sampler2D> g_tNormal;
    float g_flDetailBlendFactor;
    [SrgbRead] float4 g_vTexCoordCenter;
};

[vk::push_constant] PushData<Parameters> pc;


public struct VInput
{
    float3 pos : POSITION0;
    NormalData normalData;
    float2 uv : TEXCOORD0;
}

struct VS_OUT {
    float4 clipPos : SV_Position;
    float3 normal;
    float3 tangent;
    float3 bitangent;
    float2 uv;
    float linearDepth;
};

[shader("vertex")]
void vertMain(VInput VInput,
    out VS_OUT out_
) {
    var ViewConstants = pc.ViewConstants;
    out_.clipPos = mul(ViewConstants.g_matViewToProjection, mul(ViewConstants.g_matWorldToView, float4(VInput.pos, 1.0f)));

    out_.linearDepth = out_.clipPos.z / out_.clipPos.w;
    out_.normal = VInput.normalData.normal;
    out_.tangent = VInput.normalData.tangent;
    out_.bitangent = VInput.normalData.bitangent;
    out_.uv = VInput.uv;
}

[shader("fragment")]
void fragMain(
    VS_OUT in_,
    out float4 color: SV_Target
) {
    float3 decodedNormalMap = DecodeHemiOctahedronNormal(pc.Parameters.g_tNormal.Sample(in_.uv).xy);
    float3 normal = decodedNormalMap.x * in_.tangent + decodedNormalMap.y * in_.bitangent + decodedNormalMap.z * in_.normal;
    float3 normColor = normal * 0.5f + 0.5f;

    float4 albedo = pc.Parameters.g_tColor.Sample(in_.uv);

    var lighting = pc.Lighting;
    float NdotL = saturate(dot(normalize(normal), lighting.g_vSunDirection.xyz));

    color = float4(albedo.rgb * lighting.g_vSunColor.rgb * NdotL, albedo.a);

}
