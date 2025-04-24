using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    public class ConfigMappingDParams
    {
        public ConfigMappingDParams(ShaderFile shaderfile)
        {
            GenerateOffsetAndStateLookups(shaderfile);
        }

        int[] offsets;
        int[] nr_states;

        public int SumStates
        {
            get
            {
                var sum = 0;
                for (var i = 0; i < nr_states.Length; i++)
                {
                    sum += nr_states[i];
                }
                return sum;
            }
        }

        private void GenerateOffsetAndStateLookups(ShaderFile shaderFile)
        {
            if (shaderFile.DBlocks.Count == 0)
            {
                offsets = [];
                nr_states = [];
                return;
            }

            offsets = new int[shaderFile.DBlocks.Count];
            nr_states = new int[shaderFile.DBlocks.Count];

            offsets[0] = 1;
            nr_states[0] = shaderFile.DBlocks[0].RangeMax + 1;

            for (var i = 1; i < shaderFile.DBlocks.Count; i++)
            {
                nr_states[i] = shaderFile.DBlocks[i].RangeMax + 1;
                offsets[i] = offsets[i - 1] * nr_states[i - 1];
            }
        }

        public int[] GetConfigState(long zframeId)
        {
            var state = new int[nr_states.Length];
            for (var i = 0; i < nr_states.Length; i++)
            {
                state[i] = (int)(zframeId / offsets[i]) % nr_states[i];
            }
            return state;
        }

        public void ShowOffsetAndLayersArrays(bool hex = true)
        {
            ShowIntArray(offsets, 8, "offsets", hex: hex);
            ShowIntArray(nr_states, 8, "layers");
        }
    }
}
