using System.IO;
using System.Security.Cryptography;

namespace ValveResourceFormat.ValveFont;

/// <summary>
/// Represents a UI font file package.
/// </summary>
public class UIFontFilePackage
{
    private static readonly byte[] FontKey =
    [
        0x13, 0xE6, 0x21, 0x14, 0xC7, 0xFA, 0x3C, 0xB9,
        0x3E, 0x86, 0xF4, 0x76, 0xF6, 0xB3, 0x2C, 0x20,
        0x4D, 0x82, 0xA4, 0x19, 0xAF, 0xF3, 0x13, 0xAE,
        0xBB, 0xA1, 0xAF, 0x92, 0xE7, 0xA0, 0xAC, 0x8D,
    ];

    /// <summary>
    /// Gets the list of font files in this package.
    /// </summary>
    public List<FontFile> FontFiles { get; } = [];

    /// <summary>
    /// Represents a font file within the package.
    /// </summary>
    public class FontFile
    {
        /// <summary>
        /// Gets or initializes the file name.
        /// </summary>
        public required string FileName { get; init; }

        /// <summary>
        /// Gets or initializes the OpenType font data.
        /// </summary>
        public required byte[] OpenTypeFontData { get; init; }
    }

    /// <summary>
    /// Opens and reads the given filename.
    /// </summary>
    /// <param name="filename">The file to open and read.</param>
    public void Read(string filename)
    {
        var data = File.ReadAllBytes(filename);
        Read(data);
    }

    /// <summary>
    /// Read the given data.
    /// </summary>
    /// <param name="data">The input data.</param>
    public void Read(ReadOnlySpan<byte> data)
    {
        // uifont files:
        // 1. the entire file is a protobuf - CUIFontFilePackagePB
        //    https://github.com/SteamDatabase/Protobufs/blob/4de57e705463449b69f600184bcb122902cf8011/csgo/uifontfile_format.proto
        // 2. every encrypted_contents is AES encrypted with a hardcoded key
        // 3. the decrypted contents are protobuf - CUIFontFilePB

        var pos = 0;

        while (pos < data.Length)
        {
            var tag = ReadVarint(data[pos..], out var bytesRead);
            pos += bytesRead;

            var fieldNumber = tag >> 3;
            var wireType = tag & 0x7;

            switch (fieldNumber)
            {
                case 1: // package_version
                    var version = (int)ReadVarint(data[pos..], out bytesRead);

                    UnexpectedMagicException.Assert(version == 1, version);

                    pos += bytesRead;
                    break;

                case 2: // encrypted_font_files
                    var length = (int)ReadVarint(data[pos..], out bytesRead);
                    pos += bytesRead;

                    var encryptedFile = ParseEncryptedFontFile(data.Slice(pos, length));
                    if (encryptedFile != null)
                    {
                        var decryptedData = DecryptFontFile(encryptedFile);
                        var fontFile = ParseFontFile(decryptedData);
                        FontFiles.Add(fontFile);
                    }
                    pos += length;
                    break;

                default:
                    throw new UnexpectedMagicException("Unexpected protobuf field", (int)fieldNumber, nameof(fieldNumber));
            }
        }
    }

    private static FontFile ParseFontFile(ReadOnlySpan<byte> data)
    {
        var pos = 0;
        string? filename = null;
        byte[]? openTypeFontData = null;

        while (pos < data.Length)
        {
            var tag = ReadVarint(data[pos..], out var bytesRead);
            pos += bytesRead;

            var fieldNumber = tag >> 3;
            var wireType = tag & 0x7;

            switch (fieldNumber)
            {
                case 1: // font_file_name
                    var nameLength = (int)ReadVarint(data[pos..], out bytesRead);
                    pos += bytesRead;
                    filename = System.Text.Encoding.UTF8.GetString(data.Slice(pos, nameLength));
                    pos += nameLength;
                    break;

                case 2: // opentype_font_data
                    var dataLength = (int)ReadVarint(data[pos..], out bytesRead);
                    pos += bytesRead;
                    openTypeFontData = data.Slice(pos, dataLength).ToArray();
                    pos += dataLength;
                    break;

                default:
                    throw new UnexpectedMagicException("Unexpected protobuf field", (int)fieldNumber, nameof(fieldNumber));
            }
        }

        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(openTypeFontData);

        var result = new FontFile
        {
            FileName = filename,
            OpenTypeFontData = openTypeFontData,
        };

        return result;
    }

    private static byte[] ParseEncryptedFontFile(ReadOnlySpan<byte> data)
    {
        var pos = 0;
        byte[]? encryptedContents = null;

        while (pos < data.Length)
        {
            var tag = ReadVarint(data[pos..], out var bytesRead);
            pos += bytesRead;

            var fieldNumber = tag >> 3;
            var wireType = tag & 0x7;

            if (fieldNumber == 1) // encrypted_contents
            {
                var length = (int)ReadVarint(data[pos..], out bytesRead);
                pos += bytesRead;

                encryptedContents = data.Slice(pos, length).ToArray();
                pos += length;
            }
            else
            {
                throw new UnexpectedMagicException("Unexpected protobuf field", (int)fieldNumber, nameof(fieldNumber));
            }
        }

        ArgumentNullException.ThrowIfNull(encryptedContents);

        return encryptedContents;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, out int bytesRead)
    {
        ulong result = 0;
        var shift = 0;
        bytesRead = 0;

        while (shift < 64 && bytesRead < data.Length)
        {
            var b = data[bytesRead++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }

        throw new InvalidOperationException("Malformed varint");
    }

    // https://github.com/SteamRE/SteamKit/blob/8ba50d18ba7cc9c47dc1138c0ef25f517bb153c4/SteamKit2/SteamKit2/Util/CryptoHelper.cs
    private static byte[] DecryptFontFile(ReadOnlySpan<byte> input)
    {
        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = 256;
        aes.Key = FontKey;

        // first 16 bytes of input is the ECB encrypted IV
        Span<byte> iv = stackalloc byte[16];
        aes.DecryptEcb(input[..iv.Length], iv, PaddingMode.None);

        return aes.DecryptCbc(input[iv.Length..], iv, PaddingMode.PKCS7);
    }
}
