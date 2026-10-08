using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Particles.Renderers;

namespace Tests.Renderer
{
    public class RopeSegmentRunsTest
    {
        private readonly record struct Entry(int SegmentId, int Order) : IRopeChainEntry;

        [Test]
        public async Task InterleavedRopesAreGroupedInOrder()
        {
            // Two ropes spawned from different parents, emitted alternately
            Entry[] chain = [new(5, 0), new(2, 1), new(5, 2), new(2, 3), new(5, 4), new(2, 5)];

            RopeSegmentRuns.Group<Entry>(chain);

            Entry[] expected = [new(2, 1), new(2, 3), new(2, 5), new(5, 0), new(5, 2), new(5, 4)];
            await Assert.That(chain.SequenceEqual(expected)).IsTrue();
        }

        [Test]
        public async Task RunsEndAtSegmentBoundaries()
        {
            Entry[] chain = [new(0, 0), new(0, 1), new(0, 2), new(1, 3), new(2, 4), new(2, 5)];

            var ends = new List<int>();
            for (var start = 0; start < chain.Length;)
            {
                start = RopeSegmentRuns.RunEnd<Entry>(chain, start);
                ends.Add(start);
            }

            int[] expected = [3, 4, 6];
            await Assert.That(ends.SequenceEqual(expected)).IsTrue();
        }

        [Test]
        public async Task SingleRopeIsOrderedAndUnsplit()
        {
            Entry[] chain = [new(0, 3), new(0, 1), new(0, 2)];

            RopeSegmentRuns.Group<Entry>(chain);

            int[] expected = [1, 2, 3];

            using (Assert.Multiple())
            {
                await Assert.That(chain.Select(e => e.Order).SequenceEqual(expected)).IsTrue();
                await Assert.That(RopeSegmentRuns.RunEnd<Entry>(chain, 0)).IsEqualTo(3);
            }
        }
    }
}
