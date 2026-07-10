using System.IO;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.GameSpecific.CS2.BombDamageData;

/// <summary>
/// Parsed data class for CS2 baked bomb damage data stored in baked_bomb_damage.vdata_c files.
/// </summary>
public class BombDamageData
{
    /// <summary>
    /// Stores information about each bombsite on the map, such as AABB and bomb power.
    /// </summary>
    public BombDamageDataBombsite[] Bombsites { get; set; } = [];
    /// <summary>
    /// Contains points on the map that have associated baked damage information.
    /// </summary>
    public Vector3[] Positions { get; set; } = [];
    /// <summary>
    /// Contains baked damage information, such as yaw, angle, and phase. The length of this array should be equal to the number of positions multiplied by the number of bombsites.
    /// To retreive the damage information for a given position and bombsite, use <see cref="GetBombsiteDamageValue(int, int)"/>.
    /// </summary>
    public BombDamageDataDamageValue[] DamageValues { get; set; } = [];

    /// <summary>
    /// Parses a baked bomb damage file.
    /// </summary>
    /// <param name="resource">The baked_bomb_damage.vdata_c file to parse.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="resource"/> is not a valid baked bomb damage file.</exception>
    /// <exception cref="UnexpectedMagicException">Thrown if version of baked damage data stored in <paramref name="resource"/> is unexpected.</exception>
    public void Read(Resource resource)
    {
        if (resource.DataBlock is not BinaryKV3 dataBlock)
        {
            throw new ArgumentException("Resource provided does not contain a binary KV3 data block.", nameof(resource));
        }

        var kvRoot = dataBlock.Data.Root;
        if (!kvRoot.ContainsKey("header") || !kvRoot.ContainsKey("data"))
        {
            throw new ArgumentException("Resource provided does not contain expected KV3 data structure.", nameof(resource));
        }

        var header = kvRoot.GetSubCollection("header");
        var version = header.GetInt32Property("version");
        if (version != 1)
        {
            throw new UnexpectedMagicException($"Unexpected version for baked bomb damage data", version, nameof(version));
        }

        var data = kvRoot.GetSubCollection("data");
        if (!data.ContainsKey("bombsites") || !data.ContainsKey("positions") || !data.ContainsKey("damage_values"))
        {
            throw new ArgumentException("Resource provided does not contain expected KV3 data structure.", nameof(resource));
        }

        var bombsites = data["bombsites"].AsBlob();
        var positions = data["positions"].AsBlob();
        var damageValues = data["damage_values"].AsBlob();

        ReadBombsites(bombsites);
        ReadPositions(positions);
        ReadDamageValues(damageValues);
    }

    /// <summary>
    /// Returns the damage information for a given position and bombsite.
    /// </summary>
    /// <param name="positionIndex">Position index to retrieve damage information for.</param>
    /// <param name="bombsiteIndex">Bombsite index to retrieve damage information for. Index 0 is not guaranteed to be bombsite A.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="positionIndex"/> or <paramref name="bombsiteIndex"/> are out of range (see <seealso cref="Positions"/> and <seealso cref="Bombsites"/>).</exception>
    /// <returns></returns>
    public BombDamageDataDamageValue GetBombsiteDamageValue(int positionIndex, int bombsiteIndex)
    {
        if (bombsiteIndex < 0 || bombsiteIndex >= Bombsites.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bombsiteIndex), $"Bombsite index is out of range.");
        }
        if (positionIndex < 0 || positionIndex >= Positions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(positionIndex), $"Position index is out of range.");
        }
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
            var phaseFract = reader.ReadByte() / 255.0f;
            var phase = reader.ReadByte();
            var yaw = reader.ReadByte();
            var pitch = reader.ReadByte();

            DamageValues[i] = new BombDamageDataDamageValue
            {
                Phase = phase + phaseFract,
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
