using System.Diagnostics;

namespace ValveResourceFormat.CompiledShader
{
    /*
     * Combo id to configuration mapping
     * ---------------------------------
     *
     * During parsing, the configuration mapping is applied to all vcs files that contain static combos
     * to identify the configuration that each static combo belongs to.
     * The basic idea for mapping combo ids to static configurations is by enumerating all possible
     * legal states and writing them (in order) next to the combo ids.
     *
     * For example if there are 3 static params (S1,S2,S3) that can each take two configurations (on or off)
     * they combine to give 8 possible configurations, the combo mapping will be
     *
     * comboId  S1 S2 S3
     *  0        0  0  0
     *  1        1  0  0
     *  2        0  1  0
     *  3        1  1  0
     *  4        0  0  1
     *  5        1  0  1
     *  6        0  1  1
     *  7        1  1  1
     *
     * Sometimes static params have more than two states, for example S_DETAIL_2 from the Dota2 file
     * hero_pcgl_30_vs.vcs can be assigned to one of three (None, Add, Add Self Illum). In our example,
     * if S2 is expanded to take the values (0,1,2) the number of possible configurations becomes 12 and a new
     * mapping can be written as
     *
     * comboId  S1 S2 S3
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
     * In most shader files some static combinations are not allowed. These are described by constraints specified
     * in the static combo rules. The most common types of constraints are mutual-exclusion and dependencies
     * between pairs of parameters.
     *
     * EXC(S1,S2) means S1 and S2 are mutually exclusive and cannot appear together
     * INC(S2,S3) means S2 is dependent on S3 and cannot appear without it (but S3 can still appear without S2).
     *
     * To determine the configuration mapping where constraints are defined; the constraints are applied to the
     * mapping by deleting the rows that are disallowed. Importantly, the values of the combo ids are left
     * unaltered. Applying this idea below, rows where S1 and S2 appeared together have been removed
     * and rows where S2 appeared without S3 have been removed.
     *
     *  comboId  S1 S2 S3
     *  0        0  0  0
     *  1        1  0  0
     *  6        0  0  1
     *  7        1  0  1
     *  8        0  1  1
     * 10        0  2  1
     *
     *
     * To calculate a configuration state from a combo id observe (before any constraints are applied) that
     * S1 changes every 1 id, S2 changes every 2 ids and S3 changes every 6 ids. The values (1,2,6) are the
     * number of successive ids that a state's digit is held constant, it is also equivalent to the offset where
     * a given state changes for the first time. (S1 first changes from 0 to 1 at offset=1, S2 first changes
     * from 0 to 1 at offset=2, and S3 first changes from 0 to 1 at offset=6). We collect these offsets together with
     * the number of states that each configuration can assume.
     *
     *            S1[0]       S2[1]       S3[2]
     * offset        1           2           6
     * nr_states     2           3           2
     *
     * The state belonging to a given combo id can then be found as
     *
     *       state[i] = comboId / offset[i] % nr_states[i]
     *
     * (where comboId / offset[i] is an integer division - the remainder is discarded)
     *
     *
     * Substituting comboId = 10
     * S1 = 10 / offset[0] % nr_states[0] = 10 / 1 % 2 = 0
     * S2 = 10 / offset[1] % nr_states[1] = 10 / 2 % 3 = 2
     * S3 = 10 / offset[2] % nr_states[2] = 10 / 6 % 2 = 1
     *
     *
     *
     * Dynamic configurations
     * ----------------------
     * The same approach is also used to map from the dynamic configuration to glsl (or given platform) source.
     * That is, the shader file ids within the static combos enumerate and map in the same way to dynamic
     * configurations (these have their own constraints described by the dynamic combo rules).
     *
     *
     */
    /// <summary>
    /// Maps shader configuration states to combo IDs.
    /// </summary>
    public class ComboConfigMapping
    {
        /// <summary>
        /// Initializes a new instance for static configurations.
        /// </summary>
        public ComboConfigMapping(VfxProgramData program) : this(program, isDynamic: false)
        {
            //
        }

        /// <summary>
        /// Initializes a new instance for static or dynamic configurations.
        /// </summary>
        public ComboConfigMapping(VfxProgramData program, bool isDynamic)
        {
            GenerateOffsetAndStateLookups(isDynamic ? program.DynamicComboArray : program.StaticComboArray);
        }

        /*
         *
         * for example for water_dota_pcgl_30_ps.vcs
         *
         * combo-index = [0    1    2    3    4    5    6    7    8    9   10   11]
         * offsets =     [1    1    2    4    8   16   32   64  128  384  768 1536]
         * stateCounts = [1    2    2    2    2    2    2    2    3    2    2    2]
         *
         * Note S_TOOLS_ENABLED only has one state (which is off). It appears to be disabled (possibly
         * because it is a dev parameter), it's also possible that it is controlled by external arguments.
         *
         *
         * for blur_pcgl_30_ps.vcs (core)
         * offsets     = [1    5]
         * stateCounts = [5    2]
         *
         */
        private void GenerateOffsetAndStateLookups(VfxCombo[] combos)
        {
            if (combos.Length == 0)
            {
                return;
            }

            offsets = new int[combos.Length];
            stateCounts = new int[combos.Length];
            rangeMins = new int[combos.Length];

            offsets[0] = 1;
            stateCounts[0] = combos[0].RangeMax - combos[0].RangeMin + 1;
            rangeMins[0] = combos[0].RangeMin;

            for (var i = 1; i < combos.Length; i++)
            {
                stateCounts[i] = combos[i].RangeMax - combos[i].RangeMin + 1;
                offsets[i] = offsets[i - 1] * stateCounts[i - 1];
                rangeMins[i] = combos[i].RangeMin;
            }

            for (var i = 0; i < combos.Length; i++)
            {
                Debug.Assert(combos[i].ComboIndexValue == offsets[i]);
            }
        }

        /*
         * getting the config state is not dependent on processing the configuration constraints (but is useful for verification)
         * It is much more efficient to move from a known combo id to a configuration state
         */
        /// <summary>
        /// Gets the configuration state for a given combo ID.
        /// </summary>
        public int[] GetConfigState(long comboId)
        {
            var state = new int[stateCounts.Length];
            for (var i = 0; i < stateCounts.Length; i++)
            {
                state[i] = (int)(comboId / offsets[i] % stateCounts[i]) + rangeMins[i];
            }

            return state;
        }

        /// <summary>
        /// Calculates a static combo ID from configuration state values.
        /// </summary>
        public long CalcComboIdFromValues(int[] configState)
        {
            Debug.Assert(configState.Length == stateCounts.Length);

            var staticComboId = 0L;
            for (var i = 0; i < stateCounts.Length; i++)
            {
                staticComboId += (configState[i] - rangeMins[i]) * offsets[i];
            }

            return staticComboId;
        }

        int[] offsets = [];
        int[] stateCounts = [];
        int[] rangeMins = [];
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
         * possible combo id values are up to this value,
         * but not equal or exceeding
         *
         */
        /// <summary>
        /// Gets the maximum combo enumeration value.
        /// </summary>
        public int TotalComboCount()
        {
            return stateCounts[^1] * offsets[^1];
        }

        /*
        bool CheckComboId(int comboId)
        {
            var state = GetConfigState(comboId);
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

        /// <summary>
        /// Gets the sum of all state counts.
        /// </summary>
        public int TotalStateCount
        {
            get
            {
                var sum = 0;
                for (var i = 0; i < stateCounts.Length; i++)
                {
                    sum += stateCounts[i];
                }
                return sum;
            }
        }
    }
}
