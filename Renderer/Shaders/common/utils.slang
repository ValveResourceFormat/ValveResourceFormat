#version 460

public const float PI = 3.1415926535897932384626433832795;
// tau = PI*2
public const float TAU = 6.2831853071795864769252867665590;


// value^2
public float pow2(float val)
{
    return(val * val);
}
public vec2 pow2(vec2 val)
{
    return(val * val);
}
public vec3 pow2(vec3 val)
{
    return(val * val);
}
public vec4 pow2(vec4 val)
{
    return(val * val);
}


public float pow5(float val)
{
    return pow(val, 5.0); // Should get optimized
}

public float Random2D(vec2 Seed)
{
    return fract(sin( dot(Seed, vec2(12.9898, 78.233)) ) * 43758.5469);
}


public float min3( vec3 Vector )
{
	return min( min( Vector.x, Vector.y ), Vector.z );
}
public float max3( vec3 Vector )
{
	return max( max( Vector.x, Vector.y ), Vector.z );
}



public float length_squared(vec2 vector)
{
	return dot(vector, vector);
}
public float length_squared(vec3 vector)
{
	return dot(vector, vector);
}
public float length_squared(vec4 vector)
{
	return dot(vector, vector);
}


//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Color
//-------------------------------------------------------------------------------------------------------------------------------------------------------------

public const vec3 gamma = vec3(2.4);
public const vec3 invGamma = vec3(1.0 / gamma);

public float GetLuma(vec3 Color) {
	return dot( Color, vec3(0.2126, 0.7152, 0.0722) );
}

public float LevelsAdjust(float component, vec3 L_BlackWhiteGamma)
{
    vec3 levels = vec3(
        -L_BlackWhiteGamma.x,
        -1.4427 * log2(max(0.0001, 1.0 - L_BlackWhiteGamma.y)),
        2.0 - L_BlackWhiteGamma.z
    );

    return mix(levels.x, levels.y, pow(component, levels.z));
}

public float SrgbGammaToLinear(float color)
{
    float vLinearSegment = color / 12.92;
    float vExpSegment = pow((color / 1.055) + 0.0521327, 2.4);

    const float cap = 0.04045;
    float select = color > cap ? vExpSegment : vLinearSegment;

    return select;
}
public vec2 SrgbGammaToLinear(vec2 color)
{
    vec2 vLinearSegment = color / vec2(12.92);
    vec2 vExpSegment = pow((color / vec2(1.055)) + vec2(0.0521327), vec2(2.4));

    const float cap = 0.04045;
    float select = color.r > cap ? vExpSegment.r : vLinearSegment.r;
    float select1 = color.g > cap ? vExpSegment.g : vLinearSegment.g;

    return vec2(select, select1);
}
public vec3 SrgbGammaToLinear(vec3 color)
{
    vec3 vLinearSegment = color / vec3(12.92);
    vec3 vExpSegment = pow((color / vec3(1.055)) + vec3(0.0521327), vec3(2.4));

    const float cap = 0.04045;
    float select = color.r > cap ? vExpSegment.r : vLinearSegment.r;
    float select1 = color.g > cap ? vExpSegment.g : vLinearSegment.g;
    float select2 = color.b > cap ? vExpSegment.b : vLinearSegment.b;

    return vec3(select, select1, select2);
}
public vec4 SrgbGammaToLinear(vec4 color)
{
    vec4 vLinearSegment = color / vec4(12.92);
    vec4 vExpSegment = pow((color / vec4(1.055)) + vec4(0.0521327), vec4(2.4));

    const float cap = 0.04045;
    float select = color.r > cap ? vExpSegment.r : vLinearSegment.r;
    float select1 = color.g > cap ? vExpSegment.g : vLinearSegment.g;
    float select2 = color.b > cap ? vExpSegment.b : vLinearSegment.b;
    float select3 = color.a > cap ? vExpSegment.a : vLinearSegment.a;

    return vec4(select, select1, select2, select3);
}

public vec3 SrgbLinearToGamma( vec3 vLinearColor )
{
	// 15 asm instructions
	vec3 vLinearSegment = vLinearColor.rgb * 12.92;
	vec3 vExpSegment = ( 1.055 * pow( vLinearColor.rgb, vec3 ( 1.0 / 2.4) )) - 0.055;

	vec3 vGammaColor = vec3(( vLinearColor.r <= 0.0031308 ) ? vLinearSegment.r : vExpSegment.r,
                            ( vLinearColor.g <= 0.0031308 ) ? vLinearSegment.g : vExpSegment.g,
                            ( vLinearColor.b <= 0.0031308 ) ? vLinearSegment.b : vExpSegment.b );

	return vGammaColor.rgb;
}

public vec3 DecodeYCoCg(vec4 color)
{
    float scale = (color.z * (255.0 / 8.0)) + 1.0;
    float Co = (color.x + (-128.0 / 255.0)) / scale;
    float Cg = (color.y + (-128.0 / 255.0)) / scale;
    float Y = color.w;

    float R = Y + Co - Cg;
    float G = Y + Cg;
    float B = Y - Co - Cg;

    return vec3(R, G, B);
}

public vec2 Resize2D(float Base, vec4 StartPos_Size)
{
	return StartPos_Size.xy + Base * StartPos_Size.zw;
}

public vec2 Resize2D(vec2 Base, vec4 StartPos_Size)
{
	return StartPos_Size.xy + Base * StartPos_Size.zw;
}


public vec2 RotateVector2D(vec2 vectorInput, float rotation, vec2 scale, vec2 offset)
{
    rotation = radians(rotation);
    float sinRot = sin(rotation);
    float cosRot = cos(rotation);

    vec2 finalOffset = (scale.xy * -0.5) * vec2(cosRot - sinRot, sinRot - cosRot) + offset + vec2(0.5);
    vec4 xform = scale.xxyy * vec4(cosRot, -sinRot, sinRot, cosRot);

    return vec2(dot(xform.xy, vectorInput.xy), dot(xform.zw, vectorInput.xy)) + finalOffset;
}

public vec2 RotateVector2D(vec2 vectorInput, float rotation, vec2 scale, vec2 offset, vec2 center)
{

    rotation = radians(rotation);
    float sinRot = sin(rotation);
    float cosRot = cos(rotation);

    vec4 xform = vec4(cosRot * scale.x, -sinRot * scale.y, sinRot * scale.x, cosRot* scale.y);

    vec2 finalOffset;
    finalOffset.x = (center.x + offset.x) + (sinRot * center.y) - (cosRot * center.x);
    finalOffset.y = (center.y + offset.y) - (sinRot * center.x) + (cosRot * center.y);
    // ^ all of this should be precalculated

    return vec2(dot(xform.xy, vectorInput.xy), dot(xform.zw, vectorInput.xy)) + finalOffset;
}

// All of this is directly ported from The Lab Renderer from the unity store.
// These are their utility functions, at least as of the lab.

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
public float ClampToPositive( float flValue )
{
	return max( 0.0, flValue );
}

public vec2 ClampToPositive( vec2 vValue )
{
	return max( vec2(0.0), vValue.xy );
}

public vec3 ClampToPositive( vec3 vValue )
{
	return max( vec3(0.0), vValue.xyz );
}

public vec4 ClampToPositive( vec4 vValue )
{
	return max( vec4(0.0), vValue.xyzw );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// basically map to 0-1
public float LinearRamp( float flMin, float flMax, float flInput )
{
	return saturate( ( flInput - flMin ) / ( flMax - flMin ) );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Remap a value in the range [A,B] to [C,D].
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
public float RemapVal( float flOldVal, float flOldMin, float flOldMax, float flNewMin, float flNewMax )
{
	// Put the old val into 0-1 range based on the old min/max
	float flValNormalized = ( flOldVal - flOldMin ) / ( flOldMax - flOldMin );

	// Map 0-1 range into new min/max
	return ( flValNormalized * ( flNewMax - flNewMin ) ) + flNewMin;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Remap a value in the range [A,B] to [C,D]. Values <A map to C, and >B maps to D.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
public float RemapValClamped( float flOldVal, float flOldMin, float flOldMax, float flNewMin, float flNewMax )
{
	// Put the old val into 0-1 range based on the old min/max
	float flValNormalized = saturate( ( flOldVal - flOldMin ) / ( flOldMax - flOldMin ) );

	// Map 0-1 range into new min/max
	return ( flValNormalized * ( flNewMax - flNewMin ) ) + flNewMin;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
public vec4 PackToColor( vec4 vValue )
{
	return ( ( vValue.xyzw * 0.5 ) + 0.5 );
}

public vec3 PackToColor( vec3 vValue )
{
	return ( ( vValue.xyz * 0.5 ) + 0.5 );
}

public vec2 PackToColor( vec2 vValue )
{
	return ( ( vValue.xy * 0.5 ) + 0.5 );
}

public float PackToColor( float flValue )
{
	return ( ( flValue * 0.5 ) + 0.5 );
}

public vec4 UnpackFromColor( vec4 cColor )
{
	return ( ( cColor.xyzw * 2.0 ) - 1.0 );
}

public vec3 UnpackFromColor( vec3 cColor )
{
	return ( ( cColor.xyz * 2.0 ) - 1.0 );
}

public vec2 UnpackFromColor( vec2 cColor )
{
	return ( ( cColor.xy * 2.0 ) - 1.0 );
}

public float UnpackFromColor( float flColor )
{
	return ( ( flColor * 2.0 ) - 1.0 );
}

public vec3 DecodeDxt5Normal(vec4 bumpTexel)
{
    vec2 temp = UnpackFromColor(bumpTexel.ag);
    temp.y = -temp.y;
    return vec3(temp, sqrt(saturate(1 - dot(temp, temp))));
}

public vec3 oct_to_float32x3(vec2 e)
{
    vec3 v = vec3(e.xy, 1.0 - abs(e.x) - abs(e.y));
    return normalize(v);
}

// Unpack HemiOct normal map
public vec3 DecodeHemiOctahedronNormal(vec2 vHemiOct)
{
    //Reconstruct the tangent vector from the map
    vec2 temp = vec2(vHemiOct.x + vHemiOct.y - 1.003922, vHemiOct.x - vHemiOct.y);
    vec3 tangentNormal = oct_to_float32x3(temp);

    // This is free, it gets compiled into the TS->WS matrix mul
    tangentNormal.y = -tangentNormal.y;
    return tangentNormal;
}
