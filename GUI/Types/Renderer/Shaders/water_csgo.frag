#version 460

#include "common/utils.glsl"
#include "common/features.glsl"
#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"

#define renderMode_Cubemaps 1

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec4 vColorBlendValues;

//#define F_RENDER_BACKFACES 0;


#include "common/lighting_common.glsl"
#include "common/fullbright.glsl"
#include "common/texturing.glsl"
#include "common/pbr.glsl"
#include "common/fog.glsl"

#include "common/environment.glsl" // (S_SPECULAR == 1 || renderMode_Cubemaps == 1)

// Must be last
#include "common/lighting.glsl"



out vec4 outputColor;

#define F_REFLECTION_TYPE 0 // (0="Sky Color Only", 1="Environment Cube Map", 2="SSR over Environment Cube Map")
#define F_REFRACTION 0
#define F_CAUSTICS 0

//uniform sampler2D g_tColor; // SrgbRead(true)
//uniform sampler2D g_tDebris;
//uniform sampler2D g_tDebrisNormal;
//uniform sampler2D g_tSceneDepth;
uniform sampler2D g_tBlueNoise;

uniform sampler2D g_tWavesNormalHeight;
uniform vec4 g_vWaveScale = vec4(1.0);
uniform float g_flWavesSpeed = 1.0;

uniform float g_flSkyBoxScale = 1.0;
uniform float g_flSkyBoxFadeRange;
uniform vec4 g_vMapUVMin = vec4(-1000.0);
uniform vec4 g_vMapUVMax = vec4(1000.0);

#if (F_REFLECTION_TYPE == 0)
    uniform vec4 g_vSimpleSkyReflectionColor = vec4(1.0);
#endif

#if (F_REFRACTION == 1)
    uniform sampler2D g_tSceneColor;
    uniform sampler2D g_tSceneDepth;
#endif

uniform vec4 g_vWaterFogColor;
uniform vec4 g_vWaterDecayColor;

uniform sampler2D g_tSsrColor;
uniform sampler2D g_tSsrDepth;

vec3 hsv2rgb(vec3 c)
{
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}
vec3 refraction_func(float IOR);
float water_height(vec2 coords);
float fModulo(float value, float divisor);
double dModulo(double value, double divisor);
vec2 resolution;
vec2 uv;
vec3 normal = vec3(0, 0, 1);
vec2 distortion_direction;

float fov = 75;

float IOR = 15; //fake IOR !!!
vec3 waterBaseAbsorption = pow(g_vWaterDecayColor.rgb, vec3(0.002)); //this works fine, essentiall: taking the 1000th root for beers law. At 1000 units it will have exactly this color.
vec3 waterParticulateColor = pow(g_vWaterFogColor.rgb, vec3(0.001));

//vec3 waterBaseAbsorption = pow( vec3(0.9, 0.1, 0.1), vec3(0.01)); //Is it wine?...ITS BLOOOOD


float fade_size = 50;
float fade_out;

float local_depth;
float world_depth;


float flTime = mod(g_flTime , 5 * 3 * 31); //so I can test stuff.

//Main entry point
void main()
{
    resolution = textureSize(g_tSceneColor, 0);
    float focal_length = resolution.y / 2 / tan(radians(fov/2));

    uv = gl_FragCoord.xy / resolution;
    outputColor.w = 1.0; //I am sorry, transparency fans


    world_depth = (1.0 /   (texture(g_tSceneDepth, uv).r -0.05) ) * 0.95;
    local_depth = (1.0 / (gl_FragCoord.z - 0.05)) * 0.95;

    vec3 viewDirection = normalize(g_vCameraPositionWs - vFragPosition);

    float fresnel = clamp(1.0 - pow( viewDirection.z, 0.5), 0.2, 1.0); //Only used for reflection, fog will be done better now :^)

    vec3 pixel_dir = normalize(vec3( (gl_FragCoord.xy  - resolution / 2), -resolution.y / 2 / tan(radians(fov / 2))    ) );
    pixel_dir = inverse(mat3(g_matWorldToView)) * pixel_dir;

    vec3 local_pos = vFragPosition;


    vec2 normal_offset = vec2( -cos(vFragPosition.x / 5 + flTime * 5), -cos(vFragPosition.y / 5 + flTime * 5));
    normal_offset += vec2(-cos((vFragPosition.x - vFragPosition.y) / 4.17 + flTime * 3.1), -cos((vFragPosition.x + vFragPosition.y) / 4.17 + flTime * 3.1));
    normal_offset += vec2(-cos((vFragPosition.x + vFragPosition.y) / 4.17 + flTime * 3.1), -cos((vFragPosition.x + vFragPosition.y) / 4.17 + flTime * 3.1));
    normal_offset *= 0.02;
    normal = normalize( vec3(normal_offset, 1.0));

    bool do_wave_approximation = true;

    if(do_wave_approximation)
    {
        float accuracy_multi = 10;

        float distance_multiplier = 1/abs(pixel_dir.z) * 1;

        //float distance_multiplier = 0.05;
        bool forward = true;
        float relative_amount;
        for(int i = 1; i < accuracy_multi + 1; i++)
        {
            local_pos += (float(forward) -  float(!forward)) * distance_multiplier * pow(0.5, i) * pixel_dir;

            if(    (local_pos).z < water_height(local_pos.xy) + vFragPosition.z) // -  Random2D(uv) * pixel_dir * distance_multiplier
            {
                forward = false;
            }
            else
            {
                forward = true;
            }
        }
        local_depth += length(local_pos - vFragPosition);
        if(world_depth < local_depth)
        {
            outputColor.rgb = texture(g_tSceneColor, uv).rgb;
            return;
        }
        vec2 flat_normal = -(vec2(water_height(vec2(local_pos.x + 0.1, local_pos.y)),   water_height(vec2(local_pos.x, local_pos.y + 0.1))) - water_height(local_pos.xy)) * 10;
        normal = normalize(   vec3(  flat_normal,   1.0)); //  - water_height(local_pos.xy)  * 10 );
    }
    do_wave_approximation = false;


    vec3 viewspace_normal = mat3(g_matWorldToView) * -normal; //fuck worldspace

    distortion_direction = normal.xy * IOR;


    //CONTROL THE FADING AT THE EDGES TO PREVENT HARD TRANSITIONS

    float fade_size = resolution.y / 10;
    fade_out = min(( resolution.y / 2 - abs((gl_FragCoord.xy - resolution / 2).y) ), ( resolution.x / 2 - abs((gl_FragCoord.xy - resolution / 2).x) )) / fade_size;
	fade_out = min(1, fade_out);



    float waterdepth_at_distortion;
    

    float red_ior = 0.8; //multipliers, not true IOR, hence < 1 doesn't mean "negative" refraction
    float green_ior = 1.0;
    float blue_ior = 1.2;

    outputColor.r = refraction_func(IOR * red_ior).r;
    outputColor.g = refraction_func(IOR * green_ior).g;
    outputColor.b = refraction_func(IOR * blue_ior).b;

    vec3 ray_dir = mat3(g_matWorldToView) * normalize(reflect(pixel_dir, normal));

    ray_dir.z *= -1; //fuck the negative space


    int count = 0;


    outputColor *= 1;

    pixel_dir = mat3(g_matWorldToView) * pixel_dir;

    vec3 position = pixel_dir / -pixel_dir.z * local_depth;

    position.z *= -1;

    outputColor.w = 1.0;

    int index = 1;
    bool run_traverse = true;
    vec3 trace_coordinates;
    vec3 persistent_trace_coords = position + ray_dir * 0.01; //else water edges look noisy

    MaterialProperties_t material;
    InitProperties(material, normal);
    material.Roughness.x = 0.0001;
    material.AmbientNormal = normal;
    material.SpecularColor = vec3(1.0);

    bool has_hit = false;
    vec3 reflectionColor = GetEnvironment(material);
	do
	{

        float sample_accuracy = 4; //lower is better

		float distance_multiplier = 1 * sample_accuracy;
        float c = asin(cos(dot( normalize(persistent_trace_coords), normalize(ray_dir)))) * focal_length;

        ////this is technically a better formula, but its slower even when using less raymarching steps, which is a mystery to me.(trig is hardware implemented to my knowledge, no way that division is the reason)
        //distance_multiplier = 1 / c * local_depth * sample_accuracy;
        //distance_multiplier = pow(length(persistent_trace_coords), 0.4);

        float bluenoiserand = texelFetch(g_tBlueNoise, ivec2(mod(ivec2( gl_FragCoord.xy), ivec2(textureSize(g_tBlueNoise, 0)) )), 0)[0];

		trace_coordinates = persistent_trace_coords + ray_dir * distance_multiplier * bluenoiserand; //the rand is necessary to cut back on raymarching artifacts (visible banding).
        persistent_trace_coords = persistent_trace_coords + ray_dir * distance_multiplier; //persistent is required so we don't get wildly varying sampling resolutions

		float local_depth = trace_coordinates.z;
		vec2 screen_location = trace_coordinates.xy / trace_coordinates.z * resolution.y / 2 / tan(radians(fov / 2)) + resolution / 2;  //also needed for the fadeout, so I am keeping some resolution based processing
		vec2 depth_uv = screen_location / resolution;

		float fade_out = min(( resolution.y / 2 - abs((screen_location - resolution / 2).y) ), ( resolution.x / 2 - abs((screen_location - resolution / 2).x) )) / fade_size;
		fade_out = min(1, fade_out);

		if(local_depth < 0 || fade_out <= 0 || index > 300) //adjust index < x to get different ray lengths. high performance difference, relative to visual improvement.
        {
            break;
        }

		float distance_to_depth = local_depth - (1.0 / (texture(g_tSceneDepth, depth_uv).r - 0.05)) * 0.95;
		if(distance_to_depth < distance_multiplier * sample_accuracy && distance_to_depth > 0)
		{
            vec3 ssr_reflection =  vec3(texture(g_tSceneColor, depth_uv).rgb);
            //ssr_reflect = pow(ssr_reflect, vec3(1/2.2));

            reflectionColor =  mix(reflectionColor, ssr_reflection, fade_out); //would a mix() be cleaner?

            //reflectionColor = pow(reflectionColor, vec3(1/6.2));
            ////debugging stuff
            //vec3 hsv = vec3(0.33333 - float(index) / 500, 1.0, 1.0);
            //outputColor.rgb = hsv2rgb(hsv);
            //return;

            has_hit = true;
			run_traverse = false;
		}
         index++;
	}
	while(run_traverse);
    //reflectionColor = pow(reflectionColor, vec3(1/2.2));
    ApplyFog(outputColor.rgb, vFragPosition);
    //outputColor = vec4(0,0,0,1);
    outputColor.rgb =  mix(outputColor.rgb, reflectionColor  * 1,  fresnel);
}

//semi hardcoded, just for this shader, DO NOT BLINDLY COPY
vec3 refraction_func(float IOR) //still fake IOR, physical accuracy be damned
{
    
    float water_depth = world_depth - local_depth;
    vec2 distortion_direction = normal.xy * IOR;
    distortion_direction *= 1 / max(local_depth, 10) * fade_out *     clamp(water_depth / 20, 0, 1 );

    float waterdepth_at_distortion = (1.0 / (texture(g_tSceneDepth, uv + distortion_direction).r - 0.05)) * 0.95 - local_depth;
    if(waterdepth_at_distortion - water_depth < -10)
    {
        waterdepth_at_distortion = water_depth;
        distortion_direction = vec2(0);
    }
    vec3 ret = texture(g_tSceneColor, uv + distortion_direction).rgb;
    return (ret + (vec3(1) - pow(waterParticulateColor, pow(vec3(waterdepth_at_distortion), vec3(2)) * 0.05))  ) * pow(waterBaseAbsorption.rgb, vec3(waterdepth_at_distortion * 20));
}

float water_height2(vec2 coords)
{
    float water_height = sin(coords.x / 5 + flTime * 5) * 5 + sin(coords.y / 5 + flTime * 5) * 5;
    water_height += sin((coords.x - coords.y) / 4.17 + flTime * 3.1) * 4.17 + sin((coords.x + coords.y) / 4.17 + flTime * 3.1) * 4.17;
    water_height -= (5 + 5 + 4.17 + 4.17); //sum of all the sine waves at peak, else it would clip
    water_height *= 1 / (5 + 5 + 4.17 + 4.17);
    return water_height * 0.1;
}

float water_height(vec2 coords)
{
    float x = coords.x;
    float y = coords.y;

    //flTime -= floor(flTime / common_multiplier) * common_multiplier;

    //float tau = 2 * PI;
    float term_a_exp = 5;
    float term_a_weight = 10;
    float term_a_freq = 0.2;
    float term_a_speed = 5;

    float term_b_exp = 1;
    float term_b_weight = 8.34;
    float term_b_freq = 1 / 4.17;
    float term_b_speed =3.1;

    float term_c_exp = 1.2;
    float term_c_weight = 15;
    float term_c_freq = 0.05;
    float term_c_speed = 4;

    float post_exp = 1;

    float term_a = (sin(       float(dModulo(   double(x * term_a_freq * 1.5) + double(flTime * term_a_speed), TAU))     )    +    sin(     float(dModulo(   double(y * term_a_freq) + double(flTime * term_a_speed), TAU))   )   ) / 4 + 0.5; //low freq
    term_a = pow(term_a, term_a_exp) * term_a_weight;
        
    float term_b = (sin(            float(dModulo(   double((x - y) * term_b_freq) + double(flTime * term_b_speed)    , TAU))    ) + sin(    float(dModulo(   double((x + y) * term_b_freq) + double(flTime * term_b_speed)    , TAU))    )) / 4 + 0.5; //med freq
    term_b = pow(term_b, term_b_exp) * term_b_weight;

    float term_c = sin(  float(dModulo(   double( (1 * x + 3 * y) * term_c_freq ) + double(flTime*term_c_speed)    , TAU))   ) / 2 + 0.5; //weird diagonal higher freq
    term_c = pow(term_c, term_c_exp) * term_c_weight;

    float height_max = (term_a_weight + term_b_weight + term_c_weight);

    float water_height = term_a + term_b + term_c;
    water_height /= height_max;

    water_height = pow(water_height, post_exp);

    water_height -= 1;

    return water_height;
}


float fModulo(float value, float divisor)
{
    //return value;
    return value - floor(value /divisor) * divisor;
}
double dModulo(double value, double divisor)
{
    return value;
    return value - floor(value /divisor) * divisor;
}
