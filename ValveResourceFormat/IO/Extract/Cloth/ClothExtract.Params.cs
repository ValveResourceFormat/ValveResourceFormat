using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // Bits of m_nDynamicNodeFlags that carry a ClothParams boolean. The remaining ClothParams switches
    // leave no bit behind and fall back to the modern Source 2 defaults.
    private const uint ClothFlagUninertialRods = 0x10;

    private const uint ClothFlagFollowTheLead = 0x20;

    private const uint ClothFlagImmovable = 0x4000;

    private const uint ClothFlagCollideWorldCapsulesAndSpheres = 0x30000;

    private const uint ClothFlagCollideWorldHulls = 0x40000;

    private const uint ClothFlagCollideWorldMeshes = 0x80000;

    // Bits of m_nDynamicNodeFlags that carry a Softbody node boolean rather than a ClothParams one.
    private const uint ClothFlagPerBoneScaleEnabled = 0x8000;

    private const uint ClothFlagKeychainMotion = 0x1000000;

    // The Softbody node's own attributes, as opposed to the ClothParams child below. The two
    // switches are omitted unless their bit is present.
    private void AddSoftbodyAttributes(KVObject softbody, FeModel fe)
    {
        softbody.Add("motion_smooth_cdt", fe.MotionSmoothCdt);

        if ((fe.DynamicNodeFlags & ClothFlagPerBoneScaleEnabled) != 0)
        {
            softbody.Add("cloth_per_bone_scale_enabled", true);
        }

        if ((fe.DynamicNodeFlags & ClothFlagKeychainMotion) != 0)
        {
            softbody.Add("cloth_keychain_motion", true);
        }

        AddSoftbodyModelKeyValues(softbody, model?.KeyValues);
    }

    /// <summary>
    /// Restores the two Softbody keys the compiler writes into the model's key values instead of the
    /// FeModel: <c>stiffness_on_ragdoll</c> as <c>cloth_stiffness_on_ragdoll</c> (only when above zero) and
    /// <c>cloth_sleep_enabled</c> (only when set).
    /// </summary>
    internal static void AddSoftbodyModelKeyValues(KVObject softbody, KVObject? keyValues)
    {
        if (keyValues is null)
        {
            return;
        }

        if (keyValues.ContainsKey("cloth_stiffness_on_ragdoll"))
        {
            softbody.Add("stiffness_on_ragdoll", keyValues.GetFloatProperty("cloth_stiffness_on_ragdoll"));
        }

        if (keyValues.ContainsKey("cloth_sleep_enabled"))
        {
            softbody.Add("cloth_sleep_enabled", keyValues.GetBooleanProperty("cloth_sleep_enabled"));
        }
    }

    // Global cloth solver parameters, populated from the FeModel scalars. Field names match the compiled
    // ClothParams source node; the compiler re-derives everything not emitted here.
    private static KVObject MakeClothParams(FeModel fe, bool generatesBendRods = false, bool generatesBendOnlyRods = false,
        float addCurvature = 0f, bool explicitMasses = false)
    {
        var flags = fe.DynamicNodeFlags;
        bool Flag(uint bits) => (flags & bits) != 0;

        return MakeNode("ClothParams",
            ("default_stretch", fe.DefaultSurfaceStretch),
            // Recovered from the rod relaxation factors, NOT from m_flDefaultThreadStretch, which tracks
            // m_flDefaultSurfaceStretch whatever the shear is.
            ("additional_shear_stretch", fe.AdditionalShearStretch),
            ("extra_iterations", fe.ExtraIterations),
            ("extra_goal_iterations", fe.ExtraGoalIterations),
            ("extra_pressure_iterations", fe.ExtraPressureIterations),
            // The compiler adds this to every node's goal strength before cubing it into the force
            // attraction while the vertex attraction keeps the unbiased cube, so a model that ships the
            // two a constant cube root apart was authored with it (see FeModel.GoalStrengthBias).
            ("goal_strength_bias", fe.GoalStrengthBias),
            ("default_gravity_scale", fe.DefaultGravityScale),
            ("default_vel_air_drag", fe.DefaultVelAirDrag),
            ("default_exp_air_drag", fe.DefaultExpAirDrag),
            ("velocity_smooth_rate", fe.VelocitySmoothRate),
            ("internal_pressure", fe.InternalPressure),
            ("windage", fe.Windage),
            ("wind_drag", fe.WindDrag),
            ("velocity_smooth_iterations", fe.VelocitySmoothIterations),
            ("default_ground_friction", fe.DefaultGroundFriction),
            ("default_world_collision_penetration", 0.0f),
            ("add_world_collision_radius", fe.AddWorldCollisionRadius),
            ("local_force", fe.LocalForce),
            ("local_rotation", fe.LocalRotation),
            // A model with rigid edge hinges states its curvature only through the ring bends that switch
            // turns on: its rods are all built rigid, so the readings taken off them saturate whatever it
            // was authored with (see FeModel.RigidHingeCurvature). Where its hubs fold apart, the per-hub
            // paint carries every fold and the model-wide value stays zero (see FeModel.RigidHingeBendPaint).
            ("add_curvature", fe.HasAxialEdges || fe.HasChainRingBends
                ? (fe.RigidHingeBendPaint is null ? fe.RigidHingeCurvature : 0f)
                : addCurvature),
            ("quad_bend_tolerance", fe.QuadBendTolerance),
            ("local_drag1", fe.LocalDrag1),
            ("follow_the_lead", Flag(ClothFlagFollowTheLead)),
            ("use_per_node_local_force_and_rotation", fe.HasPerNodeLocalForce),
            ("uninertial_rods", Flag(ClothFlagUninertialRods)),
            ("explicit_masses", explicitMasses),
            ("unitless_damping", true),
            ("force_world_collision_on_all_nodes", fe.ForcesWorldCollisionOnAllNodes),
            ("new_style", true),
            ("can_collide_with_world_hulls", Flag(ClothFlagCollideWorldHulls)),
            ("can_collide_with_world_meshes", Flag(ClothFlagCollideWorldMeshes)),
            ("can_collide_with_world_capsule_and_spheres", Flag(ClothFlagCollideWorldCapsulesAndSpheres)),
            // A sheet whose compiled rods reach beyond its own face edges and diagonals was authored with
            // the extra bend network switched on. Recovering it lets the compiler regenerate those rods
            // from the surface, where declaring them as explicit springs would instead add a source
            // element per pair and leave the sheet heavier than the original.
            ("add_stiffness_rods", generatesBendRods),
            ("rigid_edge_hinges", fe.HasAxialEdges || fe.HasChainRingBends),
            ("add_bend_only_rods", generatesBendOnlyRods),
            ("immovable", Flag(ClothFlagImmovable)));
    }

    private const float ClothSourceBaseGravity = FeModel.ClothSourceBaseGravity;

    private const float ClothDragPointDampingScale = FeModel.ClothDragPointDampingScale;
}
