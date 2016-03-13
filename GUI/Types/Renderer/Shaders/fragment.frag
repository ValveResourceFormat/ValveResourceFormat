#version 330
 
in vec2 vTexCoordOut;
out vec4 outputColor;
 
uniform float alphaReference;
uniform sampler2D colorTexture;
uniform sampler2D normalTexture;

void main()
{
    vec3 normal = normalize(texture2D(normalTexture, vTexCoordOut).rgb * 2.0 - 1.0);  
  
    vec3 light_pos = normalize(vec3(1.0, 1.0, 1.5));  
  
    float diffuse = max(dot(normal, light_pos), 0.0);  
  
    vec3 color = diffuse * texture2D(colorTexture, vTexCoordOut).rgb;  

    // if(texture2D(colorTexture, vTexCoordOut).a <= alphaReference) discard;
    outputColor = vec4(color, 1.0);
}
