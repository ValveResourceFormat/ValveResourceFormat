using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>Bits of m_nDynamicNodeFlags that carry a ClothParams boolean.</summary>
    private const uint ClothFlagUninertialRods = 0x10;

    private const uint ClothFlagFollowTheLead = 0x20;

    private const uint ClothFlagImmovable = 0x4000;

    private const uint ClothFlagCollideWorldCapsulesAndSpheres = 0x30000;

    private const uint ClothFlagCollideWorldHulls = 0x40000;

    private const uint ClothFlagCollideWorldMeshes = 0x80000;

    /// <summary>Bits of m_nDynamicNodeFlags that carry a Softbody node boolean rather than a ClothParams one.</summary>
    private const uint ClothFlagPerBoneScaleEnabled = 0x8000;

    private const uint ClothFlagKeychainMotion = 0x1000000;

    /// <summary>Adds the Softbody node's own attributes; each flag key is written only when its bit is set.</summary>
    private void AddSoftbodyAttributes(KVObject softbody, FeModel feModel)
    {
        softbody.Add("motion_smooth_cdt", feModel.MotionSmoothCdt);

        if ((feModel.DynamicNodeFlags & ClothFlagPerBoneScaleEnabled) != 0)
        {
            softbody.Add("cloth_per_bone_scale_enabled", true);
        }

        if ((feModel.DynamicNodeFlags & ClothFlagKeychainMotion) != 0)
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
    private static KVObject MakeClothParams(FeModel feModel, bool generatesBendRods = false, bool generatesBendOnlyRods = false,
        float addCurvature = 0f, bool explicitMasses = false)
    {
        var flags = feModel.DynamicNodeFlags;
        bool Flag(uint bits) => (flags & bits) != 0;

        return MakeNode("ClothParams",
            ("default_stretch", feModel.DefaultSurfaceStretch),
            ("additional_shear_stretch", feModel.AdditionalShearStretch),
            ("extra_iterations", feModel.ExtraIterations),
            ("extra_goal_iterations", feModel.ExtraGoalIterations),
            ("extra_pressure_iterations", feModel.ExtraPressureIterations),
            ("goal_strength_bias", feModel.GoalStrengthBias),
            ("default_gravity_scale", feModel.DefaultGravityScale),
            ("default_vel_air_drag", feModel.DefaultVelAirDrag),
            ("default_exp_air_drag", feModel.DefaultExpAirDrag),
            ("velocity_smooth_rate", feModel.VelocitySmoothRate),
            ("internal_pressure", feModel.InternalPressure),
            ("windage", feModel.Windage),
            ("wind_drag", feModel.WindDrag),
            ("velocity_smooth_iterations", feModel.VelocitySmoothIterations),
            ("default_ground_friction", feModel.DefaultGroundFriction),
            ("default_world_collision_penetration", 0.0f),
            ("add_world_collision_radius", feModel.AddWorldCollisionRadius),
            ("local_force", feModel.LocalForce),
            ("local_rotation", feModel.LocalRotation),
            ("add_curvature", feModel.HasAxialEdges || feModel.HasChainRingBends
                ? (feModel.RigidHingeBendPaint is null ? feModel.RigidHingeCurvature : 0f)
                : addCurvature),
            ("quad_bend_tolerance", feModel.QuadBendTolerance),
            ("local_drag1", feModel.LocalDrag1),
            ("follow_the_lead", Flag(ClothFlagFollowTheLead)),
            ("use_per_node_local_force_and_rotation", feModel.HasPerNodeLocalForce),
            ("uninertial_rods", Flag(ClothFlagUninertialRods)),
            ("explicit_masses", explicitMasses),
            ("unitless_damping", true),
            ("force_world_collision_on_all_nodes", feModel.ForcesWorldCollisionOnAllNodes),
            ("new_style", true),
            ("can_collide_with_world_hulls", Flag(ClothFlagCollideWorldHulls)),
            ("can_collide_with_world_meshes", Flag(ClothFlagCollideWorldMeshes)),
            ("can_collide_with_world_capsule_and_spheres", Flag(ClothFlagCollideWorldCapsulesAndSpheres)),
            ("add_stiffness_rods", generatesBendRods),
            ("rigid_edge_hinges", feModel.HasAxialEdges || feModel.HasChainRingBends),
            ("add_bend_only_rods", generatesBendOnlyRods),
            ("immovable", Flag(ClothFlagImmovable)));
    }
}
