using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles.Upgrade;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Source1.Particles;

/// <summary>How a material draws the sprites of a particle system.</summary>
/// <param name="Texture">The path of the base texture, or <see langword="null"/> when the material has none.</param>
/// <param name="Additive">Whether the material blends additively.</param>
/// <param name="BlendOverAlpha">Whether the material adds its color over the destination, weighted by its alpha.</param>
/// <param name="OverbrightFactor">Scales the color of the texture.</param>
/// <param name="AddSelfAmount">The amount of the base texture that is added on top of itself.</param>
/// <param name="BlendFrames">Whether the frames of an animated sprite fade into each other.</param>
internal sealed record PcfMaterialInfo(string? Texture, bool Additive, bool BlendOverAlpha, float OverbrightFactor, float AddSelfAmount, bool BlendFrames);

/// <summary>
/// Converts a particle system of a <c>.pcf</c> file to the definition a Source 2 particle system runs from, by
/// mapping each function to the Source 2 class that does the same and its settings to the class's keys.
/// A function without such a class is left out.
/// </summary>
internal sealed class Source1ParticleConverter
{
    // Names that older files use for functions, which the game resolves to their current names when it loads a file
    private static readonly Dictionary<string, string> LegacyFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha_fade"] = "Alpha Fade and Decay",
        ["alpha_fade_in_random"] = "Alpha Fade In Random",
        ["alpha_fade_out_random"] = "Alpha Fade Out Random",
        ["basic_movement"] = "Movement Basic",
        ["color_fade"] = "Color Fade",
        ["controlpoint_light"] = "Color Light From Control Point",
        ["Dampen Movement Relative to Control Point"] = "Movement Dampen Relative to Control Point",
        ["Distance Between Control Points Scale"] = "Remap Distance Between Two Control Points to Scalar",
        ["Distance to Control Points Scale"] = "Remap Distance to Control Point to Scalar",
        ["lifespan_decay"] = "Lifespan Decay",
        ["lock to bone"] = "Movement Lock to Bone",
        ["postion_lock_to_controlpoint"] = "Movement Lock to Control Point",
        ["maintain position along path"] = "Movement Maintain Position Along Path",
        ["Match Particle Velocities"] = "Movement Match Particle Velocities",
        ["Max Velocity"] = "Movement Max Velocity",
        ["noise"] = "Noise Scalar",
        ["vector noise"] = "Noise Vector",
        ["oscillate_scalar"] = "Oscillate Scalar",
        ["oscillate_vector"] = "Oscillate Vector",
        ["Orient Rotation to 2D Direction"] = "Rotation Orient to 2D Direction",
        ["radius_scale"] = "Radius Scale",
        ["Random Cull"] = "Cull Random",
        ["remap_scalar"] = "Remap Scalar",
        ["rotation_movement"] = "Rotation Basic",
        ["rotation_spin"] = "Rotation Spin Roll",
        ["rotation_spin yaw"] = "Rotation Spin Yaw",
        ["alpha_random"] = "Alpha Random",
        ["color_random"] = "Color Random",
        ["create from parent particles"] = "Position From Parent Particles",
        ["Create In Hierarchy"] = "Position In CP Hierarchy",
        ["random position along path"] = "Position Along Path Random",
        ["random position on model"] = "Position on Model Random",
        ["sequential position along path"] = "Position Along Path Sequential",
        ["position_offset_random"] = "Position Modify Offset Random",
        ["position_warp_random"] = "Position Modify Warp Random",
        ["position_within_box"] = "Position Within Box Random",
        ["position_within_sphere"] = "Position Within Sphere Random",
        ["Inherit Velocity"] = "Velocity Inherit from Control Point",
        ["Initial Repulsion Velocity"] = "Velocity Repulse from World",
        ["Initial Velocity Noise"] = "Velocity Noise",
        ["Initial Scalar Noise"] = "Remap Noise to Scalar",
        ["Lifespan from distance to world"] = "Lifetime from Time to Impact",
        ["Pre-Age Noise"] = "Lifetime Pre-Age Noise",
        ["lifetime from sequence"] = "Lifetime From Sequence",
        ["remap initial scalar"] = "Remap Initial Scalar",
        ["remap control point to Vector"] = "Remap Control Point to Vector",
        ["remap scalar to vector"] = "Remap Scalar to Vector",
        ["move particles between 2 control points"] = "Move Particles Between 2 Control Points",
        ["lifetime_random"] = "Lifetime Random",
        ["radius_random"] = "Radius Random",
        ["random yaw"] = "Rotation Yaw Random",
        ["Randomly Flip Yaw"] = "Rotation Yaw Flip Random",
        ["rotation_random"] = "Rotation Random",
        ["rotation_speed_random"] = "Rotation Speed Random",
        ["sequence_random"] = "Sequence Random",
        ["second_sequence_random"] = "Sequence Two Random",
        ["trail_length_random"] = "Trail Length Random",
        ["velocity_random"] = "Velocity Random",
    };

    private const int ParticleSystemMaxControlPoint = 63;
    private const int LifeDurationField = 1;
    private const int RadiusField = 3;
    private const int RollField = 4;
    private const int RollSpeedField = 5;
    private const int ColorField = 6;
    private const int AlphaField = 7;
    private const int TrailLengthField = 10;
    private const int YawField = 12;

    private readonly PcfParticleSystem system;
    private readonly Func<string, PcfMaterialInfo?>? resolveMaterial;
    private readonly ILogger logger;
    private readonly ISet<string>? reported;

    private Source1ParticleConverter(PcfParticleSystem system, Func<string, PcfMaterialInfo?>? resolveMaterial, ILogger logger, ISet<string>? reported)
    {
        this.system = system;
        this.resolveMaterial = resolveMaterial;
        this.logger = logger;
        this.reported = reported;
    }

    /// <summary>Converts a system.</summary>
    /// <param name="system">The system to convert.</param>
    /// <param name="resolveMaterial">Reads the material the system draws with, or <see langword="null"/> to draw without a texture.</param>
    /// <param name="logger">Receives a debug message for each function that has no Source 2 equivalent.</param>
    /// <param name="reported">The functions that were already reported, so that each is reported once. Without it every use is reported.</param>
    public static ParticleSystem Convert(PcfParticleSystem system, Func<string, PcfMaterialInfo?>? resolveMaterial, ILogger logger, ISet<string>? reported = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(logger);

        var converter = new Source1ParticleConverter(system, resolveMaterial, logger, reported);

        return ParticleSystem.Create(converter.ConvertSystem(), ParticleFormatUpgrader.LatestFormat);
    }

    private KVObject ConvertSystem()
    {
        var element = system.Element;
        var data = KVObject.Collection();

        data.Add("_class", "CParticleSystemDefinition");
        data.Add("m_nBehaviorVersion", 0);
        data.Add("m_nMaxParticles", element.GetInt("max_particles", 1000));
        data.Add("m_nInitialParticles", element.GetInt("initial_particles"));
        data.Add("m_BoundingBoxMin", KVHelpers.ToKVArray(element.GetVector3("bounding_box_min", new Vector3(-10f))));
        data.Add("m_BoundingBoxMax", KVHelpers.ToKVArray(element.GetVector3("bounding_box_max", new Vector3(10f))));
        data.Add("m_flConstantRadius", element.GetFloat("radius", 5f));
        data.Add("m_ConstantColor", Color(element.GetColor("color", new DmxColor(255, 255, 255, 255))));
        data.Add("m_flConstantRotation", element.GetFloat("rotation"));
        data.Add("m_flConstantRotationSpeed", element.GetFloat("rotation_speed"));
        data.Add("m_ConstantNormal", KVHelpers.ToKVArray(element.GetVector3("normal", Vector3.UnitZ)));
        data.Add("m_nConstantSequenceNumber", element.GetInt("sequence_number"));
        data.Add("m_nConstantSequenceNumber1", element.GetInt("sequence_number 1"));
        data.Add("m_nGroupID", element.GetInt("group id"));
        data.Add("m_flMaximumTimeStep", element.GetFloat("maximum time step", 0.1f));
        data.Add("m_flMaximumSimTime", element.GetFloat("maximum sim tick rate"));
        data.Add("m_flMinimumSimTime", element.GetFloat("minimum sim tick rate"));
        data.Add("m_nMinimumFrames", element.GetInt("minimum rendered frames"));
        data.Add("m_flMaxDrawDistance", element.GetFloat("maximum draw distance", 100000f));
        data.Add("m_flNoDrawTimeToGoToSleep", element.GetFloat("time to sleep when not drawn", 8f));

        data.Add("m_Emitters", ConvertFunctions("emitter", system.Emitters, ConvertEmitter));
        data.Add("m_Initializers", ConvertFunctions("initializer", system.Initializers, ConvertInitializer));
        var preEmissionOperators = system.Operators.FindAll(IsPreEmissionOperator);
        var operators = system.Operators.FindAll(function => !IsPreEmissionOperator(function));

        data.Add("m_PreEmissionOperators", ConvertFunctions("operator", preEmissionOperators, ConvertOperator));
        data.Add("m_Operators", ConvertFunctions("operator", operators, ConvertOperator));
        data.Add("m_ForceGenerators", ConvertFunctions("force", system.Forces, ConvertForce));
        data.Add("m_Constraints", ConvertFunctions("constraint", system.Constraints, ConvertConstraint));
        data.Add("m_Renderers", ConvertFunctions("renderer", system.Renderers, ConvertRenderer));
        data.Add("m_Children", ConvertChildren());

        return data;
    }

    // These run once per frame ahead of the emitters, on the system rather than on its particles
    private static bool IsPreEmissionOperator(PcfFunction function)
    {
        var name = LegacyFunctionNames.GetValueOrDefault(function.FunctionName, function.FunctionName);

        return name == "Set Control Point Positions";
    }

    private KVObject ConvertChildren()
    {
        var children = KVObject.Array();

        foreach (var child in system.Children)
        {
            var entry = KVObject.Collection();
            entry.Add("m_ChildRef", child.Name);
            entry.Add("m_flDelay", child.Delay);
            entry.Add("m_bEndCap", child.EndCap);
            children.Add(entry);
        }

        return children;
    }

    private KVObject ConvertFunctions(string kind, List<PcfFunction> functions, Func<string, DmxElement, KVObject?> convert)
    {
        var converted = KVObject.Array();

        foreach (var function in functions)
        {
            var name = LegacyFunctionNames.GetValueOrDefault(function.FunctionName, function.FunctionName);
            var result = convert(name, function.Element);

            if (result == null)
            {
                ReportUnsupported(kind, function.FunctionName);
                continue;
            }

            AddGenericFields(result, function.Element);
            converted.Add(result);
        }

        return converted;
    }

    private void ReportUnsupported(string kind, string functionName)
    {
        if (reported != null && !reported.Add($"{kind}|{functionName}"))
        {
            return;
        }

        logger.LogDebug("Source 1 particle {Kind} '{Function}' of {System} has no Source 2 equivalent and is skipped", kind, functionName, system.Name);
    }

    private static KVObject Function(string className)
    {
        var function = KVObject.Collection();
        function.Add("_class", className);

        return function;
    }

    // The fields every operator has to fade it in and out over the life of the system
    private static void AddGenericFields(KVObject function, DmxElement source)
    {
        AddIfPresent(function, source, "operator start fadein", "m_flOpStartFadeInTime");
        AddIfPresent(function, source, "operator end fadein", "m_flOpEndFadeInTime");
        AddIfPresent(function, source, "operator start fadeout", "m_flOpStartFadeOutTime");
        AddIfPresent(function, source, "operator end fadeout", "m_flOpEndFadeOutTime");
        AddIfPresent(function, source, "operator fade oscillate", "m_flOpFadeOscillatePeriod");
        AddIfPresent(function, source, "operator time offset min", "m_flOpTimeOffsetMin");
        AddIfPresent(function, source, "operator time offset max", "m_flOpTimeOffsetMax");
        AddIfPresent(function, source, "operator time offset seed", "m_nOpTimeOffsetSeed");
    }

    private static void AddIfPresent(KVObject function, DmxElement source, string sourceKey, string key)
    {
        switch (source.Get(sourceKey))
        {
            case float value when value != 0f:
                function.Add(key, value);
                break;
            case int value when value != 0:
                function.Add(key, value);
                break;
        }
    }

    private KVObject? ConvertEmitter(string name, DmxElement source)
    {
        switch (name)
        {
            case "emit_continuously":
            {
                var emitter = Function("C_OP_ContinuousEmitter");
                emitter.Add("m_flStartTime", source.GetFloat("emission_start_time"));
                emitter.Add("m_flEmissionDuration", source.GetFloat("emission_duration"));
                emitter.Add("m_flEmitRate", source.GetFloat("emission_rate", 100f));
                emitter.Add("m_flEmissionScale", source.GetFloat("scale emission to used control points"));
                emitter.Add("m_flScalePerParentParticle", source.GetBool("use parent particles for emission scaling") ? 1f : 0f);
                emitter.Add("m_bInitFromKilledParentParticles", source.GetBool("emit particles for killed parent particles"));
                return emitter;
            }

            case "emit_instantaneously":
            {
                var emitter = Function("C_OP_InstantaneousEmitter");
                var count = source.GetInt("num_to_emit", 100);
                var countMin = source.GetInt("num_to_emit_minimum", -1);
                var startTime = source.GetFloat("emission_start_time");
                var startTimeMax = source.GetFloat("emission_start_time max", -1f);

                emitter.Add("m_nParticlesToEmit", countMin >= 0 && countMin != count ? RandomFloat(countMin, count) : new KVObject((float)count));
                emitter.Add("m_flStartTime", startTimeMax >= 0f && startTimeMax != startTime ? RandomFloat(startTime, startTimeMax) : new KVObject(startTime));
                emitter.Add("m_nMaxEmittedPerFrame", source.GetInt("maximum emission per frame", -1));
                return emitter;
            }

            case "emit noise":
            {
                var emitter = Function("C_OP_NoiseEmitter");
                emitter.Add("m_flStartTime", source.GetFloat("emission_start_time"));
                emitter.Add("m_flEmissionDuration", source.GetFloat("emission_duration"));
                emitter.Add("m_flEmissionScale", source.GetFloat("scale emission to used control points"));
                emitter.Add("m_flNoiseScale", source.GetFloat("time noise coordinate scale", 0.1f));
                emitter.Add("m_flOffset", source.GetFloat("time coordinate offset"));
                emitter.Add("m_bAbsVal", source.GetBool("absolute value"));
                emitter.Add("m_bAbsValInv", source.GetBool("invert absolute value"));
                emitter.Add("m_flOutputMin", source.GetFloat("emission minimum"));
                emitter.Add("m_flOutputMax", source.GetFloat("emission maximum", 100f));
                emitter.Add("m_flWorldTimeScale", source.GetFloat("world time noise coordinate scale"));
                return emitter;
            }

            case "emit to maintain count":
            {
                var emitter = Function("C_OP_MaintainEmitter");
                emitter.Add("m_flStartTime", source.GetFloat("emission start time"));
                emitter.Add("m_nParticlesToMaintain", source.GetInt("count to maintain", 100));
                return emitter;
            }

            default:
                return null;
        }
    }

    private KVObject? ConvertInitializer(string name, DmxElement source)
    {
        switch (name)
        {
            case "Lifetime Random":
                return RandomInit(source, "lifetime_min", "lifetime_max", "lifetime_random_exponent", 0f, 0f, LifeDurationField);

            case "Radius Random":
                return RandomInit(source, "radius_min", "radius_max", "radius_random_exponent", 1f, 1f, RadiusField);

            case "Trail Length Random":
                return RandomInit(source, "length_min", "length_max", "length_random_exponent", 0.1f, 0.1f, TrailLengthField);

            case "Alpha Random":
            {
                var min = source.GetInt("alpha_min", 255) / 255f;
                var max = source.GetInt("alpha_max", 255) / 255f;

                return InitFloat(AlphaField, min, max, source.Attributes.ContainsKey("alpha_random_exponent"), source.GetFloat("alpha_random_exponent", 1f), signFlip: false);
            }

            case "Rotation Random":
                return RotationInit(source, RollField, "rotation_initial", "rotation_offset_min", "rotation_offset_max", "rotation_random_exponent", 360f);

            case "Rotation Yaw Random":
                return RotationInit(source, YawField, "yaw_initial", "yaw_offset_min", "yaw_offset_max", "yaw_random_exponent", 360f);

            case "Rotation Speed Random":
                return RotationInit(source, RollSpeedField, "rotation_speed_constant", "rotation_speed_random_min", "rotation_speed_random_max", "rotation_speed_random_exponent", 360f);

            case "Rotation Yaw Flip Random":
            {
                var initializer = Function("C_INIT_RandomYawFlip");
                initializer.Add("m_flPercent", source.GetFloat("Flip Percentage", 0.5f));
                return initializer;
            }

            case "Color Random":
            {
                var initializer = Function("C_INIT_RandomColor");
                initializer.Add("m_ColorMin", Color(source.GetColor("color1", new DmxColor(255, 255, 255, 255))));
                initializer.Add("m_ColorMax", Color(source.GetColor("color2", new DmxColor(255, 255, 255, 255))));
                initializer.Add("m_TintMin", Color(source.GetColor("tint clamp min", new DmxColor(0, 0, 0, 0))));
                initializer.Add("m_TintMax", Color(source.GetColor("tint clamp max", new DmxColor(255, 255, 255, 255))));
                initializer.Add("m_flTintPerc", source.GetFloat("tint_perc"));
                initializer.Add("m_nTintCP", source.GetInt("tint control point"));
                initializer.Add("m_flUpdateThreshold", source.GetFloat("tint update movement threshold", 32f));
                initializer.Add("m_nFieldOutput", source.GetInt("output field", ColorField));
                return initializer;
            }

            case "Sequence Random":
            {
                var initializer = Function("C_INIT_RandomSequence");
                initializer.Add("m_nSequenceMin", source.GetInt("sequence_min"));
                initializer.Add("m_nSequenceMax", source.GetInt("sequence_max"));
                initializer.Add("m_bShuffle", source.GetBool("shuffle"));
                initializer.Add("m_bLinear", source.GetBool("linear"));
                return initializer;
            }

            case "Sequence Two Random":
            {
                var initializer = Function("C_INIT_RandomSecondSequence");
                initializer.Add("m_nSequenceMin", source.GetInt("sequence_min"));
                initializer.Add("m_nSequenceMax", source.GetInt("sequence_max"));
                return initializer;
            }

            case "Lifetime From Sequence":
            {
                var initializer = Function("C_INIT_SequenceLifeTime");
                initializer.Add("m_flFramerate", source.GetFloat("Frames Per Second", 30f));
                return initializer;
            }

            case "Position Within Sphere Random":
            {
                var initializer = Function("C_INIT_CreateWithinSphereTransform");
                initializer.Add("m_TransformInput", SphereTransform(source));
                initializer.Add("m_fRadiusMin", source.GetFloat("distance_min"));
                initializer.Add("m_fRadiusMax", source.GetFloat("distance_max"));
                initializer.Add("m_vecDistanceBias", KVHelpers.ToKVArray(source.GetVector3("distance_bias", Vector3.One)));
                initializer.Add("m_vecDistanceBiasAbs", KVHelpers.ToKVArray(source.GetVector3("distance_bias_absolute_value")));
                initializer.Add("m_bLocalCoords", source.GetBool("bias in local system"));
                initializer.Add("m_fSpeedMin", source.GetFloat("speed_min"));
                initializer.Add("m_fSpeedMax", source.GetFloat("speed_max"));
                initializer.Add("m_fSpeedRandExp", source.GetFloat("speed_random_exponent", 1f));
                initializer.Add("m_LocalCoordinateSystemSpeedMin", KVHelpers.ToKVArray(source.GetVector3("speed_in_local_coordinate_system_min")));
                initializer.Add("m_LocalCoordinateSystemSpeedMax", KVHelpers.ToKVArray(source.GetVector3("speed_in_local_coordinate_system_max")));
                return initializer;
            }

            case "Position Within Box Random":
            {
                var initializer = Function("C_INIT_CreateWithinBox");
                initializer.Add("m_vecMin", KVHelpers.ToKVArray(source.GetVector3("min")));
                initializer.Add("m_vecMax", KVHelpers.ToKVArray(source.GetVector3("max")));
                initializer.Add("m_nControlPointNumber", source.GetInt("control point number"));
                initializer.Add("m_bLocalSpace", source.GetBool("use local space"));
                return initializer;
            }

            case "Position Modify Offset Random":
            {
                var initializer = Function("C_INIT_PositionOffset");
                initializer.Add("m_OffsetMin", KVHelpers.ToKVArray(source.GetVector3("offset min")));
                initializer.Add("m_OffsetMax", KVHelpers.ToKVArray(source.GetVector3("offset max")));
                initializer.Add("m_TransformInput", new KVObject(source.GetInt("control_point_number")));
                initializer.Add("m_bLocalCoords", source.GetBool("offset in local space 0/1"));
                initializer.Add("m_bProportional", source.GetBool("offset proportional to radius 0/1"));
                return initializer;
            }

            case "Position Modify Warp Random":
            {
                var initializer = Function("C_INIT_PositionWarp");
                initializer.Add("m_vecWarpMin", KVHelpers.ToKVArray(source.GetVector3("warp min", Vector3.One)));
                initializer.Add("m_vecWarpMax", KVHelpers.ToKVArray(source.GetVector3("warp max", Vector3.One)));
                initializer.Add("m_nControlPointNumber", source.GetInt("control point number"));
                initializer.Add("m_flWarpTime", source.GetFloat("warp transition time (treats min/max as start/end sizes)"));
                initializer.Add("m_flWarpStartTime", source.GetFloat("warp transition start time"));
                initializer.Add("m_bInvertWarp", source.GetBool("reverse warp (0/1)"));
                initializer.Add("m_bUseCount", source.GetBool("use particle count instead of time"));
                return initializer;
            }

            case "Velocity Random":
            {
                var initializer = Function("C_INIT_VelocityRandom");
                initializer.Add("m_nControlPointNumber", source.GetInt("control_point_number"));
                initializer.Add("m_fSpeedMin", source.GetFloat("random_speed_min"));
                initializer.Add("m_fSpeedMax", source.GetFloat("random_speed_max"));
                initializer.Add("m_LocalCoordinateSystemSpeedMin", KVHelpers.ToKVArray(source.GetVector3("speed_in_local_coordinate_system_min")));
                initializer.Add("m_LocalCoordinateSystemSpeedMax", KVHelpers.ToKVArray(source.GetVector3("speed_in_local_coordinate_system_max")));
                return initializer;
            }

            case "Velocity Noise":
            {
                var initializer = Function("C_INIT_InitialVelocityNoise");
                initializer.Add("m_vecOutputMin", KVHelpers.ToKVArray(source.GetVector3("output minimum")));
                initializer.Add("m_vecOutputMax", KVHelpers.ToKVArray(source.GetVector3("output maximum", Vector3.One)));
                initializer.Add("m_flNoiseScale", source.GetFloat("Time Noise Coordinate Scale", 1f));
                initializer.Add("m_flNoiseScaleLoc", source.GetFloat("Spatial Noise Coordinate Scale", 0.01f));
                initializer.Add("m_flOffset", source.GetFloat("Time Coordinate Offset"));
                initializer.Add("m_vecOffsetLoc", KVHelpers.ToKVArray(source.GetVector3("Spatial Coordinate Offset")));
                initializer.Add("m_vecAbsVal", KVHelpers.ToKVArray(source.GetVector3("Absolute Value")));
                initializer.Add("m_vecAbsValInv", KVHelpers.ToKVArray(source.GetVector3("Invert Abs Value")));

                // Without a transform the velocity is in world space
                if (source.GetBool("Apply Velocity in Local Space (0/1)"))
                {
                    initializer.Add("m_TransformInput", new KVObject(source.GetInt("Control Point Number")));
                }

                return initializer;
            }

            case "Velocity Inherit from Control Point":
            {
                var initializer = Function("C_INIT_InheritVelocity");
                initializer.Add("m_nControlPointNumber", source.GetInt("control point number"));
                initializer.Add("m_flVelocityScale", source.GetFloat("velocity scale", 1f));
                return initializer;
            }

            case "Position From Parent Particles":
            {
                var initializer = Function("C_INIT_CreateFromParentParticles");
                initializer.Add("m_flVelocityScale", source.GetFloat("Inherited Velocity Scale"));
                initializer.Add("m_bRandomDistribution", source.GetBool("Random Parent Particle Distribution"));
                initializer.Add("m_flIncrement", source.GetFloat("Particle Increment Amount", 1f));
                return initializer;
            }

            case "Remap Initial Scalar":
            {
                var input = KVObject.Collection();
                input.Add("m_nType", "PF_TYPE_PARTICLE_FLOAT");
                input.Add("m_nScalarAttribute", source.GetInt("input field", 8));

                return RemapInit(source, input, source.GetInt("output field", RadiusField));
            }

            case "Remap Control Point to Scalar":
            {
                var input = KVObject.Collection();
                input.Add("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
                input.Add("m_nControlPoint", source.GetInt("input control point number"));
                input.Add("m_nVectorComponent", source.GetInt("input field 0-2 X/Y/Z"));

                return RemapInit(source, input, source.GetInt("output field", RadiusField));
            }

            case "Remap Initial Distance to Control Point to Scalar":
            {
                var initializer = Function("C_INIT_DistanceToCPInit");
                initializer.Add("m_nFieldOutput", source.GetInt("output field", RadiusField));
                initializer.Add("m_flInputMin", source.GetFloat("distance minimum"));
                initializer.Add("m_flInputMax", source.GetFloat("distance maximum", 128f));
                initializer.Add("m_flOutputMin", source.GetFloat("output minimum"));
                initializer.Add("m_flOutputMax", source.GetFloat("output maximum", 1f));
                initializer.Add("m_nStartCP", source.GetInt("control point"));
                initializer.Add("m_bLOS", source.GetBool("ensure line of sight"));
                initializer.Add("m_flMaxTraceLength", source.GetFloat("Maximum Trace Length", -1f));
                initializer.Add("m_flLOSScale", source.GetFloat("LOS Failure Scalar"));
                initializer.Add("m_bActiveRange", source.GetBool("only active within specified distance"));
                initializer.Add("m_nSetMethod", GetSetMethod(source));
                return initializer;
            }

            case "Remap Noise to Scalar":
            {
                var initializer = Function("C_INIT_CreationNoise");
                initializer.Add("m_nFieldOutput", source.GetInt("output field", RadiusField));
                initializer.Add("m_bAbsVal", source.GetBool("absolute value"));
                initializer.Add("m_bAbsValInv", source.GetBool("invert absolute value"));
                initializer.Add("m_flOffset", source.GetFloat("time coordinate offset"));
                initializer.Add("m_flOutputMin", source.GetFloat("output minimum"));
                initializer.Add("m_flOutputMax", source.GetFloat("output maximum", 1f));
                initializer.Add("m_flNoiseScale", source.GetFloat("time noise coordinate scale", 0.1f));
                initializer.Add("m_flNoiseScaleLoc", source.GetFloat("spatial noise coordinate scale", 0.001f));
                initializer.Add("m_flWorldTimeScale", source.GetFloat("world time noise coordinate scale"));
                initializer.Add("m_vecOffsetLoc", KVHelpers.ToKVArray(source.GetVector3("spatial coordinate offset")));
                return initializer;
            }

            case "Remap Scalar to Vector":
            {
                var initializer = Function("C_INIT_RemapScalarToVector");
                initializer.Add("m_nFieldInput", source.GetInt("input field", 8));
                initializer.Add("m_nFieldOutput", source.GetInt("output field"));
                initializer.Add("m_flInputMin", source.GetFloat("input minimum"));
                initializer.Add("m_flInputMax", source.GetFloat("input maximum", 1f));
                initializer.Add("m_vecOutputMin", KVHelpers.ToKVArray(source.GetVector3("output minimum")));
                initializer.Add("m_vecOutputMax", KVHelpers.ToKVArray(source.GetVector3("output maximum", Vector3.One)));
                initializer.Add("m_flStartTime", source.GetFloat("emitter lifetime start time (seconds)", -1f));
                initializer.Add("m_flEndTime", source.GetFloat("emitter lifetime end time (seconds)", -1f));
                initializer.Add("m_nSetMethod", GetSetMethod(source));
                initializer.Add("m_nControlPointNumber", source.GetInt("control_point_number"));
                initializer.Add("m_bLocalCoords", source.GetBool("use local system", true));
                return initializer;
            }

            case "Remap Control Point to Vector":
            {
                var initializer = Function("C_INIT_RemapTransformToVector");
                initializer.Add("m_nFieldOutput", source.GetInt("output field"));
                initializer.Add("m_vInputMin", KVHelpers.ToKVArray(source.GetVector3("input minimum")));
                initializer.Add("m_vInputMax", KVHelpers.ToKVArray(source.GetVector3("input maximum")));
                initializer.Add("m_vOutputMin", KVHelpers.ToKVArray(source.GetVector3("output minimum")));
                initializer.Add("m_vOutputMax", KVHelpers.ToKVArray(source.GetVector3("output maximum")));
                initializer.Add("m_TransformInput", new KVObject(source.GetInt("input control point number")));
                initializer.Add("m_flStartTime", source.GetFloat("emitter lifetime start time (seconds)", -1f));
                initializer.Add("m_flEndTime", source.GetFloat("emitter lifetime end time (seconds)", -1f));
                initializer.Add("m_nSetMethod", GetSetMethod(source));
                initializer.Add("m_bOffset", source.GetBool("offset position"));
                initializer.Add("m_bAccelerate", source.GetBool("accelerate position"));

                var localSpace = source.GetInt("local space CP", -1);

                if (localSpace >= 0)
                {
                    initializer.Add("m_LocalSpaceTransform", new KVObject(localSpace));
                }

                return initializer;
            }

            case "Move Particles Between 2 Control Points":
            {
                var initializer = Function("C_INIT_MoveBetweenPoints");
                initializer.Add("m_flSpeedMin", source.GetFloat("minimum speed", 1f));
                initializer.Add("m_flSpeedMax", source.GetFloat("maximum speed", 1f));
                initializer.Add("m_flEndSpread", source.GetFloat("end spread"));
                initializer.Add("m_flStartOffset", source.GetFloat("start offset"));
                initializer.Add("m_flEndOffset", source.GetFloat("end offset"));
                initializer.Add("m_nEndControlPointNumber", source.GetInt("end control point", 1));
                initializer.Add("m_bTrailBias", source.GetBool("bias lifetime by trail length"));
                return initializer;
            }

            case "Position Along Path Sequential":
            {
                var initializer = Function("C_INIT_CreateSequentialPath");
                initializer.Add("m_fMaxDistance", source.GetFloat("maximum distance"));
                initializer.Add("m_flNumToAssign", source.GetFloat("particles to map from start to end", 100f));
                initializer.Add("m_bLoop", source.GetBool("restart behavior (0 = bounce, 1 = loop )", true));
                initializer.Add("m_bCPPairs", source.GetBool("Use sequential CP pairs between start and end point"));
                initializer.Add("m_bSaveOffset", source.GetBool("Save Offset"));
                initializer.Add("m_PathParams", PathParameters(source));
                return initializer;
            }

            case "Position Along Path Random":
            {
                var initializer = Function("C_INIT_CreateAlongPath");
                initializer.Add("m_fMaxDistance", source.GetFloat("maximum distance"));
                initializer.Add("m_PathParams", PathParameters(source));
                initializer.Add("m_bUseRandomCPs", source.GetBool("randomly select sequential CP pairs between start and end points"));
                return initializer;
            }

            default:
                return null;
        }
    }

    // Spawns around the control point, or around a random one of those that are set above it
    private static KVObject SphereTransform(DmxElement source)
    {
        var controlPoint = source.GetInt("control_point_number");

        if (!source.GetBool("randomly distribute to highest supplied Control Point"))
        {
            return new KVObject(controlPoint);
        }

        var transform = KVObject.Collection();
        transform.Add("m_nType", "PT_TYPE_CONTROL_POINT_RANGE");
        transform.Add("m_nControlPoint", controlPoint);
        transform.Add("m_nControlPointRangeMax", ParticleSystemMaxControlPoint);
        transform.Add("m_flEndCPGrowthTime", source.GetFloat("randomly distribution growth time"));

        return transform;
    }

    private static string GetSetMethod(DmxElement source)
    {
        if (source.GetBool("output is scalar of initial random range"))
        {
            return "PARTICLE_SET_SCALE_INITIAL_VALUE";
        }

        if (source.GetBool("output is scalar of current value"))
        {
            return "PARTICLE_SET_SCALE_CURRENT_VALUE";
        }

        return "PARTICLE_SET_REPLACE_VALUE";
    }

    // The remap is an input of an InitFloat, which only acts while the emitter is within its lifetime window
    private static KVObject RemapInit(DmxElement source, KVObject input, int outputField)
    {
        var initializer = Function("C_INIT_InitFloat");
        initializer.Add("m_nOutputField", outputField);
        initializer.Add("m_nSetMethod", GetSetMethod(source));

        input.Add("m_flInput0", source.GetFloat("input minimum"));
        input.Add("m_flInput1", source.GetFloat("input maximum", 1f));
        input.Add("m_flOutput0", source.GetFloat("output minimum"));
        input.Add("m_flOutput1", source.GetFloat("output maximum", 1f));
        input.Add("m_nMapType", "PF_MAP_TYPE_REMAP");
        initializer.Add("m_InputValue", input);

        var startTime = source.GetFloat("emitter lifetime start time (seconds)", -1f);
        var endTime = source.GetFloat("emitter lifetime end time (seconds)", -1f);

        if (startTime != -1f && startTime == endTime)
        {
            initializer.Add("m_bDisableOperator", true);
        }
        else if (startTime != -1f || endTime != -1f)
        {
            var strength = KVObject.Collection();
            strength.Add("m_nType", "PF_TYPE_COLLECTION_AGE");
            strength.Add("m_nMapType", "PF_MAP_TYPE_NOTCHED");
            strength.Add("m_flNotchedRangeMin", startTime);
            strength.Add("m_flNotchedRangeMax", endTime);
            strength.Add("m_flNotchedOutputOutside", 0f);
            strength.Add("m_flNotchedOutputInside", 1f);
            initializer.Add("m_flOpStrength", strength);
        }

        return initializer;
    }

    private static KVObject PathParameters(DmxElement source, string bulgeKey = "bulge")
    {
        const string BulgeControlKey = "bulge control 0=random 1=orientation of start pnt 2=orientation of end point";

        var path = KVObject.Collection();
        path.Add("m_nStartControlPointNumber", source.GetInt("start control point number"));
        path.Add("m_nEndControlPointNumber", source.GetInt("end control point number"));
        path.Add("m_nBulgeControl", source.GetInt(BulgeControlKey));
        path.Add("m_flBulge", source.GetFloat(bulgeKey));
        path.Add("m_flMidPoint", source.GetFloat("mid point position", 0.5f));

        return path;
    }

    private KVObject? ConvertOperator(string name, DmxElement source)
    {
        switch (name)
        {
            case "Movement Basic":
            {
                var movement = Function("C_OP_BasicMovement");
                movement.Add("m_Gravity", KVHelpers.ToKVArray(source.GetVector3("gravity")));
                movement.Add("m_fDrag", source.GetFloat("drag"));
                movement.Add("m_nMaxConstraintPasses", source.GetInt("max constraint passes", 3));
                return movement;
            }

            case "Lifespan Decay":
                return Function("C_OP_Decay");

            case "Alpha Fade and Decay":
                return FadeAndKill("C_OP_FadeAndKill", source);

            case "Alpha Fade and Decay for Tracers":
                return FadeAndKill("C_OP_FadeAndKillForTracers", source);

            case "Alpha Fade In Random":
            {
                var fade = Function("C_OP_FadeInRandom");
                fade.Add("m_flFadeInTimeMin", source.GetFloat("fade in time min", 0.25f));
                fade.Add("m_flFadeInTimeMax", source.GetFloat("fade in time max", 0.25f));
                fade.Add("m_flFadeInTimeExp", source.GetFloat("fade in time exponent", 1f));
                fade.Add("m_bProportional", source.GetBool("proportional 0/1", true));
                return fade;
            }

            case "Alpha Fade Out Random":
            {
                var fade = Function("C_OP_FadeOutRandom");
                fade.Add("m_flFadeOutTimeMin", source.GetFloat("fade out time min", 0.25f));
                fade.Add("m_flFadeOutTimeMax", source.GetFloat("fade out time max", 0.25f));
                fade.Add("m_flFadeOutTimeExp", source.GetFloat("fade out time exponent", 1f));
                fade.Add("m_bProportional", source.GetBool("proportional 0/1", true));
                fade.Add("m_bEaseInAndOut", source.GetBool("ease in and out", true));
                fade.Add("m_flFadeBias", source.GetFloat("fade bias", 0.5f));
                return fade;
            }

            case "Alpha Fade In Simple":
            {
                var fade = Function("C_OP_FadeInSimple");
                fade.Add("m_flFadeInTime", source.GetFloat("proportional fade in time", 0.25f));
                return fade;
            }

            case "Alpha Fade Out Simple":
            {
                var fade = Function("C_OP_FadeOutSimple");
                fade.Add("m_flFadeOutTime", source.GetFloat("proportional fade out time", 0.25f));
                return fade;
            }

            case "Color Fade":
            {
                var fade = Function("C_OP_ColorInterpolate");
                fade.Add("m_ColorFade", Color(source.GetColor("color_fade", new DmxColor(255, 255, 255, 255))));
                fade.Add("m_flFadeStartTime", source.GetFloat("fade_start_time"));
                fade.Add("m_flFadeEndTime", source.GetFloat("fade_end_time", 1f));
                fade.Add("m_bEaseInOut", source.GetBool("ease_in_and_out", true));
                fade.Add("m_nFieldOutput", ColorField);
                return fade;
            }

            case "Radius Scale":
            {
                var scale = Function("C_OP_InterpolateRadius");
                scale.Add("m_flStartTime", source.GetFloat("start_time"));
                scale.Add("m_flEndTime", source.GetFloat("end_time", 1f));
                scale.Add("m_flStartScale", source.GetFloat("radius_start_scale", 1f));
                scale.Add("m_flEndScale", source.GetFloat("radius_end_scale", 1f));
                scale.Add("m_bEaseInAndOut", source.GetBool("ease_in_and_out"));
                scale.Add("m_flBias", source.GetFloat("scale_bias", 0.5f));
                return scale;
            }

            case "Rotation Basic":
                return Function("C_OP_SpinUpdate");

            case "Rotation Spin Roll":
            {
                var spin = Function("C_OP_Spin");
                spin.Add("m_nSpinRateDegrees", source.GetInt("spin_rate_degrees"));
                spin.Add("m_nSpinRateMinDegrees", source.GetInt("spin_rate_min"));
                spin.Add("m_fSpinRateStopTime", source.GetFloat("spin_stop_time"));
                return spin;
            }

            case "Rotation Spin Yaw":
            {
                var spin = Function("C_OP_SpinYaw");
                spin.Add("m_nSpinRateDegrees", source.GetInt("yaw_rate_degrees"));
                spin.Add("m_nSpinRateMinDegrees", source.GetInt("yaw_rate_min"));
                spin.Add("m_fSpinRateStopTime", source.GetFloat("yaw_stop_time"));
                return spin;
            }

            case "Movement Lock to Control Point":
            {
                var lockToPoint = Function("C_OP_PositionLock");
                lockToPoint.Add("m_TransformInput", new KVObject(source.GetInt("control_point_number")));
                lockToPoint.Add("m_flStartTime_min", source.GetFloat("start_fadeout_min"));
                lockToPoint.Add("m_flStartTime_max", source.GetFloat("start_fadeout_max"));
                lockToPoint.Add("m_flStartTime_exp", source.GetFloat("start_fadeout_exponent", 1f));
                lockToPoint.Add("m_flEndTime_min", source.GetFloat("end_fadeout_min", 1f));
                lockToPoint.Add("m_flEndTime_max", source.GetFloat("end_fadeout_max", 1f));
                lockToPoint.Add("m_flEndTime_exp", source.GetFloat("end_fadeout_exponent", 1f));
                lockToPoint.Add("m_flRange", source.GetFloat("distance fade range"));
                lockToPoint.Add("m_bLockRot", source.GetBool("lock rotation"));
                return lockToPoint;
            }

            case "Set child control points from particle positions":
            {
                var setPoints = Function("C_OP_SetChildControlPoints");
                setPoints.Add("m_nChildGroupID", source.GetInt("Group ID to affect"));
                setPoints.Add("m_nFirstControlPoint", source.GetInt("First control point to set", 1));
                setPoints.Add("m_nNumControlPoints", source.GetInt("# of control points to set", 1));
                setPoints.Add("m_nFirstSourcePoint", source.GetInt("first particle to copy"));
                setPoints.Add("m_bSetOrientation", source.GetBool("Set cp orientation for particles"));
                return setPoints;
            }

            case "Oscillate Scalar":
            {
                var oscillate = Function("C_OP_OscillateScalar");
                oscillate.Add("m_nField", source.GetInt("oscillation field"));
                oscillate.Add("m_RateMin", source.GetFloat("oscillation rate min"));
                oscillate.Add("m_RateMax", source.GetFloat("oscillation rate max"));
                oscillate.Add("m_FrequencyMin", source.GetFloat("oscillation frequency min", 1f));
                oscillate.Add("m_FrequencyMax", source.GetFloat("oscillation frequency max", 1f));
                AddOscillationTiming(oscillate, source);
                return oscillate;
            }

            case "Oscillate Vector":
            {
                var oscillate = Function("C_OP_OscillateVector");
                oscillate.Add("m_nField", source.GetInt("oscillation field"));
                oscillate.Add("m_RateMin", KVHelpers.ToKVArray(source.GetVector3("oscillation rate min")));
                oscillate.Add("m_RateMax", KVHelpers.ToKVArray(source.GetVector3("oscillation rate max")));
                oscillate.Add("m_FrequencyMin", KVHelpers.ToKVArray(source.GetVector3("oscillation frequency min", Vector3.One)));
                oscillate.Add("m_FrequencyMax", KVHelpers.ToKVArray(source.GetVector3("oscillation frequency max", Vector3.One)));
                AddOscillationTiming(oscillate, source);
                return oscillate;
            }

            case "Movement Max Velocity":
            {
                var limit = Function("C_OP_MaxVelocity");
                limit.Add("m_flMaxVelocity", source.GetFloat("Maximum Velocity"));
                return limit;
            }

            case "Movement Rotate Particle Around Axis":
            {
                var rotate = Function("C_OP_MovementRotateParticleAroundAxis");
                rotate.Add("m_vecRotAxis", KVHelpers.ToKVArray(source.GetVector3("Rotation Axis", Vector3.UnitZ)));
                rotate.Add("m_flRotRate", source.GetFloat("Rotation Rate"));
                rotate.Add("m_TransformInput", new KVObject(source.GetInt("Control Point")));
                rotate.Add("m_bLocalSpace", source.GetBool("Use Local Space"));
                return rotate;
            }

            case "Remap Scalar":
            {
                var remap = Function("C_OP_RemapScalar");
                remap.Add("m_nFieldInput", source.GetInt("input field", 8));
                remap.Add("m_nFieldOutput", source.GetInt("output field", RadiusField));
                remap.Add("m_flInputMin", source.GetFloat("input minimum"));
                remap.Add("m_flInputMax", source.GetFloat("input maximum", 1f));
                remap.Add("m_flOutputMin", source.GetFloat("output minimum"));
                remap.Add("m_flOutputMax", source.GetFloat("output maximum", 1f));
                return remap;
            }

            case "Remap Distance to Control Point to Scalar":
            {
                var remap = Function("C_OP_DistanceToTransform");
                remap.Add("m_TransformStart", new KVObject(source.GetInt("control point")));
                remap.Add("m_nFieldOutput", source.GetInt("output field", RadiusField));
                remap.Add("m_flInputMin", source.GetFloat("distance minimum"));
                remap.Add("m_flInputMax", source.GetFloat("distance maximum", 128f));
                remap.Add("m_flOutputMin", source.GetFloat("output minimum"));
                remap.Add("m_flOutputMax", source.GetFloat("output maximum", 1f));
                remap.Add("m_bLOS", source.GetBool("ensure line of sight"));
                remap.Add("m_flMaxTraceLength", source.GetFloat("Maximum Trace Length", -1f));
                remap.Add("m_flLOSScale", source.GetFloat("LOS Failure Scalar"));
                remap.Add("m_bActiveRange", source.GetBool("only active within specified distance"));
                remap.Add("m_nSetMethod", GetSetMethod(source));
                return remap;
            }

            case "Movement Dampen Relative to Control Point":
            {
                var dampen = Function("C_OP_DampenToCP");
                dampen.Add("m_nControlPointNumber", source.GetInt("control_point_number"));
                dampen.Add("m_flRange", source.GetFloat("falloff range", 100f));
                dampen.Add("m_flScale", source.GetFloat("dampen scale", 1f));
                return dampen;
            }

            case "Rotation Orient Relative to CP":
            {
                var orient = Function("C_OP_Orient2DRelToCP");
                orient.Add("m_flRotOffset", source.GetFloat("Rotation Offset"));
                orient.Add("m_flSpinStrength", source.GetFloat("Spin Strength", 1f));
                orient.Add("m_nCP", source.GetInt("Control Point"));
                orient.Add("m_nFieldOutput", source.GetInt("rotation field", RollField));
                return orient;
            }

            case "Cull when crossing plane":
            {
                var cull = Function("C_OP_PlaneCull");
                cull.Add("m_nPlaneControlPoint", source.GetInt("Control Point for point on plane"));
                cull.Add("m_flPlaneOffset", source.GetFloat("Cull plane offset"));
                cull.Add("m_vecPlaneDirection", KVHelpers.ToKVArray(source.GetVector3("Plane Normal", Vector3.UnitZ)));
                return cull;
            }

            case "Set Control Point Positions":
            {
                var setPositions = Function("C_OP_SetControlPointPositions");
                setPositions.Add("m_nCP1", source.GetInt("First Control Point Number", 1));
                setPositions.Add("m_vecCP1Pos", KVHelpers.ToKVArray(source.GetVector3("First Control Point Location", new Vector3(128f, 0f, 0f))));
                setPositions.Add("m_nCP2", source.GetInt("Second Control Point Number", 2));
                setPositions.Add("m_vecCP2Pos", KVHelpers.ToKVArray(source.GetVector3("Second Control Point Location", new Vector3(0f, 128f, 0f))));
                setPositions.Add("m_nCP3", source.GetInt("Third Control Point Number", 3));
                setPositions.Add("m_vecCP3Pos", KVHelpers.ToKVArray(source.GetVector3("Third Control Point Location", new Vector3(-128f, 0f, 0f))));
                setPositions.Add("m_nCP4", source.GetInt("Fourth Control Point Number", 4));
                setPositions.Add("m_vecCP4Pos", KVHelpers.ToKVArray(source.GetVector3("Fourth Control Point Location", new Vector3(0f, -128f, 0f))));
                setPositions.Add("m_bUseWorldLocation", source.GetBool("Set positions in world space"));
                setPositions.Add("m_nHeadLocation", source.GetInt("Control Point to offset positions from"));
                return setPositions;
            }

            case "Noise Scalar":
            {
                var noise = Function("C_OP_Noise");
                noise.Add("m_nFieldOutput", source.GetInt("output field", AlphaField));
                noise.Add("m_flOutputMin", source.GetFloat("output minimum"));
                noise.Add("m_flOutputMax", source.GetFloat("output maximum", 1f));
                noise.Add("m_fl4NoiseScale", source.GetFloat("noise coordinate scale", 0.1f));
                return noise;
            }

            default:
                return null;
        }
    }

    private static KVObject FadeAndKill(string className, DmxElement source)
    {
        var fade = Function(className);
        fade.Add("m_flStartAlpha", source.GetFloat("start_alpha", 1f));
        fade.Add("m_flEndAlpha", source.GetFloat("end_alpha"));
        fade.Add("m_flStartFadeInTime", source.GetFloat("start_fade_in_time"));
        fade.Add("m_flEndFadeInTime", source.GetFloat("end_fade_in_time", 0.5f));
        fade.Add("m_flStartFadeOutTime", source.GetFloat("start_fade_out_time", 0.5f));
        fade.Add("m_flEndFadeOutTime", source.GetFloat("end_fade_out_time", 1f));

        return fade;
    }

    private static void AddOscillationTiming(KVObject oscillate, DmxElement source)
    {
        oscillate.Add("m_flStartTime_min", source.GetFloat("start time min"));
        oscillate.Add("m_flStartTime_max", source.GetFloat("start time max"));
        oscillate.Add("m_flEndTime_min", source.GetFloat("end time min", 1f));
        oscillate.Add("m_flEndTime_max", source.GetFloat("end time max", 1f));
        oscillate.Add("m_flOscMult", source.GetFloat("oscillation multiplier", 2f));
        oscillate.Add("m_flOscAdd", source.GetFloat("oscillation start phase", 0.5f));
        oscillate.Add("m_bProportional", source.GetBool("proportional 0/1", true));
        oscillate.Add("m_bProportionalOp", source.GetBool("start/end proportional", true));
    }

    private KVObject? ConvertForce(string name, DmxElement source)
    {
        switch (name)
        {
            case "random force":
            {
                var force = Function("C_OP_RandomForce");
                force.Add("m_MinForce", KVHelpers.ToKVArray(source.GetVector3("min force")));
                force.Add("m_MaxForce", KVHelpers.ToKVArray(source.GetVector3("max force")));
                return force;
            }

            case "twist around axis":
            {
                var force = Function("C_OP_TwistAroundAxis");
                force.Add("m_fForceAmount", source.GetFloat("amount of force"));
                force.Add("m_TwistAxis", KVHelpers.ToKVArray(source.GetVector3("twist axis", Vector3.UnitZ)));
                force.Add("m_bLocalSpace", source.GetBool("object local space axis 0/1"));
                return force;
            }

            case "Pull towards control point":
            {
                var force = Function("C_OP_AttractToControlPoint");
                force.Add("m_fForceAmount", source.GetFloat("amount of force"));
                force.Add("m_fFalloffPower", source.GetFloat("falloff power", 2f));
                force.Add("m_TransformInput", new KVObject(source.GetInt("control point number")));
                force.Add("m_vecComponentScale", KVHelpers.ToKVArray(source.GetVector3("component scale", Vector3.One)));
                return force;
            }

            default:
                return null;
        }
    }

    private static KVObject? ConvertConstraint(string name, DmxElement source)
    {
        switch (name)
        {
            case "Constrain distance to control point":
            {
                var constraint = Function("C_OP_ConstrainDistance");
                constraint.Add("m_fMinDistance", source.GetFloat("minimum distance"));
                constraint.Add("m_fMaxDistance", source.GetFloat("maximum distance", 100f));
                constraint.Add("m_nControlPointNumber", new KVObject(source.GetInt("control point number")));
                constraint.Add("m_CenterOffset", KVHelpers.ToKVArray(source.GetVector3("offset of center")));
                constraint.Add("m_bGlobalCenter", source.GetBool("global center point"));
                return constraint;
            }

            case "Constrain distance to path between two control points":
            {
                var constraint = Function("C_OP_ConstrainDistanceToPath");
                constraint.Add("m_fMinDistance", source.GetFloat("minimum distance"));
                constraint.Add("m_flMaxDistance0", source.GetFloat("maximum distance", 100f));
                constraint.Add("m_flMaxDistanceMid", source.GetFloat("maximum distance middle", -1f));
                constraint.Add("m_flMaxDistance1", source.GetFloat("maximum distance end", -1f));
                constraint.Add("m_flTravelTime", source.GetFloat("travel time", 10f));
                constraint.Add("m_PathParameters", PathParameters(source, bulgeKey: "random bulge"));
                return constraint;
            }

            default:
                return null;
        }
    }

    private KVObject? ConvertRenderer(string name, DmxElement source)
    {
        switch (name)
        {
            case "render_animated_sprites":
            {
                var renderer = Function("C_OP_RenderSprites");
                AddSpriteSettings(renderer, source);
                renderer.Add("m_nOrientationType", source.GetInt("orientation_type"));
                renderer.Add("m_nOrientationControlPoint", source.GetInt("orientation control point", -1));
                renderer.Add("m_flAnimationRate", source.GetFloat("animation rate", 0.1f));
                renderer.Add("m_nAnimationType", source.GetBool("animation_fit_lifetime") ? "ANIMATION_TYPE_FIT_LIFETIME" : "ANIMATION_TYPE_FIXED_RATE");
                renderer.Add("m_bAnimateInFPS", source.GetBool("use animation rate as FPS"));
                return renderer;
            }

            case "render_sprite_trail":
            {
                var renderer = Function("C_OP_RenderTrails");
                AddSpriteSettings(renderer, source);
                renderer.Add("m_flAnimationRate", source.GetFloat("animation rate", 0.1f));
                renderer.Add("m_flLengthFadeInTime", source.GetFloat("length fade in time"));
                renderer.Add("m_flMaxLength", source.GetFloat("max length", 2000f));
                renderer.Add("m_flMinLength", source.GetFloat("min length"));
                return renderer;
            }

            case "render_rope":
            {
                var renderer = Function("C_OP_RenderRopes");
                AddSpriteSettings(renderer, source);
                renderer.Add("m_nMinTesselation", source.GetInt("subdivision_count", 3));
                renderer.Add("m_nMaxTesselation", source.GetInt("subdivision_count", 3));
                renderer.Add("m_flTextureVWorldSize", source.GetFloat("texel_size", 4f));
                renderer.Add("m_flTextureVScrollRate", source.GetFloat("texture_scroll_rate"));
                return renderer;
            }

            default:
                return null;
        }
    }

    // The material of the system, which the renderers of Source 1 draw with, and the visibility settings they share
    private void AddSpriteSettings(KVObject renderer, DmxElement source)
    {
        var materialName = system.Element.GetString("material");
        var material = string.IsNullOrEmpty(materialName) ? null : resolveMaterial?.Invoke(materialName);

        var layers = KVObject.Array();

        if (material?.Texture is { } texture)
        {
            var layer = KVObject.Collection();
            layer.Add("m_hTexture", texture);
            layers.Add(layer);
        }

        renderer.Add("m_vecTexturesInput", layers);

        if (material != null)
        {
            var blendMode = "PARTICLE_OUTPUT_BLEND_MODE_ALPHA";

            if (material.BlendOverAlpha)
            {
                blendMode = "PARTICLE_OUTPUT_BLEND_MODE_BLEND_ADD";
            }
            else if (material.Additive)
            {
                blendMode = "PARTICLE_OUTPUT_BLEND_MODE_ADD";
            }

            renderer.Add("m_nOutputBlendMode", blendMode);
            renderer.Add("m_flOverbrightFactor", material.OverbrightFactor);
            renderer.Add("m_flAddSelfAmount", material.AddSelfAmount);
            renderer.Add("m_bBlendFramesSeq0", material.BlendFrames);
        }

        var visibility = KVObject.Collection();
        visibility.Add("m_flCameraBias", source.GetFloat("Visibility Camera Depth Bias"));
        visibility.Add("m_nCPin", source.GetInt("Visibility Proxy Input Control Point Number", -1));
        visibility.Add("m_flProxyRadius", source.GetFloat("Visibility Proxy Radius", 1f));
        visibility.Add("m_flInputMin", source.GetFloat("Visibility input minimum"));
        visibility.Add("m_flInputMax", source.GetFloat("Visibility input maximum", 1f));
        visibility.Add("m_flAlphaScaleMin", source.GetFloat("Visibility Alpha Scale minimum"));
        visibility.Add("m_flAlphaScaleMax", source.GetFloat("Visibility Alpha Scale maximum", 1f));
        visibility.Add("m_flRadiusScaleMin", source.GetFloat("Visibility Radius Scale minimum", 1f));
        visibility.Add("m_flRadiusScaleMax", source.GetFloat("Visibility Radius Scale maximum", 1f));
        renderer.Add("VisibilityInputs", visibility);
    }

    private static KVObject RandomInit(DmxElement source, string minKey, string maxKey, string exponentKey, float minDefault, float maxDefault, int field)
    {
        var min = source.GetFloat(minKey, minDefault);
        var max = source.GetFloat(maxKey, maxDefault);

        return InitFloat(field, min, max, source.Attributes.ContainsKey(exponentKey), source.GetFloat(exponentKey, 1f), signFlip: false);
    }

    // The angle is the base plus the offset, so the offsets are summed with it
    private static KVObject RotationInit(DmxElement source, int field, string baseKey, string minKey, string maxKey, string exponentKey, float maxDefault)
    {
        var degrees = source.GetFloat(baseKey);
        var min = degrees + source.GetFloat(minKey);
        var max = degrees + source.GetFloat(maxKey, maxDefault);
        var flip = source.GetBool("randomly_flip_direction");

        return InitFloat(field, min, max, source.Attributes.ContainsKey(exponentKey), source.GetFloat(exponentKey, 1f), flip);
    }

    private static KVObject InitFloat(int field, float min, float max, bool hasExponent, float exponent, bool signFlip)
    {
        var initializer = Function("C_INIT_InitFloat");
        initializer.Add("m_nOutputField", field);

        KVObject input;

        if (min == max && !hasExponent)
        {
            input = KVObject.Collection();
            input.Add("m_nType", "PF_TYPE_LITERAL");
            input.Add("m_flLiteralValue", min);
        }
        else
        {
            input = RandomFloat(min, max, hasExponent);

            if (hasExponent)
            {
                input.Add("m_nBiasType", "PF_BIAS_TYPE_EXPONENTIAL");
                input.Add("m_flBiasParameter", BiasParameter(exponent));
            }
        }

        if (signFlip)
        {
            input.Add("m_bHasRandomSignFlip", true);
        }

        initializer.Add("m_InputValue", input);

        return initializer;
    }

    // Exponents from one up map to a bias of zero down to minus one over nineteen units, smaller ones map to one down to zero
    private static float BiasParameter(float exponent)
        => exponent >= 1f
            ? 0f - MathUtils.Saturate((exponent - 1f) / 19f)
            : 1f - MathUtils.Saturate(exponent);

    private static KVObject RandomFloat(float min, float max, bool biased = false)
    {
        var input = KVObject.Collection();
        input.Add("m_nType", biased ? "PF_TYPE_RANDOM_BIASED" : "PF_TYPE_RANDOM_UNIFORM");
        input.Add("m_flRandomMin", min);
        input.Add("m_flRandomMax", max);
        input.Add("m_nRandomMode", "PF_RANDOM_MODE_CONSTANT");

        return input;
    }

    private static KVObject Color(DmxColor color)
    {
        var array = KVObject.Array();
        array.Add(new KVObject((int)color.R));
        array.Add(new KVObject((int)color.G));
        array.Add(new KVObject((int)color.B));
        array.Add(new KVObject((int)color.A));

        return array;
    }
}
