// https://seblagarde.wordpress.com/2012/09/29/image-based-lighting-approaches-and-parallax-corrected-cubemap/
vec3 CubeMapBoxProjection(vec3 pos, vec3 R, vec3 mins, vec3 maxs, vec3 center)
{
    // Following is the parallax-correction code
    // Find the ray intersection with box plane
    vec3 FirstPlaneIntersect = (maxs - pos) / R;
    vec3 SecondPlaneIntersect = (mins - pos) / R;
    // Get the furthest of these intersections along the ray
    // (Ok because x/0 give +inf and -x/0 give –inf )
    vec3 FurthestPlane = max(FirstPlaneIntersect, SecondPlaneIntersect);
    // Find the closest far intersection
    float Distance = min(min(FurthestPlane.x, FurthestPlane.y), FurthestPlane.z);

    // Get the intersection position
    vec3 IntersectPositionWS = pos + R * Distance;
    // Get corrected reflection
    return IntersectPositionWS - center;
    // End parallax-correction code
}
