#version 330

in vec3 vFragPosition;
in vec3 vViewDirection;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float alphaReference;
uniform sampler2D colorTexture;
uniform sampler2D normalTexture;
uniform sampler2D mask1Texture;
uniform sampler2D mask2Texture;

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

    //Read textures
    vec4 color = texture2D(colorTexture, vTexCoordOut);
    vec4 mask1 = texture2D(mask1Texture, vTexCoordOut);
    vec4 mask2 = texture2D(mask2Texture, vTexCoordOut);

    //Get the world normal for this fragment
    vec3 worldNormal = calculateWorldNormal();

    //Calculate half-lambert lighting
    float illumination = dot(worldNormal, lightDirection);
    illumination = illumination * 0.5 + 0.5;
    illumination = illumination * illumination;

    //Self illumination - mask 1 channel A
    illumination = illumination + mask1.a;

    //Calculate ambient color
    vec3 ambientColor = illumination * color.rgb;

    //Calculate Blinn specular based on reflected light
    vec3 halfDir = normalize(lightDirection + vViewDirection);
    float specularAngle = max(dot(halfDir, worldNormal), 0.0);

    //Calculate final specular based on specular exponent - mask 2 channel A
    float specular = pow(specularAngle, mask2.a * 100);
    //Multiply by mapped specular intensity - mask 2 channel R
    specular = specular * mask2.r;

    //Calculate specular light color based on the specular tint map - mask 2 channel B
    vec3 specularColor = mix(vec3(1.0, 1.0, 1.0), color.rgb, mask2.b);

    //Calculate rim light
    float rimLight = 1.0 - max(dot(worldNormal, lightDirection), 0.0);
    rimLight = smoothstep( 0.5, 1.0, rimLight );

    //Multiply the rim light by the rim light intensity - mask 2 channel G
    rimLight = 2 * rimLight * mask2.g;

    //Get metalness for future use - mask 1 channel B
    float metalness = mask1.b;

    //Final color
    vec3 finalColor = ambientColor  * mix(1, 0.5, metalness) + specularColor * specular + specularColor * rimLight;

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(finalColor, color.a);
}


