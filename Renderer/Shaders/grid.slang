import common.Core;
import common.PushData;

static const float2 trianglePositions[3] = {
    float2(-1.0f, -1.0f),
    float2( 3.0f, -1.0f),
    float2(-1.0f,  3.0f)
};

struct VS_OUT {
    float4 clipPos : SV_Position;
    float3 nearPoint : TEXCOORD0;
    float3 farPoint : TEXCOORD1;
};

[vk::push_constant] PushData<NoParams> pc;

float3 UnprojectToWorld(ViewConstantsData viewConstants, float4 clipPos)
{
    // g_matProjectionToWorld only inverts the projection, so finish with the inverse of the rigid world to view transform
    float4 viewPos = mul(viewConstants.g_matProjectionToWorld, clipPos);
    return viewConstants.g_vCameraPositionWs + mul(viewPos.xyz / viewPos.w, (float3x3)viewConstants.g_matWorldToView);
}

[shader("vertex")]
void vertMain(
    uint pos : SV_VertexID,
    out VS_OUT out_
) {
    var viewConstants = pc.ViewConstants;

    float2 vertexPos = trianglePositions[pos];
    out_.clipPos = float4(vertexPos, 0.0f, 1.0f);

    // Unproject two points along the view ray for the plane intersection. Reverse Z puts the near plane
    // at 1 and the infinite far plane at 0, which does not unproject, so the second point stays short of it
    out_.nearPoint = UnprojectToWorld(viewConstants, float4(vertexPos, 1.0f, 1.0f));
    out_.farPoint = UnprojectToWorld(viewConstants, float4(vertexPos, 0.01f, 1.0f));
}

[shader("fragment")]
void fragMain(
    VS_OUT in_,
    out float4 color: SV_Target,
    out float outDepth : SV_Depth
) {
    var viewConstants = pc.ViewConstants;

    float t = -in_.nearPoint.z / (in_.farPoint.z - in_.nearPoint.z);

    if (t < 0.0f) {
        // No intersection with grid plane, fully transparent
        color = float4(0.0f, 0.0f, 0.0f, 0.0f);
        outDepth = 0.0f; // Write far depth so this fragment doesn't block anything
        return;
    }

    float3 fragPos3D = in_.nearPoint + t * (in_.farPoint - in_.nearPoint);
    float2 fragPosAbs = abs(fragPos3D.xy);

    float4 clip_space_pos = mul(viewConstants.g_matWorldToProjection, float4(fragPos3D, 1.0f));
    float depth = clip_space_pos.z / clip_space_pos.w;

    // Written depth skips the viewport transform, so apply its depth range here
    outDepth = lerp(viewConstants.g_flViewportMinZ, viewConstants.g_flViewportMaxZ, depth);

    float fading = 0.05f + pow(saturate(depth), 0.1f);

    bool bIsNearOrigin = fragPosAbs.x <= 120.0f && fragPosAbs.y <= 120.0f;
    float scale = bIsNearOrigin ? 15.0f : 60.0f;

    float2 coord = fragPos3D.xy / scale;
    float2 derivative = fwidth(fragPos3D.xy) / scale;

    float2 grid = abs(fract(coord - 0.5f) - 0.5f) / derivative.xy;
    float line = min(grid.x, grid.y);
    float4 gridColor = float4(0.9f, 0.9f, 0.95f, 1.0f - min(line, 1.0f));

    float angleFade = min(1.0f, pow(abs(normalize(fragPos3D - viewConstants.g_vCameraPositionWs).z), 1.2f) * 20.0f);
    angleFade = lerp(1.0f, angleFade, min(length(fragPosAbs), 1.0f));

    float2 axisLines = abs(coord) / derivative;

    if (axisLines.x < 1.0f) {
        float axisLineAlpha = (1.0f - min(axisLines.x, 1.0f));
        gridColor.a = 1.0f - (1.0f - axisLineAlpha) * (min(grid.y, 1.0f));
        gridColor.xyz = gridColor.xyz * (1.0f - axisLineAlpha) * (1.0f - min(grid.y, 1.0f)) + float3(0.2f, 0.8f, 0.2f) * axisLineAlpha;
        gridColor.xyz /= gridColor.a;
        gridColor.a *= 2.0f - (1.0f - min(grid.y, 1.0f)) / (2.0f - axisLines.x - min(grid.y, 1.0f));
    }

    if (axisLines.y < 1.0f) {
        float axisLineAlpha = (1.0f - min(axisLines.y, 1.0f));
        float crossAxisLineAlpha = 1.0f - min(grid.x, 1.0f);

        if (min(axisLines.x, 1.0f) == 1.0f) {
            gridColor.a = 1.0f - (1.0f - axisLineAlpha) * (1.0f - crossAxisLineAlpha);
            gridColor.xyz = gridColor.xyz * (1.0f - axisLineAlpha) * crossAxisLineAlpha + float3(0.9f, 0.2f, 0.2f) * axisLineAlpha;
            gridColor.xyz /= gridColor.a;
            gridColor.a *= lerp(2.0f, 1.0f, crossAxisLineAlpha / (axisLineAlpha + crossAxisLineAlpha));
        } else {
            gridColor.xyz = lerp(float3(0.2f, 0.8f, 0.2f), float3(0.9f, 0.2f, 0.2f), axisLineAlpha / (axisLineAlpha + crossAxisLineAlpha));
            gridColor.a = max(axisLineAlpha, crossAxisLineAlpha) * 2.0f;
        }
    }

    if (bIsNearOrigin) {
        gridColor.xyz *= 1.2f;
        fading *= 1.4f;
    }

    // Grid fades with distance and viewing angle
    gridColor.a *= fading * angleFade;

    color = gridColor * float(t > 0);

    color.rgb = pow(color.rgb, float3(2.2f));
}
