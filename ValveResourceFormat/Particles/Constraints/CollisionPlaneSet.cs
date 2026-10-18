namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// The surfaces a plane set collision mode collides with: the hits of traces from a control point,
    /// plus those found by confirmation traces. The set at index 0 of a tree also serves as a ring of
    /// cached particle traces, with their endpoints, for the initializers that share world hits.
    /// </summary>
    sealed class CollisionPlaneSet
    {
        /// <summary>The most surfaces a set holds.</summary>
        public const int MaxPlanes = 41;

        private const int DirectionPlanes = 26;
        private const float TraceLength = 1000f;

        private readonly Vector3[] points = new Vector3[MaxPlanes];
        private readonly Vector3[] normals = new Vector3[MaxPlanes];
        private readonly Vector3[] traceStarts = new Vector3[MaxPlanes];
        private readonly Vector3[] traceEnds = new Vector3[MaxPlanes];
        private readonly bool[] valid = new bool[MaxPlanes];
        private int count;
        private int nextSlot;
        private int retestSlot;
        private Vector3 lastOrigin;

        /// <summary>How many slots hold a surface or a cached trace.</summary>
        public int Count => count;

        /// <summary>The system age at the last refresh, or -1 before the first.</summary>
        public float LastRefreshTime { get; private set; } = -1f;

        /// <summary>
        /// Traces the set from <paramref name="origin"/>. The single downward trace is made only once.
        /// The directional set is retraced in full when the origin has moved by
        /// <paramref name="tolerance"/>, and a forced refresh of a full set retraces one direction.
        /// </summary>
        public void Refresh(IParticleCollision collision, ParticleCollisionMode mode, Vector3 origin, float tolerance, bool force, float time)
        {
            var traceDown = mode == ParticleCollisionMode.COLLISION_MODE_INITIAL_TRACE_DOWN;

            if (traceDown && count != 0)
            {
                return;
            }

            if (!force && Vector3.DistanceSquared(origin, lastOrigin) < tolerance * tolerance)
            {
                return;
            }

            LastRefreshTime = time;
            lastOrigin = origin;

            if (traceDown)
            {
                Trace(collision, 0, origin, -Vector3.UnitZ);
                count = nextSlot = 1;
                return;
            }

            if (force && nextSlot >= DirectionPlanes)
            {
                // Retests cycle through slots 0 to 26, tracing slot n along its base 3 digits less one, which is not the order the slots were filled in.
                var slot = retestSlot > DirectionPlanes ? 0 : retestSlot;
                retestSlot = slot + 1;
                Trace(collision, slot, origin, new Vector3((slot % 3) - 1, (slot / 3 % 3) - 1, (slot / 9 % 3) - 1));
                return;
            }

            var next = 0;

            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    Trace(collision, next++, origin, new Vector3(x, y, -1f));

                    if (x != 0 || y != 0)
                    {
                        Trace(collision, next++, origin, new Vector3(x, y, 0f));
                    }

                    Trace(collision, next++, origin, new Vector3(x, y, 1f));
                }
            }

            count = nextSlot = next;
        }

        /// <summary>
        /// Adds a surface found by a confirmation trace, filling the free slots first and then cycling
        /// through those after the directional ones.
        /// </summary>
        public void Add(Vector3 point, Vector3 normal)
        {
            var slot = nextSlot++;

            if (count < MaxPlanes)
            {
                slot = count++;
            }
            else if (nextSlot >= MaxPlanes)
            {
                slot = nextSlot = DirectionPlanes;
            }

            Store(slot, point, normal);
        }

        /// <summary>
        /// Finds the nearest plane the segment crosses from its front side, as a fraction along the
        /// segment. The normal is up when nothing is crossed.
        /// </summary>
        public bool Intersect(Vector3 start, Vector3 end, out float fraction, out Vector3 normal)
        {
            fraction = 2f;
            normal = Vector3.UnitZ;

            for (var i = 0; i < count; i++)
            {
                if (!valid[i])
                {
                    continue;
                }

                var startDistance = Vector3.Dot(start - points[i], normals[i]);
                var endDistance = Vector3.Dot(end - points[i], normals[i]);

                if (startDistance < 0f || endDistance >= 0f)
                {
                    continue;
                }

                var crossing = startDistance / (startDistance - endDistance);

                if (crossing < fraction)
                {
                    fraction = crossing;
                    normal = normals[i];
                }
            }

            return fraction < 1f;
        }

        /// <summary>
        /// Moves a sphere at <paramref name="position"/> out to the front side of every surface it
        /// sinks behind, one surface after the other.
        /// </summary>
        /// <returns>Whether the sphere was moved.</returns>
        public bool PushOut(ref Vector3 position, float radius)
        {
            var pushed = false;

            for (var i = 0; i < count; i++)
            {
                if (!valid[i])
                {
                    continue;
                }

                var depth = Vector3.Dot(position - points[i], normals[i]) - radius;

                if (depth < 0f)
                {
                    position -= normals[i] * depth;
                    pushed = true;
                }
            }

            return pushed;
        }

        /// <summary>
        /// Finds the cached trace whose endpoints are both nearest to <paramref name="start"/> and
        /// <paramref name="end"/>, by the larger of the two squared distances, later slots winning ties.
        /// </summary>
        /// <param name="start">Start of the trace to look up.</param>
        /// <param name="end">End of the trace to look up.</param>
        /// <param name="point">Where the cached trace ended, left unchanged when there is none.</param>
        /// <returns>The larger squared endpoint distance, or <see cref="float.MaxValue"/> when nothing is cached.</returns>
        public float FindNearestTrace(Vector3 start, Vector3 end, ref Vector3 point)
        {
            var best = float.MaxValue;

            for (var i = 0; i < count; i++)
            {
                if (!valid[i])
                {
                    continue;
                }

                var distance = MathF.Max(Vector3.DistanceSquared(start, traceStarts[i]), Vector3.DistanceSquared(end, traceEnds[i]));

                if (!(best < distance))
                {
                    best = distance;
                    point = points[i];
                }
            }

            return best;
        }

        /// <summary>
        /// Caches a trace in the ring of slots up to <paramref name="maxSlot"/>, writing after the
        /// last one written and wrapping to the first past it.
        /// </summary>
        public void AddTrace(Vector3 start, Vector3 end, Vector3 point, Vector3 normal, int maxSlot)
        {
            var slot = nextSlot > maxSlot ? 0 : nextSlot;

            traceStarts[slot] = start;
            traceEnds[slot] = end;
            Store(slot, point, normal);

            nextSlot = slot + 1;
            count = Math.Max(count, nextSlot);
        }

        /// <summary>Gets the surface point and normal held in <paramref name="slot"/>.</summary>
        public (Vector3 Point, Vector3 Normal) GetPlane(int slot) => (points[slot], normals[slot]);

        private void Trace(IParticleCollision collision, int slot, Vector3 origin, Vector3 direction)
        {
            if (collision.TraceRay(origin, origin + (direction * TraceLength), out var position, out var normal, out _))
            {
                Store(slot, position, normal);
            }
            else
            {
                valid[slot] = false;
            }
        }

        private void Store(int slot, Vector3 point, Vector3 normal)
        {
            points[slot] = point;
            normals[slot] = normal;
            valid[slot] = true;
        }
    }
}
