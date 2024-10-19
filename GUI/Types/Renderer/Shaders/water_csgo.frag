#version 460


#include "common/ViewConstants.glsl"
#include "common/utils.glsl"
#include "common/fog.glsl"
//#include "common/LightingConstants.glsl"
//#include "common/environment.glsl"
//#include "common/texturing.glsl"
#include "common/rendermodes.glsl"
#include "common/features.glsl"
#include "common/LightingConstants.glsl"
#include "complex_features.glsl"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec4 vColorBlendValues;

#include "common/lighting_common.glsl"
#include "common/texturing.glsl"
#include "common/pbr.glsl"

#define renderMode_Cubemaps 1

#if (S_SPECULAR == 1 || renderMode_Cubemaps == 1)
#include "common/environment.glsl"
#endif

// Must be last
#include "common/lighting.glsl"




out vec4 outputColor;

//uniform sampler2D g_tColor; // SrgbRead(true)
//uniform sampler2D g_tDebris;
//uniform sampler2D g_tDebrisNormal;
uniform sampler2DMS depth_map;
uniform sampler2DMS color_map;

uniform sampler2DMS g_tSsrColor;
uniform sampler2DMS g_tSsrDepth;


uniform vec2 resolution;

uniform vec4 g_vWaterFogColor;
uniform vec4 g_vWaterDecayColor;
//uniform samplerCube g_tEnvironmentMap;


//uses "globally" supplied resolution, not flexible
vec4 readMSFramebufferZeroth(sampler2DMS tex, vec2 uv)
{
    ivec2 tex_coord = ivec2(uv * textureSize(tex));
    return texelFetch(tex, tex_coord, 0);
}
//vec4 readMSFramebufferAvgBi(sampler2DMS tex, vec2 uv) //later
//{
//    ivec2 tex_coord = ivec2(uv * resolution);
//    vec4 result[4];
//
//    for(int i = 0; i<8; i++)
//        result += texelFetch(tex, tex_coord, i);
//    return result/8; //can we "softcode" this somehow? do we always have 8 samples?
//}


float fov = 50;

//Main entry point
void main()
{

    vec2 uv = gl_FragCoord.xy / resolution;
    outputColor.w = 1.0;

    //outputColor.rgb = texture(g_tSceneDepth, uv).rgb; //this works fine, although I can't make sense of what the texture is supposed to be. EDIT: after a pull it seems to not do anything anymore
    //outputColor.rgb = texture(g_tSsrColor, uv).rgb; //this does not work fine.
    outputColor.rgb = readMSFramebufferZeroth(g_tSsrColor, uv + sin(g_flTime) * 0.05).rgb ;

    // To visualise depth
    //outputColor.rgb = RemapVal(readMSFramebufferZeroth(g_tSsrDepth, uv).r, 0.0, 0.08, 1, 0).xxx;

    return;


    
    //outputColor.rgb = vec3(1.0);
    

    vec3 viewDirection = normalize(g_vCameraPositionWs - vFragPosition);
    vec3 normal = normalize( vec3(sin(vFragPosition.x / 5 + g_flTime) * 0.02, sin(vFragPosition.y / 5 + g_flTime) * 0.02, 1));

    float fresnel = clamp(1.0 - pow( viewDirection.z, 0.5), 0.2, 1.0); //really important, I always want some level of reflection, could do it later, but it matters very little for the old cloudy water stuff

    float fog_factor = max(pow(fresnel, 0.8), 0.5);
    float decay_factor = min(pow(fresnel, 0.6), 0.85);

    vec3 color = mix(g_vWaterFogColor.rgb, g_vWaterDecayColor.rgb, decay_factor);

    


    float IOR = 1.333; //Index of Refraction of water, 

    outputColor.w = 1.0;
    //outputColor = readMSFramebufferZeroth(color_map, uv + distortion_direction * 0.002).rgbw;
    //outputColor = vec4(distortion_direction, 0.0, 1.0);
    //outputColor = vec4(SrgbGammaToLinear(color), 1.0); // max(decay_factor, fog_factor)
    //outputColor.rgb = texelFetch(color_map, ivec2(gl_FragCoord.xy * 2), 0).rgb;
    //outputColor.rgb = vec3(1.0);
    

    

//    vec3 pixel_dir = normalize(vec3( (gl_FragCoord.xy  - resolution / 2), -resolution.y / 2 / tan(radians(fov / 2))    ) );
//    pixel_dir = inverse(mat3(g_matWorldToView)) * pixel_dir;
//    
//    vec3 ray_dir = mat3(g_matWorldToView) * normalize(reflect(pixel_dir, normal));
//    ray_dir.z *= -1; //fuck the negative space
//
//    float linear_depth = (1.0 /   (texelFetch(depth_map, ivec2(floor(gl_FragCoord.xy)), 0).r -0.05) ) * 0.95;
//    float local_depth = (1.0 / (gl_FragCoord.z - 0.05)) * 0.95;
//    pixel_dir = mat3(g_matWorldToView) * pixel_dir;
//
//    vec3 position = pixel_dir / -pixel_dir.z * local_depth;
//
//    position.z *= -1;
//
//
//    vec3 viewspace_normal = mat3(g_matWorldToView) * normal; //fuck worldspace
//    float refract_direction_length = dot(pixel_dir, viewspace_normal); //we do a little trickery
//
//    vec2 distortion_direction = vec2(pixel_dir + viewspace_normal * refract_direction_length * IOR); //the keen eyed might have noticed that the dot product above is always negative, it cancels out here though.
//    distortion_direction = vec2(-0.0);
//    float fade_size = 50;
//    float fade_out = min(( resolution.y / 2 - abs((gl_FragCoord.xy - resolution / 2).y) ), ( resolution.x / 2 - abs((gl_FragCoord.xy - resolution / 2).x) )) / fade_size;
//	fade_out = min(1, fade_out);
//
//    //outputColor.rgb = texelFetch(color_map, ivec2(gl_FragCoord.xy * 2), 0).rgb;
//    //outputColor.w = 1.0;
//    //return;
//
//    
//
//
//    int index = 1;
//    bool run_traverse = true;
//    vec3 trace_coordinates;
//    vec3 persistent_trace_coords = position + ray_dir * 0.01; //else water edges look noisy
//
//    MaterialProperties_t material;
//    InitProperties(material, normal); //do I even need this?
//    material.Normal = normal;
//
//    bool has_hit = false;
//    vec3 reflectionColor = SrgbLinearToGamma(GetEnvironment(material));
//    //return;
//
//
//	do
//	{
//		float distance_multiplier = 1; //fix later with a better formula :^)
//		trace_coordinates = persistent_trace_coords + ray_dir * 4 * distance_multiplier * Random2D(gl_FragCoord.xy); //the rand is necessary to cut back on raymarching artifacts (visible banding).
//        persistent_trace_coords = persistent_trace_coords + ray_dir * 4 * distance_multiplier; //persistent is required so we don't get wildly varying sampling resolutions
//
//		float local_depth = trace_coordinates.z;
//		vec2 screen_location = trace_coordinates.xy / trace_coordinates.z * resolution.y / 2 / tan(radians(fov / 2)) + resolution / 2;
//		vec2 depth_uv = screen_location / resolution; // actually: we don't even need this. With the MS buffer you would input "pixel numbers" anyways, would remove my helper func
//
//		float fade_size = 50;
//		float fade_out = min(( resolution.y / 2 - abs((screen_location - resolution / 2).y) ), ( resolution.x / 2 - abs((screen_location - resolution / 2).x) )) / fade_size;
//		fade_out = min(1, fade_out);
//
//		index++;
//
//		if(local_depth < 0 || fade_out <= 0 || index > 500)
//			run_traverse = false;
//
//		float distance_to_depth = abs(local_depth - (1.0 / (readMSFramebufferZeroth(depth_map, depth_uv).r - 0.05)) * 0.95 );
//		if(distance_to_depth < 4)
//		{
//            reflectionColor =  reflectionColor * (1 - fade_out) + vec3(readMSFramebufferZeroth(color_map, depth_uv).rgb)  * fresnel * fade_out * .8; //would a mix() be cleaner?
//            has_hit = true;
//			run_traverse = false;
//		}
//	}
//	while(run_traverse);
//    outputColor.rgb +=  reflectionColor  * fresnel  * 0.8;
        

    //ApplyFog(outputColor.rgb, vFragPosition); //if this is uncommented, the shader won't work for some unknown to god reason.
}



