#version 330

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float alphaReference;
uniform sampler2D colorTexture;
uniform sampler2D normalTexture;

uniform vec3 vLightPosition;

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal() 
{
    //Get the noral from the texture map -- Normal map seems broken
    vec4 bumpNormal = texture2D(normalTexture, vTexCoordOut);

    //Reconstruct the tangent vector from the map
    vec2 temp = vec2(bumpNormal.y, bumpNormal.w) * 2 - 1;
    vec3 tangentNormal = vec3(temp, 1 - temp.x*temp.x - temp.y*temp.y);

    vec3 normal = vNormalOut;
    vec3 tangent = vTangentOut.xyz;
    vec3 bitangent = vBitangentOut;

    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * tangentNormal);
}

//Main entry point
void main()
{
    //Get the direction from the fragment to the light - light position == camera position for now
    vec3 lightDirection = normalize(vLightPosition - vFragPosition);

    //Get the ambient color from the color texture
    vec4 color = texture2D(colorTexture, vTexCoordOut);

    //Get the world normal for this fragment
    vec3 worldNormal = calculateWorldNormal();

    //Calculate half-lambert lighting
    float illumination = dot(worldNormal, lightDirection);
    illumination = illumination * 0.5 + 0.5;
    illumination = illumination * illumination;

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(illumination * color.rgb, color.a);
}


