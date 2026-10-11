import common.Core;
import common.compression;
import common.PushData;

struct Parameters : IParameters
{
    [SrgbRead] DescriptorHandle<Sampler2D> g_tColor;
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
    float3 normal : TEXCOORD0;
    float2 uv : TEXCOORD1;
    float linearDepth : TEXCOORD2;
};

[shader("vertex")]
void vertMain(VInput VInput,
    out VS_OUT out_
) {
    var ViewConstants = pc.ViewConstants;
    out_.clipPos = mul(ViewConstants.g_matViewToProjection, mul(ViewConstants.g_matWorldToView, float4(VInput.pos, 1.0f)));

    out_.linearDepth = out_.clipPos.z / out_.clipPos.w;
    out_.normal = VInput.normalData.normal;
    out_.uv = VInput.uv;
}

[shader("fragment")]
void fragMain(
    VS_OUT in_,
    out float4 color: SV_Target,
    out float outDepth: SV_Depth
) {

    // Visualize normals: convert from [-1,1] to [0,1] range
    float3 normColor = in_.normal * 0.5f + 0.5f;
    color = float4(pc.Parameters.g_vTexCoordCenter); //float4(normColor, 1.0f);
    
    float4 albedo = pc.Parameters.g_tColor.Sample(in_.uv);

    var lighting = pc.Lighting;
    float NdotL = saturate(dot(normalize(in_.normal), lighting.g_vSunDirection.xyz));

    color = float4(albedo.rgb * lighting.g_vSunColor.rgb * NdotL, albedo.a);

    outDepth = in_.linearDepth;
}
