using System.Diagnostics;
using System.IO;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshFile
    {
        public const uint MAGIC = 0xFEEDFACE;

        private readonly Dictionary<uint, NavMeshArea> AreasDictionary = [];
        private readonly Dictionary<byte, List<NavMeshArea>> LayerAreas = [];
        public NavMeshLadder[] Ladders { get; private set; }
        public int LayerCount { get; private set; }
        public bool IsAnalyzed { get; private set; }
        public IEnumerable<NavMeshArea> Areas => AreasDictionary.Values;

        public static NavMeshFile Read(BinaryReader binaryReader)
        {
            var navMeshFile = new NavMeshFile();

            var magic = binaryReader.ReadUInt32();
            if (magic != MAGIC)
            {
                throw new UnexpectedMagicException($"Unexpected magic, expected {MAGIC:X}", magic, nameof(magic));
            }

            var version = binaryReader.ReadUInt32();
            var subVersion = binaryReader.ReadUInt32();
            navMeshFile.IsAnalyzed = binaryReader.ReadUInt32() != 0;

            var areaCount = binaryReader.ReadUInt32();
            for (var i = 0; i < areaCount; i++)
            {
                var startPosition = binaryReader.BaseStream.Position;

                var area = NavMeshArea.Read(binaryReader, version);
                navMeshFile.AddArea(area);
            }

            var ladderCount = binaryReader.ReadUInt32();
            navMeshFile.Ladders = new NavMeshLadder[ladderCount];
            for (var i = 0; i < ladderCount; i++)
            {
                navMeshFile.Ladders[i] = NavMeshLadder.Load(binaryReader, navMeshFile);
            }

            var unkBytes = binaryReader.ReadBytes(144); //TODO
            Debug.Assert(binaryReader.BaseStream.Position == binaryReader.BaseStream.Length);

            return navMeshFile;
        }

        private void AddArea(NavMeshArea area)
        {
            AreasDictionary[area.AreaId] = area;
            LayerCount = Math.Max(LayerCount, area.AgentLayer + 1);

            if (LayerAreas.TryGetValue(area.AgentLayer, out var areas))
            {
                areas.Add(area);
            }
            else
            {
                LayerAreas[area.AgentLayer] = [area];
            }
        }

        public List<NavMeshArea> GetLayerAreas(byte layer)
        {
            return LayerAreas.GetValueOrDefault(layer);
        }

        public NavMeshArea GetArea(uint areaId)
        {
            return AreasDictionary.GetValueOrDefault(areaId);
        }
    }
}
