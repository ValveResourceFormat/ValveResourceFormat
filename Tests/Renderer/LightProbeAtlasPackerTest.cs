using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Utils;

namespace Tests.Renderer
{
    public class LightProbeAtlasPackerTest
    {
        [Test]
        public async Task PlacesEveryGridWithoutOverlap()
        {
            (int Width, int Height, int Depth)[] sizes =
            [
                (28, 12, 12), (12, 12, 12), (28, 8, 4), (12, 8, 12), (16, 16, 12), (8, 8, 12),
                (24, 16, 20), (16, 20, 12), (16, 16, 16), (128, 56, 44), (4, 4, 4), (52, 72, 24),
            ];

            var slots = LightProbeAtlasPacker.Pack(sizes, 2048, 2048 / 6, out var atlasSize);

            await Assert.That(slots.All(static slot => slot != null)).IsTrue();

            var boxes = sizes.Zip(slots, static (size, slot) => (Slot: slot!.Value, Size: size)).ToArray();

            using (Assert.Multiple())
            {
                foreach (var (slot, size) in boxes)
                {
                    await Assert.That(slot.X % LightProbeAtlasPacker.BlockSize).IsEqualTo(0);
                    await Assert.That(slot.Y % LightProbeAtlasPacker.BlockSize).IsEqualTo(0);
                    await Assert.That(slot.X + size.Width).IsLessThanOrEqualTo(atlasSize.Width);
                    await Assert.That(slot.Y + size.Height).IsLessThanOrEqualTo(atlasSize.Height);
                    await Assert.That(slot.Z + size.Depth).IsLessThanOrEqualTo(atlasSize.Depth);
                }

                for (var i = 0; i < boxes.Length; i++)
                {
                    for (var j = i + 1; j < boxes.Length; j++)
                    {
                        await Assert.That(Overlap(boxes[i], boxes[j])).IsFalse();
                    }
                }
            }
        }

        [Test]
        public async Task LeavesOutWhatDoesNotFit()
        {
            (int Width, int Height, int Depth)[] sizes = [(16, 16, 8), (64, 16, 8), (16, 16, 40), (0, 4, 4)];

            var slots = LightProbeAtlasPacker.Pack(sizes, 32, 32, out var atlasSize);

            using (Assert.Multiple())
            {
                await Assert.That(slots[0]).IsNotNull();
                await Assert.That(slots[1]).IsNull();
                await Assert.That(slots[2]).IsNull();
                await Assert.That(slots[3]).IsNull();
                await Assert.That(atlasSize.Width).IsLessThanOrEqualTo(32);
                await Assert.That(atlasSize.Depth).IsLessThanOrEqualTo(32);
            }
        }

        [Test]
        public async Task PacksNothingFromNothing()
        {
            var slots = LightProbeAtlasPacker.Pack([], 2048, 341, out var atlasSize);

            await Assert.That(slots).IsEmpty();
            await Assert.That(atlasSize).IsEqualTo(default((int, int, int)));
        }

        // Slots take whole blocks in X and Y, so an overlap is checked against the aligned footprint
        private static bool Overlap((LightProbeAtlasSlot Slot, (int Width, int Height, int Depth) Size) a, (LightProbeAtlasSlot Slot, (int Width, int Height, int Depth) Size) b)
        {
            static int Align(int value) => (value + LightProbeAtlasPacker.BlockSize - 1) / LightProbeAtlasPacker.BlockSize * LightProbeAtlasPacker.BlockSize;

            return a.Slot.X < b.Slot.X + Align(b.Size.Width) && b.Slot.X < a.Slot.X + Align(a.Size.Width)
                && a.Slot.Y < b.Slot.Y + Align(b.Size.Height) && b.Slot.Y < a.Slot.Y + Align(a.Size.Height)
                && a.Slot.Z < b.Slot.Z + b.Size.Depth && b.Slot.Z < a.Slot.Z + a.Size.Depth;
        }
    }
}
