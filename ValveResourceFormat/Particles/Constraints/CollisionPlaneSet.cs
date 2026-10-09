namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// The surfaces a plane set collision mode collides with: the hits of traces from a control point,
    /// plus those found by confirmation traces.
    /// </summary>
    sealed class CollisionPlaneSet
    {
        private const int MaxPlanes = 41;
        private const int DirectionPlanes = 26;
        private const float TraceLength = 1000f;

        private readonly Vector3[] points = new Vector3[MaxPlanes];
        private readonly Vector3[] normals = new Vector3[MaxPlanes];
        private readonly bool[] valid = new bool[MaxPlanes];
        private int count;
        private int nextSlot;
        private int retestSlot;
        private Vector3 lastOrigin;

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
                // Source 2 bug: a retest traces some slots in a different direction than they were filled with, so one direction is never retested and the first confirmed plane is overwritten.
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

        private void Trace(IParticleCollision collision, int slot, Vector3 origin, Vector3 direction)
        {
            if (collision.TraceRay(origin, origin + (direction * TraceLength), out var hit))
            {
                Store(slot, hit.Position, hit.Normal);
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
