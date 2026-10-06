// VRF-TEST
// SPIR-V source, HLSL reflection with SPIRV-Cross by KhronosGroup
// Dynamic combos: D_WARP, D_WARP_UVS_ON_MESH

static float4 gl_Position;
static float2 vPositionPs;
static float2 vTexCoordR;
static float2 vTexCoordG;
static float2 vTexCoordB;
static float2 output_0;
static float2 output_1;
static float2 output_2;

struct VS_INPUT
{
    float2 vPositionPs : TEXCOORD0;
    float2 vTexCoordR : TEXCOORD1;
    float2 vTexCoordG : TEXCOORD2;
    float2 vTexCoordB : TEXCOORD3;
};

struct PS_INPUT
{
    noperspective float2 output_0 : TEXCOORD0;
    noperspective float2 output_1 : TEXCOORD1;
    noperspective float2 output_2 : TEXCOORD2;
    float4 gl_Position : SV_Position;
};

void main_inner()
{
    float4 _18779 = float4(vPositionPs, 0.0f, 1.0f);
    output_0 = vTexCoordR;
    output_1 = vTexCoordG;
    output_2 = vTexCoordB;
    _18779.y = -vPositionPs.y;
    gl_Position = _18779;
}

PS_INPUT main(VS_INPUT stage_input)
{
    vPositionPs = stage_input.vPositionPs;
    vTexCoordR = stage_input.vTexCoordR;
    vTexCoordG = stage_input.vTexCoordG;
    vTexCoordB = stage_input.vTexCoordB;
    main_inner();
    PS_INPUT stage_output;
    stage_output.gl_Position = gl_Position;
    stage_output.output_0 = output_0;
    stage_output.output_1 = output_1;
    stage_output.output_2 = output_2;
    return stage_output;
}

