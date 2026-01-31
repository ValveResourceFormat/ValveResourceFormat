import "common/ViewConstants.slang";

// https://github.com/BabylonJS/Babylon.js/blob/bd7351cfc97884d3293d5858b8a0190cda640b2f/packages/dev/materials/src/grid/grid.fragment.fx
// https://asliceofrendering.com/scene%20helper/2020/01/05/InfiniteGrid/

struct VertexInput
{
    float2 aVertexPosition : POSITION;
};

struct VertexOutput
{
    float3 nearPoint : NEAR_POINT;
    float3 farPoint : FAR_POINT;
    float4 position : SV_Position;
};

float4x4 inverse(float4x4 m)
{
    float n11 = m[0][0], n12 = m[1][0], n13 = m[2][0], n14 = m[3][0];
    float n21 = m[0][1], n22 = m[1][1], n23 = m[2][1], n24 = m[3][1];
    float n31 = m[0][2], n32 = m[1][2], n33 = m[2][2], n34 = m[3][2];
    float n41 = m[0][3], n42 = m[1][3], n43 = m[2][3], n44 = m[3][3];

    float t11 = n23 * n34 * n42 - n24 * n33 * n42 + n24 * n32 * n43 - n22 * n34 * n43 - n23 * n32 * n44 + n22 * n33 * n44;
    float t12 = n14 * n33 * n42 - n13 * n34 * n42 - n14 * n32 * n43 + n12 * n34 * n43 + n13 * n32 * n44 - n12 * n33 * n44;
    float t13 = n13 * n24 * n42 - n14 * n23 * n42 + n14 * n22 * n43 - n12 * n24 * n43 - n13 * n22 * n44 + n12 * n23 * n44;
    float t14 = n14 * n23 * n32 - n13 * n24 * n32 - n14 * n22 * n33 + n12 * n24 * n33 + n13 * n22 * n34 - n12 * n23 * n34;

    float det = n11 * t11 + n21 * t12 + n31 * t13 + n41 * t14;
    float idet = 1.0f / det;

    float4x4 ret;

    ret[0][0] = t11 * idet;
    ret[0][1] = (n24 * n33 * n41 - n23 * n34 * n41 - n24 * n31 * n43 + n21 * n34 * n43 + n23 * n31 * n44 - n21 * n33 * n44) * idet;
    ret[0][2] = (n22 * n34 * n41 - n24 * n32 * n41 + n24 * n31 * n42 - n21 * n34 * n42 - n22 * n31 * n44 + n21 * n32 * n44) * idet;
    ret[0][3] = (n23 * n32 * n41 - n22 * n33 * n41 - n23 * n31 * n42 + n21 * n33 * n42 + n22 * n31 * n43 - n21 * n32 * n43) * idet;

    ret[1][0] = t12 * idet;
    ret[1][1] = (n13 * n34 * n41 - n14 * n33 * n41 + n14 * n31 * n43 - n11 * n34 * n43 - n13 * n31 * n44 + n11 * n33 * n44) * idet;
    ret[1][2] = (n14 * n32 * n41 - n12 * n34 * n41 - n14 * n31 * n42 + n11 * n34 * n42 + n12 * n31 * n44 - n11 * n32 * n44) * idet;
    ret[1][3] = (n12 * n33 * n41 - n13 * n32 * n41 + n13 * n31 * n42 - n11 * n33 * n42 - n12 * n31 * n43 + n11 * n32 * n43) * idet;

    ret[2][0] = t13 * idet;
    ret[2][1] = (n14 * n23 * n41 - n13 * n24 * n41 - n14 * n21 * n43 + n11 * n24 * n43 + n13 * n21 * n44 - n11 * n23 * n44) * idet;
    ret[2][2] = (n12 * n24 * n41 - n14 * n22 * n41 + n14 * n21 * n42 - n11 * n24 * n42 - n12 * n21 * n44 + n11 * n22 * n44) * idet;
    ret[2][3] = (n13 * n22 * n41 - n12 * n23 * n41 - n13 * n21 * n42 + n11 * n23 * n42 + n12 * n21 * n43 - n11 * n22 * n43) * idet;

    ret[3][0] = t14 * idet;
    ret[3][1] = (n13 * n24 * n31 - n14 * n23 * n31 + n14 * n21 * n33 - n11 * n24 * n33 - n13 * n21 * n34 + n11 * n23 * n34) * idet;
    ret[3][2] = (n14 * n22 * n31 - n12 * n24 * n31 - n14 * n21 * n32 + n11 * n24 * n32 + n12 * n21 * n34 - n11 * n22 * n34) * idet;
    ret[3][3] = (n12 * n23 * n31 - n13 * n22 * n31 + n13 * n21 * n32 - n11 * n23 * n32 - n12 * n21 * n33 + n11 * n22 * n33) * idet;

    return ret;
}


float3 UnprojectPoint(float x, float y, float z)
{
    transpose(g_matProjectionToWorld);

    float4x4 viewInv = inverse(g_matWorldToView);
    float4x4 projInv = inverse(g_matViewToProjection);
    float4 unprojectedPoint = mul(mul(viewInv, projInv), float4(x, y, 1.0 - z, 1.0));
    return unprojectedPoint.xyz / unprojectedPoint.w;
}

[shader("vertex")]
VertexOutput vertexMain(VertexInput input)
{
    VertexOutput output;
    
    output.nearPoint = UnprojectPoint(input.aVertexPosition.x, input.aVertexPosition.y, 0.0);
    output.farPoint = UnprojectPoint(input.aVertexPosition.x, input.aVertexPosition.y, 0.99);
    output.position = float4(input.aVertexPosition, 0.0, 1.0);
    
    return output;
}

struct FragmentInput
{
    float3 nearPoint : NEAR_POINT;
    float3 farPoint : FAR_POINT;
};

struct FragmentOutput
{
    float4 color : SV_Target;
    float depth : SV_Depth;
};

Sampler2D g_tNoise;

static const float near = 0.01;
static const float far = 100.0;
static const float3 colorRed = float3(0.9, 0.2, 0.2);
static const float3 colorGreen = float3(0.2, 0.8, 0.2);

float computeDepth(float4 clip_space_pos)
{
    return (clip_space_pos.z / clip_space_pos.w);
}

[shader("fragment")]
FragmentOutput fragmentMain(FragmentInput input)
{
    FragmentOutput output;
    
    float t = -input.nearPoint.z / (input.farPoint.z - input.nearPoint.z);
    float3 fragPos3D = input.nearPoint + t * (input.farPoint - input.nearPoint);
    float2 fragPosAbs = abs(fragPos3D.xy);
    
    float4 clip_space_pos = mul(mul(g_matViewToProjection, g_matWorldToView), float4(fragPos3D.xyz, 1.0));
    float linearDepth = computeDepth(clip_space_pos);
    
    // Convert to depth range [0, 1] for OpenGL compatibility
    output.depth = linearDepth * 0.5 + 0.5;
    
    float fading = max(0.0, (0.05 + pow(linearDepth, 0.15)));
    
    bool bIsNearOrigin = all(fragPosAbs <= float2(120.0, 120.0));
    float scale = bIsNearOrigin ? 15.0 : 60.0;
    
    float2 coord = fragPos3D.xy / scale;
    float2 derivative = fwidth(fragPos3D.xy) / scale;
    float2 grid = abs(frac(coord - 0.5) - 0.5) / derivative;
    float line = min(grid.x, grid.y);

    float4 gridColor = float4(0.9, 0.9, 1.0, 1.0 - min(line, 1.0)) * g_tNoise.Sample(float2(fragPos3D.xy / 100000));

    
    
    float angleFade = min(1.0, pow(abs(normalize(fragPos3D - g_vCameraPositionWs).z), 1.4) * 100.0);
    angleFade = lerp(1.0, angleFade, min(length(fragPosAbs) / 2000.0, 1.0));
    
    float2 axisLines = abs(coord) / derivative;
    
    if (axisLines.x < 1.0)
    {
        float axisLineAlpha = (1.0 - min(axisLines.x, 1.0));
        gridColor.a = 1.0 - (1.0 - axisLineAlpha) * (min(grid.y, 1.0));
        gridColor.xyz = gridColor.xyz * (1.0 - axisLineAlpha) * (1.0 - min(grid.y, 1.0)) + colorGreen * axisLineAlpha;
        gridColor.xyz /= gridColor.a;
        gridColor.a *= 2.0 - (1.0 - min(grid.y, 1.0)) / (2.0 - axisLines.x - min(grid.y, 1.0));
    }
    
    if (axisLines.y < 1.0)
    {
        float axisLineAlpha = (1.0 - min(axisLines.y, 1.0));
        float crossAxisLineAlpha = 1.0 - min(grid.x, 1.0);
        
        if (min(axisLines.x, 1.0) == 1.0)
        {
            gridColor.a = 1.0 - (1.0 - axisLineAlpha) * (1.0 - crossAxisLineAlpha);
            gridColor.xyz = gridColor.xyz * (1.0 - axisLineAlpha) * crossAxisLineAlpha + colorRed * axisLineAlpha;
            gridColor.xyz /= gridColor.a;
            gridColor.a *= lerp(2.0, 1.0, crossAxisLineAlpha / (axisLineAlpha + crossAxisLineAlpha));
        }
        else
        {
            gridColor.xyz = lerp(colorGreen, colorRed, axisLineAlpha / (axisLineAlpha + crossAxisLineAlpha));
            gridColor.a = max(axisLineAlpha, crossAxisLineAlpha) * 2.0;
        }
    }
    
    if (bIsNearOrigin)
    {
        gridColor.xyz *= 1.2;
        fading *= 1.4;
    }
    
    gridColor.a *= fading * angleFade;
    output.color = gridColor * float(t > 0.0);


    #ifdef TEST_VALUE
        output.color.a = 1.0f;
    #endif
    return output;
}
