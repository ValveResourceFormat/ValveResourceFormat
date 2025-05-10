using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    /*
     * ZFrameId to static-configuration mapping
     * ----------------------------------------
     *
     * During parsing, the configuration mapping is applied to all vcs files that contain zframes
     * to identify the configuration that each zframes belongs to.
     * The basic idea for mapping zframe-Ids to static configurations is by enumerating all possible
     * legal states and writing them (in order) next to the zframes.
     *
     * For example if there are 3 static-params (S1,S2,S2) that can each take two configurations (on or off)
     * they combine to give 8 possible configurations, the zframe mapping will be
     *
     * zframeId S1 S2 S3
     *  0        0  0  0
     *  1        1  0  0
     *  2        0  1  0
     *  3        1  1  0
     *  4        0  0  1
     *  5        1  0  1
     *  6        0  1  1
     *  7        1  1  1
     *
     * Sometimes static-params have more than two states, for example S_DETAIL_2 from the Dota2 file
     * hero_pcgl_30_vs.vcs can be assigned to one of three (None, Add, Add Self Illum). In our example,
     * if S2 is expanded to take the values (0,1,2) the number of possible configurations becomes 12 and a new
     * mapping can be written as
     *
     * zframeId S1 S2 S3
     *  0        0  0  0
     *  1        1  0  0
     *  2        0  1  0
     *  3        1  1  0
     *  4        0  2  0
     *  5        1  2  0
     *  6        0  0  1
     *  7        1  0  1
     *  8        0  1  1
     *  9        1  1  1
     * 10        0  2  1
     * 11        1  2  1
     *
     * In most shader files some static-combinations are not allowed. These are described by constraints specified
     * in the the Sf-constraints blocks. The most common types of constraints are mutual-exclusion and dependencies
     * between pairs of parameters.
     *
     * EXC(S1,S2) means S1 and S2 are mutually exclusive and cannot appear together
     * INC(S2,S3) means S2 is dependent on S3 and cannot appear without it (but S3 can still appear without S2).
     *
     * To determine the configuration mapping where constraints are defined; the constraints are applied to the
     * mapping by deleting the rows that are disallowed. Importantly, the values of the zframeId's are left
     * unaltered. Applying this idea below, rows where S1 and S2 appeared together have been removed
     * and rows where S2 appeared without S3 have been removed.
     *
     *  zframeId S1 S2 S3
     *  0        0  0  0
     *  1        1  0  0
     *  6        0  0  1
     *  7        1  0  1
     *  8        0  1  1
     * 10        0  2  1
     *
     *
     * To calculate a configuration state from a zframeId observe (before any constraints are applied) that
     * S1 changes every 1 frame, S2 changes every 2 frames and S3 changes every 6 frames. The values (1,2,6) are the
     * number of successive frames that a state's digit is held constant, it is also equivalent to the offset where
     * a given state changes for the first time. (S1 first changes from 0 to 1 at offset=1, S2 first changes
     * from 0 to 1 at offset=2, and S3 first changes from 0 to 1 at offset=6). We collect these offsets together with
     * the number of states that each configuration can assume.
     *
     *            S1[0]       S2[1]       S3[2]
     * offset        1           2           6
     * nr_states     2           3           2
     *
     * The state belonging to a given zframeId can then be found as
     *
     *       state[i] = zframeId / offset[i] % nr_states[i]
     *
     * (where zframeId / offset[i] is an integer division - the remainder is discarded)
     *
     *
     * Substituting zframeId = 10
     * S1 = 10 / offset[0] % nr_states[0] = 10 / 1 % 2 = 0
     * S2 = 10 / offset[1] % nr_states[1] = 10 / 2 % 3 = 2
     * S3 = 10 / offset[2] % nr_states[2] = 10 / 6 % 2 = 1
     *
     *
     *
     * Dynamic-configurations
     * ----------------------
     * The same approach is also used to map from the dynamic-configuration to glsl (or given platform) source.
     * That is, the source-ids within the zframes enumerates and maps in the same way to dynamic-configurations
     * (these have their own constraints described by the 'DConstraintsBlocks'). The data that matches
     * dynamic configurations in the zframe files are called 'data-blocks'.
     *
     *
     */
    public class ConfigMappingSParams
    {
        public ConfigMappingSParams(VfxProgramData shaderfile)
        {
            GenerateOffsetAndStateLookups(shaderfile);
        }

        /*
         *
         * for example for water_dota_pcgl_30_ps.vcs
         *
         * sf-index =   [0    1    2    3    4    5    6    7    8    9   10   11]
         * offsets =    [1    1    2    4    8   16   32   64  128  384  768 1536]
         * nr_states =  [1    2    2    2    2    2    2    2    3    2    2    2]
         *
         * Note S_TOOLS_ENABLED only has one state (which is off). It appears to be disabled (possibly
         * because it is a dev parameter), it's also possible that it is controlled by an external arguments.
         *
         *
         * for blur_pcgl_30_ps.vcs (core)
         * offsets   = [1    5]
         * nr_states = [5    2]
         *
         */
        private void GenerateOffsetAndStateLookups(VfxProgramData shaderFile)
        {
            if (shaderFile.StaticCombos.Count == 0)
            {
                offsets = [];
                nr_states = [];
                return;
            }

            offsets = new int[shaderFile.StaticCombos.Count];
            nr_states = new int[shaderFile.StaticCombos.Count];

            offsets[0] = 1;
            nr_states[0] = shaderFile.StaticCombos[0].RangeMax + 1;

            for (var i = 1; i < shaderFile.StaticCombos.Count; i++)
            {
                nr_states[i] = shaderFile.StaticCombos[i].RangeMax + 1;
                offsets[i] = offsets[i - 1] * nr_states[i - 1];
            }
        }

        /*
         * getting the config state is not dependent on processing the configuration constraints (but is useful for verification)
         * It is much more efficient to move from a known zframeId to a configuration state
         */
        public int[] GetConfigState(long zframeId)
        {
            var state = new int[nr_states.Length];
            for (var i = 0; i < nr_states.Length; i++)
            {
                state[i] = (int)(zframeId / offsets[i] % nr_states[i]);
            }
            return state;
        }

        public long GetZframeId(int[] configState)
        {
            var zframeId = 0L;
            for (var i = 0; i < nr_states.Length; i++)
            {
                zframeId += configState[i] * offsets[i];
            }
            return zframeId;
        }

        int[] offsets;
        int[] nr_states;
        /*
        readonly bool[,] exclusions = new bool[100, 100];
        readonly bool[,] inclusions = new bool[100, 100];
        void AddExclusionRule(int s1, int s2, int s3)
        {
            AddExclusionRule(s1, s2);
            AddExclusionRule(s1, s3);
            AddExclusionRule(s2, s3);
        }
        void AddExclusionRule(int s1, int s2)
        {
            exclusions[s1, s2] = true;
            exclusions[s2, s1] = true;
        }
        void AddInclusionRule(int s1, int s2)
        {
            inclusions[s1, s2] = true;
        }
        */

        /*
         * possible zframe values are upto this value,
         * but not equal or exceeding
         *
         */
        public int MaxEnumeration()
        {
            return nr_states[^1] * offsets[^1];
        }

        /*
        bool CheckZFrame(int zframe)
        {
            var state = GetConfigState(zframe);
            // checking exclusion rules
            for (var j = 2; j < offsets.Length; j++)
            {
                for (var i = 1; i < j; i++)
                {
                    var s1 = state[i];
                    var s2 = state[j];
                    if (s1 == 0 || s2 == 0)
                    {
                        continue;
                    }
                    if (exclusions[i, j] == true)
                    {
                        return false;
                    }
                    if (inclusions[i, j] == true)
                    {
                        return false;
                    }
                }
            }
            // checking inclusion rules
            for (var i = 1; i < offsets.Length; i++)
            {
                var s1 = state[i];
                if (s1 == 0)
                {
                    continue;
                }

                for (var j = 1; j < offsets.Length; j++)
                {
                    if (inclusions[i, j] && state[j] == 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        */

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

        public void ShowOffsetAndNrStatesArrays()
        {
            ShowIntArray(offsets, 8, "offsets", hex: true);
            ShowIntArray(nr_states, 8, "nr_states");

        }
    }
}
