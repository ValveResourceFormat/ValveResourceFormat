#version 330

in vec2 vTexCoordOut;
out vec4 outputColor;

uniform float alphaReference;
uniform sampler2D colorTexture;
uniform sampler2D normalTexture;

uniform vec3 vLightPosition;

void main()
{
    vec4 light_color = vec4(0.5, 0.5, 0.5, 1.0);
    vec4 ambient_color = vec4(0.9, 0.9, 0.9, 1.0);
    vec3 falloff = vec3(0.4, 3.0, 20.0);

    //RGBA of our diffuse color
    vec4 DiffuseColor = texture2D(colorTexture, vTexCoordOut);

    //RGB of our normal map
    vec3 NormalMap = texture2D(normalTexture, vTexCoordOut).rgb;

    //Determine distance (used for attenuation) BEFORE we normalize our LightDir
    float D = length(vLightPosition);

    //normalize our vectors
    vec3 N = normalize(NormalMap * 2.0 - 1.0);
    vec3 L = normalize(vLightPosition);

    //Pre-multiply light color with intensity
    //Then perform "N dot L" to determine our diffuse term
    vec3 Diffuse = (light_color.rgb * light_color.a) * max(dot(N, L), 0.0);

    //pre-multiply ambient color with intensity
    vec3 Ambient = ambient_color.rgb * ambient_color.a;

    //calculate attenuation
    float Attenuation = 1.0 / ( falloff.x + (falloff.y*D) + (falloff.z*D*D) );

    //the calculation which brings it all together
    vec3 Intensity = Ambient + Diffuse * Attenuation;
    vec3 FinalColor = DiffuseColor.rgb * Intensity;
    outputColor = vec4(FinalColor, DiffuseColor.a);
}
