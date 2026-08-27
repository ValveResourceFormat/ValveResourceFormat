using System;
using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Tests
{
    public class ModelExtractIKTest
    {
        private static string ExtractValveModel(string fileName)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", fileName));
            return new ModelExtract(resource, new NullFileLoader()).ToValveModel();
        }

        [Test]
        public async Task EmitsIKChainsWithNestedJoints()
        {
            var vmdl = ExtractValveModel("box_creature_ik_model.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).Contains("_class = \"IKData\"");
                await Assert.That(vmdl).Contains("_class = \"IKChain\"");
                await Assert.That(vmdl).Contains("name = \"IK_chain_new_foot_r\"");
                await Assert.That(vmdl).Contains("name = \"IK_chain_new_foot_l\"");

                await Assert.That(vmdl).Contains("_class = \"IKChainJoint\"");
                await Assert.That(vmdl).Contains("bone = \"upper_leg_r\"");
                await Assert.That(vmdl).Contains("bone = \"foot_r\"");

                await Assert.That(vmdl).Contains("m_DefaultSolverSettings.m_SolverType \" = \"IKSOLVER_Fabrik\"");
                await Assert.That(vmdl).Contains("m_Name = \"foot_r_ik_target\"");
            }
        }

        /// <summary>
        /// Each joint owns the joints below it, so the second joint is nested inside the first and
        /// therefore closes before its parent writes its own bone key.
        /// </summary>
        [Test]
        public async Task NestsEachJointInsideItsParent()
        {
            var vmdl = ExtractValveModel("box_creature_ik_model.vmdl_c");

            var parent = vmdl.IndexOf("bone = \"upper_leg_r\"", StringComparison.Ordinal);
            var child = vmdl.IndexOf("bone = \"foot_r\"", StringComparison.Ordinal);

            using (Assert.Multiple())
            {
                await Assert.That(child).IsGreaterThan(0);
                await Assert.That(parent).IsGreaterThan(child);
            }
        }

        /// <summary>
        /// ModelDoc registers some of these keys with a trailing space and others behind an m_Data
        /// prefix, and ignores every other spelling, so they have to survive serialization verbatim.
        /// </summary>
        [Test]
        public async Task WritesTheExactKeySpellingsModelDocReadsBack()
        {
            var vmdl = ExtractValveModel("box_creature_ik_model.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).Contains("\"m_PoleVectorForAxis \" = ");
                await Assert.That(vmdl).Contains("\"m_DefaultSolverSettings.m_SolverType \" = ");
                await Assert.That(vmdl).Contains("m_Data.m_bParentJointRequiresAlignment = ");
                await Assert.That(vmdl).Contains("m_Data.m_EndEffectorFixedOffsetAttachment = ");
                await Assert.That(vmdl).Contains("m_Data.m_DefaultTargetSettings.m_AnimgraphParameterNamePosition = ");
            }
        }

        [Test]
        public async Task EmitsHingeConstraintsAndTargetSettings()
        {
            var vmdl = ExtractValveModel("alyx_hand_left.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).Contains("_class = \"IKJointConstraint_Hinge\"");
                await Assert.That(vmdl).Contains("hinge_axis = \"Up\"");
                await Assert.That(vmdl).Contains("max_radians = 1.745329");
                await Assert.That(vmdl).Contains("constrained_joint = \"\"");

                await Assert.That(vmdl).Contains("m_DefaultTargetSettings.m_TargetSource = \"Animgraph Parameter\"");
                await Assert.That(vmdl).Contains("m_Name = \"fingertip_thumb\"");
                await Assert.That(vmdl).Contains("m_DefaultSolverSettings.m_SolverType \" = \"IKSOLVER_CCD\"");
            }
        }

        /// <summary>
        /// These three are filled in by the compiler, so ModelDoc exposes no attribute for them.
        /// </summary>
        [Test]
        public async Task OmitsCompilerDerivedFields()
        {
            var vmdl = ExtractValveModel("alyx_hand_left.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).DoesNotContain("m_qFixupRotation");
                await Assert.That(vmdl).DoesNotContain("m_ParentJoint");
                await Assert.That(vmdl).DoesNotContain("m_TargetCoordSystem");
            }
        }

        [Test]
        public async Task OmitsIKDataWhenTheModelHasNoChains()
        {
            var vmdl = ExtractValveModel("lod_test.vmdl_c");

            await Assert.That(vmdl).DoesNotContain("_class = \"IKData\"");
        }

        /// <summary>
        /// Models authored against the legacy rig carry their chains in the control rig and leave
        /// the m_IKChains entries without joints. ModelDoc rejects a chain in that state, so the
        /// jointless entry is dropped and the rig it belongs to is written out instead.
        /// </summary>
        [Test]
        public async Task SkipsChainsThatKeptNoJointsAndEmitsTheRigInstead()
        {
            var vmdl = ExtractValveModel("box_creature_model.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).DoesNotContain("_class = \"IKChain\"");
                await Assert.That(vmdl).DoesNotContain("_class = \"IKChainJoint\"");

                await Assert.That(vmdl).Contains("_class = \"IKData\"");
                await Assert.That(vmdl).Contains("_class = \"IKRigSimple\"");
                await Assert.That(vmdl).Contains("_class = \"IKChainOld\"");
                await Assert.That(vmdl).Contains("name = \"test\"");
                await Assert.That(vmdl).Contains("solver = \"IKSOLVER_Fabrik\"");
            }
        }
    }
}
