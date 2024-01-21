// Credit to https://github.com/mafaca/Etc

using System.Runtime.CompilerServices;

namespace ValveResourceFormat.TextureDecoders
{
    internal class CommonETC
    {
        protected static readonly byte[] WriteOrderTable = [0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15];
        protected static readonly int[,] Etc1ModifierTable =
        {
            { 2, 8, -2, -8, },
            { 5, 17, -5, -17, },
            { 9, 29, -9, -29,},
            { 13, 42, -13, -42, },
            { 18, 60, -18, -60, },
            { 24, 80, -24, -80, },
            { 33, 106, -33, -106, },
            { 47, 183, -47, -183, }
        };
        protected static readonly byte[,] Etc1SubblockTable =
        {
            {0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1},
            {0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1}
        };
        protected static readonly byte[] Etc2DistanceTable = [3, 6, 11, 16, 23, 32, 41, 64];


        protected readonly uint[] m_buf = new uint[16];
        protected readonly byte[,] m_c = new byte[3, 3];

        protected void DecodeEtc2Block(Span<byte> block)
        {
            var j = (ushort)(block[6] << 8 | block[7]);
            var k = (ushort)(block[4] << 8 | block[5]);

            if ((block[3] & 2) != 0)
            {
                var r = (byte)(block[0] & 0xf8);
                var dr = (short)((block[0] << 3 & 0x18) - (block[0] << 3 & 0x20));
                var g = (byte)(block[1] & 0xf8);
                var dg = (short)((block[1] << 3 & 0x18) - (block[1] << 3 & 0x20));
                var b = (byte)(block[2] & 0xf8);
                var db = (short)((block[2] << 3 & 0x18) - (block[2] << 3 & 0x20));
                if (r + dr < 0 || r + dr > 255)
                {
                    // T
                    unchecked
                    {
                        m_c[0, 0] = (byte)(block[0] << 3 & 0xc0 | block[0] << 4 & 0x30 | block[0] >> 1 & 0xc | block[0] & 3);
                        m_c[0, 1] = (byte)(block[1] & 0xf0 | block[1] >> 4);
                        m_c[0, 2] = (byte)(block[1] & 0x0f | block[1] << 4);
                        m_c[1, 0] = (byte)(block[2] & 0xf0 | block[2] >> 4);
                        m_c[1, 1] = (byte)(block[2] & 0x0f | block[2] << 4);
                        m_c[1, 2] = (byte)(block[3] & 0xf0 | block[3] >> 4);
                    }
                    var d = Etc2DistanceTable[block[3] >> 1 & 6 | block[3] & 1];
                    uint[] color_set =
                    [
                        ApplicateColorRaw(m_c, 0),
                        ApplicateColor(m_c, 1, d),
                        ApplicateColorRaw(m_c, 1),
                        ApplicateColor(m_c, 1, -d)
                    ];
                    for (var i = 0; i < 16; i++, j >>= 1, k >>= 1)
                    {
                        m_buf[WriteOrderTable[i]] = color_set[k << 1 & 2 | j & 1];
                    }
                }
                else if (g + dg < 0 || g + dg > 255)
                {
                    // H
                    unchecked
                    {
                        m_c[0, 0] = (byte)(block[0] << 1 & 0xf0 | block[0] >> 3 & 0xf);
                        m_c[0, 1] = (byte)(block[0] << 5 & 0xe0 | block[1] & 0x10);
                        m_c[0, 1] |= (byte)(m_c[0, 1] >> 4);
                        m_c[0, 2] = (byte)(block[1] & 8 | block[1] << 1 & 6 | block[2] >> 7);
                        m_c[0, 2] |= (byte)(m_c[0, 2] << 4);
                        m_c[1, 0] = (byte)(block[2] << 1 & 0xf0 | block[2] >> 3 & 0xf);
                        m_c[1, 1] = (byte)(block[2] << 5 & 0xe0 | block[3] >> 3 & 0x10);
                        m_c[1, 1] |= (byte)(m_c[1, 1] >> 4);
                        m_c[1, 2] = (byte)(block[3] << 1 & 0xf0 | block[3] >> 3 & 0xf);
                    }
                    var di = block[3] & 4 | block[3] << 1 & 2;
                    if (m_c[0, 0] > m_c[1, 0] || (m_c[0, 0] == m_c[1, 0] && (m_c[0, 1] > m_c[1, 1] || (m_c[0, 1] == m_c[1, 1] && m_c[0, 2] >= m_c[1, 2]))))
                    {
                        ++di;
                    }
                    var d = Etc2DistanceTable[di];
                    uint[] color_set =
                    [
                        ApplicateColor(m_c, 0, d),
                        ApplicateColor(m_c, 0, -d),
                        ApplicateColor(m_c, 1, d),
                        ApplicateColor(m_c, 1, -d)
                    ];
                    for (var i = 0; i < 16; i++, j >>= 1, k >>= 1)
                    {
                        m_buf[WriteOrderTable[i]] = color_set[k << 1 & 2 | j & 1];
                    }
                }
                else if (b + db < 0 || b + db > 255)
                {
                    // planar
                    unchecked
                    {
                        m_c[0, 0] = (byte)(block[0] << 1 & 0xfc | block[0] >> 5 & 3);
                        m_c[0, 1] = (byte)(block[0] << 7 & 0x80 | block[1] & 0x7e | block[0] & 1);
                        m_c[0, 2] = (byte)(block[1] << 7 & 0x80 | block[2] << 2 & 0x60 | block[2] << 3 & 0x18 | block[3] >> 5 & 4);
                        m_c[0, 2] |= (byte)(m_c[0, 2] >> 6);
                        m_c[1, 0] = (byte)(block[3] << 1 & 0xf8 | block[3] << 2 & 4 | block[3] >> 5 & 3);
                        m_c[1, 1] = (byte)(block[4] & 0xfe | block[4] >> 7);
                        m_c[1, 2] = (byte)(block[4] << 7 & 0x80 | block[5] >> 1 & 0x7c);
                        m_c[1, 2] |= (byte)(m_c[1, 2] >> 6);
                        m_c[2, 0] = (byte)(block[5] << 5 & 0xe0 | block[6] >> 3 & 0x1c | block[5] >> 1 & 3);
                        m_c[2, 1] = (byte)(block[6] << 3 & 0xf8 | block[7] >> 5 & 0x6 | block[6] >> 4 & 1);
                        m_c[2, 2] = (byte)(block[7] << 2 | block[7] >> 4 & 3);
                    }
                    for (int y = 0, i = 0; y < 4; y++)
                    {
                        for (var x = 0; x < 4; x++, i++)
                        {
                            var ri = Clamp255((x * (m_c[1, 0] - m_c[0, 0]) + y * (m_c[2, 0] - m_c[0, 0]) + 4 * m_c[0, 0] + 2) >> 2);
                            var gi = Clamp255((x * (m_c[1, 1] - m_c[0, 1]) + y * (m_c[2, 1] - m_c[0, 1]) + 4 * m_c[0, 1] + 2) >> 2);
                            var bi = Clamp255((x * (m_c[1, 2] - m_c[0, 2]) + y * (m_c[2, 2] - m_c[0, 2]) + 4 * m_c[0, 2] + 2) >> 2);
                            m_buf[i] = Color(ri, gi, bi, 255);
                        }
                    }
                }
                else
                {
                    // differential
                    byte[] code = [(byte)(block[3] >> 5), (byte)(block[3] >> 2 & 7)];
                    var ti = block[3] & 1;
                    unchecked
                    {
                        m_c[0, 0] = (byte)(r | r >> 5);
                        m_c[0, 1] = (byte)(g | g >> 5);
                        m_c[0, 2] = (byte)(b | b >> 5);
                        m_c[1, 0] = (byte)(r + dr);
                        m_c[1, 1] = (byte)(g + dg);
                        m_c[1, 2] = (byte)(b + db);
                        m_c[1, 0] |= (byte)(m_c[1, 0] >> 5);
                        m_c[1, 1] |= (byte)(m_c[1, 1] >> 5);
                        m_c[1, 2] |= (byte)(m_c[1, 2] >> 5);
                    }
                    for (var i = 0; i < 16; i++, j >>= 1, k >>= 1)
                    {
                        var s = Etc1SubblockTable[ti, i];
                        var index = k << 1 & 2 | j & 1;
                        var m = Etc1ModifierTable[code[s], index];
                        m_buf[WriteOrderTable[i]] = ApplicateColor(m_c, s, m);
                    }
                }
            }
            else
            {
                // individual
                byte[] code = [(byte)(block[3] >> 5), (byte)(block[3] >> 2 & 7)];
                var ti = block[3] & 1;
                unchecked
                {
                    m_c[0, 0] = (byte)(block[0] & 0xf0 | block[0] >> 4);
                    m_c[1, 0] = (byte)(block[0] & 0x0f | block[0] << 4);
                    m_c[0, 1] = (byte)(block[1] & 0xf0 | block[1] >> 4);
                    m_c[1, 1] = (byte)(block[1] & 0x0f | block[1] << 4);
                    m_c[0, 2] = (byte)(block[2] & 0xf0 | block[2] >> 4);
                    m_c[1, 2] = (byte)(block[2] & 0x0f | block[2] << 4);
                }
                for (var i = 0; i < 16; i++, j >>= 1, k >>= 1)
                {
                    var s = Etc1SubblockTable[ti, i];
                    var index = k << 1 & 2 | j & 1;
                    var m = Etc1ModifierTable[code[s], index];
                    m_buf[WriteOrderTable[i]] = ApplicateColor(m_c, s, m);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static int Clamp255(int n)
        {
            return n < 0 ? 0 : n > 255 ? 255 : n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Color(int r, int g, int b, int a)
        {
            return unchecked((uint)(r << 16 | g << 8 | b | a << 24));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ApplicateColor(byte[,] c, int o, int m)
        {
            return Color(Clamp255(c[o, 0] + m), Clamp255(c[o, 1] + m), Clamp255(c[o, 2] + m), 255);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ApplicateColorRaw(byte[,] c, int o)
        {
            return Color(c[o, 0], c[o, 1], c[o, 2], 255);
        }
    }
}
