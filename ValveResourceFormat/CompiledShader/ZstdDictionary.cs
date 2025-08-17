/**
 * Zstd dictionary format
 * ----------------------
 *
 * The first 4 bytes of the dictionary is the magic number 0xec30a437 (common to all Zstd dictionaries),
 * and the following 4 bytes is the dictionary id 0x2bc2fa87. Where these two values are known we can scan
 * for the presence of a target dictionary (see 'Extracting the Zstd dictionary from Valve binaries' below).
 *
 * The Zstd format and dictionary are defined by RFC 8878, see
 * https://github.com/facebook/zstd/blob/dev/doc/zstd_compression_format.md (Zstd format)
 * https://github.com/facebook/zstd/blob/dev/doc/zstd_compression_format.md#dictionary-format (dictionary)
 *
 * The DID is user defined (it can be assigned) but is usually a checksum of its content;
 * what is certain is the DID (and the dictionary itself) is derived from uncompressed training data,
 * something representative of the target data; GPU source code (PCGL), GPU bytecode (DXIL,VULKAN)
 * and header data (variables, buffer write sequences, references to graphics objects).
 *
 * Currently two dictionaries have been found in the Valve vcs files: DID(0x2bc2fa87) and DID(0x255df362).
 * The number of dictionaries may be subject to change at any time (and should be expected).
 *
 * These dictionaries are used by the <see cref="ValveResourceFormat.CompiledShader.VfxStaticComboVcsEntry.Unserialize()"/> function.
 *
 *
 *
 * Reading the dictionary id (DID) from Zstd frames (as they appear in the vcs bytestreams)
 * ----------------------------------------------------------------------------------------
 *
 * The first 11 bytes of the compressed Zstd frame are 28 B5 2F FD 63 87 FA C2 2B 99 DC
 *
 *      - The first 4 bytes is the magic ID 0xfd2fb528 (common to all compressed Zstd frames)
 *      - The next byte (63) is the Frame_Header_Descriptor and carries 6 pieces of information; among
 *        them (specific to this case) is the Frame_Content_Size_flag=1 (bits 6-7),
 *        Dictionary_ID_flag=3 (bits 0-1) and Single_Segment_Flag=0 (bit 5).
 *      - A Dictionary_ID_flag=3 implies the dictionary ID will be 4 bytes long; it is described
 *        by the bytes 87 FA C2 2B (0x2bc2fa87)
 *      - A Frame_Content_Size_flag=1 implies the size of the Zstd frame is described by the next 2 bytes
 *        (Frame_Content_Size), according to the specification should be fitted to the range 256 - 65791 by
 *        adding 256, thus '99 DC' or 0xdc99 = 56473 implies the size of the uncompressed data = 56473 + 256 = 56729.
 *
 * The position of the dictionary id (DID) can vary by 0-1 bytes (depending on the presence of a
 * Window_Descriptor, that in turn depends on Single_Segment_Flag), and the length of the DID can
 * vary by 0-4 bytes (0 bytes means a dictionary is not used and decompression can be achieved without).
 * The DID bytes are adjacent to the Frame_Content_Size bytes, so checking the Zstd frame size against the
 * 'zframe' header data confirms where the DID ends.
 *
 *
 * Extracting the Zstd dictionary from Valve binaries
 * --------------------------------------------------
 *
 * By having knowledge of the DID (0x2bc2fa87), combined with the dictionary magic number (0xec30a437),
 * the first 8 bytes of a target Zstd dictionary can be predicted.
 *
 * Scanning all Dota2 files (on Windows) for this byte sequence it was found to exist in 5 different files
 *
 *      /game/bin/win64/materialsystem2.dll
 *      /game/bin/win64/resourcecompiler.dll
 *      /game/bin/win64/tools/met.dll
 *      /game/bin/win64/tools/model_editor.dll
 *      /game/bin/win64/vfxcompile.dll
 *
 * To complete extraction of the dictionary knowledge of the length is also needed. In the current game files
 * it can be found by decompiling the dlls. The name is given as V_ZSTD_createDDict_byReference in the dlls,
 * and the length is seen as 65536.
 *
 *      V_ZSTD_createDDict_byReference(&unk_1802027B0, 65536i64);
 *
 *
 * To extract it with IDA
 * ----------------------
 *
 * - Search for 0xec30a437 as raw bytes, you should find the calls to createDDict.
 * - Set the type to byte[65536]
 * - in the "Edit > Export Data" menu save it as raw bytes.
 *
 */

using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace ValveResourceFormat.CompiledShader
{
    internal static class ZstdDictionary
    {
        private static readonly Lock zstdDictLock = new();
        private static byte[]? zstdDict_2bc2fa87;
        private static byte[]? zstdDict_255df362;

        public static byte[] GetDictionary_2bc2fa87()
        {
            lock (zstdDictLock)
            {
                if (zstdDict_2bc2fa87 != null)
                {
                    return zstdDict_2bc2fa87;
                }

                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ValveResourceFormat.Utils.vfx_zstd_2bc2fa87.bin");
                Debug.Assert(stream != null);

                zstdDict_2bc2fa87 = new byte[0x10000];
                stream.ReadExactly(zstdDict_2bc2fa87, 0, zstdDict_2bc2fa87.Length);

                return zstdDict_2bc2fa87;
            }
        }

        public static byte[] GetDictionary_255df362() // Added in 2025
        {
            lock (zstdDictLock)
            {
                if (zstdDict_255df362 != null)
                {
                    return zstdDict_255df362;
                }

                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ValveResourceFormat.Utils.vfx_zstd_255df362.bin");
                Debug.Assert(stream != null);

                zstdDict_255df362 = new byte[0x10000];
                stream.ReadExactly(zstdDict_255df362, 0, zstdDict_255df362.Length);

                return zstdDict_255df362;
            }
        }
    }
}
