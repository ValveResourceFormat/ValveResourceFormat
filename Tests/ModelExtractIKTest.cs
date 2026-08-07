using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Tests
{
    [TestFixture]
    public class ModelExtractIKTest
    {
        private static string ExtractValveModel(string fileName)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", fileName));
            return new ModelExtract(resource, new NullFileLoader()).ToValveModel();
        }

        [Test]
        public void EmitsIKChainsWithNestedJoints()
        {
            var vmdl = ExtractValveModel("box_creature_ik_model.vmdl_c");

            Assert.Multiple(() =>
            {
                Assert.That(vmdl, Does.Contain("_class = \"IKData\""));
                Assert.That(vmdl, Does.Contain("_class = \"IKChain\""));
                Assert.That(vmdl, Does.Contain("name = \"IK_chain_new_foot_r\""));
                Assert.That(vmdl, Does.Contain("name = \"IK_chain_new_foot_l\""));

                Assert.That(vmdl, Does.Contain("_class = \"IKChainJoint\""));
                Assert.That(vmdl, Does.Contain("bone = \"upper_leg_r\""));
                Assert.That(vmdl, Does.Contain("bone = \"foot_r\""));

                Assert.That(vmdl, Does.Contain("m_DefaultSolverSettings.m_SolverType \" = \"IKSOLVER_Fabrik\""));
                Assert.That(vmdl, Does.Contain("m_Name = \"foot_r_ik_target\""));
            });
        }

        /// <summary>
        /// Each joint owns the joints below it, so the second joint is nested inside the first and
        /// therefore closes before its parent writes its own bone key.
        /// </summary>
        [Test]
        public void NestsEachJointInsideItsParent()
        {
            var vmdl = ExtractValveModel("box_creature_ik_model.vmdl_c");

            var parent = vmdl.IndexOf("bone = \"upper_leg_r\"", System.StringComparison.Ordinal);
            var child = vmdl.IndexOf("bone = \"foot_r\"", System.StringComparison.Ordinal);

            Assert.That(child, Is.GreaterThan(0));
            Assert.That(parent, Is.GreaterThan(child));
        }

        /// <summary>
        /// ModelDoc registers some of these keys with a trailing space and others behind an m_Data
        /// prefix, and ignores every other spelling, so they have to survive serialization verbatim.
        /// </summary>
        [Test]
        public void WritesTheExactKeySpellingsModelDocReadsBack()
        {
            var vmdl = ExtractValveModel("box_creature_ik_model.vmdl_c");

            Assert.Multiple(() =>
            {
                Assert.That(vmdl, Does.Contain("\"m_PoleVectorForAxis \" = "));
                Assert.That(vmdl, Does.Contain("\"m_DefaultSolverSettings.m_SolverType \" = "));
                Assert.That(vmdl, Does.Contain("m_Data.m_bParentJointRequiresAlignment = "));
                Assert.That(vmdl, Does.Contain("m_Data.m_EndEffectorFixedOffsetAttachment = "));
                Assert.That(vmdl, Does.Contain("m_Data.m_DefaultTargetSettings.m_AnimgraphParameterNamePosition = "));
            });
        }

        [Test]
        public void EmitsHingeConstraintsAndTargetSettings()
        {
            var vmdl = ExtractValveModel("alyx_hand_left.vmdl_c");

            Assert.Multiple(() =>
            {
                Assert.That(vmdl, Does.Contain("_class = \"IKJointConstraint_Hinge\""));
                Assert.That(vmdl, Does.Contain("hinge_axis = \"Up\""));
                Assert.That(vmdl, Does.Contain("max_radians = 1.745329"));
                Assert.That(vmdl, Does.Contain("constrained_joint = \"\""));

                Assert.That(vmdl, Does.Contain("m_DefaultTargetSettings.m_TargetSource = \"Animgraph Parameter\""));
                Assert.That(vmdl, Does.Contain("m_Name = \"fingertip_thumb\""));
                Assert.That(vmdl, Does.Contain("m_DefaultSolverSettings.m_SolverType \" = \"IKSOLVER_CCD\""));
            });
        }

        /// <summary>
        /// These three are filled in by the compiler, so ModelDoc exposes no attribute for them.
        /// </summary>
        [Test]
        public void OmitsCompilerDerivedFields()
        {
            var vmdl = ExtractValveModel("alyx_hand_left.vmdl_c");

            Assert.Multiple(() =>
            {
                Assert.That(vmdl, Does.Not.Contain("m_qFixupRotation"));
                Assert.That(vmdl, Does.Not.Contain("m_ParentJoint"));
                Assert.That(vmdl, Does.Not.Contain("m_TargetCoordSystem"));
            });
        }

        [Test]
        public void OmitsIKDataWhenTheModelHasNoChains()
        {
            var vmdl = ExtractValveModel("lod_test.vmdl_c");

            Assert.That(vmdl, Does.Not.Contain("_class = \"IKData\""));
        }
    }
}
