#version 460

#define S_TEXTURE_LAYERS 0

// Where a card's vertices come from.
//   0  the buffer, one vertex per corner, which is what a rope strip hands over
//   1  generated, one set of attributes per particle at divisor 1, for a sprite card
//   2  generated, as 1, but a trail: a colour and a half width per end rather than one of each
//
// Generated paths reuse the buffer path's names where the data has the same role, because locations
// are allocated over every name declared here, even if the combo does not keep the declaration.
#define S_PARTICLE_INSTANCED 0

// A sheet of motion vectors warps the first two layers toward the frames they blend between. It plays
// the first layer's sequence, timing and placement from its own sheet.
#define S_MOTION_VECTORS 0

#if S_PARTICLE_INSTANCED == 2
// The trail's centre, and the tail's alpha in w.
in vec4 vPOSITION;
#else
// The card's centre, or for the buffer path the corner as placed before the size clamp.
in vec3 vPOSITION;
#endif
// The colour, alpha before any fade. A trail's is before either end's colour scale, and its alpha is the head's.
in vec4 vCOLOR;
// The particle's random value, its shader extra data 1, the age the sheet plays by, and its shader
// extra data 2.
in vec4 vTEXCOORD;
#if S_PARTICLE_INSTANCED
// The sequence the first layer and the motion vectors play in x, the one later layers play in y.
in vec2 vTEXCOORD1;
#else
// The card corner in xy, after flipping, and the rope's V before it is mapped to the texture in z.
in vec3 vTEXCOORD1;
#endif
#if S_PARTICLE_INSTANCED == 0
// The alpha and the radius of the particle the rope segment starts at, which the per particle modes read.
in vec2 vSegmentAlphaRadius;
#endif
// The axis whose alignment with the view fades the card out by view angle.
in vec3 vNORMAL;

#if S_PARTICLE_INSTANCED == 2
// A trail tapers independently at each end, so the half widths ride in w: across in xyz with the
// head's half width, along and already halved with the tail's.
in vec4 vAxisU;
in vec4 vAxisV;
// How much of the card coordinate the flip and crop leave, signed by the flip.
in vec2 vTrailUvCrop;

// Each end's colour scale, the same for every particle of the system.
uniform vec3 uTrailHeadColorScale = vec3(1.0);
uniform vec3 uTrailTailColorScale = vec3(1.0);
#else
// A card's unit axes, so a corner is centre + (u * x + v * y) * radius. For the buffer path, the unit
// axis from the strip's centre line out to this corner.
in vec3 vAxisU;
#if S_PARTICLE_INSTANCED == 1
in vec3 vAxisV;
#endif
// The particle's roll, yaw and pitch, and its radius before the size clamp.
in vec4 vTEXCOORD3;
#endif

// The cards are lit at their vertices, through includes that otherwise light fragments
#define VRF_VERTEX_STAGE

#include "common/utils.slang"
#include "common/ViewConstants.slang"
#include "common/features.slang"
#include "common/lighting.particle.slang"

#if (D_BAKED_LIGHTING_FROM_PROBE == 1)
uniform uint uObjectIndex = 0u;
#endif

// m_bGammaCorrectVertexColors, which defaults on. Particle colours are authored in gamma space; without
// this they are used as though they were already linear. Alpha is left alone.
uniform bool uGammaCorrectVertexColors = true;

// m_flDepthBias negated, in world units along the view direction. The card keeps its drawn position and
// only its depth is re-taken from a point moved toward the camera, so a positive bias makes the card win
// the depth test against geometry just in front of it.
uniform float uDepthBias = 0.0;

// The size clamp and the distance fade, which take radii per unit of camera distance: the minimum size,
// where the fade starts and ends, and the maximum size.
uniform bool uSizeClampAndFade = false;
uniform vec4 uSizeBounds = vec4(0.0, 1.0, 2.0, 1.0);

// The absolute cosine between the facing axis and the direction to the camera over which the card
// fades out by view angle.
uniform bool uViewAngleFade = false;
uniform vec2 uViewAngleFadeDots = vec2(1.0, 2.0);

#if S_PARTICLE_INSTANCED == 1
// m_flCenterXOffset and m_flCenterYOffset, in radii along the card's axes.
uniform vec2 uCenterOffset = vec2(0.0);
#endif

#if S_PARTICLE_INSTANCED == 0
// While no layer has a sheet, the rope's V is offset by x and scaled by a positive y, then held to 0-1
// when clamped.
uniform vec2 uRopeVMapping = vec2(0.0, 1.0);
uniform bool uClampV = false;
#endif

// Per-layer uv controls: scale in xy, offset in zw, then the rotation. A layer without a sheet turns by
// its rotation, in radians; a layer with one plays its sheet that many times as fast instead, or at the
// animation rate when that comes to 0.
uniform vec4 uLayerUvScaleOffset[S_TEXTURE_LAYERS + 1];
uniform float uLayerUvRotation[S_TEXTURE_LAYERS + 1];

// The per particle value (SpriteCardPerParticleScale_t) each control of each layer is multiplied by,
// four bits per control in the order of PP_SCALE to PP_BLEND, and whether the layer's offset is moved by
// a random amount per particle in bit PP_RANDOMIZE_OFFSETS_BIT.
uniform uint uLayerPerParticleModes[S_TEXTURE_LAYERS + 1];

// Whether each layer maps the colour beneath it through a ramp rather than placing its texture on the card.
uniform bool uLayerLookup[S_TEXTURE_LAYERS + 1];

// Whether any layer plays a sheet, which gives every layer without one the other set of keys.
uniform bool uAnyLayerHasSheet = false;

// The per particle values the alpha test threshold and window are taken from.
uniform int uAlphaReferenceMode = 0;
uniform int uAlphaRefWindowMode = 0;

// Whether the particle alpha test is on, which leaves a ribbon's U interpolated straight across it.
uniform bool uAlphaReference = false;

// The sheet cache: per layer, the row its first sequence starts at and how many sequences it has, 0
// for a layer without a sheet.
uniform sampler2D uSheetCache;
uniform vec2 uLayerSheet[S_TEXTURE_LAYERS + 1];
#if S_MOTION_VECTORS
uniform vec2 uMotionVectorSheet;
#endif

// How far the first and second layers zoom towards the card centre as they cross between frames, as
// the share of the card that remains. Later layers do not zoom.
uniform vec2 uFrameZoom = vec2(1.0);

// Passes through the sequence per second, or with uAnimateInFps sheet seconds per second.
uniform float uAnimationRate = 1.0;
uniform bool uAnimateInFps = false;

// Whether a card whose first layer shows a sheet frame shrinks to the art of the frames it blends.
uniform bool uCropFrames;

#define SHEET_COLUMNS 512
#define NO_FRAME_BLEND (-1.0)

// The controls a per particle value multiplies, which also index the keys below
#define PP_SCALE 0
#define PP_OFFSET_U 1
#define PP_OFFSET_V 2
#define PP_ROTATION 3
#define PP_ZOOM 4
#define PP_DISTORTION 5
#define PP_BLEND 6
#define PP_RANDOM_U 7
#define PP_RANDOM_V 8
#define PP_RANDOMIZE_OFFSETS_BIT 28

// What a control multiplies the particle's random value by before taking its fraction is its layer's key
// times its own, so every control of every layer reads a different value. A chain where any layer plays
// a sheet gives its layers without one the second set of layer keys.
const float PER_PARTICLE_CONTROL_KEYS[9] = float[9](1.0, 0.2177092, 0.27271673, 0.5249507, 0.3436811, 0.47799698, 0.8085732, 0.30374956, 0.2248184);
const float PER_PARTICLE_LAYER_KEYS[5] = float[5](5657.411, 169.49466, 59.664104, 19.181442, 3.0533476);
const float PER_PARTICLE_SEQUENCE_LAYER_KEYS[5] = float[5](33.658, 63.17984, 78.20789, 146.17879, 333.92426);

// The rotation key of each layer that plays a sheet, whose rotation scales how fast it plays.
const float SHEET_RATE_KEYS[5] = float[5](1.347, 3.5802, 5.734, 832.234, 48.376);

#define ALPHA_REFERENCE_KEY 238.84
#define ALPHA_REF_WINDOW_KEY 84.7456

out vec2 vTexCoordOut;
out vec2 vTexCoordNextFrameOut;
#if S_TEXTURE_LAYERS > 0
out vec4 vLayerUvOut[S_TEXTURE_LAYERS];
#endif
// Each layer's per particle controls. A lookup layer reads its ramp at ((luma + x) * z, y) and blends
// by w. Any other layer zooms x times as fast and weighs its distortion, or else its blend, by y.
flat out vec4 vLayerControlsOut[S_TEXTURE_LAYERS + 1];
#if S_MOTION_VECTORS
out vec4 vMotionVectorUvOut;
#endif
// The first layer's frame blend in x, the second layer's in y.
out vec2 vFrameBlendOut;
flat out vec2 vAlphaReferenceOut;
// The weight every layer's U carries, which the fragment shader divides back out.
out float vUvWeightOut;
out vec2 vCardUvOut;
out vec4 vColor;
out vec3 vFragPosition;
// The scene light at the vertex, before the diffuse amount
out vec3 vLightOut;

// The two frames a layer's sheet shows, and how far it has crossed from the first to the second.
struct SheetFrame
{
    // Frame A's and frame B's whole tiles, min in xy and max in zw
    vec4 TileA;
    vec4 TileB;

    // The part of the tile the art of both frames covers, in tile units
    vec4 Window;

    float Blend;
};

vec4 FetchSheetTexel(int column, int row)
{
    return texelFetch(uSheetCache, ivec2(column, row), 0);
}

// Rebuilds a frame's whole tile from the tile narrowed to the window and the window itself.
vec4 WidenTile(vec4 narrowed, vec4 window)
{
    vec2 size = (narrowed.zw - narrowed.xy) / (window.zw - window.xy);
    vec2 origin = narrowed.xy - (window.xy * size);

    return vec4(origin, origin + size);
}

// Looks up the frames a sequence of a sheet shows at the particle's age.
SheetFrame FetchSheetFrame(vec2 sheet, float sequence, float rate)
{
    // A sequence past the sheet's last plays the first. A negative one is not caught, and reads the
    // rows before the sheet's own, wrapped around the cache.
    if (sequence - sheet.y >= 0.0)
    {
        sequence = 0.0;
    }

    int cacheHeight = textureSize(uSheetCache, 0).y;
    int row = int(sheet.x) + (3 * int(sequence));
    row = int(mod(float(row), float(cacheHeight)));

    vec4 header = FetchSheetTexel(0, row);

    // The length is stored in sixteenths of a second, so a sequence shorter than that divides by zero
    float lengthScale = uAnimateInFps ? ((16.0 / 65535.0) / header.y) : 1.0;
    float position = lengthScale * float(SHEET_COLUMNS) * 0.5 * (vTEXCOORD.z * rate) * uAnimationRate;

    // Each sample takes a pair of texels. A clamped sequence stops on its last pair, and a negative
    // position wraps around the row either way.
    float column = 2.0 * floor(position);

    if (header.z > 0.0)
    {
        column = min(float(SHEET_COLUMNS - 2), column);
    }

    int texel = int(mod(column, float(SHEET_COLUMNS)));

    vec4 timing = FetchSheetTexel(texel, row);
    vec4 narrowedA = FetchSheetTexel(texel, row + 1);
    vec4 window = FetchSheetTexel(texel + 1, row + 1);
    vec4 narrowedB = FetchSheetTexel(texel, row + 2);

    SheetFrame frame;
    frame.TileA = WidenTile(narrowedA, window);
    frame.TileB = WidenTile(narrowedB, window);
    frame.Window = window;
    frame.Blend = mix(timing.x, timing.w, fract(position));

    return frame;
}

// Places a card coordinate in a sheet layer's two frames, in tile units: zoomed towards the centre
// as the layer crosses between them, then divided by the layer's scale and moved by its offset.
vec4 PlaceInSheet(vec2 cardUv, float blend, float zoom, vec4 scaleOffset)
{
    vec2 zoomed = ((cardUv - vec2(0.5)) * zoom) + vec2(0.5);
    vec2 placedA = mix(cardUv, zoomed, 0.5 + (0.5 * blend));
    vec2 placedB = mix(cardUv, zoomed, 0.5 * blend);

    return vec4(
        (placedA / scaleOffset.xy) + scaleOffset.zw,
        (placedB / scaleOffset.xy) + scaleOffset.zw);
}

// The direction a placed coordinate is pulled in as the card shrinks to its art, by the first
// layer's scale and offset whichever layer it belongs to.
vec4 CropPullDirection(vec4 placed)
{
    vec4 scaleOffset = uLayerUvScaleOffset[0];

    return ((scaleOffset.zwzw - placed) * 2.0) + scaleOffset.xyxy;
}

vec4 ResolveInTiles(SheetFrame frame, vec4 placed)
{
    return vec4(
        frame.TileA.xy + (placed.xy * (frame.TileA.zw - frame.TileA.xy)),
        frame.TileB.xy + (placed.zw * (frame.TileB.zw - frame.TileB.xy)));
}

// The particle values the per particle modes read besides the random value, the extra data and the age.
struct ParticleInputs
{
    float Alpha;
    float Radius;

    // Roll, yaw and pitch, in radians
    vec3 Rotation;
};

// The value a per particle mode reads, with key spreading the particle's random value so that each
// control reads its own. No mode reads 1.
float PerParticleValue(int mode, float key, ParticleInputs inputs)
{
    float random = fract(vTEXCOORD.x * key);
    float age = vTEXCOORD.z;

    switch (mode)
    {
        case 1: return age + 1.0;
        case 2: return (age * uAnimationRate) + 1.0;
        case 3: return vTEXCOORD.y;
        case 4: return vTEXCOORD.w;
        case 5: return inputs.Alpha;
        case 6: return inputs.Radius;
        case 7: return inputs.Rotation.x;
        case 8: return inputs.Rotation.y;
        case 9: return inputs.Rotation.z;
        case 10: return random;
        case 11: return (random - 0.5) * 2.0;
        case 12: return (age * random) + 1.0;
        case 13:
        {
            // Moved away from 0 by its own sign, so the value starts at -1 or 1 rather than 0
            float signedRandom = (random - 0.5) * 2.0;
            return (age * signedRandom) + sign(signedRandom);
        }
    }

    return 1.0;
}

bool LayerHasSheet(int layer)
{
    return uLayerSheet[layer].y > 0.0;
}

bool LayerRandomizesOffsets(int layer)
{
    return ((uLayerPerParticleModes[layer] >> uint(PP_RANDOMIZE_OFFSETS_BIT)) & 1u) != 0u;
}

// The key that spreads the particle's random value for one control of a layer without a sheet.
float LayerKey(int layer, int control)
{
    float layerKey = uAnyLayerHasSheet ? PER_PARTICLE_SEQUENCE_LAYER_KEYS[layer] : PER_PARTICLE_LAYER_KEYS[layer];

    return layerKey * PER_PARTICLE_CONTROL_KEYS[control];
}

// The value one control of a layer without a sheet is multiplied by for this particle.
float LayerValue(int layer, int control, ParticleInputs inputs)
{
    int mode = int((uLayerPerParticleModes[layer] >> uint(control * 4)) & 15u);

    return PerParticleValue(mode, LayerKey(layer, control), inputs);
}

// How many times as fast as the animation rate a layer with a sheet plays it for this particle.
float LayerSheetRate(int layer, ParticleInputs inputs)
{
    int mode = int((uLayerPerParticleModes[layer] >> uint(PP_ROTATION * 4)) & 15u);
    float rate = uLayerUvRotation[layer] * PerParticleValue(mode, SHEET_RATE_KEYS[layer], inputs);

    return rate == 0.0 ? 1.0 : rate;
}

// The random shift of a layer that randomizes its offsets, added after the offset has wrapped.
vec2 LayerRandomOffset(int layer)
{
    if (!LayerRandomizesOffsets(layer))
    {
        return vec2(0.0);
    }

    return fract(vTEXCOORD.x * vec2(LayerKey(layer, PP_RANDOM_U), LayerKey(layer, PP_RANDOM_V)));
}

// Places a card coordinate in a layer without a sheet: turned about the card centre, divided by the
// scale, landed at the wrapped offset, then shifted by the random offset. Rotation, scale and offset are
// each multiplied by their per particle value.
vec2 PlaceOffSheet(vec2 cardUv, int layer, ParticleInputs inputs)
{
    vec4 scaleOffset = uLayerUvScaleOffset[layer];
    vec2 centred = cardUv - vec2(0.5);

    float rotation = uLayerUvRotation[layer] * LayerValue(layer, PP_ROTATION, inputs);
    float sinRotation = sin(rotation);
    float cosRotation = cos(rotation);

    vec2 rotated = vec2(
        (centred.x * cosRotation) - (centred.y * sinRotation),
        (centred.x * sinRotation) + (centred.y * cosRotation));

    vec2 offset = scaleOffset.zw * vec2(LayerValue(layer, PP_OFFSET_U, inputs), LayerValue(layer, PP_OFFSET_V, inputs));
    vec2 origin = offset + vec2(0.5);
    vec2 wrapped = origin - floor(origin);

    return ((rotated / scaleOffset.xy) * (1.0 / LayerValue(layer, PP_SCALE, inputs))) + wrapped + LayerRandomOffset(layer);
}

// A lookup layer's ramp coordinates, in the layout of vLayerControlsOut. Unlike a placed texture, its
// offset neither centres nor wraps, and it does not turn.
vec4 LookupControls(int layer, ParticleInputs inputs)
{
    vec4 scaleOffset = uLayerUvScaleOffset[layer];

    // TODO: What a lookup layer with a sheet reads for its ramp is not known; it takes its own scale and
    // offset, without per particle values
    if (LayerHasSheet(layer))
    {
        return vec4(scaleOffset.z, scaleOffset.w / scaleOffset.y, 1.0 / scaleOffset.x, 1.0);
    }

    vec2 offset = (scaleOffset.zw * vec2(LayerValue(layer, PP_OFFSET_U, inputs), LayerValue(layer, PP_OFFSET_V, inputs)))
        + LayerRandomOffset(layer);
    vec2 inverseScale = vec2(1.0) / (scaleOffset.xy * LayerValue(layer, PP_SCALE, inputs));

    return vec4(offset.x, offset.y * inverseScale.y, inverseScale.x, LayerValue(layer, PP_BLEND, inputs));
}

// A layer's per particle controls, in the layout of vLayerControlsOut. A layer with a sheet takes none.
// The renderer clears the zoom mode of a layer that does not zoom and the distortion mode of one that
// does not distort, so they read 1 there.
vec4 LayerControls(int layer, ParticleInputs inputs)
{
    if (uLayerLookup[layer])
    {
        return LookupControls(layer, inputs);
    }

    if (LayerHasSheet(layer))
    {
        return vec4(1.0, 1.0, 0.0, 0.0);
    }

    float weight = LayerValue(layer, PP_DISTORTION, inputs) * LayerValue(layer, PP_BLEND, inputs);

    return vec4(LayerValue(layer, PP_ZOOM, inputs), weight, 0.0, 0.0);
}

float LayerFrameZoom(int layer)
{
    if (layer == 0)
    {
        return uFrameZoom.x;
    }

    if (layer == 1)
    {
        return uFrameZoom.y;
    }

    return 1.0;
}

// What is left of a card by the view angle fade, which fades it out as the facing axis lines up with
// the direction to the camera.
float ViewAngleFade(vec3 facingAxis, vec3 position)
{
    float facing = abs(dot(normalize(facingAxis), normalize(g_vCameraPositionWs - position)));

    return 1.0 - smoothstep(uViewAngleFadeDots.x, uViewAngleFadeDots.y, facing);
}

// Fades the whole colour out as the radius grows past the fade start at this distance, and takes it
// away entirely past the fade end.
vec4 DistanceFade(vec4 color, float radius, float distance)
{
    float fadeStart = uSizeBounds.y * distance;

    if (radius <= fadeStart)
    {
        return color;
    }

    if (radius > uSizeBounds.z * distance)
    {
        return vec4(0.0);
    }

    return color * (1.0 - ((radius - fadeStart) / (distance * (uSizeBounds.z - uSizeBounds.y))));
}

// Nested min and max rather than a clamp, so an inverted range resolves to the maximum.
float ClampSize(float radius, float distance)
{
    return min(max(radius, uSizeBounds.x * distance), uSizeBounds.w * distance);
}

void main(void) {
#if S_PARTICLE_INSTANCED
    // 0 -> (-1,+1), 1 -> (-1,-1), 2 -> (+1,+1), 3 -> (+1,-1). The strip's shared edge, and so the
    // diagonal it splits on, is the one the index buffer used. A sprite card is a parallelogram and does
    // not care, but a trail is a trapezoid wherever its ends taper differently, and there the diagonal
    // decides how the sheet interpolates across it.
    vec2 corner = vec2(
        (float(gl_VertexID / 2) * 2.0) - 1.0,
        1.0 - (float(gl_VertexID % 2) * 2.0));
#else
    vec2 corner = vTEXCOORD1.xy;
#endif

#if S_PARTICLE_INSTANCED == 2
    // Flipping and cropping scale the card coordinate about its centre and leave the geometry alone
    vec2 cardUv = (corner * vTrailUvCrop * 0.5) + vec2(0.5);
#elif S_PARTICLE_INSTANCED == 1
    // u runs with the corner, v against it
    vec2 cardUv = vec2((corner.x + 1.0) * 0.5, (1.0 - corner.y) * 0.5);
#else
    // A rope's card is mirrored across its width, so u runs against the corner as v does
    vec2 cardUv = (vec2(1.0) - corner) * 0.5;
#endif

#if S_PARTICLE_INSTANCED
    float sequence = vTEXCOORD1.x;
    float laterSequence = vTEXCOORD1.y;
#else
    // A rope plays the first sequence of every layer's sheet
    float sequence = 0.0;
    float laterSequence = 0.0;
#endif

    // A rope with no sheet on any layer takes V from its length instead of the card coordinate. Once
    // any layer has a sheet, the layers without one keep the card coordinate too.
    vec2 layerUv = cardUv;
#if S_PARTICLE_INSTANCED == 0
    if (!uAnyLayerHasSheet && uRopeVMapping.y > 0.0)
    {
        float ropeV = (vTEXCOORD1.z + uRopeVMapping.x) * uRopeVMapping.y;
        layerUv.y = uClampV ? saturate(ropeV) : ropeV;
    }
#endif

    // The per particle modes read the alpha before the fades and the radius before the size clamp. A
    // trail hands over the tail's and no rotation; a rope hands over those of the particle its segment
    // starts at.
    ParticleInputs inputs;
#if S_PARTICLE_INSTANCED == 1
    inputs.Alpha = vCOLOR.a;
    inputs.Radius = vTEXCOORD3.w;
    inputs.Rotation = vTEXCOORD3.xyz;
#elif S_PARTICLE_INSTANCED == 2
    inputs.Alpha = vPOSITION.w;
    inputs.Radius = vAxisV.w;
    inputs.Rotation = vec3(0.0);
#else
    inputs.Alpha = vSegmentAlphaRadius.x;
    inputs.Radius = vSegmentAlphaRadius.y;
    inputs.Rotation = vTEXCOORD3.xyz;
#endif

    for (int layer = 0; layer <= S_TEXTURE_LAYERS; ++layer)
    {
        vLayerControlsOut[layer] = LayerControls(layer, inputs);
    }

    // The motion vectors play at the first layer's rate
    float firstSheetRate = LayerSheetRate(0, inputs);

    // The first layer, which alone decides how far the card shrinks to its art
    vec2 cropPull = vec2(0.0);
    vec4 firstUvs = PlaceOffSheet(layerUv, 0, inputs).xyxy;
    float firstBlend = NO_FRAME_BLEND;

    if (LayerHasSheet(0))
    {
        SheetFrame frame = FetchSheetFrame(uLayerSheet[0], sequence, firstSheetRate);
        vec4 placed = PlaceInSheet(cardUv, frame.Blend, LayerFrameZoom(0), uLayerUvScaleOffset[0]);

        if (uCropFrames)
        {
            // Each corner is pulled towards the card centre until its coordinate reaches the edge of
            // the art. The corner and its coordinate move by the same pull, so with a scale other
            // than 1 the art lands at a different size and place than the scale alone would put it.
            vec4 direction = CropPullDirection(placed);
#if S_PARTICLE_INSTANCED == 1
            vec2 edge = mix(frame.Window.zw, frame.Window.xy, step(0.0, direction.xy));
#else
            // A trail or a rope is only cropped to the art across its width. Along it the pull stops
            // at the edge of the tile, so a coordinate inside the tile is not pulled, and the
            // geometry is only narrowed.
            vec2 edge = mix(vec2(frame.Window.z, 1.0), vec2(frame.Window.x, 0.0), step(0.0, direction.xy));
#endif

            cropPull = clamp((edge - placed.xy) / direction.xy, 0.0, 1.0);
            placed += direction * cropPull.xyxy;
        }

        firstUvs = ResolveInTiles(frame, placed);
        firstBlend = frame.Blend;
    }

    vTexCoordOut = firstUvs.xy;
    vTexCoordNextFrameOut = firstUvs.zw;

    float secondBlend = NO_FRAME_BLEND;

#if S_TEXTURE_LAYERS > 0
    // Later layers play the second sequence. Those with a sheet follow the first layer's crop; those
    // without keep the uncropped card coordinate.
    for (int layer = 1; layer <= S_TEXTURE_LAYERS; ++layer)
    {
        vec4 uvs = PlaceOffSheet(layerUv, layer, inputs).xyxy;

        if (LayerHasSheet(layer))
        {
            SheetFrame frame = FetchSheetFrame(uLayerSheet[layer], laterSequence, LayerSheetRate(layer, inputs));
            vec4 placed = PlaceInSheet(cardUv, frame.Blend, LayerFrameZoom(layer), uLayerUvScaleOffset[layer]);

            placed += CropPullDirection(placed) * cropPull.xyxy;
            uvs = ResolveInTiles(frame, placed);

            if (layer == 1)
            {
                secondBlend = frame.Blend;
            }
        }

        vLayerUvOut[layer - 1] = uvs;
    }
#endif

#if S_MOTION_VECTORS
    // The motion vectors play the first layer's sequence, rate, zoom and placement in their own sheet
    vMotionVectorUvOut = firstUvs;

    // TODO: Which coordinates the motion vectors take when they have no sheet is not known; they follow the first layer
    if (uMotionVectorSheet.y > 0.0)
    {
        SheetFrame frame = FetchSheetFrame(uMotionVectorSheet, sequence, firstSheetRate);
        vec4 placed = PlaceInSheet(cardUv, frame.Blend, LayerFrameZoom(0), uLayerUvScaleOffset[0]);

        placed += CropPullDirection(placed) * cropPull.xyxy;
        vMotionVectorUvOut = ResolveInTiles(frame, placed);
    }
#endif

    vFrameBlendOut = vec2(firstBlend, secondBlend);

#if S_PARTICLE_INSTANCED == 2
    // The head is the end the ribbon runs towards, which the along axis points away from
    bool isHead = corner.y < 0.0;

    vec3 colorScale = isHead ? uTrailHeadColorScale : uTrailTailColorScale;
    float alpha = isHead ? vCOLOR.a : vPOSITION.w;
    vColor = vec4(saturate(vCOLOR.rgb * colorScale), alpha);
#else
    vColor = vCOLOR;
#endif

    if (uGammaCorrectVertexColors)
    {
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }

#if S_PARTICLE_INSTANCED == 1
    vec3 center = vPOSITION;
    float radius = vTEXCOORD3.w;
#elif S_PARTICLE_INSTANCED == 2
    // Each end is faded and clamped by its own half width and its own distance from the camera
    vec3 center = vPOSITION.xyz + (vAxisV.xyz * corner.y);
    float radius = isHead ? vAxisU.w : vAxisV.w;
#else
    vec3 center = vPOSITION - (vAxisU * vTEXCOORD3.w);
    float radius = vTEXCOORD3.w;
#endif

#if S_PARTICLE_INSTANCED == 1
    vUvWeightOut = 1.0;
#else
    // A ribbon's ends can differ in width, down to a point where it tapers to nothing. Weighting U by
    // the half width at each end, before the size clamp, keeps every slice across the ribbon spanning
    // the whole of U rather than squeezing the texture towards the narrow end.
    vUvWeightOut = uAlphaReference ? 1.0 : radius;
    vTexCoordOut.x *= vUvWeightOut;
    vTexCoordNextFrameOut.x *= vUvWeightOut;

#if S_TEXTURE_LAYERS > 0
    for (int layer = 0; layer < S_TEXTURE_LAYERS; ++layer)
    {
        vLayerUvOut[layer].xz *= vUvWeightOut;
    }
#endif
#endif

    if (uViewAngleFade)
    {
        vColor.a *= ViewAngleFade(vNORMAL, center);
    }

    if (uSizeClampAndFade)
    {
        float cameraDistance = length(g_vCameraPositionWs - center);

        vColor = DistanceFade(vColor, radius, cameraDistance);
        radius = ClampSize(radius, cameraDistance);
    }

#if S_PARTICLE_INSTANCED == 1
    // A card with next to no alpha left is collapsed to nothing
    if (vColor.a < (1.0 / 255.0))
    {
        radius = 0.0;
    }

    // The crop shrinks the card before the centre offset moves it
    vec2 croppedCorner = corner * (vec2(1.0) - (cropPull * 2.0));
    vec3 positionWs = center
        + (((vAxisU * (croppedCorner.x + uCenterOffset.x)) + (vAxisV * (croppedCorner.y + uCenterOffset.y))) * radius);

    cardUv = vec2((croppedCorner.x + 1.0) * 0.5, (1.0 - croppedCorner.y) * 0.5);
#elif S_PARTICLE_INSTANCED == 2
    // The crop narrows the ribbon but keeps its length
    float across = corner.x * (1.0 - (cropPull.x * 2.0));
    vec3 positionWs = center + (vAxisU.xyz * (across * radius));
#else
    // The crop narrows the strip but keeps its length
    vec3 positionWs = center + (vAxisU * (radius * (1.0 - (cropPull.x * 2.0))));
#endif

    vAlphaReferenceOut = vec2(
        1.0 - PerParticleValue(uAlphaReferenceMode, ALPHA_REFERENCE_KEY, inputs),
        PerParticleValue(uAlphaRefWindowMode, ALPHA_REF_WINDOW_KEY, inputs));
    vCardUvOut = cardUv;
    vFragPosition = positionWs;

#if (D_BAKED_LIGHTING_FROM_PROBE == 1)
    g_nObjectIndex = uObjectIndex;
#endif

    // Every card, trail and rope is lit where each of its vertices is drawn, and the light is
    // interpolated across it
    ParticleLight_t light = CalculateParticleLighting(positionWs, -g_vCameraDirWs, 0.0, false);
    vLightOut = light.Ambient + light.Direct;

    gl_Position = g_matWorldToProjection * vec4(positionWs, 1.0);

    if (uDepthBias != 0.0)
    {
        vec4 biased = g_matWorldToProjection * vec4(positionWs + (g_vCameraDirWs * uDepthBias), 1.0);

        // Rehomogenised against the unbiased w so only depth moves, then floored off the near plane
        gl_Position.z = max(gl_Position.w * (biased.z / biased.w), min(0.001, gl_Position.z));
    }
}
