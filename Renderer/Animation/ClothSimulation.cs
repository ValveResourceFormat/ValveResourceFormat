using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Simulates a model's cloth from its <see cref="FeModel"/> and poses the cloth bones from the result.
    /// </summary>
    /// <remarks>
    /// Position based Verlet on the cloth nodes, in world space. Each step predicts the dynamic nodes from their
    /// velocity and gravity, relaxes the constraints, pulls the nodes toward their animated goals and pushes them out of
    /// the model's capsules, spheres and planes, then builds a transform per control from the node positions. Static
    /// nodes follow the animated bones, position driven nodes take their animated position.
    /// Not simulated yet: box and SDF colliders, springs, prisms, stray boxes, effects and world collision.
    /// </remarks>
    public sealed class ClothSimulation
    {
        // Goal attraction and damping are fractions per step, so the cloth steps on a fixed tick, not per frame
        private const float TickInterval = 1f / 60f;
        private const float MaxFrameTime = 0.1f;

        // Moves longer than this per step bring the cloth along instead of letting it trail
        private const float TeleportDistanceSquared = 100f * 100f;
        private const float TeleportBlendRange = 3.333333e-05f;

        private const uint AnimationAttractionFlags = 0x600;
        private const uint GoalAttractionFlag = 0x80;
        private const uint PointDampingFlag = 0x100;
        private const uint CollideAfterIterationsFlag = 0x2000;

        private const float OneDegree = MathF.PI / 180f;

        private readonly FeModel model;
        private readonly int nodeCount;
        private readonly int staticNodes;
        private readonly int rotLockStaticNodes;
        private readonly int firstPositionDrivenNode;

        private Vector3[] pos0;
        private Vector3[] pos1;
        private readonly Vector3[] goals;

        // Node positions at the frame time, carried forward from the last tick, that the bones are posed from
        private readonly Vector3[] outputPositions;
        private float tickTime;
        private readonly Matrix4x4[] anim;
        private readonly Matrix4x4[] sim;
        private readonly Matrix4x4[] restRelativeToParent;

        // Bone of each control, or -1 for controls the skeleton has no bone for
        private readonly int[] ctrlBone;

        // Bones the cloth poses take their animated target from their bind pose under the animated parent
        private readonly Skeleton skeleton;
        private readonly Matrix4x4[] boneAnim;
        private readonly Matrix4x4 clothRootInverseBind;
        private readonly int[] ctrlSkelParent;
        private readonly int[] derivedCtrlOrder;
        private readonly int[] outputBones;
        private readonly bool[] boneSimulated;

        private readonly Rod[] rods;
        private readonly AnimRod[] animRods;
        private readonly Twist[] twists;
        private readonly float[] nodeGravity;
        private readonly float[] nodeDamping;
        private readonly float[] nodeGoalStrength;
        private readonly float[] nodeGoalDamping;
        private readonly float[] nodeInvMasses;
        private readonly float[] nodeCollisionRadii;
        private readonly int[] nodeCollisionMasks;
        private readonly float[] nodeFriction;

        private readonly (FeModel.FeRigidColliderIndices Begin, FeModel.FeRigidColliderIndices End)[] colliderGroups;
        private readonly Matrix4x4[] previousCapsuleFrames;
        private readonly Matrix4x4[] previousSphereFrames;
        private bool hasPreviousRigidFrames;

        // Dynamic nodes the goal attraction pulls, all of them when null
        private readonly bool[]? goalAttracted;

        private Vector3 simOrigin;
        private bool initialized;

        private readonly record struct Rod(int Node0, int Node1, float MinDist, float MaxDist, float Weight0, float RelaxationFactor);

        private readonly record struct AnimRod(int Node0, int Node1, float Weight0, float RelaxationFactor);

        private readonly record struct Twist(int NodeOrient, int NodeEnd, Vector3 LocalRestDirection, Quaternion EndToOrient,
            float TwistRelax, float SwingRelax);

        /// <summary>Gets the number of constraint iterations per step, one plus the model's extra iterations.</summary>
        public int Iterations => Math.Min(model.ExtraIterations + 1, 256);

        /// <summary>Gets or sets the multiplier on each node's gravity.</summary>
        public float GravityScale { get; set; }

        /// <summary>Gets or sets the world height of the ground plane the ground colliding nodes stay above, or null for none.</summary>
        public float? GroundHeight { get; set; }

        /// <summary>Gets the simulated node positions in world space.</summary>
        public ReadOnlySpan<Vector3> NodePositions => pos1;

        /// <summary>Gets the cloth model being simulated.</summary>
        public FeModel Model => model;

        /// <summary>Gets whether the simulation poses the bone at <paramref name="boneIndex"/>.</summary>
        public bool IsBoneSimulated(int boneIndex) => boneSimulated[boneIndex];

        /// <summary>
        /// Prepares the simulation of <paramref name="feModel"/> on a model with <paramref name="skeleton"/>.
        /// </summary>
        public ClothSimulation(FeModel feModel, Skeleton skeleton)
        {
            model = feModel;
            nodeCount = feModel.NodeCount;
            staticNodes = feModel.StaticNodes;
            rotLockStaticNodes = feModel.RotLockStaticNodes;
            firstPositionDrivenNode = feModel.FirstPositionDrivenNode is { } firstDriven and > 0 ? Math.Min(firstDriven, nodeCount) : nodeCount;
            GravityScale = feModel.DefaultGravityScale is { } scale and not 0f ? scale : 1f;

            pos0 = new Vector3[nodeCount];
            pos1 = new Vector3[nodeCount];
            goals = new Vector3[nodeCount];
            outputPositions = new Vector3[nodeCount];

            var ctrlCount = feModel.CtrlName.Length;
            anim = new Matrix4x4[ctrlCount];
            sim = new Matrix4x4[ctrlCount];
            ctrlBone = new int[ctrlCount];

            this.skeleton = skeleton;
            boneAnim = new Matrix4x4[skeleton.Bones.Length];
            clothRootInverseBind = skeleton.ClothSimulationRoot is { } clothRoot && Matrix4x4.Invert(ModelSpaceBindPose(clothRoot), out var inverse)
                ? inverse
                : Matrix4x4.Identity;

            var outputs = new List<int>();

            for (var ctrl = 0; ctrl < ctrlCount; ctrl++)
            {
                var bone = skeleton.GetBoneIndex(feModel.CtrlName[ctrl]);
                ctrlBone[ctrl] = bone;

                // Bones driven by animation keep it, the cloth only poses its own procedural bones
                if (bone >= 0 && skeleton.Bones[bone].IsProceduralCloth && ctrl >= rotLockStaticNodes && ctrl < nodeCount)
                {
                    outputs.Add(ctrl);
                }
            }

            outputBones = [.. outputs];
            boneSimulated = new bool[skeleton.Bones.Length];

            foreach (var ctrl in outputBones)
            {
                boneSimulated[ctrlBone[ctrl]] = true;
            }

            ctrlSkelParent = new int[ctrlCount];
            restRelativeToParent = new Matrix4x4[ctrlCount];
            var initPose = feModel.InitPose;
            var skelParents = feModel.SkelParents;

            for (var ctrl = 0; ctrl < ctrlCount; ctrl++)
            {
                var parent = ctrl < skelParents.Length ? skelParents[ctrl] : -1;
                ctrlSkelParent[ctrl] = parent >= 0 && parent < ctrlCount && parent != ctrl ? parent : -1;

                var rest = ctrl < initPose.Length ? ToMatrix(initPose[ctrl]) : Matrix4x4.Identity;

                if (ctrlSkelParent[ctrl] >= 0 && ctrlSkelParent[ctrl] < initPose.Length)
                {
                    Matrix4x4.Invert(ToMatrix(initPose[ctrlSkelParent[ctrl]]), out var parentInverse);
                    rest *= parentInverse;
                }

                restRelativeToParent[ctrl] = rest;
            }

            derivedCtrlOrder = SortDerivedCtrls();

            rods = ReadRods(feModel);
            animRods = ReadAnimRods(feModel);
            twists = ReadTwists(feModel);

            var integrators = feModel.NodeIntegrator;
            nodeGravity = new float[nodeCount];
            nodeDamping = new float[nodeCount];
            nodeGoalStrength = new float[nodeCount];
            nodeGoalDamping = new float[nodeCount];

            for (var node = 0; node < nodeCount && node < integrators.Length; node++)
            {
                nodeDamping[node] = integrators[node].PointDamping;
                nodeGoalStrength[node] = integrators[node].AnimationForceAttraction;
                nodeGoalDamping[node] = integrators[node].AnimationVertexAttraction;
            }

            for (var node = 0; node < nodeCount; node++)
            {
                // Without integrators every node falls at the default 360 units/s^2
                nodeGravity[node] = node < integrators.Length ? integrators[node].Gravity : 360f;
            }

            nodeInvMasses = new float[nodeCount];
            feModel.NodeInvMasses.AsSpan(0, Math.Min(nodeCount, feModel.NodeInvMasses.Length)).CopyTo(nodeInvMasses);

            var dynamicNodes = nodeCount - staticNodes;
            nodeCollisionRadii = new float[dynamicNodes];
            nodeCollisionMasks = new int[dynamicNodes];
            nodeFriction = new float[dynamicNodes];

            for (var dynNode = 0; dynNode < dynamicNodes; dynNode++)
            {
                nodeCollisionRadii[dynNode] = dynNode < feModel.NodeCollisionRadii.Length ? feModel.NodeCollisionRadii[dynNode] : 0f;
                nodeCollisionMasks[dynNode] = dynNode < feModel.TreeCollisionMasks.Length ? feModel.TreeCollisionMasks[dynNode] : 0xFFFF;
                nodeFriction[dynNode] = dynNode < feModel.DynNodeFriction.Length ? feModel.DynNodeFriction[dynNode] : 0f;
            }

            colliderGroups = ReadColliderGroups(feModel);
            previousCapsuleFrames = new Matrix4x4[feModel.TaperedCapsuleRigids.Length];
            previousSphereFrames = new Matrix4x4[feModel.SphereRigids.Length];

            // A bit per dynamic node
            var goalMask = feModel.GoalDampedSpringIntegrators;

            if (goalMask.Length > 0)
            {
                goalAttracted = new bool[dynamicNodes];

                for (var dynNode = 0; dynNode < dynamicNodes && (dynNode >> 5) < goalMask.Length; dynNode++)
                {
                    goalAttracted[dynNode] = (goalMask[dynNode >> 5] & (1u << (dynNode & 31))) != 0;
                }
            }
        }

        /// <summary>
        /// Advances the cloth by <paramref name="timeStep"/> seconds with <paramref name="pose"/> as the animated
        /// pose, then writes the simulated bones back into it.
        /// </summary>
        /// <param name="pose">The model space bone matrices, updated in place.</param>
        /// <param name="transform">The model's world transform.</param>
        /// <param name="timeStep">Elapsed time in seconds.</param>
        public void Update(Matrix4x4[] pose, Matrix4x4 transform, float timeStep)
        {
            var modelScale = transform.MaxAxisScale();
            if (modelScale <= 0f)
            {
                return;
            }

            SetAnimatedTransforms(pose, transform, modelScale);

            if (!initialized)
            {
                ResetToAnimation();
                simOrigin = transform.Translation;
                initialized = true;
            }

            SetAbsOrigin(transform.Translation);

            tickTime += MathF.Min(MathF.Max(timeStep, 0f), MaxFrameTime);
            // The epsilon keeps frames of exactly one tick from rounding down to none
            var ticks = (int)((tickTime / TickInterval) + 1e-3f);
            tickTime = MathF.Max(tickTime - (ticks * TickInterval), 0f);

            for (var i = 0; i < ticks; i++)
            {
                MoveGoals(ticks - i);
                Step(TickInterval, modelScale);
            }

            ComputeOutputPositions(tickTime / TickInterval);
            ComputeSimTransforms(modelScale);
            WriteBones(pose, transform, modelScale);
        }

        // Static and position driven nodes are where the animation is now, simulated nodes move on by their last
        // tick's velocity for the time since that tick
        private void ComputeOutputPositions(float tickFraction)
        {
            for (var node = 0; node < nodeCount; node++)
            {
                outputPositions[node] = node >= staticNodes && node < firstPositionDrivenNode
                    ? pos1[node] + ((pos1[node] - pos0[node]) * tickFraction)
                    : anim[node].Translation;
            }
        }

        /// <summary>Snaps every node back onto the animated pose and clears the velocities.</summary>
        public void Reset()
        {
            tickTime = 0f;
            initialized = false;
            hasPreviousRigidFrames = false;
        }

        private void SetAnimatedTransforms(Matrix4x4[] pose, Matrix4x4 transform, float modelScale)
        {
            ComputeBoneAnimation(pose);

            var unscale = Matrix4x4.CreateScale(1f / modelScale);
            var unscaledWorld = unscale * transform;

            for (var ctrl = 0; ctrl < anim.Length; ctrl++)
            {
                if (ctrlBone[ctrl] >= 0)
                {
                    anim[ctrl] = unscale * boneAnim[ctrlBone[ctrl]] * transform;
                }
            }

            // Controls without a bone follow their skeleton parent at the rest offset between them

            foreach (var ctrl in derivedCtrlOrder)
            {
                var parent = ctrlSkelParent[ctrl];
                anim[ctrl] = parent >= 0
                    ? restRelativeToParent[ctrl] * anim[parent]
                    : restRelativeToParent[ctrl] * unscaledWorld;
            }

            ApplyCtrlOffsets(anim, modelScale, keepSimulated: false);
        }

        // Offset controls take their parent's rotation at a fixed offset from it, soft offset controls keep alpha of
        // their own position and move the rest of the way to the offset from their parent. On the animated pose this
        // places the goals of simulated nodes, so on the simulated pose those must keep what the cloth computed.
        private void ApplyCtrlOffsets(Matrix4x4[] transforms, float modelScale, bool keepSimulated)
        {
            foreach (var offset in model.CtrlOffsets)
            {
                if (keepSimulated && IsSimulated(offset.CtrlChild))
                {
                    continue;
                }

                var parent = transforms[offset.CtrlParent];
                transforms[offset.CtrlChild] = WithTranslation(parent, Vector3.Transform(offset.Offset * modelScale, parent));
            }

            foreach (var offset in model.CtrlSoftOffsets)
            {
                if (keepSimulated && IsSimulated(offset.CtrlChild))
                {
                    continue;
                }

                var target = Vector3.Transform(offset.Offset * modelScale, transforms[offset.CtrlParent]);
                ref var child = ref transforms[offset.CtrlChild];
                child.Translation = (child.Translation * offset.Alpha) + (target * (1f - offset.Alpha));
            }
        }

        // The pose of the procedural cloth bones is the cloth's output, so their animated target is rebuilt from
        // the bind pose: under their parent, or pinned to the cloth root for root bones
        private void ComputeBoneAnimation(Matrix4x4[] pose)
        {
            var clothRootDelta = skeleton.ClothSimulationRoot is { } clothRoot
                ? clothRootInverseBind * pose[clothRoot.Index]
                : Matrix4x4.Identity;

            foreach (var bone in skeleton.Bones)
            {
                if (!bone.IsProceduralCloth)
                {
                    boneAnim[bone.Index] = pose[bone.Index];
                }
                else if (bone.Parent is { } parent)
                {
                    boneAnim[bone.Index] = bone.BindPose * boneAnim[parent.Index];
                }
                else
                {
                    boneAnim[bone.Index] = bone.BindPose * clothRootDelta;
                }
            }
        }

        private static Matrix4x4 ModelSpaceBindPose(Bone bone)
            => bone.Parent is { } parent ? bone.BindPose * ModelSpaceBindPose(parent) : bone.BindPose;

        private void ResetToAnimation()
        {
            for (var node = 0; node < nodeCount; node++)
            {
                pos0[node] = pos1[node] = goals[node] = anim[node].Translation;
                sim[node] = anim[node];
            }
        }

        // Moving the model drags its nodes along by the model's local force: a local force of 1 keeps the cloth in
        // world space, except for moves long enough to count as teleports.
        private void SetAbsOrigin(Vector3 origin)
        {
            if (origin == simOrigin)
            {
                return;
            }

            var delta = origin - simOrigin;
            simOrigin = origin;

            var teleport = (delta.LengthSquared() - TeleportDistanceSquared) * TeleportBlendRange;
            var localForces = model.LocalForces;

            if (localForces.Length > 0)
            {
                for (var node = 0; node < staticNodes; node++)
                {
                    pos0[node] += delta;
                    pos1[node] += delta;
                }

                for (var node = staticNodes; node < nodeCount; node++)
                {
                    var fraction = MathUtils.Clamp(teleport, 1f - localForces[node - staticNodes], 1f);
                    pos0[node] += delta * fraction;
                    pos1[node] += delta * fraction;
                }

                return;
            }

            var shared = model.LocalForce <= 0f ? 1f : MathUtils.Clamp(teleport, 1f - model.LocalForce, 1f);

            if (shared == 0f)
            {
                return;
            }

            for (var node = 0; node < nodeCount; node++)
            {
                pos0[node] += delta * shared;
                pos1[node] += delta * shared;
            }
        }

        // Within a frame the goals move to the new animated positions in equal parts per step
        private void MoveGoals(int remainingSteps)
        {
            var fraction = 1f / remainingSteps;

            for (var node = 0; node < nodeCount; node++)
            {
                goals[node] = Vector3.Lerp(goals[node], anim[node].Translation, fraction);
            }
        }

        private void Step(float timeStep, float modelScale)
        {
            Predict(timeStep);
            AddAnimationAttraction(timeStep);

            // Models built to collide late collide once after the last iteration, so the constraints cannot pull
            // nodes back into the colliders afterwards
            var collideLast = (model.DynamicNodeFlags & CollideAfterIterationsFlag) != 0;
            var iterations = Iterations;
            var firstGoalIteration = iterations - model.ExtraGoalIterations - 1;

            if (!collideLast)
            {
                Collide(modelScale);
            }

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                RelaxKelagerBends();
                RelaxHingeLimits();
                RelaxRods(modelScale);
                RelaxAnimRods();
                RelaxAnimStrayRadii(modelScale);
                RelaxQuads(modelScale);
                RelaxTris(modelScale);

                if (iteration >= firstGoalIteration)
                {
                    AddGoalAttraction();
                }

                if (collideLast && iteration == iterations - 1)
                {
                    Collide(modelScale);
                }
            }

            LockToGoal();
        }

        private void Predict(float timeStep)
        {
            var gravityStep = -GravityScale * timeStep * timeStep;
            var damping = (model.DynamicNodeFlags & PointDampingFlag) != 0;

            // pos0 receives the prediction, then the buffers swap: pos1 is predicted, pos0 is the start of the step.
            // Position driven nodes are not integrated and keep the position they had a step before.
            for (var node = 0; node < staticNodes; node++)
            {
                pos0[node] = goals[node];
            }

            for (var node = staticNodes; node < firstPositionDrivenNode; node++)
            {
                var velocity = pos1[node] - pos0[node];

                if (damping)
                {
                    velocity *= MathF.Max(1f - (nodeDamping[node] * timeStep), 0f);
                }

                pos0[node] = pos1[node] + velocity + new Vector3(0f, 0f, nodeGravity[node] * gravityStep);
            }

            (pos0, pos1) = (pos1, pos0);
        }

        // Pulls the predicted dynamic nodes toward their goals, in part without changing their velocity
        private void AddAnimationAttraction(float timeStep)
        {
            if ((model.DynamicNodeFlags & AnimationAttractionFlags) == 0 || model.NodeIntegrator.Length == 0)
            {
                return;
            }

            for (var node = staticNodes; node < nodeCount; node++)
            {
                if (goalAttracted?[node - staticNodes] == true)
                {
                    continue;
                }

                var delta = goals[node] - pos1[node];
                var positionAttraction = MathF.Min(nodeGoalDamping[node] * timeStep, 1f);
                var velocityAttraction = 2f * nodeGoalStrength[node] * timeStep;

                pos0[node] += delta * (positionAttraction * (1f - positionAttraction));
                pos1[node] += delta * (positionAttraction + velocityAttraction);
            }
        }

        // Each dynamic node moves its strength of the way to its goal and loses its damping of its velocity, then
        // position driven nodes take their goals
        private void AddGoalAttraction()
        {
            if ((model.DynamicNodeFlags & GoalAttractionFlag) == 0)
            {
                return;
            }

            for (var node = staticNodes; node < firstPositionDrivenNode; node++)
            {
                if (goalAttracted?[node - staticNodes] == false)
                {
                    continue;
                }

                var strength = Math.Clamp(nodeGoalStrength[node], 0f, 1f);
                var damping = Math.Clamp(nodeGoalDamping[node], 0f, 1f);
                ref var position = ref pos1[node];

                if (strength > 0.9999f)
                {
                    position = pos0[node] = goals[node];
                    continue;
                }

                if (strength >= 1.1920929e-07f)
                {
                    position = Vector3.Lerp(position, goals[node], strength);
                }

                pos0[node] = position - ((position - pos0[node]) * (1f - damping));
            }

            for (var node = firstPositionDrivenNode; node < nodeCount; node++)
            {
                pos1[node] = goals[node];
            }
        }

        private void LockToGoal()
        {
            foreach (var node in model.LockToGoal)
            {
                pos1[node] = goals[node];
            }
        }

        // Keeps the middle node of a bend within its rest height of the triangle's centroid
        private void RelaxKelagerBends()
        {
            foreach (var bend in model.KelagerBends)
            {
                var nodes = bend.Nodes;
                ref var p0 = ref pos1[nodes[0]];
                ref var p1 = ref pos1[nodes[1]];
                ref var p2 = ref pos1[nodes[2]];

                var offset = p0 - ((p0 + p1 + p2) * (1f / 3f));
                var height = offset.Length();

                if (height <= bend.Height0)
                {
                    continue;
                }

                offset *= 1f - (bend.Height0 / height);
                p0 += offset * bend.Weights[0];
                p1 += offset * bend.Weights[1];
                p2 += offset * bend.Weights[2];
            }
        }

        private void RelaxRods(float scale)
        {
            foreach (var rod in rods)
            {
                var delta = pos1[rod.Node1] - pos1[rod.Node0];
                var length = MathF.Sqrt(MathF.Max(delta.LengthSquared(), 9.313226e-10f));
                var target = MathF.Min(MathF.Max(length, rod.MinDist * scale), rod.MaxDist * scale);
                var correction = delta * (((target / length) - 1f) * rod.RelaxationFactor);

                pos1[rod.Node0] -= correction * rod.Weight0;
                pos1[rod.Node1] += correction * (1f - rod.Weight0);
            }
        }

        // Rods whose length follows the animated distance between their nodes
        private void RelaxAnimRods()
        {
            foreach (var rod in animRods)
            {
                var target = Vector3.Distance(anim[rod.Node1].Translation, anim[rod.Node0].Translation);
                var delta = pos1[rod.Node1] - pos1[rod.Node0];
                var length = MathF.Sqrt(MathF.Max(delta.LengthSquared(), 9.313226e-10f));
                var correction = delta * (((target / length) - 1f) * rod.RelaxationFactor);

                pos1[rod.Node0] -= correction * rod.Weight0;
                pos1[rod.Node1] += correction * (1f - rod.Weight0);
            }
        }

        // A node may stray at most MaxDist from the goal of the first node
        private void RelaxAnimStrayRadii(float scale)
        {
            foreach (var stray in model.AnimStrayRadii)
            {
                var anchor = goals[stray.Nodes[0]];
                var node = stray.Nodes[1];
                var offset = pos1[node] - anchor;
                var distance = MathF.Sqrt(MathF.Max(offset.LengthSquared(), 9.313226e-10f));
                var correction = (MathF.Min(distance, stray.MaxDist * scale) / distance) - 1f;

                pos1[node] += offset * (correction * stray.RelaxationFactor);
            }
        }

        // Keeps the dihedral angle of nodes 2 and 3 about the edge from node 0 to node 1 within the hinge's extents.
        // Nodes 2 and 3 may sit between two nodes each, by weight 4 and 5.
        private void RelaxHingeLimits()
        {
            Span<Vector3> points = stackalloc Vector3[4];
            Span<float> invMasses = stackalloc float[4];

            foreach (var hinge in model.HingeLimits)
            {
                var nodes = hinge.Nodes;

                for (var i = 0; i < 4; i++)
                {
                    points[i] = pos1[nodes[i]];
                    invMasses[i] = nodeInvMasses[nodes[i]];
                }

                if (hinge.Weight4 > 0f)
                {
                    points[2] = Vector3.Lerp(points[2], pos1[nodes[4]], hinge.Weight4);
                }

                if (hinge.Weight5 > 0f)
                {
                    points[3] = Vector3.Lerp(points[3], pos1[nodes[5]], hinge.Weight5);
                }

                var frame = new HingeFrame(points, hinge.AngleCenter);
                var angle = frame.Angle;

                if (hinge.AngleExtents + OneDegree >= MathF.Abs(angle))
                {
                    continue;
                }

                invMasses[2] *= 1f - hinge.Weight4;
                invMasses[3] *= 1f - hinge.Weight5;

                var target = MathF.CopySign(hinge.AngleExtents, angle);
                var corrections = (Node2: Vector3.Zero, Node3: Vector3.Zero);
                var solved = false;

                // A degenerate hinge at any pass drops the whole correction
                for (var iteration = 0; iteration < 5; iteration++)
                {
                    var delta = target - angle;
                    solved = frame.IsValid && frame.Rotate(points, invMasses, Math.Clamp(delta, -MathF.PI / 4f, MathF.PI / 4f), out corrections);

                    if (!solved || MathF.Abs(delta) < 4f * OneDegree)
                    {
                        break;
                    }

                    frame = new HingeFrame(points, hinge.AngleCenter);
                    angle = frame.Angle;

                    if (MathF.Abs(angle - target) <= OneDegree)
                    {
                        break;
                    }
                }

                if (!solved)
                {
                    continue;
                }

                if (invMasses[0] > 0f)
                {
                    pos1[nodes[0]] = points[0];
                }

                if (invMasses[1] > 0f)
                {
                    pos1[nodes[1]] = points[1];
                }

                WriteHingeNode(nodes[2], nodes[4], hinge.Weight4, invMasses[2], points[2], corrections.Node2);
                WriteHingeNode(nodes[3], nodes[5], hinge.Weight5, invMasses[3], points[3], corrections.Node3);
            }
        }

        // A node blended with a second one takes the last correction split between the two
        private void WriteHingeNode(int node, int blendNode, float blend, float invMass, Vector3 point, Vector3 lastCorrection)
        {
            if (invMass <= 0f)
            {
                return;
            }

            if (blend <= 0f)
            {
                pos1[node] = point;
                return;
            }

            pos1[node] += lastCorrection * (blend / (1f - blend));
            pos1[blendNode] += lastCorrection * ((1f - blend) / blend);
        }

        // The hinge edge and the directions of nodes 2 and 3 perpendicular to it
        private readonly struct HingeFrame
        {
            private readonly Vector3 axis;
            private readonly Vector3 dirA;
            private readonly Vector3 dirB;
            private readonly float edgeLength;
            private readonly float alongA;
            private readonly float alongB;
            private readonly float lengthSquaredA;
            private readonly float lengthSquaredB;
            private readonly float invLengthA;
            private readonly float invLengthB;

            public float Angle { get; }

            public bool IsValid => lengthSquaredB >= 1e-4f && lengthSquaredA >= 1e-4f && edgeLength >= 0.01f;

            public HingeFrame(ReadOnlySpan<Vector3> points, float center)
            {
                var edge = points[1] - points[0];
                var edgeLengthSquared = edge.LengthSquared();

                if (edgeLengthSquared > 1e-6f)
                {
                    edgeLength = MathF.Sqrt(edgeLengthSquared);
                    axis = edge / edgeLength;
                }

                (dirA, alongA, lengthSquaredA, invLengthA) = Perpendicular(points[2] - points[0], axis);
                (dirB, alongB, lengthSquaredB, invLengthB) = Perpendicular(points[3] - points[0], axis);

                var angle = MathF.Atan2(Vector3.Dot(dirB, Vector3.Cross(axis, dirA)), Vector3.Dot(dirB, dirA)) - center;

                if (angle > MathF.PI)
                {
                    angle -= MathF.Tau;
                }
                else if (angle < -MathF.PI)
                {
                    angle += MathF.Tau;
                }

                Angle = angle;
            }

            private static (Vector3 Direction, float Along, float LengthSquared, float InvLength) Perpendicular(Vector3 offset, Vector3 axis)
            {
                var along = Vector3.Dot(axis, offset);
                var perpendicular = offset - (axis * along);
                var lengthSquared = perpendicular.LengthSquared();

                if (lengthSquared <= 1e-6f)
                {
                    return (Vector3.Zero, along, lengthSquared, 0f);
                }

                var invLength = 1f / MathF.Sqrt(lengthSquared);
                return (perpendicular * invLength, along, lengthSquared, invLength);
            }

            // Turns node 3 about the edge by angle relative to node 2, sharing the move by inverse mass
            public bool Rotate(Span<Vector3> points, ReadOnlySpan<float> invMasses, float angle, out (Vector3 Node2, Vector3 Node3) corrections)
            {
                var gradient2 = -Vector3.Cross(axis, dirA) * invLengthA;
                var gradient3 = Vector3.Cross(axis, dirB) * invLengthB;
                var gradient1 = -((gradient2 * (alongA / edgeLength)) + (gradient3 * (alongB / edgeLength)));
                var gradient0 = -(gradient1 + gradient2 + gradient3);

                var denominator = (invMasses[0] * gradient0.LengthSquared()) + (invMasses[1] * gradient1.LengthSquared())
                    + (invMasses[2] * gradient2.LengthSquared()) + (invMasses[3] * gradient3.LengthSquared());

                if (MathF.Abs(denominator) < 1.1920929e-07f)
                {
                    corrections = default;
                    return false;
                }

                var lambda = angle / denominator;
                corrections = (gradient2 * (invMasses[2] * lambda), gradient3 * (invMasses[3] * lambda));

                points[0] += gradient0 * (invMasses[0] * lambda);
                points[1] += gradient1 * (invMasses[1] * lambda);
                points[2] += corrections.Node2;
                points[3] += corrections.Node3;
                return true;
            }
        }

        // Quads fit their rest shape by rotation: about the static edge with two static nodes, about the static node
        // with one, about the weighted centre with none
        private void RelaxQuads(float scale)
        {
            var quads = model.Quads;
            var quad = 0;

            for (; quad < model.QuadCount2 && quad < quads.Length; quad++)
            {
                RelaxQuad2(quads[quad], scale);
            }

            for (; quad < model.QuadCount1 && quad < quads.Length; quad++)
            {
                RelaxQuadRotation(quads[quad], scale, fixedOrigin: true);
            }

            for (; quad < quads.Length; quad++)
            {
                RelaxQuadRotation(quads[quad], scale, fixedOrigin: false);
            }
        }

        private void RelaxQuad2(FeModel.FeQuad quad, float scale)
        {
            var nodes = quad.Nodes;
            var shape = quad.Shapes;
            ref var p0 = ref pos1[nodes[0]];
            ref var p1 = ref pos1[nodes[1]];
            ref var p2 = ref pos1[nodes[2]];
            ref var p3 = ref pos1[nodes[3]];

            var center = (p0 + p1) * 0.5f;
            var (axisX, axisY, axisZ) = QuadBasis(p1 - p0, p2 + p3 - (2f * p0));
            var x2 = p2 - center;
            var x3 = p3 - center;

            // TODO: The rotation sign is inferred, the fit turns the rest shape toward the current pose
            var cos = (shape[2].W * ((Vector3.Dot(x2, axisZ) * shape[2].Z) + (Vector3.Dot(x2, axisY) * shape[2].Y)))
                + (shape[3].W * ((Vector3.Dot(x3, axisZ) * shape[3].Z) + (Vector3.Dot(x3, axisY) * shape[3].Y)));
            var sin = (shape[2].W * ((Vector3.Dot(x2, axisZ) * shape[2].Y) - (Vector3.Dot(x2, axisY) * shape[2].Z)))
                + (shape[3].W * ((Vector3.Dot(x3, axisZ) * shape[3].Y) - (Vector3.Dot(x3, axisY) * shape[3].Z)));
            (sin, cos) = NormalizeRotation(sin, cos);

            p2 = center + (QuadLocalToWorld(axisX, axisY, axisZ, RotateYZ(shape[2], sin, cos)) * scale);
            p3 = center + (QuadLocalToWorld(axisX, axisY, axisZ, RotateYZ(shape[3], sin, cos)) * scale);
        }

        private static Vector3 RotateYZ(Vector4 shape, float sin, float cos)
            => new(shape.X, (shape.Y * cos) - (shape.Z * sin), (shape.Z * cos) + (shape.Y * sin));

        // Linearised rotation fit of the rest shape onto the current nodes, about node 0 or the weighted centre
        private void RelaxQuadRotation(FeModel.FeQuad quad, float scale, bool fixedOrigin)
        {
            var nodes = quad.Nodes;
            var shape = quad.Shapes;
            var first = fixedOrigin ? 1 : 0;

            var origin = pos1[nodes[0]];

            if (!fixedOrigin)
            {
                origin = (pos1[nodes[0]] * shape[0].W) + (pos1[nodes[1]] * shape[1].W) + (pos1[nodes[2]] * shape[2].W) + (pos1[nodes[3]] * shape[3].W);
            }

            var (axisX, axisY, axisZ) = QuadBasis(pos1[nodes[2]] - pos1[nodes[0]], pos1[nodes[3]] - pos1[nodes[1]]);

            Span<Vector3> current = stackalloc Vector3[4];
            Span<Vector3> rest = stackalloc Vector3[4];
            var rhs = Vector3.Zero;
            float a00 = 0f, a10 = 0f, a11 = 0f, a20 = 0f, a21 = 0f, a22 = 0f;

            for (var i = first; i < 4; i++)
            {
                var mass = shape[i].W;
                var x = current[i] = pos1[nodes[i]] - origin;
                rest[i] = QuadLocalToWorld(axisX, axisY, axisZ, new Vector3(shape[i].X, shape[i].Y, shape[i].Z) * scale);

                rhs -= Vector3.Cross(x, rest[i]) * mass;
                a00 += mass * ((x.Y * x.Y) + (x.Z * x.Z));
                a11 += mass * ((x.X * x.X) + (x.Z * x.Z));
                a22 += mass * ((x.X * x.X) + (x.Y * x.Y));
                a10 -= mass * x.X * x.Y;
                a20 -= mass * x.X * x.Z;
                a21 -= mass * x.Y * x.Z;
            }

            var omega = SolveCholesky(a00, a10, a11, a20, a21, a22, rhs);

            for (var i = first; i < 4; i++)
            {
                pos1[nodes[i]] = origin + rest[i] + Vector3.Cross(omega, rest[i]);
            }
        }

        private static (Vector3 X, Vector3 Y, Vector3 Z) QuadBasis(Vector3 a, Vector3 b)
        {
            var axisX = MathUtils.SafeNormalize(a, Vector3.UnitX);
            var axisY = MathUtils.SafeNormalize(b - (axisX * Vector3.Dot(axisX, b)), Vector3.UnitY);
            return (axisX, axisY, Vector3.Cross(axisX, axisY));
        }

        private static Vector3 QuadLocalToWorld(Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 local)
            => (axisX * local.X) + (axisY * local.Y) + (axisZ * local.Z);

        private static (float Sin, float Cos) NormalizeRotation(float sin, float cos)
        {
            var length = MathF.Sqrt((sin * sin) + (cos * cos));
            return length > 1.1920929e-07f ? (sin / length, cos / length) : (0f, 1f);
        }

        // Solves the symmetric 3x3 system by Cholesky decomposition
        private static Vector3 SolveCholesky(float a00, float a10, float a11, float a20, float a21, float a22, Vector3 rhs)
        {
            var l00 = MathF.Sqrt(a00);
            var l10 = a10 / l00;
            var l20 = a20 / l00;
            var l11 = MathF.Sqrt(a11 - (l10 * l10));
            var l21 = (a21 - (l20 * l10)) / l11;
            var l22 = MathF.Sqrt(a22 - (l20 * l20) - (l21 * l21));

            var y0 = rhs.X / l00;
            var y1 = (rhs.Y - (l10 * y0)) / l11;
            var y2 = (rhs.Z - (l20 * y0) - (l21 * y1)) / l22;

            var z = y2 / l22;
            var y = (y1 - (l21 * z)) / l11;
            var x = (y0 - (l10 * y) - (l20 * z)) / l00;

            var result = new Vector3(x, y, z);
            return float.IsFinite(result.X) && float.IsFinite(result.Y) && float.IsFinite(result.Z) ? result : Vector3.Zero;
        }

        // Triangles fit their rest shape by rotation in their plane: node 2 alone with two static nodes, about the
        // static node with one, about the weighted centroid with none
        private void RelaxTris(float scale)
        {
            var tris = model.Tris;
            var tri = 0;

            for (; tri < model.TriCount2 && tri < tris.Length; tri++)
            {
                RelaxTri(tris[tri], scale, staticCount: 2);
            }

            for (; tri < model.TriCount1 && tri < tris.Length; tri++)
            {
                RelaxTri(tris[tri], scale, staticCount: 1);
            }

            for (; tri < tris.Length; tri++)
            {
                RelaxTri(tris[tri], scale, staticCount: 0);
            }
        }

        private void RelaxTri(FeModel.FeTri tri, float scale, int staticCount)
        {
            ref var p0 = ref pos1[tri.Nodes[0]];
            ref var p1 = ref pos1[tri.Nodes[1]];
            ref var p2 = ref pos1[tri.Nodes[2]];

            var edge = p1 - p0;
            var length1 = edge.Length();
            var axisX = edge / length1;
            var offset2 = p2 - p0;
            var along2 = Vector3.Dot(axisX, offset2);
            var perpendicular = offset2 - (axisX * along2);
            var height2 = perpendicular.Length();
            var axisY = perpendicular / height2;

            // A collapsed triangle has no basis to fit in
            if (!(length1 > 0f && height2 > 0f))
            {
                return;
            }

            var restX1 = tri.V1x * scale;
            var rest2 = tri.V2 * scale;
            var current2 = new Vector2(along2, height2);

            if (staticCount == 2)
            {
                var localX = ((length1 - restX1) * 0.5f) + rest2.X;
                p2 = p0 + (axisX * localX) + (axisY * rest2.Y);
                return;
            }

            float sin, cos;

            if (staticCount == 1)
            {
                cos = (Vector2.Dot(rest2, current2) * tri.W2) + (tri.W1 * restX1 * length1);
                sin = Cross2D(rest2, current2) * tri.W2;
                (sin, cos) = NormalizeRotation(sin, cos);
            }
            else
            {
                var w0 = 1f - (tri.W1 + tri.W2);
                var currentCenter = new Vector2((tri.W1 * length1) + (tri.W2 * current2.X), tri.W2 * current2.Y);
                var restCenter = new Vector2((tri.W1 * restX1) + (tri.W2 * rest2.X), tri.W2 * rest2.Y);
                var current1 = new Vector2(length1 - currentCenter.X, -currentCenter.Y);
                var rest1 = new Vector2(restX1 - restCenter.X, -restCenter.Y);
                var currentRel2 = current2 - currentCenter;
                var restRel2 = rest2 - restCenter;

                cos = (w0 * Vector2.Dot(restCenter, currentCenter)) + (tri.W1 * Vector2.Dot(rest1, current1)) + (tri.W2 * Vector2.Dot(restRel2, currentRel2));
                sin = (w0 * Cross2D(restCenter, currentCenter)) + (tri.W1 * Cross2D(rest1, current1)) + (tri.W2 * Cross2D(restRel2, currentRel2));
                (sin, cos) = NormalizeRotation(sin, cos);

                var move0 = currentCenter - Rotate2D(restCenter, sin, cos);
                p0 += (axisX * move0.X) + (axisY * move0.Y);
            }

            p1 = p0 + (axisX * (restX1 * cos)) + (axisY * (restX1 * sin));
            var rotated2 = Rotate2D(rest2, sin, cos);
            p2 = p0 + (axisX * rotated2.X) + (axisY * rotated2.Y);
        }

        private static float Cross2D(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

        private static Vector2 Rotate2D(Vector2 v, float sin, float cos) => new((cos * v.X) - (sin * v.Y), (sin * v.X) + (cos * v.Y));

        // Rigid colliders collide by priority group, the last group first so the first wins, and within a group
        // capsules and spheres in reverse order, then the planes
        private void Collide(float scale)
        {
            var friction = model.DynNodeFriction.Length > 0;

            foreach (var (begin, end) in colliderGroups)
            {
                for (var capsule = end.TaperedCapsuleRigidIndex - 1; capsule >= begin.TaperedCapsuleRigidIndex; capsule--)
                {
                    CollideWithCapsule(capsule, scale, friction);
                }

                for (var sphere = end.SphereRigidIndex - 1; sphere >= begin.SphereRigidIndex; sphere--)
                {
                    CollideWithSphereRigid(sphere, scale, friction);
                }

                for (var plane = begin.CollisionPlaneIndex; plane < end.CollisionPlaneIndex; plane++)
                {
                    CollideWithPlane(model.CollisionPlanes[plane]);
                }
            }

            hasPreviousRigidFrames = true;
            CollideWithGround();
        }

        // Groups of [begin, end) collider indices in collision order
        private static (FeModel.FeRigidColliderIndices Begin, FeModel.FeRigidColliderIndices End)[] ReadColliderGroups(FeModel feModel)
        {
            var priorities = feModel.RigidColliderPriorities;

            if (priorities.Length == 0)
            {
                var all = new FeModel.FeRigidColliderIndices(feModel.TaperedCapsuleRigids.Length, feModel.SphereRigids.Length,
                    feModel.BoxRigids.Length, 0, feModel.CollisionPlanes.Length, null);
                return [(default, all)];
            }

            var groups = new (FeModel.FeRigidColliderIndices, FeModel.FeRigidColliderIndices)[priorities.Length - 1];

            for (var group = priorities.Length - 1; group >= 1; group--)
            {
                groups[priorities.Length - 1 - group] = (priorities[group - 1], priorities[group]);
            }

            return groups;
        }

        // The rigid's frame now, and its motion since the last collision for friction to carry nodes along with
        private Matrix4x4 RigidFrame(Matrix4x4[] previousFrames, int rigid, int ctrl, out Matrix4x4 motion)
        {
            var frame = anim[ctrl];
            motion = hasPreviousRigidFrames && Matrix4x4.Invert(previousFrames[rigid], out var previousInverse)
                ? previousInverse * frame
                : Matrix4x4.Identity;
            previousFrames[rigid] = frame;
            return frame;
        }

        private void CollideWithSphereRigid(int index, float scale, bool friction)
        {
            var rigid = model.SphereRigids[index];
            var frame = RigidFrame(previousSphereFrames, index, rigid.Node, out var motion);

            if (rigid.Sphere is not { } shape)
            {
                return;
            }

            var center = Vector3.Transform(shape.AsVector3() * scale, frame);
            var radius = MathF.Abs(shape.W * scale);

            for (var dynNode = 0; dynNode < nodeCollisionMasks.Length; dynNode++)
            {
                if ((nodeCollisionMasks[dynNode] & rigid.CollisionMask) != 0)
                {
                    PushOutOfSphere(dynNode, center, radius, friction, motion);
                }
            }
        }

        private void CollideWithCapsule(int index, float scale, bool friction)
        {
            var rigid = model.TaperedCapsuleRigids[index];
            var frame = RigidFrame(previousCapsuleFrames, index, rigid.Node, out var motion);

            if (rigid.Spheres.Length < 2)
            {
                return;
            }

            var center0 = Vector3.Transform(rigid.Spheres[0].AsVector3() * scale, frame);
            var center1 = Vector3.Transform(rigid.Spheres[1].AsVector3() * scale, frame);
            var radius0 = MathF.Abs(rigid.Spheres[0].W * scale);
            var radius1 = MathF.Abs(rigid.Spheres[1].W * scale);

            var axis = center1 - center0;
            var length = axis.Length();

            if (length > 0f)
            {
                axis /= length;
            }

            // A capsule barely longer than the difference of its radii collides as its second sphere
            var asSphere = length - (radius1 - radius0) <= 0.5f;
            var slope = length > 0f ? (radius1 - radius0) / length : 0f;

            for (var dynNode = 0; dynNode < nodeCollisionMasks.Length; dynNode++)
            {
                if ((nodeCollisionMasks[dynNode] & rigid.CollisionMask) == 0)
                {
                    continue;
                }

                if (asSphere)
                {
                    PushOutOfSphere(dynNode, center1, radius1, friction, motion);
                    continue;
                }

                // The sphere along the axis whose surface is nearest, found through the cone's slope
                var offset = pos1[staticNodes + dynNode] - center0;
                var axial = Vector3.Dot(offset, axis);
                var projection = ((offset - (axis * axial)).Length() * slope) + axial;

                if (projection <= 0f)
                {
                    PushOutOfSphere(dynNode, center0, radius0, friction, motion);
                }
                else if (projection > length)
                {
                    PushOutOfSphere(dynNode, center1, radius1, friction, motion);
                }
                else
                {
                    var blend = projection / length;
                    PushOutOfSphere(dynNode, Vector3.Lerp(center0, center1, blend), float.Lerp(radius0, radius1, blend), friction, motion);
                }
            }
        }

        // Pushes a dynamic node out to the sphere grown by the node's radius. With friction the node also slides
        // toward where its start of step position moved with the collider, by at most its friction times the depth.
        private void PushOutOfSphere(int dynNode, Vector3 center, float radius, bool friction, Matrix4x4 motion)
        {
            var node = staticNodes + dynNode;
            ref var position = ref pos1[node];
            radius += nodeCollisionRadii[dynNode];

            var offset = position - center;
            var distanceSquared = offset.LengthSquared();

            if (radius * radius <= distanceSquared)
            {
                return;
            }

            if (!friction)
            {
                position = distanceSquared > 1.1920929e-07f
                    ? center + (offset * (radius / MathF.Sqrt(distanceSquared)))
                    : center + new Vector3(0f, 0f, radius);
                return;
            }

            Vector3 normal;
            float depth;

            if (distanceSquared < 0.01f)
            {
                normal = Vector3.UnitZ;
                depth = radius;
            }
            else
            {
                var distance = MathF.Sqrt(distanceSquared);
                normal = offset / distance;
                depth = radius - distance;
            }

            var push = normal * depth;
            var maxFriction = nodeFriction[dynNode] * depth;

            if (maxFriction > 0f)
            {
                var slide = Vector3.Transform(pos0[node], motion) - position;
                slide -= normal * Vector3.Dot(slide, normal);
                var slideLength = slide.Length();

                push += slideLength > maxFriction ? slide * (maxFriction / slideLength) : slide;
            }

            position += push;
        }

        private void CollideWithPlane(FeModel.FeCollisionPlane plane)
        {
            var tm = anim[plane.CtrlParent];
            var normal = Vector3.Normalize(Vector3.TransformNormal(plane.Plane.Normal, tm));
            ref var position = ref pos1[plane.ChildNode];
            var distance = Vector3.Dot(position - tm.Translation, normal) - plane.Plane.Offset;

            if (distance < 0f)
            {
                position -= normal * (distance * plane.Strength);
            }
        }

        private void CollideWithGround()
        {
            if (GroundHeight is not { } ground)
            {
                return;
            }

            var nodes = model.WorldCollisionNodes;

            foreach (var parameters in model.WorldCollisionParams)
            {
                for (var i = parameters.ListBegin; i < parameters.ListEnd; i++)
                {
                    var node = nodes[i];
                    ref var position = ref pos1[node];

                    if (position.Z >= ground)
                    {
                        continue;
                    }

                    position.Z = ground;

                    // Friction pulls the previous position toward the current one
                    var goZ = parameters.WorldFriction;
                    var goXY = (1f - parameters.GroundFriction) * goZ;
                    ref var previous = ref pos0[node];
                    previous.X = float.Lerp(position.X, previous.X, goXY);
                    previous.Y = float.Lerp(position.Y, previous.Y, goXY);
                    previous.Z = float.Lerp(position.Z, previous.Z, goZ);
                }
            }
        }

        private void ComputeSimTransforms(float scale)
        {
            for (var ctrl = 0; ctrl < rotLockStaticNodes; ctrl++)
            {
                sim[ctrl] = anim[ctrl];
            }

            // Static nodes, and dynamic nodes nothing below orients, keep their animated rotation
            for (var node = rotLockStaticNodes; node < nodeCount; node++)
            {
                sim[node] = WithTranslation(anim[node], outputPositions[node]);
            }

            // Controls past the nodes only move by offsets from other controls
            for (var ctrl = nodeCount; ctrl < sim.Length; ctrl++)
            {
                sim[ctrl] = anim[ctrl];
            }

            foreach (var basis in model.NodeBases)
            {
                var axisY = MathUtils.SafeNormalize(outputPositions[basis.NodeY1] - outputPositions[basis.NodeY0], -Vector3.UnitZ);
                var axisX = outputPositions[basis.NodeX1] - outputPositions[basis.NodeX0];
                axisX -= axisY * Vector3.Dot(axisY, axisX);

                var length = axisX.Length();
                axisX = length > 0.05f ? axisX / length : PerpendicularTo(axisY);

                var frame = FromAxes(axisX, axisY, Vector3.Cross(axisX, axisY), outputPositions[basis.Node]);

                sim[basis.Node] = basis.Adjust.W == 1f
                    ? frame
                    : Matrix4x4.CreateFromQuaternion(basis.Adjust) * frame;
            }

            ComputeRopeTransforms();

            var freeNodes = model.FreeNodes;
            for (var i = freeNodes.Length - 1; i >= 0; i--)
            {
                var node = freeNodes[i];
                sim[node] = WithTranslation(anim[node], outputPositions[node]);
            }

            foreach (var twist in twists)
            {
                ApplyTwist(twist);
            }

            var reverseOffsets = model.ReverseOffsets;
            for (var i = reverseOffsets.Length - 1; i >= 0; i--)
            {
                var offset = reverseOffsets[i];
                ref var bone = ref sim[offset.BoneCtrl];
                bone.Translation = outputPositions[offset.TargetNode] - Vector3.TransformNormal(offset.Offset * scale, bone);
            }

            ComputeFitTransforms(scale);

            foreach (var node in model.LockToGoal)
            {
                sim[node].Translation = outputPositions[node];
            }

            foreach (var lockToParent in model.LockToParent)
            {
                var parent = sim[lockToParent.CtrlParent];
                sim[lockToParent.CtrlChild].Translation = Vector3.Transform(lockToParent.Offset * scale, parent);
            }

            ApplyCtrlOffsets(sim, scale, keepSimulated: true);
        }

        private bool IsSimulated(int ctrl) => ctrl >= staticNodes && ctrl < firstPositionDrivenNode;

        // Turns the orient node so its rest direction to the end node points at the end node, and takes the twist
        // about that direction from the end node's rotation, each by its relax factor
        private void ApplyTwist(Twist twist)
        {
            var animRotation = Quaternion.CreateFromRotationMatrix(anim[twist.NodeOrient]);
            var current = Quaternion.CreateFromRotationMatrix(sim[twist.NodeOrient]);
            Quaternion full;

            if (twist.NodeOrient == twist.NodeEnd)
            {
                full = animRotation;
            }
            else
            {
                var target = Quaternion.CreateFromRotationMatrix(sim[twist.NodeEnd]) * twist.EndToOrient;
                var direction = MathUtils.SafeNormalize(outputPositions[twist.NodeEnd] - outputPositions[twist.NodeOrient]);
                var localDirection = Vector3.Transform(direction, Quaternion.Conjugate(target));
                full = target * RotationBetween(twist.LocalRestDirection, localDirection);
            }

            Quaternion result;

            if (twist.TwistRelax + twist.SwingRelax >= 2f)
            {
                result = full;
            }
            else
            {
                if (Quaternion.Dot(full, current) < 0f)
                {
                    full = -full;
                }

                if (twist.TwistRelax == twist.SwingRelax)
                {
                    result = (current * (1f - twist.TwistRelax)) + (full * twist.TwistRelax);
                }
                else
                {
                    var delta = Quaternion.Conjugate(current) * full;
                    var deltaAxis = new Vector3(delta.X, delta.Y, delta.Z);
                    var restAxis = twist.LocalRestDirection;
                    var twistPart = new Quaternion(restAxis * Vector3.Dot(restAxis, deltaAxis), delta.W);
                    var relaxed = NormalizeOrIdentity((Quaternion.Identity * (1f - twist.TwistRelax)) + (twistPart * twist.TwistRelax));

                    if (twist.SwingRelax != 0f)
                    {
                        var swingPart = delta * Quaternion.Conjugate(twistPart);
                        relaxed = NormalizeOrIdentity((Quaternion.Identity * (1f - twist.SwingRelax)) + (swingPart * twist.SwingRelax)) * relaxed;
                    }

                    result = current * relaxed;
                }
            }

            result = result.LengthSquared() > 1.5378700e-05f ? Quaternion.Normalize(result) : animRotation;
            sim[twist.NodeOrient] = WithTranslation(Matrix4x4.CreateFromQuaternion(result), outputPositions[twist.NodeOrient]);
        }

        private static Quaternion NormalizeOrIdentity(Quaternion q)
            => q.LengthSquared() > 1.5378700e-05f ? Quaternion.Normalize(q) : Quaternion.Identity;

        // Rotation from a to b through their half vector, unnormalised
        private static Quaternion RotationBetween(Vector3 from, Vector3 to)
        {
            var half = (from + to) * 0.5f;

            if (half.LengthSquared() > 1.1920929e-07f)
            {
                return new Quaternion(Vector3.Cross(from, half), Vector3.Dot(from, half));
            }

            return MathF.Abs(from.X) > 0.5f ? new Quaternion(from.Y, -from.X, 0f, 0f) : new Quaternion(0f, from.Z, -from.Y, 0f);
        }

        // Shape matching: each fit control takes the rotation that best maps the rest positions of its weighted nodes
        // onto their current ones, at their weighted centre
        private void ComputeFitTransforms(float scale)
        {
            var weights = model.FitWeights;
            var initPose = model.InitPose;
            var begin = 0;

            foreach (var fit in model.FitMatrices)
            {
                var end = Math.Min(fit.End, weights.Length);

                if (fit.Node >= nodeCount || end <= begin)
                {
                    begin = end;
                    continue;
                }

                var center = Vector3.Zero;
                var weightSum = 0f;

                for (var i = begin; i < end; i++)
                {
                    center += outputPositions[weights[i].Node] * weights[i].Weight;
                    weightSum += weights[i].Weight;
                }

                center /= weightSum;

                // Columns of sum(w * current * rest^T)
                var column0 = Vector3.Zero;
                var column1 = Vector3.Zero;
                var column2 = Vector3.Zero;

                for (var i = begin; i < end; i++)
                {
                    var current = (outputPositions[weights[i].Node] - center) * weights[i].Weight;
                    var rest = initPose[weights[i].Node].Position - fit.Center;
                    column0 += current * rest.X;
                    column1 += current * rest.Y;
                    column2 += current * rest.Z;
                }

                var rotation = ExtractRotation(column0, column1, column2, Quaternion.CreateFromRotationMatrix(sim[fit.Node]));
                var fitFrame = WithTranslation(Matrix4x4.CreateFromQuaternion(rotation), center);
                var bone = Matrix4x4.CreateFromQuaternion(fit.Bone.Orientation) * Matrix4x4.CreateTranslation(fit.Bone.Position * scale);

                sim[fit.Node] = bone * fitFrame;
                begin = end;
            }
        }

        // The rotation part of the matrix with the given columns, by iterating from an initial guess
        private static Quaternion ExtractRotation(Vector3 column0, Vector3 column1, Vector3 column2, Quaternion rotation)
        {
            for (var iteration = 0; iteration < 20; iteration++)
            {
                var r0 = Vector3.Transform(Vector3.UnitX, rotation);
                var r1 = Vector3.Transform(Vector3.UnitY, rotation);
                var r2 = Vector3.Transform(Vector3.UnitZ, rotation);

                var omega = (Vector3.Cross(r0, column0) + Vector3.Cross(r1, column1) + Vector3.Cross(r2, column2))
                    / (MathF.Abs(Vector3.Dot(r0, column0) + Vector3.Dot(r1, column1) + Vector3.Dot(r2, column2)) + 1e-9f);
                var angle = omega.Length();

                if (angle < 1e-9f)
                {
                    break;
                }

                rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(omega / angle, angle) * rotation);
            }

            return rotation;
        }

        private void ComputeRopeTransforms()
        {
            var ropes = model.Ropes;
            var ropeBegin = model.RopeCount;

            for (var rope = 0; rope < model.RopeCount; rope++)
            {
                var ropeEnd = ropes[rope];
                var endNode = ropes[ropeEnd - 1];

                if (ropeEnd - ropeBegin == 2)
                {
                    var previous = ropes[ropeEnd - 2];
                    sim[endNode] = AlignX(anim[endNode], outputPositions[endNode] - outputPositions[previous], outputPositions[endNode]);
                }
                else
                {
                    // The first node is the static root and keeps its transform
                    var aligned = Matrix4x4.Identity;

                    for (var link = ropeBegin + 2; link < ropeEnd; link++)
                    {
                        var node = ropes[link - 1];
                        aligned = AlignX(anim[node], outputPositions[ropes[link]] - outputPositions[node], outputPositions[node]);
                        sim[node] = aligned;
                    }

                    sim[endNode] = WithTranslation(aligned, outputPositions[endNode]);
                }

                ropeBegin = ropeEnd;
            }
        }

        private void WriteBones(Matrix4x4[] pose, Matrix4x4 transform, float modelScale)
        {
            if (!Matrix4x4.Invert(transform, out var inverseTransform))
            {
                return;
            }

            var scale = Matrix4x4.CreateScale(modelScale);

            foreach (var ctrl in outputBones)
            {
                pose[ctrlBone[ctrl]] = scale * sim[ctrl] * inverseTransform;
            }
        }

        // Rotates tm so its X axis points along direction, keeping it as is when the direction is too short
        private static Matrix4x4 AlignX(Matrix4x4 tm, Vector3 direction, Vector3 origin)
        {
            var length = direction.Length();

            if (length <= 0.03f)
            {
                return tm;
            }

            var axisX = Vector3.Normalize(new Vector3(tm.M11, tm.M12, tm.M13));
            var rotated = tm * Matrix4x4.CreateFromQuaternion(RotateBetween(axisX, direction / length));
            rotated.Translation = origin;
            return rotated;
        }

        private static Quaternion RotateBetween(Vector3 from, Vector3 to)
        {
            var dot = Vector3.Dot(from, to);

            if (dot < -0.999999f)
            {
                return Quaternion.CreateFromAxisAngle(PerpendicularTo(from), MathF.PI);
            }

            return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1f + dot));
        }

        // Unit vector perpendicular to v, built from the axis v is least aligned with
        private static Vector3 PerpendicularTo(Vector3 v)
        {
            var abs = Vector3.Abs(v);
            var axis = abs.X <= abs.Y && abs.X <= abs.Z ? Vector3.UnitX : abs.Y <= abs.Z ? Vector3.UnitY : Vector3.UnitZ;
            return Vector3.Normalize(axis - (v * (Vector3.Dot(axis, v) / v.LengthSquared())));
        }

        private static Matrix4x4 FromAxes(Vector3 x, Vector3 y, Vector3 z, Vector3 origin) => new(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            origin.X, origin.Y, origin.Z, 1f);

        private static Matrix4x4 WithTranslation(Matrix4x4 tm, Vector3 origin)
        {
            tm.Translation = origin;
            return tm;
        }

        private static Matrix4x4 ToMatrix(FeModel.FeTransform transform)
            => Matrix4x4.CreateFromQuaternion(transform.Orientation) * Matrix4x4.CreateTranslation(transform.Position);

        // Controls without an animated bone, ordered so each comes after the skeleton parent it is derived from
        private int[] SortDerivedCtrls()
        {
            var order = new List<int>();
            var state = new byte[anim.Length];

            void Visit(int ctrl)
            {
                if (state[ctrl] != 0)
                {
                    return;
                }

                state[ctrl] = 1;

                if (ctrlSkelParent[ctrl] >= 0)
                {
                    Visit(ctrlSkelParent[ctrl]);
                }

                if (ctrlBone[ctrl] < 0)
                {
                    order.Add(ctrl);
                }
            }

            for (var ctrl = 0; ctrl < anim.Length; ctrl++)
            {
                Visit(ctrl);
            }

            return [.. order];
        }

        private static Twist[] ReadTwists(FeModel feModel)
        {
            var initPose = feModel.InitPose;
            var result = new List<Twist>(feModel.Twists.Length);

            foreach (var twist in feModel.Twists)
            {
                if (twist.NodeOrient >= initPose.Length || twist.NodeEnd >= initPose.Length)
                {
                    continue;
                }

                var orient = initPose[twist.NodeOrient];
                var end = initPose[twist.NodeEnd];
                var restDirection = MathUtils.SafeNormalize(end.Position - orient.Position);

                result.Add(new Twist(twist.NodeOrient, twist.NodeEnd,
                    Vector3.Transform(restDirection, Quaternion.Conjugate(orient.Orientation)),
                    Quaternion.Conjugate(end.Orientation) * orient.Orientation,
                    twist.TwistRelax, twist.SwingRelax));
            }

            return [.. result];
        }

        // The runtime relaxes rods in the order of the SIMD schedule, which also repeats a few as lane padding
        private static Rod[] ReadRods(FeModel feModel)
        {
            var simdRods = feModel.SimdRods;

            if (simdRods.Length == 0)
            {
                return [.. feModel.Rods.Select(static r => new Rod(r.Nodes[0], r.Nodes[1], r.MinDist, r.MaxDist, r.Weight0, r.RelaxationFactor))];
            }

            var result = new List<Rod>(simdRods.Length * 4);

            foreach (var simd in simdRods)
            {
                for (var lane = 0; lane < 4; lane++)
                {
                    var rod = new Rod(simd.Nodes[0][lane], simd.Nodes[1][lane], simd.MinDist[lane], simd.MaxDist[lane],
                        simd.Weight0[lane], simd.RelaxationFactor[lane]);

                    // A copy within the same block writes the same result twice, so once is enough
                    var duplicate = false;
                    for (var previous = result.Count - lane; previous < result.Count; previous++)
                    {
                        duplicate |= result[previous] == rod;
                    }

                    if (!duplicate)
                    {
                        result.Add(rod);
                    }
                }
            }

            return [.. result];
        }

        private static AnimRod[] ReadAnimRods(FeModel feModel)
        {
            var result = new List<AnimRod>(feModel.SimdRodsAnim.Length * 4);

            foreach (var simd in feModel.SimdRodsAnim)
            {
                for (var lane = 0; lane < 4; lane++)
                {
                    var rod = new AnimRod(simd.Nodes[0][lane], simd.Nodes[1][lane], simd.Weight0[lane], simd.RelaxationFactor[lane]);

                    var duplicate = false;
                    for (var previous = result.Count - lane; previous < result.Count; previous++)
                    {
                        duplicate |= result[previous] == rod;
                    }

                    if (!duplicate)
                    {
                        result.Add(rod);
                    }
                }
            }

            return [.. result];
        }
    }
}
