using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.GenericData.CS2;

/// <summary>
/// CS2 baked bomb damage data.
/// </summary>
public sealed class BombDamage : GenericData
{
    /// <summary>
    /// The <c>generic_data_type</c> value identifying a baked bomb damage resource.
    /// </summary>
    public const string DataType = "CS2_BOMB_DAMAGE_DATA";

    /// <summary>
    /// Version of the baked bomb damage data format.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Stores information about each bombsite on the map, such as AABB and bomb power.
    /// </summary>
    public BombDamageBombsite[] Bombsites { get; private set; } = [];

    /// <summary>
    /// Contains points on the map that have associated baked damage information.
    /// </summary>
    public Vector3[] Positions { get; private set; } = [];

    /// <summary>
    /// Contains baked damage information, such as yaw, angle, and phase. The length of this array should be equal to the number of positions multiplied by the number of bombsites.
    /// To retrieve the damage information for a given position and bombsite, use <see cref="GetBombsiteDamageValue(int, int)"/>.
    /// </summary>
    public BombDamageDamageValue[] DamageValues { get; private set; } = [];

    /// <summary>
    /// Wraps an already-read <see cref="BinaryKV3"/> DATA block and parses the bomb damage payload from it.
    /// </summary>
    /// <param name="kv3">The KV3 DATA block that has already been read.</param>
    [SetsRequiredMembers]
    public BombDamage(BinaryKV3 kv3) : base(kv3)
    {
        ParseData();
    }

    /// <summary>
    /// Returns the damage information for a given position and bombsite.
    /// </summary>
    /// <param name="positionIndex">Position index to retrieve damage information for.</param>
    /// <param name="bombsiteIndex">Bombsite index to retrieve damage information for. Index 0 is not guaranteed to be bombsite A.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="positionIndex"/> or <paramref name="bombsiteIndex"/> are out of range (see <seealso cref="Positions"/> and <seealso cref="Bombsites"/>).</exception>
    public BombDamageDamageValue GetBombsiteDamageValue(int positionIndex, int bombsiteIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bombsiteIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bombsiteIndex, Bombsites.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(positionIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(positionIndex, Positions.Length);

        return DamageValues[Positions.Length * bombsiteIndex + positionIndex];
    }

    /// <summary>
    /// Calculates the damage dealt by the bomb for a given damage value, replicating the game's formula.
    /// This is approximately <c>100 * BombPower / Phase</c> when <see cref="BombDamageDamageValue.Phase"/> is at most 1800.
    /// </summary>
    /// <param name="bombsite">The bombsite the bomb is planted at.</param>
    /// <param name="damageValue">The baked damage value for a position, see <see cref="GetBombsiteDamageValue(int, int)"/>.</param>
    /// <returns>Damage in the range [0, 255].</returns>
    public static float CalculateDamage(in BombDamageBombsite bombsite, in BombDamageDamageValue damageValue)
    {
        const float MaxDamage = 100f;
        const float MaxPhase = 1800f;

        float phase = damageValue.Phase;
        var clampedPhase = MathF.Min(phase, MaxPhase);

        if (phase == 0f)
        {
            return phase >= bombsite.BombPower ? 0f : MaxDamage;
        }

        var damage = MaxDamage - MaxDamage * (phase - bombsite.BombPower) / clampedPhase;
        return Math.Clamp(damage, 0f, 255f);
    }

    private void ParseData()
    {
        var kvRoot = Data.Root;

        if (!kvRoot.ContainsKey("header") || !kvRoot.ContainsKey("data"))
        {
            throw new ArgumentException("Baked bomb damage resource does not contain expected KV3 data structure.");
        }

        var header = kvRoot.GetSubCollection("header");
        Version = header.GetInt32Property("version");
        if (Version != 1)
        {
            throw new UnexpectedMagicException("Unexpected version for baked bomb damage data", Version, nameof(Version));
        }

        var data = kvRoot.GetSubCollection("data");
        if (!data.ContainsKey("bombsites") || !data.ContainsKey("positions") || !data.ContainsKey("damage_values"))
        {
            throw new ArgumentException("Baked bomb damage resource does not contain expected KV3 data structure.");
        }

        ReadBombsites(data["bombsites"].AsBlob());
        ReadPositions(data["positions"].AsBlob());
        ReadDamageValues(data["damage_values"].AsBlob());

        if (DamageValues.Length != Bombsites.Length * Positions.Length)
        {
            throw new InvalidDataException("Baked bomb damage value count does not match bombsite count multiplied by position count.");
        }
    }

    private void ReadBombsites(byte[] blob)
    {
        var floats = MemoryMarshal.Cast<byte, float>(blob);
        Bombsites = new BombDamageBombsite[floats.Length / 7]; // 7 floats per bombsite

        for (var i = 0; i < Bombsites.Length; i++)
        {
            var bombsite = floats.Slice(i * 7, 7);
            Bombsites[i] = new BombDamageBombsite
            {
                BoundsMin = new Vector3(bombsite[0], bombsite[1], bombsite[2]),
                BoundsMax = new Vector3(bombsite[3], bombsite[4], bombsite[5]),
                BombPower = bombsite[6]
            };
        }
    }

    private void ReadPositions(byte[] blob)
    {
        var coords = MemoryMarshal.Cast<byte, short>(blob);
        Positions = new Vector3[coords.Length / 3]; // 3 shorts per position

        for (var i = 0; i < Positions.Length; i++)
        {
            Positions[i] = new Vector3(coords[i * 3], coords[i * 3 + 1], coords[i * 3 + 2]);
        }
    }

    private void ReadDamageValues(byte[] blob)
    {
        DamageValues = new BombDamageDamageValue[blob.Length / 4]; // 4 bytes per damage value

        for (var i = 0; i < DamageValues.Length; i++)
        {
            var packed = blob.AsSpan(i * 4, 4);
            DamageValues[i] = new BombDamageDamageValue
            {
                Phase = BinaryPrimitives.ReadUInt16LittleEndian(packed),
                Yaw = packed[2],
                Pitch = packed[3]
            };
        }
    }
}
