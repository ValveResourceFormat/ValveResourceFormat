using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Utils;

namespace Tests
{
    /// <summary>
    /// Pins what <see cref="DeferredQueue{T}"/> is for: work posted from anywhere reaches the owner
    /// intact, and a steady stream of it stops allocating once the buffers have grown.
    /// </summary>
    public class DeferredQueueTest
    {
        [Test]
        public async Task DrainReturnsEverythingPostedConcurrently()
        {
            const int threads = 8;
            const int perThread = 4000;
            const int total = threads * perThread;

            var queue = new DeferredQueue<int>();
            var posting = new Task[threads];

            for (var thread = 0; thread < threads; thread++)
            {
                var first = thread * perThread;

                posting[thread] = Task.Run(() =>
                {
                    for (var i = 0; i < perThread; i++)
                    {
                        queue.Post(first + i);
                    }
                });
            }

            await Task.WhenAll(posting);

            // Counted before any await, because the drained items are a span
            var seen = new bool[total];
            var drained = queue.Drain();
            var drainedCount = drained.Length;
            var duplicates = 0;

            foreach (var value in drained)
            {
                if (seen[value])
                {
                    duplicates++;
                }

                seen[value] = true;
            }

            var missing = 0;

            foreach (var value in seen)
            {
                if (!value)
                {
                    missing++;
                }
            }

            await Assert.That(drainedCount).IsEqualTo(total);
            await Assert.That(duplicates).IsEqualTo(0);
            await Assert.That(missing).IsEqualTo(0);

            // Drained once means drained: the second call sees nothing
            await Assert.That(queue.Drain().Length).IsEqualTo(0);
        }

        [Test]
        public async Task SteadyStateDoesNotAllocate()
        {
            const int perRound = 300;

            var queue = new DeferredQueue<long>();

            // Both buffers have to reach the high water mark, and they swap, so this takes a few rounds
            for (var round = 0; round < 8; round++)
            {
                PostRound(queue, perRound);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            const int rounds = 200;
            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var round = 0; round < rounds; round++)
            {
                PostRound(queue, perRound);
            }

            var perRoundBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)rounds;

            await Assert.That(perRoundBytes).IsEqualTo(0);
        }

        private static void PostRound(DeferredQueue<long> queue, int count)
        {
            for (var i = 0; i < count; i++)
            {
                queue.Post(i);
            }

            queue.Drain();
        }
    }
}
