using System.Collections.Generic;
using System.IO;

namespace ValveResourceFormat.ShaderParser
{
    public static class ShaderUtilHelpers
    {
        public const uint PI_MURMUR_SEED = 0x31415926;

        private static byte[] zstdDictionary;
        public static byte[] GetZFrameDictionary()
        {
            if (zstdDictionary == null)
            {
                zstdDictionary = File.ReadAllBytes("../../CompiledShader/zstdictionary_2bc2fa87.dat");
            }
            return zstdDictionary;
        }

        public static VcsFileType GetVcsFileType(string filenamepath)
        {
            if (filenamepath.EndsWith("features.vcs"))
            {
                return VcsFileType.Features;
            }
            if (filenamepath.EndsWith("vs.vcs"))
            {
                return VcsFileType.VertexShader;
            }
            if (filenamepath.EndsWith("ps.vcs"))
            {
                return VcsFileType.PixelShader;
            }
            if (filenamepath.EndsWith("psrs.vcs"))
            {
                return VcsFileType.PotentialShadowReciever;
            }
            if (filenamepath.EndsWith("gs.vcs"))
            {
                return VcsFileType.GeometryShader;
            }
            throw new ShaderParserException($"don't know what this file is {filenamepath}");
        }

        public static VcsSourceType GetVcsSourceType(string filenamepath) {
            string[] nameTokens = filenamepath.Split("_");

            if (nameTokens.Length >= 3 && nameTokens[^3].ToLower().EndsWith("pcgl")) {
                return VcsSourceType.Glsl;
            }
            if (nameTokens.Length >= 3 && nameTokens[^3].ToLower().EndsWith("pc")) {
                if (nameTokens[^2].EndsWith("30")) {
                    return VcsSourceType.DXIL;
                } else {
                    return VcsSourceType.DXBC;
                }
            }

            // todo - needs implementation: Vulkan (+ any other known types?)
            throw new ShaderParserException($"Source type unknown or not supported {filenamepath}");
        }

        public static string ShortenShaderParam(string shaderParam)
        {
            if (shaderParam.Length <= 4)
            {
                return shaderParam;
            }
            string[] splitName = shaderParam[2..].Split("_");
            string newName = "";
            if (splitName[0] == "MODE")
            {
                if (splitName.Length == 2)
                {
                    return splitName[1].Length > 3 ? $"M_{splitName[1][0..3]}" : $"M_{splitName[1]}";
                }
                newName = "M_";
                for (int i = 1; i < splitName.Length; i++)
                {
                    newName += splitName[i][0..1];
                }
                return newName;
            }
            if (splitName.Length > 2)
            {
                for (int i = 0; i < splitName.Length && i < 5; i++)
                {
                    newName += splitName[i].Substring(0, 1);
                }
                return newName;
            }
            if (splitName.Length == 1)
            {
                return splitName[0].Length > 4 ? splitName[0].Substring(0, 4) : splitName[0];
            }
            newName = splitName[0].Length > 3 ? splitName[0][0..3] : splitName[0];
            return $"{newName}_{splitName[1][0..1]}";
        }

        public static string CombineIntArray(int[] ints0, bool includeParenth = false)
        {
            if (ints0.Length == 0) return $"_";
            string valueString = "";
            foreach (int i in ints0)
            {
                valueString += $"{i},";
            }
            valueString = valueString[0..^1];
            return includeParenth ? $"({valueString})" : $"{valueString}";
        }

        public static string CombineIntsSpaceSep(int[] ints0, int padding = 5)
        {
            if (ints0.Length == 0) return $"_".PadLeft(padding);
            string valueString = "";
            foreach (int v in ints0)
            {
                string intPadded = $"{(v != 0 ? v : "_")}".PadLeft(padding);
                valueString += $"{intPadded}";
            }
            // return $"{valueString[0..^padding]}";
            return $"{valueString}";
        }

        public static string CombineStringsSpaceSep(string[] strings0, int padding = 5)
        {
            string combinedString = "";
            foreach (string s in strings0)
            {
                combinedString += s.PadLeft(padding);
            }
            return combinedString;
        }

        public static string CombineStringArray(string[] strings0, bool includeParenth = false)
        {
            if (strings0.Length == 0) return $"_";
            string combinedString = "";
            foreach (string s in strings0)
            {
                combinedString += $"{s}, ";
            }
            combinedString = combinedString[0..^2];
            return includeParenth ? $"({combinedString})" : $"{combinedString}";
        }

        public static string[] CombineValuesBreakString(string[] strings0, int breakLen)
        {
            List<string> stringCollection = new();
            if (strings0.Length == 0)
            {
                stringCollection.Add("");
                return stringCollection.ToArray();
            }
            string line = strings0[0] + ", ";
            for (int i = 1; i < strings0.Length; i++)
            {
                if (line.Length + strings0[i].Length + 1 < breakLen)
                {
                    line += strings0[i] + ", ";
                } else
                {
                    stringCollection.Add(line[0..^2]);
                    line = strings0[i] + ", ";
                }
            }
            if (line.Length > 0)
            {
                stringCollection.Add(line[0..^2]);
            }
            return stringCollection.ToArray(); ;
        }

    }
}
