#version 330

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec4 vTangentOut;
in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float alphaReference;
uniform sampler2D colorTexture;
uniform sampler2D normalTexture;

uniform vec3 vLightPosition;

//The lights used to calculate the normal map
const vec3 bumpLight1 = vec3(sqrt(2.0) / sqrt(3.0), 0.0, 1.0 / sqrt(3.0)); 
const vec3 bumpLight2 = vec3(-1.0 / sqrt(6.0),  1.0 / sqrt(2.0), 1.0 / sqrt(3.0));
const vec3 bumpLight3 = vec3(-1.0 / sqrt(6.0), -1.0 / sqrt(2.0), 1.0 / sqrt(3.0));

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal() 
{
    //Get the noral from the texture map -- Normal map seems broken
    vec3 bumpNormal = texture2D(normalTexture, vTexCoordOut).rga;

    //Reconstruct the tangent vector from the map
    vec3 tangentNormal = normalize(bumpLight1 * bumpNormal.x + bumpLight2 * bumpNormal.y + bumpLight3 * bumpNormal.z);
    //Invert the x and y of the tangent normal because ???
    tangentNormal.x *= -1;
    tangentNormal.y *= -1;

    //Get normal and tangent and calculate the final tangent-space axis (bitangent)
    vec3 normal = normalize(vNormalOut);
    vec4 tangent = normalize(vTangentOut);
    vec3 bitangent = cross( normal, tangent.xyz );
    bitangent *= tangent.w;

    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent.xyz, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return tangentSpace * tangentNormal;
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
    //vec3 worldNormal = DecompressNormal(vNormalOut);

    //Calculate the illumination value by taking the dot product
    float illumination = dot(worldNormal, lightDirection);

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(worldNormal, color.a);
}


