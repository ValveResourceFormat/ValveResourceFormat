#version 460

// Baked lighting
public extern static const int D_BAKED_LIGHTING_FROM_LIGHTMAP = 0;
public extern static const int D_BAKED_LIGHTING_FROM_VERTEX_STREAM = 0;
public extern static const int D_BAKED_LIGHTING_FROM_PROBE = 0;

public extern static const int S_LIGHTMAP_VERSION_MAJOR = 0;
public extern static const int S_LIGHTMAP_VERSION_MINOR = 0;

public uniform int F_RENDER_BACKFACES;
public uniform int F_DONT_FLIP_BACKFACE_NORMALS;
public uniform int F_RETRO_REFLECTIVE;


//Solution for easy data-passing between stages when the "in" keyword is basically not useful anymore
public struct FeatureData
{
    //BAKED LIGHTING
    public Conditional<vec3, D_BAKED_LIGHTING_FROM_LIGHTMAP == 1> vLightmapUVScaled;
    public Conditional<vec3, D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1> vPerVertexLightingOut;



    //Call at start of fragment shader
    public void MakeGlobal()
    {
        globalFragFeatureData = this;
    }
};



//Idea: set this right at the start of the frag shader.
public static FeatureData globalFragFeatureData;


