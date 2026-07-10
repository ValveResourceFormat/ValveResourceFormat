using System.IO;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.GameSpecific.CS2.BombDamageData;

public class BombDamageData
{
    public BombDamageDataBombsite[] Bombsites { get; set; }
    public Vector3[] Positions { get; set; }
    public BombDamageDataDamageValue[] DamageValues { get; set; }

    public void Read(Resource resource)
    {
        var dataBlock = (BinaryKV3?)resource.DataBlock;
        if (dataBlock == null)
        {
            throw new ArgumentNullException("resource.DataBlock");
        }

        var kvRoot = dataBlock.Data.Root;
        var header = kvRoot.GetSubCollection("header");
        var version = header.GetInt32Property("version");
        if (version != 1)
        {
            throw new UnexpectedMagicException($"Unexpected version for baked bomb damage data", version, nameof(version));
        }

        var data = kvRoot.GetSubCollection("data");
        var bombsites = data["bombsites"].AsBlob();
        var positions = data["positions"].AsBlob();
        var damageValues = data["damage_values"].AsBlob();

        ReadBombsites(bombsites);
        ReadPositions(positions);
        ReadDamageValues(damageValues);
    }

    public BombDamageDataDamageValue GetBombsiteDamageValue(int positionIndex, int bombsiteIndex)
    {
        //return DamageValues[positionIndex * Bombsites.Length + bombsiteIndex];
        return DamageValues[Positions.Length * bombsiteIndex + positionIndex];
    }

    private void ReadBombsites(byte[] blob)
    {
        var bombsiteCount = blob.Length / (4 * 7); // 7 floats per bombsite
        if (bombsiteCount == 0)
        {
            Bombsites = [];
            return;
        }

        Bombsites = new BombDamageDataBombsite[bombsiteCount];
        var reader = BlobToReader(blob);
        for (var i = 0; i < bombsiteCount; i++)
        {
            var minX = reader.ReadSingle();
            var minY = reader.ReadSingle();
            var minZ = reader.ReadSingle();

            var maxX = reader.ReadSingle();
            var maxY = reader.ReadSingle();
            var maxZ = reader.ReadSingle();

            var bombPower = reader.ReadSingle();

            Bombsites[i] = new BombDamageDataBombsite
            {
                BoundsMin = new Vector3(minX, minY, minZ),
                BoundsMax = new Vector3(maxX, maxY, maxZ),
                BombPower = bombPower
            };
        }
    }

    private void ReadPositions(byte[] blob)
    {
        var positionCount = blob.Length / (2 * 3); // 3 shorts per position
        if (positionCount == 0)
        {
            Positions = [];
            return;
        }

        Positions = new Vector3[positionCount];
        var reader = BlobToReader(blob);
        for (var i = 0; i < positionCount; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var z = reader.ReadInt16();

            Positions[i] = new Vector3(x, y, z);
        }
    }

    private void ReadDamageValues(byte[] blob)
    {
        var damageValueCount = blob.Length / 4; // 4 bytes per damage value
        if (damageValueCount == 0)
        {
            DamageValues = [];
            return;
        }

        DamageValues = new BombDamageDataDamageValue[damageValueCount];
        var reader = BlobToReader(blob);
        for (var i = 0; i < damageValueCount; i++)
        {
            var distanceUnk1 = reader.ReadByte();
            var distanceUnk2 = reader.ReadByte();
            var yaw = reader.ReadByte();
            var pitch = reader.ReadByte();

            DamageValues[i] = new BombDamageDataDamageValue
            {
                DistanceUnk1 = distanceUnk1,
                DistanceUnk2 = distanceUnk2,
                Yaw = yaw * 360.0f / 255.0f,
                Pitch = pitch * 360.0f / 255.0f
            };
        }
    }

    private static BinaryReader BlobToReader(byte[] blob)
    {
        var stream = new MemoryStream(blob);
        return new BinaryReader(stream, Encoding.ASCII, false);
    }
}
