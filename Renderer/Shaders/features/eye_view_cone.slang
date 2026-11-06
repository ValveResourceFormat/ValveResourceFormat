#version 460

#ifndef VRF_EYE_VIEW_CONE_INCLUDE
#define VRF_EYE_VIEW_CONE_INCLUDE

mat3 RotationMatrix(vec3 axis, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    float oc = 1.0 - c;

    return mat3(oc * axis.x * axis.x + c,           oc * axis.x * axis.y - axis.z * s,  oc * axis.z * axis.x + axis.y * s,
                oc * axis.x * axis.y + axis.z * s,  oc * axis.y * axis.y + c,           oc * axis.y * axis.z - axis.x * s,
                oc * axis.z * axis.x - axis.y * s,  oc * axis.y * axis.z + axis.x * s,  oc * axis.z * axis.z + c);
}

// Turns the view direction back to the edge of a 40 degree cone around coneAxis when it leaves it.
void ConstrainEyeDirection(vec3 coneAxis, vec3 eyePosition, inout vec3 targetPosition, inout vec3 viewDir)
{
    const float maxAngle = radians(40.0);

    if (acos(dot(viewDir, coneAxis)) > maxAngle)
    {
        vec3 axis = normalize(cross(coneAxis, viewDir));
        mat3 rotation = RotationMatrix(axis, maxAngle);

        targetPosition = ((coneAxis * rotation) * distance(targetPosition, eyePosition)) + eyePosition;
        viewDir = normalize(targetPosition - eyePosition);
    }
}

#endif
