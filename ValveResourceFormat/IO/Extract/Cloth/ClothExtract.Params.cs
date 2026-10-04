using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // m_nDynamicNodeFlags bits that carry a ClothParams boolean
    private const uint ClothFlagUninertialRods = 0x10;
    private const uint ClothFlagFollowTheLead = 0x20;
    private const uint ClothFlagImmovable = 0x4000;
    private const uint ClothFlagCollideWorldCapsulesAndSpheres = 0x30000;
    private const uint ClothFlagCollideWorldHulls = 0x40000;
    private const uint ClothFlagCollideWorldMeshes = 0x80000;

    // m_nDynamicNodeFlags bits that carry a Softbody node boolean
    private const uint ClothFlagPerBoneScaleEnabled = 0x8000;
    private const uint ClothFlagKeychainMotion = 0x1000000;

    /// <summary>Adds the Softbody node's own attributes; each flag key is written only when its bit is set.</summary>
    private void AddSoftbodyAttributes(KVObject softbody, ClothReconstruction cloth)
    {
        softbody.Add("motion_smooth_cdt", cloth.Fe.MotionSmoothCdt);

        if ((cloth.Fe.DynamicNodeFlags & ClothFlagPerBoneScaleEnabled) != 0)
        {
            softbody.Add("cloth_per_bone_scale_enabled", true);
        }

        if ((cloth.Fe.DynamicNodeFlags & ClothFlagKeychainMotion) != 0)
        {
            softbody.Add("cloth_keychain_motion", true);
        }

        AddSoftbodyModelKeyValues(softbody, model?.KeyValues);
    }

    /// <summary>Restores the Softbody keys the compiler moves into the model's key values.</summary>
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

    private static KVObject MakeClothParams(ClothReconstruction cloth, bool generatesBendRods = false, bool generatesBendOnlyRods = false,
        float addCurvature = 0f, bool forceExplicitMasses = false)
    {
        var flags = cloth.Fe.DynamicNodeFlags;
        bool Flag(uint bits) => (flags & bits) != 0;

        var rigidEdgeHinges = cloth.Index.HasAxialEdges || cloth.HasChainRingBends;

        return MakeNode("ClothParams",
            ("default_stretch", cloth.Fe.DefaultSurfaceStretch),
            ("additional_shear_stretch", cloth.AdditionalShearStretch),
            ("extra_iterations", cloth.Fe.ExtraIterations),
            ("extra_goal_iterations", cloth.Fe.ExtraGoalIterations),
            ("extra_pressure_iterations", cloth.Fe.ExtraPressureIterations),
            ("goal_strength_bias", cloth.GoalStrengthBias),
            ("default_gravity_scale", cloth.Index.DefaultGravityScale),
            ("default_vel_air_drag", cloth.Fe.DefaultVelAirDrag),
            ("default_exp_air_drag", cloth.Fe.DefaultExpAirDrag),
            ("velocity_smooth_rate", cloth.Fe.RodVelocitySmoothRate),
            ("internal_pressure", cloth.Fe.InternalPressure),
            ("windage", cloth.Fe.Windage),
            ("wind_drag", cloth.Fe.WindDrag),
            ("velocity_smooth_iterations", cloth.Fe.RodVelocitySmoothIterations),
            ("default_ground_friction", cloth.DefaultGroundFriction),
            ("default_world_collision_penetration", 0.0f),
            ("add_world_collision_radius", cloth.Fe.AddWorldCollisionRadius),
            ("local_force", cloth.Fe.LocalForce),
            ("local_rotation", cloth.Fe.LocalRotation),
            ("add_curvature", rigidEdgeHinges
                ? (cloth.RigidHingeBendPaint is null ? cloth.RigidHingeCurvature : 0f)
                : addCurvature),
            ("quad_bend_tolerance", cloth.QuadBendTolerance),
            ("local_drag1", cloth.Fe.LocalDrag1),
            ("follow_the_lead", Flag(ClothFlagFollowTheLead)),
            ("use_per_node_local_force_and_rotation", cloth.Index.HasPerNodeLocalForce),
            ("uninertial_rods", Flag(ClothFlagUninertialRods)),
            ("explicit_masses", forceExplicitMasses || cloth.HasExplicitMasses),
            ("unitless_damping", true),
            ("force_world_collision_on_all_nodes", cloth.ForcesWorldCollisionOnAllNodes),
            ("new_style", true),
            ("can_collide_with_world_hulls", Flag(ClothFlagCollideWorldHulls)),
            ("can_collide_with_world_meshes", Flag(ClothFlagCollideWorldMeshes)),
            ("can_collide_with_world_capsule_and_spheres", Flag(ClothFlagCollideWorldCapsulesAndSpheres)),
            ("add_stiffness_rods", generatesBendRods),
            ("rigid_edge_hinges", rigidEdgeHinges),
            ("add_bend_only_rods", generatesBendOnlyRods),
            ("immovable", Flag(ClothFlagImmovable)));
    }
}
