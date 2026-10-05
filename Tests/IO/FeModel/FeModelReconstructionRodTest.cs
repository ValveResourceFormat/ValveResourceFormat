using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Reading a chain joint's springs, iterations, suspenders, twists and clusters off the compiled rods.</summary>
    public class FeModelReconstructionRodTest : FeModelTestModels
    {
        /// <summary>
        /// A static root over <paramref name="joints"/> - 1 free joints j1, j2, ... hanging <paramref name="spacing"/>
        /// apart down -Z, each parented to the one above, with no constraints.
        /// </summary>
        private static FeModelBuilder StraightChain(int joints, float spacing = 10f) => new()
        {
            Names = ["root", .. Enumerable.Range(1, joints - 1).Select(static joint => $"j{joint}")],
            StaticNodes = 1,
            Parents = [.. Enumerable.Range(-1, joints)],
            Positions = [.. Enumerable.Range(0, joints).Select(joint => new Vector3(0f, 0f, -spacing * joint))],
        };

        /// <summary>
        /// A chain rod's relaxation is the slider scaled by <c>exp(-default_stretch)</c>, which the reading divides
        /// back out: 0.5 * exp(-0.985) = 0.18672.
        /// </summary>
        [Test]
        public async Task ChainRodRelaxationDividesOutTheDefaultStretch()
        {
            var cloth = (StraightChain(3) with
            {
                DefaultSurfaceStretch = 0.985f,
                Rods = [RigidRod(0, 1, 10f, 0.18672f), RigidRod(1, 2, 10f, 0.18672f)],
            }).Reconstruct();

            var chains = cloth.BuildBoneChains();
            await Assert.That(chains.Count).IsEqualTo(1);

            using (Assert.Multiple())
            {
                foreach (var joint in chains[0].Joints)
                {
                    await Assert.That(joint.StretchStiffness).IsEqualTo(0.5f).Within(1e-3f);
                }
            }
        }

        /// <summary>
        /// Without <c>default_stretch</c> the slider is the compiled relaxation verbatim.
        /// </summary>
        [Test]
        public async Task ChainRodRelaxationIsVerbatimWithoutDefaultStretch()
        {
            var cloth = (StraightChain(2) with { Rods = [RigidRod(0, 1, 10f, 0.8f)] }).Reconstruct();

            var joint = cloth.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            await Assert.That(joint).IsNotNull();
            await Assert.That(joint!.StretchStiffness).IsEqualTo(0.8f).Within(1e-4f);
        }

        /// <summary>
        /// On a pair holding the chain's rod and a banded constraint, the chain keeps its matching rod and the banded
        /// copy comes back as surplus in either order.
        /// </summary>
        [Test]
        public async Task GetUngeneratedRodsKeepsTheChainRodAndReturnsTheBandedCopy()
        {
            var cloth = (StraightChain(2, 3f) with { Rods = [Rod(0, 1, 1f, 5f), RigidRod(0, 1, 3f, 0.6f)] }).Reconstruct();

            var chains = cloth.BuildBoneChains();
            var surplus = cloth.GetUngeneratedRods(chains);

            using (Assert.Multiple())
            {
                await Assert.That(chains[0].Joints.Find(j => j.Name == "j1")!.StretchStiffness)
                    .IsEqualTo(0.6f).Within(1e-4f);
                await Assert.That(surplus.Count).IsEqualTo(1);
                await Assert.That(surplus[0].MinDist).IsEqualTo(1f);
                await Assert.That(surplus[0].MaxDist).IsEqualTo(5f);
            }
        }

        /// <summary>
        /// A twist entry pointing at the joint's own extrude ring carries the authored value scaled by
        /// the child branch factor: 0.5 * 0.382 = 0.191.
        /// </summary>
        [Test]
        public async Task TwistRelaxDividesByTheChildBranchFactor()
        {
            var cloth = TwistModel(1, 2, 0.191f);

            await Assert.That(cloth.GetAuthoredTwistRelax(1, 0, 2)).IsEqualTo(0.5f).Within(1e-4f);
        }

        /// <summary>
        /// A joint read through its parent-ward entry instead carries the other branch factor:
        /// 0.5 * 0.618 = 0.309.
        /// </summary>
        [Test]
        public async Task TwistRelaxDividesByTheParentBranchFactorWithoutARing()
        {
            var cloth = TwistModel(1, 0, 0.309f);

            await Assert.That(cloth.GetAuthoredTwistRelax(1, 0, -1)).IsEqualTo(0.5f).Within(1e-4f);
        }

        private static ClothReconstruction TwistModel(int orient, int end, float relax) => new FeModelBuilder
        {
            Names = ["root", "j1", "$ccj1_0"],
            StaticNodes = 1,
            Parents = [-1, 0, 1],
            Twists = [Twist(orient, end, relax, 0f)],
        }.Reconstruct();

        /// <summary>
        /// Three rigid copies of a joint's parent span read two extra iterations.
        /// </summary>
        [Test]
        public async Task ExtraIterationsCountsTheRigidCopiesOfASpan()
        {
            var cloth = (StraightChain(2, 3f) with { Rods = [RigidRod(0, 1, 3f), RigidRod(0, 1, 3f), RigidRod(0, 1, 3f)] }).Reconstruct();

            var joint = cloth.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.ExtraIterations).IsEqualTo(2);
                await Assert.That(joint.Suspender).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// Three identical slack copies of a parent span read two extra iterations, as rigid copies do.
        /// </summary>
        [Test]
        public async Task ExtraIterationsCountsIdenticalSlackCopiesOfASpan()
        {
            var cloth = (StraightChain(2, 3f) with { Rods = [Rod(0, 1, 0f, 3f), Rod(0, 1, 0f, 3f), Rod(0, 1, 0f, 3f)] }).Reconstruct();

            var joint = cloth.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.ExtraIterations).IsEqualTo(2);
                await Assert.That(joint.Suspender).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A span rod holding a quarter of its rest span reads <c>antishrink</c> 0.25.
        /// </summary>
        [Test]
        public async Task ChainJointAntishrinkIsTheSlackItsOwnSpanKeeps()
        {
            var cloth = (StraightChain(2, 3f) with { Rods = [Rod(0, 1, 0.75f, 3f)] }).Reconstruct();

            var joint = cloth.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            await Assert.That(joint!.Antishrink).IsEqualTo(0.25f);
        }

        /// <summary>
        /// Slack rods on one pair that are not the same record state neither an extra iteration nor an antishrink.
        /// </summary>
        [Test]
        public async Task SlackRodsThatDisagreeAreNotExtraIterations()
        {
            var cloth = (StraightChain(2, 3f) with { Rods = [Rod(0, 1, 0f, 3f), Rod(0, 1, 1f, 3f)] }).Reconstruct();

            var joint = cloth.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.ExtraIterations).IsEqualTo(0);
                await Assert.That(joint.Antishrink).IsEqualTo(1f);
            }
        }

        /// <summary>
        /// The rods between every pair of a joint's children read as its <c>child_sibling_spring</c>.
        /// </summary>
        [Test]
        public async Task ChildSiblingSpringIsTheRodBetweenTwoChildrenOfOneJoint()
        {
            var joint = SiblingChain(RigidRod(0, 1, 3f), RigidRod(0, 2, 3f), RigidRod(0, 3, 3f), RigidRod(1, 2, 3f, 0.5f),
                RigidRod(1, 3, 3f, 0.5f), RigidRod(2, 3, 3f, 0.5f));

            await Assert.That(joint!.ChildSiblingSpring).IsEqualTo(0.5f);
        }

        /// <summary>
        /// A sibling set missing one of its pairs states no <c>child_sibling_spring</c>.
        /// </summary>
        [Test]
        public async Task AnIncompleteSiblingSetIsNotAChildSiblingSpring()
        {
            var joint = SiblingChain(RigidRod(0, 1, 3f), RigidRod(0, 2, 3f), RigidRod(0, 3, 3f), RigidRod(1, 2, 3f, 0.5f));

            await Assert.That(joint!.ChildSiblingSpring).IsEqualTo(0f);
        }

        private static BoneChainJoint? SiblingChain(params FeRodConstraint[] rods) => new FeModelBuilder
        {
            Names = ["root", "j1", "j2", "j3"],
            StaticNodes = 1,
            Parents = [-1, 0, 0, 0],
            Positions = [new(0f, 0f, 0f), new(3f, 0f, 0f), new(0f, 3f, 0f), new(0f, 0f, 3f)],
            Rods = rods,
        }.Reconstruct().BuildBoneChains()[0].Joints.Find(j => j.Name == "root");

        /// <summary>
        /// Rods joining a node to itself or missing an endpoint are dropped at parse.
        /// </summary>
        [Test]
        public async Task SelfRodsAndDegenerateRodsAreDropped()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["a", "b", "c", "d"],
                Rods = [RigidRod(3, 3, 1f), new FeRodConstraint([2], 1f, 1f, 0.5f, 1f), RigidRod(0, 1, 2f)],
            }.Reconstruct();

            await Assert.That(cloth.Index.Rods.Length).IsEqualTo(1);

            using (Assert.Multiple())
            {
                await Assert.That(cloth.Index.Rods[0].NodeA).IsEqualTo(0);
                await Assert.That(cloth.Index.Rods[0].NodeB).IsEqualTo(1);
                await Assert.That(cloth.Index.Rods[0].MaxDist).IsEqualTo(2f);
            }
        }

        /// <summary>
        /// A chain root reads its iterations off its only child's span, three copies reading two; a root with two
        /// children keeps zero.
        /// </summary>
        [Test]
        public async Task AChainRootCountsItsIterationsOnItsOnlyChildsSpan()
        {
            var oneChild = (StraightChain(3) with
            {
                Rods = [RigidRod(0, 1, 10f), RigidRod(0, 1, 10f), RigidRod(0, 1, 10f), RigidRod(1, 2, 10f), RigidRod(1, 2, 10f), RigidRod(1, 2, 10f)],
            }).Reconstruct();

            var twoChildren = new FeModelBuilder
            {
                Names = ["root", "j1", "j2"],
                StaticNodes = 1,
                Parents = [-1, 0, 0],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, 0f)],
                Rods = [RigidRod(0, 1, 10f), RigidRod(0, 1, 10f), RigidRod(0, 1, 10f), RigidRod(0, 2, 10f), RigidRod(0, 2, 10f), RigidRod(0, 2, 10f)],
            }.Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(RootJoint(oneChild).ExtraIterations).IsEqualTo(2);
                await Assert.That(RootJoint(twoChildren).ExtraIterations).IsEqualTo(0);
            }
        }

        private static BoneChainJoint RootJoint(ClothReconstruction cloth)
            => cloth.BuildBoneChains()[0].Joints.Find(static joint => joint.IsRoot)!;

        /// <summary>
        /// A joint whose upward span reaches the root reads the odd rod on that span as its suspender companion.
        /// </summary>
        [Test]
        public async Task ASuspenderCompanionIsTheSurplusRodOnTheRootSpan()
        {
            var cloth = (StraightChain(3) with
            {
                Rods =
                [
                    RigidRod(0, 1, 10f),
                    RigidRod(1, 2, 10f),
                    RigidRod(1, 2, 10f),
                    RigidRod(0, 2, 20f),
                    RigidRod(0, 2, 20f),
                    RigidRod(0, 2, 20f, 0.35f),
                ],
            }).Reconstruct();

            var joint = cloth.BuildBoneChains()[0].Joints.Find(static j => j.Name == "j2");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.Suspender).IsEqualTo(0.35f).Within(1e-4f);
                await Assert.That(joint.ExtraIterations).IsEqualTo(1);
            }
        }

        /// <summary>
        /// Past the torsion span every rigid rod on the pair is a suspender copy, so two agreeing copies read 0.42.
        /// </summary>
        [Test]
        public async Task ASuspenderPastTheTorsionSpanIsReadFromItsRepeatedCopies()
        {
            var joint = LongChainWithSuspender().BuildBoneChains()[0].Joints
                .Find(static j => j.Name == "j4");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.Suspender).IsEqualTo(0.42f).Within(1e-4f);
                await Assert.That(joint.ExtraIterations).IsEqualTo(1);
            }
        }

        /// <summary>
        /// At two iterations both suspender companion copies are regenerated and none is surplus.
        /// </summary>
        [Test]
        public async Task ASuspenderCompanionIsRegeneratedOncePerIteration()
        {
            var cloth = LongChainWithSuspender();

            await Assert.That(cloth.GetUngeneratedRods(cloth.BuildBoneChains())).IsEmpty();
        }

        private static ClothReconstruction LongChainWithSuspender() => (StraightChain(5) with
        {
            Rods =
            [
                RigidRod(0, 1, 10f),
                RigidRod(1, 2, 10f),
                RigidRod(2, 3, 10f),
                RigidRod(3, 4, 10f),
                RigidRod(3, 4, 10f),
                RigidRod(0, 4, 40f, 0.42f),
                RigidRod(0, 4, 40f, 0.42f),
            ],
        }).Reconstruct();

        /// <summary>
        /// A two-corner source element between two chain joints is re-declared as a spring with the rod's fields, and
        /// the joint's stretch slider is zeroed.
        /// </summary>
        [Test]
        public async Task ASourceElementBetweenTwoChainJointsIsAnAuthoredSpring()
        {
            var cloth = SpringedChain.Reconstruct();
            var chains = cloth.BuildBoneChains();
            var springs = cloth.GetAuthoredSourceSprings(chains);

            using (Assert.Multiple())
            {
                await Assert.That(chains[0].Joints.Find(static j => j.Name == "j2")!.StretchStiffness)
                    .IsEqualTo(0f);
                await Assert.That(springs.Count).IsEqualTo(1);
                await Assert.That(springs[0]).IsEqualTo((1, 2, 1));
            }
        }

        /// <summary>
        /// On a compile with no node bases a roped node keeps its chain span, and its two-corner element is not a
        /// spring.
        /// </summary>
        [Test]
        public async Task ARopedChainKeepsItsOwnSpanWhereTheCompileWroteNoNodeBase()
        {
            var cloth = (SpringedChain with { RopeCount = 1, Ropes = [4, 0, 1, 2] }).Reconstruct();
            var chains = cloth.BuildBoneChains();

            using (Assert.Multiple())
            {
                await Assert.That(chains[0].Joints.Find(static j => j.Name == "j2")!.StretchStiffness)
                    .IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(cloth.GetAuthoredSourceSprings(chains)).IsEmpty();
            }
        }

        /// <summary><see cref="StraightChain"/> of three with a two-corner source element over j1 and j2.</summary>
        private static FeModelBuilder SpringedChain => StraightChain(3) with
        {
            SourceElems = [0, 1, 0, 0, 1, 2],
            Rods = [RigidRod(0, 1, 10f), RigidRod(1, 2, 10f, 0.5f)],
        };

        /// <summary>
        /// A chain root whose ring pair carries a rigid rod at 0.6 beside a banded surface rod at 1.0 reads its
        /// <c>stretch_spring</c> off the rigid rod alone.
        /// </summary>
        [Test]
        public async Task AChainRootReadsItsStretchSpringOffItsOwnRigidRingRod()
        {
            var joint = RingWithASurfaceRod(1.0f).BuildBoneChains()[0].Joints[0];

            await Assert.That(joint.StretchStiffness).IsEqualTo(0.6f).Within(1e-4f);
        }

        /// <summary>
        /// Where every rod inside the root's extrusion agrees, that reading stands over the rigid rods.
        /// </summary>
        [Test]
        public async Task AChainRootWhoseExtrusionAgreesKeepsTheWholeReading()
        {
            var joint = RingWithASurfaceRod(0.6f).BuildBoneChains()[0].Joints[0];

            await Assert.That(joint.StretchStiffness).IsEqualTo(0.6f).Within(1e-4f);
        }

        /// <summary>
        /// A chain root with a two-node ring whose pair carries a rigid rod at 0.6 beside a banded rod at <paramref
        /// name="surfaceRelaxation"/>.
        /// </summary>
        private static ClothReconstruction RingWithASurfaceRod(float surfaceRelaxation) => new FeModelBuilder
        {
            Names = ["root", "$ccroot_0", "$ccroot_1"],
            StaticNodes = 1,
            Parents = [-1, 0, 0],
            Positions = [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, -2f, 0f)],
            Rods = [RigidRod(1, 2, 4f, 0.6f), Rod(1, 2, 1f, 8f, 0.5f, surfaceRelaxation)],
        }.Reconstruct();

        [Test]
        public async Task MotionBiasIsReadOffTheJointsOwnSpanRod()
        {
            var joint = new BoneChainJoint { Node = 2, Name = "j2", ParentNode = 1, InvMass = 1f };
            using (Assert.Multiple())
            {
                await Assert.That(BiasedRope(0.5f).GetMotionBias(joint)).IsNull();
                await Assert.That(BiasedRope(0.333333f).GetMotionBias(joint)!.Value).IsEqualTo(0.5f).Within(1e-3f);
                await Assert.That(BiasedRope(0.666667f).GetMotionBias(joint)!.Value).IsEqualTo(-0.5f).Within(1e-3f);
                await Assert.That(BiasedRope(0f).GetMotionBias(joint)!.Value).IsEqualTo(1f).Within(1e-3f);
            }
        }

        private static ClothReconstruction BiasedRope(float weight) => (StraightChain(4) with { Rods = [RigidRod(1, 2, 10f, 1f, weight)] }).Reconstruct();

        /// <summary>
        /// A span with one pair carrying an extra rod reads <c>extra_iterations</c> off the count every pair reaches.
        /// </summary>
        [Test]
        public async Task ExtraIterationsIsTheFloorOfASpanWhoseOnePairCarriesASurplusRod()
        {
            var even = RingSpanChain(surplus: 0).BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");
            var lopsided = RingSpanChain(surplus: 1).BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(even!.ExtraIterations).IsEqualTo(2);
                await Assert.That(lopsided!.ExtraIterations).IsEqualTo(2);
            }
        }

        /// <summary>
        /// A ringed root and a ringed joint, so the joint's own span is the four pairs of the two rings.
        /// Every pair carries three copies; <paramref name="surplus"/> more go on one of them.
        /// </summary>
        private static ClothReconstruction RingSpanChain(int surplus)
        {
            var rods = new List<FeRodConstraint>();
            foreach (var (a, b) in new[] { (1, 4), (1, 5), (2, 4), (2, 5) })
            {
                var copies = 3 + (a == 1 && b == 4 ? surplus : 0);
                for (var i = 0; i < copies; i++)
                {
                    rods.Add(RigidRod(a, b, 4f));
                }
            }

            return new FeModelBuilder
            {
                Names = ["root", "$ccroot_0", "$ccroot_1", "j1", "$ccj1_0", "$ccj1_1"],
                StaticNodes = 1,
                Parents = [-1, 0, 0, 0, 3, 3],
                Positions = [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, -2f, 0f), new(0f, 0f, -4f), new(0f, 2f, -4f), new(0f, -2f, -4f)],
                Rods = [.. rods],
            }.Reconstruct();
        }

        /// <summary>
        /// A complete clique of banded rods reads as a <c>ClothSelfCollisionCluster</c>; a triangle stays plain rods.
        /// </summary>
        [Test]
        public async Task ACompleteBandedRodCliqueIsReadBackAsASelfCollisionCluster()
        {
            var clique = ClusterCloth(4).Reconstruct();
            var triangle = ClusterCloth(3).Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(clique.SelfCollisionClusters.Count).IsEqualTo(1);
                await Assert.That(string.Join(",", clique.SelfCollisionClusters[0].Nodes)).IsEqualTo("1,2,3,4");
                await Assert.That(clique.SelfCollisionClusters[0].MinDist).IsEqualTo(2f);
                await Assert.That(clique.SelfCollisionClusters[0].MaxDist).IsEqualTo(10f);
                await Assert.That(clique.SelfCollisionClusterRods.Count).IsEqualTo(6);
                await Assert.That(triangle.SelfCollisionClusters.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A chain whose every span a cluster owns reads <c>stretch_spring</c> 0; without a cluster it keeps the
        /// default.
        /// </summary>
        [Test]
        public async Task AChainWhoseSpansACollisionClusterOwnsRecoversAZeroStretchSlider()
        {
            var clustered = ClusteredChain().BuildBoneChains()[0].Joints;
            var plain = StretchlessChain(RigidRod(0, 2, 20f), RigidRod(1, 3, 20f)).Reconstruct().BuildBoneChains()[0].Joints;

            using (Assert.Multiple())
            {
                await Assert.That(clustered.Count).IsEqualTo(5);
                await Assert.That(clustered.TrueForAll(joint => joint.IsRoot || joint.StretchStiffness == 0f))
                    .IsTrue();
                await Assert.That(plain.TrueForAll(joint => joint.IsRoot || joint.StretchStiffness == 1f))
                    .IsTrue();
            }
        }

        private static ClothReconstruction ClusteredChain()
        {
            var cluster = ClusterCloth(4);
            return (cluster with { Rods = [.. cluster.Rods!, RigidRod(0, 2, 20f), RigidRod(1, 3, 20f), RigidRod(2, 4, 20f)] }).Reconstruct();
        }

        /// <summary>
        /// A free chain of <paramref name="members"/> joints below j0, every node position-driven, its joints joined
        /// pairwise by rods banded 2 to 10.
        /// </summary>
        private static FeModelBuilder ClusterCloth(int members)
        {
            var rods = new List<FeRodConstraint>();
            for (var a = 1; a <= members; a++)
            {
                for (var b = a + 1; b <= members; b++)
                {
                    rods.Add(Rod(a, b, 2f, 10f));
                }
            }

            return new FeModelBuilder
            {
                Names = [.. Enumerable.Range(0, members + 1).Select(static i => $"j{i}")],
                Parents = [.. Enumerable.Range(-1, members + 1)],
                FirstPositionDrivenNode = members + 1,
                Positions = [.. Enumerable.Range(0, members + 1).Select(static i => new Vector3(0f, 0f, -10f * i))],
                Rods = [.. rods],
            };
        }

        /// <summary>
        /// A joint whose parent is the root reads a doubled parent span as a suspender, not an extra iteration.
        /// </summary>
        [Test]
        public async Task AJointOnTheChainRootReadsItsDoubledRootPairAsASuspender()
        {
            var joint = SuspendedRope().BuildBoneChains()[0].Joints[1];

            using (Assert.Multiple())
            {
                await Assert.That(joint.Suspender).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(joint.ExtraIterations).IsEqualTo(0);
            }
        }

        private static ClothReconstruction SuspendedRope() => (StraightChain(4) with
        {
            Rods =
            [
                RigidRod(0, 1, 10f),
                RigidRod(0, 1, 10f, 0.5f),
                RigidRod(1, 2, 10f),
                RigidRod(2, 3, 10f),
                RigidRod(0, 2, 20f, 0.5f),
                RigidRod(0, 3, 30f, 0.5f),
            ],
        }).Reconstruct();

        /// <summary>
        /// A three-member cluster is read off a shared 12 to 48 band no pair rests at; the same triangle banded at a
        /// pair's rest distance is a surface, not a cluster.
        /// </summary>
        [Test]
        public async Task AThreeMemberClusterIsReadOffABandNoPairRestsAt()
        {
            var cluster = ThreeMemberCluster().Reconstruct().SelfCollisionClusters;
            var surface = ThreeMemberCluster(bandMin: 2f, bandMax: 8.5f).Reconstruct().SelfCollisionClusters;

            using (Assert.Multiple())
            {
                await Assert.That(cluster.Count).IsEqualTo(1);
                await Assert.That(cluster[0].Nodes.SequenceEqual([2, 4, 6])).IsTrue();
                await Assert.That(cluster[0].MinDist).IsEqualTo(12f);
                await Assert.That(cluster[0].MaxDist).IsEqualTo(48f);
                await Assert.That(surface.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A cluster member's stiffness is read off the products its pair rods carry: 0.25 on every pair is three
        /// members at 0.5, and 0.25 / 0.5 / 0.5 is 0.5, 0.5 and 1.0.
        /// </summary>
        [Test]
        public async Task AClusterMembersStiffnessIsReadOffTheProductsItsPairRodsCarry()
        {
            var uniform = ThreeMemberCluster(firstRelaxation: 0.25f, otherRelaxation: 0.25f).Reconstruct().SelfCollisionClusters;
            var mixed = ThreeMemberCluster(firstRelaxation: 0.25f, otherRelaxation: 0.5f).Reconstruct().SelfCollisionClusters;

            using (Assert.Multiple())
            {
                await Assert.That(uniform.Count).IsEqualTo(1);
                await Assert.That(uniform[0].Stiffness.All(static s => MathF.Abs(s - 0.5f) < 1e-4f)).IsTrue();
                await Assert.That(mixed.Count).IsEqualTo(1);
                await Assert.That(mixed[0].Stiffness[0]).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(mixed[0].Stiffness[1]).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(mixed[0].Stiffness[2]).IsEqualTo(1f).Within(1e-4f);
            }
        }

        /// <summary>
        /// <see cref="FeModelTestModels.Coattail"/> whose joints j1, j2 and the tip are joined pairwise by rods banded
        /// <paramref name="bandMin"/> to <paramref name="bandMax"/>, the j1-j2 band at <paramref name="firstRelaxation"/>
        /// and the others at <paramref name="otherRelaxation"/>.
        /// </summary>
        private static FeModelBuilder ThreeMemberCluster(float bandMin = 12f, float bandMax = 48f, float firstRelaxation = 1f,
            float otherRelaxation = 1f) => Coattail with
            {
                InvMasses = [0f, 0f, 0.002017f, 0.003444f, 0.002338f, 0.003427f, 0.002794f, 0.0065f],
                Rods =
            [
                RigidRod(0, 2, 8.499948f, 1f, 0f),
                RigidRod(0, 3, 8.646747f, 1f, 0f),
                RigidRod(1, 2, 8.732066f, 1f, 0f),
                RigidRod(1, 3, 8.412711f, 1f, 0f),
                RigidRod(2, 3, 2f),
                Rod(2, 4, bandMin, bandMax, 0.5f, firstRelaxation),
                RigidRod(2, 5, 8.735466f),
                Rod(2, 6, bandMin, bandMax, 0.5f, otherRelaxation),
                RigidRod(3, 4, 8.732044f),
                RigidRod(3, 5, 8.503419f),
                RigidRod(4, 5, 2.000001f),
                RigidRod(6, 4, 8.500037f),
                RigidRod(4, 7, 8.732154f),
                RigidRod(6, 5, 8.732168f),
                RigidRod(5, 7, 8.500037f),
                RigidRod(6, 7, 2.000001f),
                RigidRod(2, 4, 8.499931f),
                Rod(6, 4, bandMin, bandMax, 0.5f, otherRelaxation),
            ],
            };

        /// <summary>
        /// A joint's <c>motion_bias</c> is read off its span weights whatever its ends weigh: 1/3 reads 0.5 and 2/3
        /// reads -0.5.
        /// </summary>
        [Test]
        public async Task AJointsMotionBiasIsReadOffItsSpanWhateverItsEndsWeigh()
        {
            var positive = BiasedChain(0.333333f).Reconstruct().BuildBoneChains()[0].Joints.First(static joint => joint.Node == 6);
            var negativeModel = BiasedChain(0.666667f).Reconstruct();
            var negative = negativeModel.BuildBoneChains()[0].Joints.First(static joint => joint.Node == 6);

            using (Assert.Multiple())
            {
                await Assert.That(BiasedChain(0.333333f).Reconstruct().GetMotionBias(positive)!.Value).IsEqualTo(0.5f).Within(1e-3f);
                await Assert.That(negativeModel.GetMotionBias(negative)!.Value).IsEqualTo(-0.5f).Within(1e-3f);
            }
        }

        /// <summary>
        /// <see cref="FeModelTestModels.Coattail"/> whose rods from j1 down weigh their upper end <paramref name="weight"/>.
        /// </summary>
        private static FeModelBuilder BiasedChain(float weight) => Coattail with
        {
            InvMasses = [0f, 0f, 0.003428f, 0.003444f, 0.003428f, 0.003427f, 0.0065f, 0.0065f],
            Rods =
            [
                RigidRod(0, 2, 8.499948f, 1f, 0f),
                RigidRod(0, 3, 8.646747f, 1f, 0f),
                RigidRod(1, 2, 8.732066f, 1f, 0f),
                RigidRod(1, 3, 8.412711f, 1f, 0f),
                RigidRod(2, 4, 8.499931f, 1f, weight),
                RigidRod(2, 5, 8.735466f, 1f, weight),
                RigidRod(3, 4, 8.732044f, 1f, weight),
                RigidRod(3, 5, 8.503419f, 1f, weight),
                RigidRod(2, 3, 2f),
                RigidRod(4, 6, 8.500037f, 1f, weight),
                RigidRod(4, 7, 8.732154f, 1f, weight),
                RigidRod(5, 6, 8.732168f, 1f, weight),
                RigidRod(5, 7, 8.500037f, 1f, weight),
                RigidRod(4, 5, 2.000001f),
                RigidRod(6, 7, 2.000001f),
            ],
        };

        /// <summary>
        /// A joint with no rod on its span and none on its ring reads <c>stretch_spring</c> 0; putting its ring rod
        /// back reads 1.
        /// </summary>
        [Test]
        public async Task AJointsZeroStretchSpringIsReadOffItsRodlessSpanAndRing()
        {
            var joints = AlternatingStretch.Reconstruct().BuildBoneChains()[0].Joints;
            var ringed = (AlternatingStretch with { Rods = [.. AlternatingStretch.Rods![..4], RigidRod(3, 4, 2f), .. AlternatingStretch.Rods[4..]] })
                .Reconstruct().BuildBoneChains()[0].Joints;

            using (Assert.Multiple())
            {
                await Assert.That(joints.First(static joint => joint.Name == "coattail_1_L").StretchStiffness).IsEqualTo(0f);
                await Assert.That(joints.First(static joint => joint.Name == "coattail_2_L").StretchStiffness).IsEqualTo(1f);
                await Assert.That(joints.First(static joint => joint.Name == "coattail_end_L").StretchStiffness).IsEqualTo(0f);
                await Assert.That(ringed.First(static joint => joint.Name == "coattail_1_L").StretchStiffness).IsEqualTo(1f);
            }
        }

        /// <summary>
        /// A coattail whose first joint and both its rings are static, held by the rods of its second span alone.
        /// </summary>
        private static FeModelBuilder AlternatingStretch => new()
        {
            Names = ["coattail_0_L", "$cccoattail_0_L_0", "$cccoattail_end_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_2_L",
                "$cccoattail_2_L_0", "coattail_end_L"],
            StaticNodes = 3,
            InvMasses = [0f, 0f, 0f, 0.007253f, 0.007252f, 0.0065f, 0.006497f, 1f],
            Poses =
            [
                Pose(-8.915481f, 4.000124f, 65.447983f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-10.723646f, 4.561181f, 66.092773f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-19.686529f, 5.562407f, 42.336063f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-11.695464f, 4.267121f, 57.419937f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-13.473376f, 4.824905f, 58.146507f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-14.808016f, 4.637866f, 49.519089f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-16.587204f, 5.195801f, 50.242416f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-17.907341f, 5.004471f, 41.612736f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
            ],
            Parents = [-1, 0, 7, 0, 3, 3, 5, 5],
            SourceElems = [0, 0, 0, 2, 4, 3, 5, 6, 3, 4, 6, 5],
            Rods = [RigidRod(5, 3, 8.499931f), RigidRod(6, 3, 8.735466f), RigidRod(5, 4, 8.732044f), RigidRod(6, 4, 8.503419f), RigidRod(5, 6, 2.000001f)],
        };

        /// <summary>
        /// A joint's <c>animated_length</c> is read off the rods it and its children move into <c>m_SimdRodsAnim</c>;
        /// on a childless tip only a node base tells it from a zero <c>stretch_spring</c>.
        /// </summary>
        [Test]
        public async Task AJointsAnimatedLengthIsReadOffTheRodsItAndItsChildrenLose()
        {
            var joints = AnimatedJointTwo().Reconstruct().BuildBoneChains()[0].Joints;
            var tipBased = (AnimatedEveryJoint with { NodeBases = [TipBase] }).Reconstruct().BuildBoneChains()[0].Joints
                .First(static joint => joint.Name == "coattail_end_L");
            var tipFree = AnimatedEveryJoint.Reconstruct().BuildBoneChains()[0].Joints
                .First(static joint => joint.Name == "coattail_end_L");

            using (Assert.Multiple())
            {
                await Assert.That(joints.First(static joint => joint.Name == "coattail_2_L").AnimatedLength).IsTrue();
                await Assert.That(joints.First(static joint => joint.Name == "coattail_2_L").StretchStiffness).IsEqualTo(1f);
                await Assert.That(joints.First(static joint => joint.Name == "coattail_1_L").AnimatedLength).IsFalse();
                await Assert.That(joints.First(static joint => joint.Name == "coattail_end_L").AnimatedLength).IsFalse();
                await Assert.That(tipBased.AnimatedLength).IsTrue();
                await Assert.That(tipFree.AnimatedLength).IsFalse();
                await Assert.That(tipFree.StretchStiffness).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A coattail whose second joint's span and its children's rods are animated, the second joint's ring and the
        /// joints below weighing their upper end <paramref name="childWeight"/>.
        /// </summary>
        private static FeModelBuilder AnimatedJointTwo(float childWeight = 0.5f) => new()
        {
            Names = ["coattail_0_L", "$cccoattail_0_L_0", "$cccoattail_2_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_end_L",
                "$cccoattail_end_L_0", "coattail_2_L"],
            StaticNodes = 3,
            InvMasses = [0f, 0f, 0f, 0.0065f, 0.006558f, 0.0625f, 0.0625f, 1f],
            Poses =
            [
                Pose(-8.915481f, 4.000124f, 65.447983f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-10.723646f, 4.561181f, 66.092773f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-16.587204f, 5.195801f, 50.242416f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-11.695464f, 4.267121f, 57.419937f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-13.473376f, 4.824905f, 58.146507f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-17.907341f, 5.004471f, 41.612736f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-19.686529f, 5.562407f, 42.336063f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-14.808016f, 4.637866f, 49.519089f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
            ],
            Parents = [-1, 0, 7, 0, 3, 7, 5, 3],
            SourceElems = [0, 0, 0, 6, 2, 7, 5, 6, 7, 2, 6, 5, 4, 3, 7, 2, 3, 4, 2, 7, 1, 0, 3, 4, 0, 1, 4, 3],
            Rods =
            [
                RigidRod(0, 3, 8.499948f, 1f, 0f),
                RigidRod(0, 4, 8.646747f, 1f, 0f),
                RigidRod(1, 3, 8.732066f, 1f, 0f),
                RigidRod(1, 4, 8.412711f, 1f, 0f),
                RigidRod(3, 4, 2f),
                RigidRod(5, 6, 2.000001f),
            ],
            SimdRodsAnim =
            [
                new FeSimdRodConstraintAnim([[2, 2, 2, 2], [7, 7, 7, 7]], [0f, 0f, 0f, 0f], [1f, 1f, 1f, 1f]),
                new FeSimdRodConstraintAnim([[3, 2, 2, 2], [7, 4, 4, 4]], [0.5f, 0f, 0f, 0f], [1f, 1f, 1f, 1f]),
                new FeSimdRodConstraintAnim([[4, 2, 2, 2], [7, 3, 3, 3]], [0.5f, 0f, 0f, 0f], [1f, 1f, 1f, 1f]),
                new FeSimdRodConstraintAnim([[5, 2, 2, 2], [7, 6, 6, 6]], [childWeight, 0f, 0f, 0f], [1f, 1f, 1f, 1f]),
                new FeSimdRodConstraintAnim([[6, 2, 2, 2], [7, 5, 5, 5]], [childWeight, 0f, 0f, 0f], [1f, 1f, 1f, 1f]),
            ],
        };

        /// <summary>
        /// A coattail whose rings are all static and every joint span is animated, with no plain rods.
        /// </summary>
        private static FeModelBuilder AnimatedEveryJoint => new()
        {
            Names = ["coattail_0_L", "$cccoattail_0_L_0", "$cccoattail_1_L_0", "$cccoattail_2_L_0", "$cccoattail_end_L_0", "coattail_1_L",
                "coattail_2_L", "coattail_end_L"],
            StaticNodes = 5,
            Poses =
            [
                Pose(-8.915481f, 4.000124f, 65.447983f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-10.723646f, 4.561181f, 66.092773f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-13.473376f, 4.824905f, 58.146507f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-16.587204f, 5.195801f, 50.242416f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-19.686529f, 5.562407f, 42.336063f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-11.695464f, 4.267121f, 57.419937f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-14.808016f, 4.637866f, 49.519089f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-17.907341f, 5.004471f, 41.612736f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
            ],
            Parents = [-1, 0, 5, 6, 7, 0, 5, 6],
            SourceElems = [0, 0, 0, 6, 3, 6, 7, 4, 6, 3, 4, 7, 2, 5, 6, 3, 5, 2, 3, 6, 1, 0, 5, 2, 0, 1, 2, 5],
            Rods = [],
            SimdRodsAnim =
            [
                new FeSimdRodConstraintAnim([[2, 0, 4, 4], [6, 5, 7, 7]], [0f, 0f, 0f, 0f], [1f, 1f, 1f, 1f]),
                new FeSimdRodConstraintAnim([[3, 1, 4, 4], [6, 5, 7, 7]], [0f, 0f, 0f, 0f], [1f, 1f, 1f, 1f]),
                new FeSimdRodConstraintAnim([[4, 3, 2, 2], [6, 7, 5, 5]], [0f, 0f, 0f, 0f], [1f, 1f, 1f, 1f]),
                new FeSimdRodConstraintAnim([[6, 4, 4, 4], [5, 7, 7, 7]], [0.5f, 0f, 0f, 0f], [1f, 1f, 1f, 1f]),
                new FeSimdRodConstraintAnim([[7, 3, 3, 3], [6, 5, 5, 5]], [0.5f, 0f, 0f, 0f], [1f, 1f, 1f, 1f]),
            ],
        };

        private static FeNodeBase TipBase => NodeBase(7, 7, 3, 4, 6);

        private static FeNodeBase[] InnerBases => [NodeBase(5, 3, 0, 1, 6), NodeBase(6, 5, 4, 7, 2)];

        /// <summary>
        /// A suspender on a joint next to the root is read off the root span's copies even where every span is
        /// repeated: suspender 0.5 at two extra iterations.
        /// </summary>
        [Test]
        public async Task ARootAdjacentSuspenderIsReadWhereEverySpanIsRepeated()
        {
            var coattail = RepeatedSuspenderChain().BuildBoneChains()[0].Joints
                .First(static joint => joint.Name == "coattail_1_L");

            using (Assert.Multiple())
            {
                await Assert.That(coattail.Suspender).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(coattail.ExtraIterations).IsEqualTo(2);
            }
        }

        /// <summary>
        /// <see cref="FeModelTestModels.Coattail"/> whose rods are written three times over, the suspenders at 0.5, with
        /// further copies of the root span.
        /// </summary>
        private static ClothReconstruction RepeatedSuspenderChain()
        {
            FeRodConstraint[] declaration =
            [
                RigidRod(0, 4, 16.995832f, 0.5f, 0f),
                RigidRod(0, 5, 17.073202f, 0.5f, 0f),
                RigidRod(0, 6, 25.49473f, 0.5f, 0f),
                RigidRod(0, 7, 25.546371f, 0.5f, 0f),
                RigidRod(1, 2, 8.732066f, 1f, 0f),
                RigidRod(1, 3, 8.412711f, 1f, 0f),
                RigidRod(1, 4, 17.06971f, 0.5f, 0f),
                RigidRod(1, 5, 16.912064f, 0.5f, 0f),
                RigidRod(1, 6, 25.516155f, 0.5f, 0f),
                RigidRod(1, 7, 25.410963f, 0.5f, 0f),
                RigidRod(2, 3, 2f),
                RigidRod(4, 2, 8.499931f),
                RigidRod(5, 2, 8.735466f),
                RigidRod(4, 3, 8.732044f),
                RigidRod(5, 3, 8.503419f),
                RigidRod(4, 5, 2.000001f),
                RigidRod(6, 4, 8.500037f),
                RigidRod(7, 4, 8.732154f),
                RigidRod(6, 5, 8.732168f),
                RigidRod(7, 5, 8.500037f),
                RigidRod(6, 7, 2.000001f),
            ];

            return (Coattail with
            {
                InvMasses = [0f, 0f, 0.000776f, 0.000781f, 0.000591f, 0.000591f, 0.000593f, 0.000594f],
                Rods =
                [
                    RigidRod(0, 2, 8.499948f, 0.5f, 0f),
                    RigidRod(0, 3, 8.646747f, 0.5f, 0f),
                    .. declaration,
                    RigidRod(0, 2, 8.499948f, 1f, 0f),
                    RigidRod(0, 3, 8.646747f, 1f, 0f),
                    .. declaration,
                    RigidRod(0, 2, 8.499948f, 1f, 0f),
                    RigidRod(0, 3, 8.646747f, 0.5f, 0f),
                    .. declaration,
                    RigidRod(0, 2, 8.499948f, 1f, 0f),
                    RigidRod(0, 3, 8.646747f, 1f, 0f),
                    RigidRod(1, 2, 8.732066f, 0.5f, 0f),
                    RigidRod(1, 3, 8.412711f, 0.5f, 0f),
                    RigidRod(0, 2, 8.499948f, 0.5f, 0f),
                    RigidRod(0, 3, 8.646747f, 1f, 0f),
                    RigidRod(1, 2, 8.732066f, 0.5f, 0f),
                    RigidRod(1, 3, 8.412711f, 0.5f, 0f),
                    RigidRod(0, 2, 8.499948f, 0.5f, 0f),
                    RigidRod(0, 3, 8.646747f, 0.5f, 0f),
                    RigidRod(1, 2, 8.732066f, 0.5f, 0f),
                    RigidRod(1, 3, 8.412711f, 0.5f, 0f),
                ],
            }).Reconstruct();
        }

        /// <summary>
        /// Under version 1 a joint cut off from its children reads as animated where it keeps a node base, and as a
        /// zero <c>stretch_spring</c> where it does not.
        /// </summary>
        [Test]
        public async Task AnAnimatedLengthJointIsReadOffTheNodeBaseItKeeps()
        {
            var based = (AnimatedEveryJoint with { NodeBases = [.. InnerBases, TipBase] }).Reconstruct().BuildBoneChains()[0].Joints;
            var tipFree = (AnimatedEveryJoint with { NodeBases = InnerBases }).Reconstruct().BuildBoneChains()[0].Joints;

            using (Assert.Multiple())
            {
                await Assert.That(based.Where(static joint => !joint.IsRoot).All(static joint => joint.AnimatedLength)).IsTrue();
                await Assert.That(tipFree.First(static joint => joint.Name == "coattail_1_L").AnimatedLength).IsTrue();
                await Assert.That(tipFree.First(static joint => joint.Name == "coattail_end_L").AnimatedLength).IsFalse();
                await Assert.That(tipFree.First(static joint => joint.Name == "coattail_end_L").StretchStiffness).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A suspender of 1.0 next to the root is read off the ring rod carrying as many copies as the base span, not
        /// as a repeat.
        /// </summary>
        [Test]
        public async Task ASuspenderAtTheChainsOwnRelaxationIsReadOffItsRingCopies()
        {
            var coattail = SuspenderAtNaturalRelaxation.Reconstruct().BuildBoneChains()[0].Joints
                .First(static joint => joint.Name == "coattail_1_L");

            using (Assert.Multiple())
            {
                await Assert.That(coattail.Suspender).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(coattail.ExtraIterations).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A twist link a static root authored carries no relaxation either way, while the simulated child's link
        /// carries 0.5 * 0.618 = 0.309.
        /// </summary>
        [Test]
        public async Task ARelaxlessTwistLinkIsTheStaticEndsOwnTwist()
        {
            var rootAuthored = TwistPair(0f, 0f).Reconstruct();
            var childAuthored = TwistPair(0f, 0.309f).Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(rootAuthored.HasRelaxlessTwistLink(0)).IsTrue();
                await Assert.That(childAuthored.HasRelaxlessTwistLink(0)).IsFalse();
                await Assert.That(childAuthored.GetAuthoredTwistRelax(1, 0, -1)).IsEqualTo(0.5f).Within(1e-4f);
            }
        }

        /// <summary>
        /// A joint whose extrusion has no <c>m_SimdRodsAnim</c> entry reads a zero <c>stretch_spring</c>, not an
        /// animated length.
        /// </summary>
        [Test]
        public async Task AJointWithNoAnimatedRodDeclaresNoStretchInstead()
        {
            var control = (AnimatedEveryJoint with { NodeBases = InnerBases }).Reconstruct().BuildBoneChains()[0].Joints;
            var rodless = (AnimatedEveryJoint with { NodeBases = InnerBases, SimdRodsAnim = null }).Reconstruct().BuildBoneChains()[0].Joints;

            using (Assert.Multiple())
            {
                await Assert.That(control.First(static joint => joint.Name == "coattail_1_L").AnimatedLength).IsTrue();
                await Assert.That(rodless.First(static joint => joint.Name == "coattail_1_L").AnimatedLength).IsFalse();
                await Assert.That(rodless.First(static joint => joint.Name == "coattail_1_L").StretchStiffness)
                    .IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A source spring's rod copies are its <c>extra_iterations</c> and leave no surplus rod, and the spring keeps
        /// its source element's corner order.
        /// </summary>
        [Test]
        public async Task ASourceSpringsRodCopiesAreItsExtraIterations()
        {
            var repeated = SpringCopies(3);
            var single = SpringCopies(1);

            using (Assert.Multiple())
            {
                await Assert.That(repeated.GetAuthoredSourceSprings(repeated.BuildBoneChains())[0])
                    .IsEqualTo((2, 1, 3));
                await Assert.That(repeated.GetUngeneratedRods(repeated.BuildBoneChains())).IsEmpty();
                await Assert.That(single.GetAuthoredSourceSprings(single.BuildBoneChains())[0])
                    .IsEqualTo((2, 1, 1));
            }
        }

        private static ClothReconstruction SpringCopies(int copies) => (StraightChain(3) with
        {
            SourceElems = [0, 1, 0, 0, 2, 1],
            Rods = [RigidRod(0, 1, 10f), .. Enumerable.Repeat(RigidRod(1, 2, 10f, 0.5f), copies)],
        }).Reconstruct();

        /// <summary>
        /// A <c>motion_bias</c> under an animated parent is read off the <c>m_SimdRodsAnim</c> weights of its span: 2/3
        /// reads 0.5.
        /// </summary>
        [Test]
        public async Task AMotionBiasIsReadOffTheAnimatedRodsOfItsSpan()
        {
            var biased = AnimatedJointTwo(childWeight: 0.666667f).Reconstruct();
            var unbiased = AnimatedJointTwo().Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(biased.GetMotionBias(TipOf(biased)) ?? float.NaN).IsEqualTo(0.5f).Within(1e-3f);
                await Assert.That(unbiased.GetMotionBias(TipOf(unbiased)).HasValue).IsFalse();
            }
        }

        private static BoneChainJoint TipOf(ClothReconstruction cloth)
            => cloth.BuildBoneChains()[0].Joints.First(static joint => joint.Name == "coattail_end_L");

        /// <summary>
        /// On a root pair holding one bend rod and one suspender companion, the suspender is the rod away from the bend
        /// reading in either order.
        /// </summary>
        [Test]
        public async Task ASuspenderBesideASingleBendRodIsTheRodAwayFromTheBendReading()
        {
            static BoneChainJoint Tip(bool companionFirst)
            {
                var bend = RigidRod(0, 2, 20f);
                var companion = RigidRod(0, 2, 20f, 0.21f);
                var cloth = (StraightChain(3) with
                {
                    Rods = [RigidRod(0, 1, 10f, 1f), RigidRod(1, 2, 10f, 1f), companionFirst ? companion : bend, companionFirst ? bend : companion],
                }).Reconstruct();
                return cloth.BuildBoneChains()[0].Joints.Find(static joint => joint.Name == "j2")!;
            }

            var bendFirst = Tip(companionFirst: false);
            var suspenderFirst = Tip(companionFirst: true);

            using (Assert.Multiple())
            {
                await Assert.That(bendFirst.BendSpring).IsTrue();
                await Assert.That(bendFirst.BendStiffness).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(bendFirst.Suspender).IsEqualTo(0.21f).Within(1e-4f);
                await Assert.That(bendFirst.ExtraIterations).IsEqualTo(0);
                await Assert.That(suspenderFirst.Suspender).IsEqualTo(0.21f).Within(1e-4f);
                await Assert.That(suspenderFirst.ExtraIterations).IsEqualTo(0);
            }
        }

        /// <summary>
        /// Where both root-pair rods differ from the chain's own relaxation, the higher is read as the bend and the
        /// lower as the suspender, in either record order.
        /// </summary>
        [Test]
        public async Task ASuspenderAndABendRodOffTheChainRateAreSplitByValue()
        {
            static BoneChainJoint Tip(bool companionFirst)
            {
                var bend = RigidRod(0, 2, 20f);
                var companion = RigidRod(0, 2, 20f, 0.2f);
                var cloth = (StraightChain(3) with
                {
                    Rods = [RigidRod(0, 1, 10f, 0.9f), RigidRod(1, 2, 10f, 0.9f), companionFirst ? companion : bend, companionFirst ? bend : companion],
                }).Reconstruct();
                return cloth.BuildBoneChains()[0].Joints.Find(static joint => joint.Name == "j2")!;
            }

            var bendFirst = Tip(companionFirst: false);
            var suspenderFirst = Tip(companionFirst: true);

            using (Assert.Multiple())
            {
                await Assert.That(bendFirst.BendSpring).IsTrue();
                await Assert.That(bendFirst.BendStiffness).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(bendFirst.Suspender).IsEqualTo(0.2f).Within(1e-4f);
                await Assert.That(suspenderFirst.BendStiffness).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(suspenderFirst.Suspender).IsEqualTo(0.2f).Within(1e-4f);
            }
        }

        /// <summary>
        /// The sibling spring is the relaxation every child pair shares, so an extra rod on one pair does not refute
        /// it; pairs sharing no value or two values state none.
        /// </summary>
        [Test]
        public async Task AForeignRodOnOnePairDoesNotRefuteTheSiblingSpring()
        {
            FeRodConstraint[] spokes = [RigidRod(0, 1, 3f), RigidRod(0, 2, 3f), RigidRod(0, 3, 3f)];
            var shared = SiblingChain([.. spokes, RigidRod(1, 2, 3f, 0.5f), RigidRod(1, 2, 3f, 0.9f), RigidRod(1, 3, 3f, 0.5f), RigidRod(2, 3, 3f, 0.5f)]);

            var disjoint = SiblingChain([.. spokes, RigidRod(1, 2, 3f, 0.9f), RigidRod(1, 3, 3f, 0.5f), RigidRod(2, 3, 3f, 0.5f)]);

            var ambiguous = SiblingChain([.. spokes, RigidRod(1, 2, 3f, 0.5f), RigidRod(1, 2, 3f, 0.9f), RigidRod(1, 3, 3f, 0.5f),
                RigidRod(1, 3, 3f, 0.9f), RigidRod(2, 3, 3f, 0.5f), RigidRod(2, 3, 3f, 0.9f)]);

            using (Assert.Multiple())
            {
                await Assert.That(shared!.ChildSiblingSpring).IsEqualTo(0.5f);
                await Assert.That(disjoint!.ChildSiblingSpring).IsEqualTo(0f);
                await Assert.That(ambiguous!.ChildSiblingSpring).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A chain root reads <c>extra_iterations</c> from its children's sibling rod copies; single copies read 0, and
        /// one extra copy on one pair does not lift the reading.
        /// </summary>
        [Test]
        public async Task ARootReadsItsIterationsFromItsChildrensSiblingRods()
        {
            static ClothReconstruction Fan(int copies, int extraOnOnePair) => new FeModelBuilder
            {
                Names = ["root", "c1", "c2", "c3"],
                StaticNodes = 1,
                Parents = [-1, 0, 0, 0],
                Positions = [new(0f, 0f, 0f), new(-10f, 0f, -10f), new(0f, 0f, -10f), new(10f, 0f, -10f)],
                Rods =
                [
                    RigidRod(0, 1, 14.142136f),
                    RigidRod(0, 2, 10f),
                    RigidRod(0, 3, 14.142136f),
                    .. Enumerable.Repeat(RigidRod(1, 2, 10f, 0.5f), copies),
                    .. Enumerable.Repeat(RigidRod(1, 3, 20f, 0.5f), copies + extraOnOnePair),
                    .. Enumerable.Repeat(RigidRod(2, 3, 10f, 0.5f), copies),
                ],
            }.Reconstruct();

            static int Iterations(ClothReconstruction cloth)
                => cloth.BuildBoneChains()[0].Joints.Find(static joint => joint.Name == "root")!.ExtraIterations;

            using (Assert.Multiple())
            {
                await Assert.That(Iterations(Fan(3, 0))).IsEqualTo(2);
                await Assert.That(Iterations(Fan(1, 0))).IsEqualTo(0);
                await Assert.That(Iterations(Fan(3, 1))).IsEqualTo(2);
            }
        }

        /// <summary>
        /// A joint whose upward span carries no rod reads <c>extra_iterations</c> off its children's sibling rods; a
        /// rod on the span, disagreeing span counts, or a single child do not read the siblings.
        /// </summary>
        [Test]
        public async Task AJointWhoseSpanCarriesNoRodStillReadsItsSiblingRods()
        {
            static ClothReconstruction Rodless(int siblings, bool twoChildren = true) => new FeModelBuilder
            {
                Names = ["root", "p", "j", "c1", "c2", "sibling_of_j"],
                StaticNodes = 3,
                Parents = [-1, 0, 1, 2, 2, 1],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(-10f, 0f, -30f), new(10f, 0f, -30f), new(30f, 0f, -20f)],
                Rods =
                [
                    RigidRod(1, 5, 30f),
                    RigidRod(1, 3, 22.36068f),
                    RigidRod(2, 3, 14.142136f),
                    .. twoChildren ? [RigidRod(2, 4, 14.142136f), .. Enumerable.Repeat(RigidRod(3, 4, 20f, 0.5f), siblings)] : Array.Empty<FeRodConstraint>(),
                ],
            }.Reconstruct();

            static ClothReconstruction Spanned(int spanRods, int bendRods, int siblings) => new FeModelBuilder
            {
                Names = ["root", "p1", "p2", "j", "c1", "c2"],
                StaticNodes = 1,
                Parents = [-1, 0, 1, 2, 3, 3],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(-10f, 0f, -40f), new(10f, 0f, -40f)],
                Rods =
                [
                    RigidRod(0, 1, 10f),
                    RigidRod(1, 2, 10f),
                    .. Enumerable.Repeat(RigidRod(2, 3, 10f), spanRods),
                    .. Enumerable.Repeat(RigidRod(1, 3, 20f), bendRods),
                    RigidRod(3, 4, 14.142136f),
                    RigidRod(3, 5, 14.142136f),
                    .. Enumerable.Repeat(RigidRod(4, 5, 20f, 0.5f), siblings),
                ],
            }.Reconstruct();

            static int Iterations(ClothReconstruction cloth)
            {
                foreach (var chain in cloth.BuildBoneChains())
                {
                    if (chain.Joints.Find(static joint => joint.Name == "j") is { } joint)
                    {
                        return joint.ExtraIterations;
                    }
                }

                return -1;
            }

            using (Assert.Multiple())
            {
                await Assert.That(Iterations(Rodless(2))).IsEqualTo(1);
                await Assert.That(Iterations(Rodless(4))).IsEqualTo(3);
                await Assert.That(Iterations(Rodless(7))).IsEqualTo(6);

                await Assert.That(Iterations(Spanned(1, 0, 4))).IsEqualTo(0);

                await Assert.That(Iterations(Spanned(2, 3, 5))).IsEqualTo(1);

                await Assert.That(Iterations(Rodless(4, twoChildren: false))).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A spring over a pair the chain also spans keeps the joint's stretch and declares only the surplus copy; a
        /// spring on a pair with its own rod alone zeroes the span.
        /// </summary>
        [Test]
        public async Task ASpringOverAChainSpanAddsARodRatherThanReplacingIt()
        {
            var doubled = SpringOverASpan(secondRodOnThePair: true);
            var single = SpringOverASpan(secondRodOnThePair: false);

            var doubledChain = doubled.BuildBoneChains()[0];
            var singleChain = single.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(string.Join(",", doubledChain.Joints.Select(static joint => joint.Name)))
                    .IsEqualTo("j0,j1,j2,j3");
                await Assert.That(doubled.Index.SourceSprings.Length).IsEqualTo(1);

                await Assert.That(doubledChain.Joints[2].StretchStiffness).IsNotEqualTo(0f);
                await Assert.That(doubled.GetAuthoredSourceSprings([doubledChain])
                    .Select(static spring => spring.Copies).Sum()).IsEqualTo(1);

                await Assert.That(singleChain.Joints[2].StretchStiffness).IsEqualTo(0f);
                await Assert.That(single.GetAuthoredSourceSprings([singleChain])
                    .Select(static spring => spring.Copies).Sum()).IsEqualTo(1);
            }
        }

        /// <summary>
        /// Four chain joints with one-node rings, rods between consecutive extrusions and a two-corner source element on
        /// (j1, j2); <paramref name="secondRodOnThePair"/> doubles that pair's rod.
        /// </summary>
        private static ClothReconstruction SpringOverASpan(bool secondRodOnThePair) => new FeModelBuilder
        {
            Names = ["j0", "$ccj0_0", "j1", "$ccj1_0", "j2", "$ccj2_0", "j3", "$ccj3_0"],
            StaticNodes = 2,
            Parents = [-1, 0, 0, 2, 2, 4, 4, 6],
            Positions = [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f), new(0f, 0f, -16f), new(0f, 2f, -16f),
                new(0f, 0f, -24f), new(0f, 2f, -24f)],
            CtrlOffsets = [Offset(0, 1, 0f, 2f, 0f), Offset(2, 3, 0f, 2f, 0f), Offset(4, 5, 0f, 2f, 0f), Offset(6, 7, 0f, 2f, 0f)],
            SourceElems = [0, 1, 0, 0, 2, 4],
            Rods =
            [
                RigidRod(0, 2, 8f),
                RigidRod(0, 3, 8.246211f),
                RigidRod(1, 2, 8.246211f),
                RigidRod(1, 3, 8f),
                RigidRod(2, 3, 2f),
                RigidRod(2, 4, 8f),
                RigidRod(2, 5, 8.246211f),
                RigidRod(3, 4, 8.246211f),
                RigidRod(3, 5, 8f),
                RigidRod(4, 5, 2f),
                RigidRod(4, 6, 8f),
                RigidRod(4, 7, 8.246211f),
                RigidRod(5, 6, 8.246211f),
                RigidRod(5, 7, 8f),
                RigidRod(6, 7, 2f),
                .. secondRodOnThePair ? [RigidRod(2, 4, 8f)] : Array.Empty<FeRodConstraint>(),
            ],
        }.Reconstruct();

        /// <summary>
        /// A fold-weighted rod between chain joints reads as <c>add_stiffness_rods</c> and is no surplus; the
        /// even-split rod, or a fan-weighted rod on an unfolded pair, is not the switch's.
        /// </summary>
        [Test]
        public async Task ASurfaceFoldBetweenChainJointsIsTheSwitchsAndNoSurplus()
        {
            var folded = FoldedChainModel(0.666667f, fanPair: true);
            var declared = FoldedChainModel(0.5f, fanPair: true);
            var elsewhere = FoldedChainModel(0.666667f, fanPair: false);

            static bool Holds(List<FeModelIndex.Rod> rods, int a, int b)
                => rods.Exists(rod => (rod.NodeA == a && rod.NodeB == b) || (rod.NodeA == b && rod.NodeB == a));

            using (Assert.Multiple())
            {
                await Assert.That(declared.HasChainStiffnessRods(declared.BuildBoneChains())).IsFalse();
                await Assert.That(Holds(declared.GetUngeneratedRods(declared.BuildBoneChains(), true), 1, 3)).IsTrue();
                await Assert.That(elsewhere.HasChainStiffnessRods(elsewhere.BuildBoneChains())).IsFalse();

                await Assert.That(folded.HasChainStiffnessRods(folded.BuildBoneChains())).IsTrue();
                await Assert.That(Holds(folded.GetUngeneratedRods(folded.BuildBoneChains(), true), 1, 3)).IsFalse();
            }
        }

        /// <summary>
        /// A static root over joints a, b and c, two triangles folding across root-b, and a banded rod a-c at <paramref
        /// name="weight"/>; without <paramref name="fanPair"/> nothing folds onto a-c.
        /// </summary>
        private static ClothReconstruction FoldedChainModel(float weight, bool fanPair) => new FeModelBuilder
        {
            Names = ["root", "a", "b", "c"],
            StaticNodes = 1,
            Parents = [-1, 0, 0, 2],
            InvMasses = [0f, 1f, 1f, 0.5f],
            Positions = [new(0f, 0f, 0f), new(-1f, 0f, -2f), new(0f, 0f, -2f), new(1f, 0f, -4f)],
            SourceElems = fanPair ? [0, 0, 2, 0, 0, 1, 2, 0, 2, 3] : [0, 0, 2, 0, 0, 1, 2, 0, 3, 1],
            Rods = [RigidRod(0, 1, 2.236068f), RigidRod(0, 2, 2f), RigidRod(2, 3, 2.236068f), Rod(1, 3, 1.5f, 2.828427f, weight)],
        }.Reconstruct();

        /// <summary>
        /// A centre-only end effector's bend and torsion spans run from the joint's own node, reading 0.6 and 0.8; its
        /// stretch reads 0.6.
        /// </summary>
        [Test]
        public async Task ACentreOnlyEndEffectorsSpansRunFromTheJointsOwnNode()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["root", "j1", "j2", "tip", "$cctip_Ctr"],
                StaticNodes = 1,
                Parents = [-1, 0, 1, 2, 3],
                Positions = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f), new(40f, 0f, 0f)],
                Rods =
                [
                    RigidRod(0, 1, 10f, 0.6f),
                    RigidRod(1, 2, 10f, 0.6f),
                    RigidRod(2, 3, 10f, 0.6f),
                    RigidRod(1, 3, 20f, 0.6f),
                    RigidRod(0, 3, 30f, 0.8f),
                    RigidRod(3, 4, 10f, 0.6f),
                    RigidRod(2, 4, 20f, 0.6f),
                    RigidRod(1, 4, 30f, 0.8f),
                ],
            }.Reconstruct();

            var tip = cloth.BuildBoneChains().SelectMany(chain => chain.Joints).FirstOrDefault(joint => joint.Name == "tip");

            using (Assert.Multiple())
            {
                await Assert.That(tip?.BendStiffness ?? -1f).IsEqualTo(0.6f).Within(1e-4f);
                await Assert.That(tip?.TorsionStiffness ?? -1f).IsEqualTo(0.8f).Within(1e-4f);

                await Assert.That(tip?.StretchStiffness ?? -1f).IsEqualTo(0.6f).Within(1e-4f);
            }
        }

        /// <summary>
        /// A joint whose grandparent span is only a fold-weighted rod declares no bend spring; the even-split rod is
        /// its bend spring.
        /// </summary>
        [Test]
        public async Task ABendSpanThatIsOnlyASurfaceFoldIsNoBendSpring()
        {
            static bool Bends(float weight) => new FeModelBuilder
            {
                Names = ["L0", "R0", "L1", "R1", "L2", "R2", "L3", "R3"],
                StaticNodes = 2,
                Parents = [-1, -1, 0, 1, 2, 3, 4, 5],
                InvMasses = [0f, 0f, 0.02f, 0.02f, 0.015f, 0.015f, 0.01f, 0.01f],
                Positions = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, -10f), new(0f, 0f, -20f), new(10f, 0f, -20f),
                    new(0f, 0f, -30f), new(10f, 0f, -30f)],
                Quads = [Quad(4, 2, 3, 5), Quad(4, 6, 7, 5)],
                Rods =
                [
                    RigidRod(0, 2, 10f, 1f, 0f),
                    RigidRod(1, 3, 10f, 1f, 0f),
                    RigidRod(2, 4, 10f),
                    RigidRod(3, 5, 10f),
                    RigidRod(4, 6, 10f),
                    RigidRod(5, 7, 10f),
                    Rod(2, 6, 12f, 20f, weight),
                    Rod(3, 7, 12f, 20f, weight),
                ],
            }.Reconstruct().BuildBoneChains().SelectMany(static chain => chain.Joints).Where(static joint => joint.Name == "L3")
                .Select(static joint => joint.BendSpring).DefaultIfEmpty(false).First();

            using (Assert.Multiple())
            {
                await Assert.That(Bends(0.5f)).IsTrue();

                await Assert.That(Bends(0.666667f)).IsFalse();
            }
        }

        /// <summary>
        /// A two-wide joint whose parent cross links carry two records against one on its own ring states no extra
        /// iteration, since the repeat would copy the own ring rod too; with its own ring rod doubled as well it states one.
        /// </summary>
        [Test]
        public async Task CrossLinksOutnumberingTheOwnRingRodStateNoExtraIteration()
        {
            using (Assert.Multiple())
            {
                await Assert.That(RingJoint(ownCopies: 1).ExtraIterations).IsEqualTo(0);
                await Assert.That(RingJoint(ownCopies: 2).ExtraIterations).IsEqualTo(1);
            }
        }

        /// <summary>
        /// A static root and a simulated joint, each with a two-node ring 10 apart, 20 apart along -Z: the joint's ring rod
        /// written <paramref name="ownCopies"/> times and each of its four cross links to the root ring twice.
        /// </summary>
        private static BoneChainJoint RingJoint(int ownCopies)
        {
            var rods = new List<FeRodConstraint> { RigidRod(1, 2, 10f) };
            for (var copy = 0; copy < ownCopies; copy++)
            {
                rods.Add(RigidRod(4, 5, 10f));
            }

            foreach (var (a, b, length) in (ReadOnlySpan<(int, int, float)>)[(1, 4, 20f), (2, 5, 20f), (1, 5, 22.36068f), (2, 4, 22.36068f)])
            {
                rods.Add(RigidRod(a, b, length));
                rods.Add(RigidRod(a, b, length));
            }

            return new FeModelBuilder
            {
                Names = ["root", "$ccroot_0", "$ccroot_1", "j1", "$ccj1_0", "$ccj1_1"],
                StaticNodes = 3,
                Parents = [-1, 0, 0, 0, 3, 3],
                Positions = [new(0f, 0f, 0f), new(5f, 0f, 0f), new(-5f, 0f, 0f), new(0f, 0f, -20f), new(5f, 0f, -20f), new(-5f, 0f, -20f)],
                Rods = [.. rods],
            }.Reconstruct().BuildBoneChains()[0].Joints.Find(static joint => joint.Name == "j1")!;
        }

        /// <summary>
        /// A chain that lists one joint twice still matches its rods, by the joint's first entry.
        /// </summary>
        [Test]
        public async Task AChainListingAJointTwiceStillMatchesItsRods()
        {
            var cloth = StretchlessChain(RigidRod(0, 1, 10f)).Reconstruct();
            var chain = new BoneChain { RootBone = "j0" };
            chain.Joints.Add(new BoneChainJoint { Node = 0, Name = "j0", ParentNode = -1 });
            chain.Joints.Add(new BoneChainJoint { Node = 1, Name = "j1", ParentNode = 0, StretchStiffness = 1f });
            chain.Joints.Add(new BoneChainJoint { Node = 1, Name = "j1", ParentNode = 0, StretchStiffness = 1f });

            await Assert.That(cloth.GetUngeneratedRods([chain])).IsEmpty();
        }

        /// <summary>
        /// A suspender's root rods are scaled by <c>exp(-default_stretch)</c> like every other chain rod, so the reading
        /// divides it back out: 0.5 * exp(-0.5) = 0.30327 reads as 0.5 and regenerates the compiled rods.
        /// </summary>
        [Test]
        public async Task ASuspenderUnderDefaultStretchReadsTheAuthoredValue()
        {
            var cloth = (StraightChain(4) with
            {
                DefaultSurfaceStretch = 0.5f,
                DefaultThreadStretch = 0.5f,
                Rods =
                [
                    RigidRod(0, 1, 10f, 0.60653066f),
                    RigidRod(0, 1, 10f, 0.30326533f),
                    RigidRod(1, 2, 10f, 0.60653066f),
                    RigidRod(2, 3, 10f, 0.60653066f),
                    RigidRod(0, 2, 20f, 0.30326533f),
                    RigidRod(0, 3, 30f, 0.30326533f),
                ],
            }).Reconstruct();
            var chains = cloth.BuildBoneChains();
            var joint = chains[0].Joints[1];

            using (Assert.Multiple())
            {
                await Assert.That(joint.Suspender).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(joint.ExtraIterations).IsEqualTo(0);
                await Assert.That(cloth.GetUngeneratedRods(chains)).IsEmpty();
            }
        }
    }
}
