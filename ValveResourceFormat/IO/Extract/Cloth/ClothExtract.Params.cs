using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // Bits of m_nDynamicNodeFlags that carry a ClothParams boolean.
    private const uint ClothFlagUninertialRods = 0x10;

    private const uint ClothFlagFollowTheLead = 0x20;

    private const uint ClothFlagImmovable = 0x4000;

    private const uint ClothFlagCollideWorldCapsulesAndSpheres = 0x30000;

    private const uint ClothFlagCollideWorldHulls = 0x40000;

    private const uint ClothFlagCollideWorldMeshes = 0x80000;

    // Bits of m_nDynamicNodeFlags that carry a Softbody node boolean rather than a ClothParams one.
    private const uint ClothFlagPerBoneScaleEnabled = 0x8000;

    private const uint ClothFlagKeychainMotion = 0x1000000;

    /// <summary>Adds the Softbody node's own attributes; each flag key is written only when its bit is set.</summary>
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
    /// Restores the Softbody keys the compiler stores in the model's key values: <c>stiffness_on_ragdoll</c> and
    /// <c>cloth_sleep_enabled</c>.
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

    /// <summary>The <c>ClothParams</c> node, read off the FeModel's scalars and dynamic node flags.</summary>
    private static KVObject MakeClothParams(FeModel fe, bool generatesBendRods = false, bool generatesBendOnlyRods = false,
        float addCurvature = 0f, bool explicitMasses = false)
    {
        var flags = fe.DynamicNodeFlags;
        bool Flag(uint bits) => (flags & bits) != 0;

        return MakeNode("ClothParams",
            ("default_stretch", fe.DefaultSurfaceStretch),
            // Read off the rod relaxation factors; m_flDefaultThreadStretch only tracks the surface stretch.
            ("additional_shear_stretch", fe.AdditionalShearStretch),
            ("extra_iterations", fe.ExtraIterations),
            ("extra_goal_iterations", fe.ExtraGoalIterations),
            ("extra_pressure_iterations", fe.ExtraPressureIterations),
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
            ("add_stiffness_rods", generatesBendRods),
            ("rigid_edge_hinges", fe.HasAxialEdges || fe.HasChainRingBends),
            ("add_bend_only_rods", generatesBendOnlyRods),
            ("immovable", Flag(ClothFlagImmovable)));
    }
}
