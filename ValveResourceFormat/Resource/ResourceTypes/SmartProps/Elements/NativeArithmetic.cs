namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// Float to int conversion and sorting with the results of the native x64 runtime, where they decide counts and order.
    /// </summary>
    internal static class NativeArithmetic
    {
        /// <summary>Truncates toward zero; NaN, infinities and values outside the int range give <see cref="int.MinValue"/>.</summary>
        public static int Truncate(float value) => value is >= -2147483648f and < 2147483648f ? (int)value : int.MinValue;

        /// <summary>Absolute value that leaves <see cref="int.MinValue"/> negative.</summary>
        public static int Abs(int value) => value > 0 ? value : unchecked(-value);

        /// <summary>
        /// Sorts in place with the C runtime's <c>qsort</c> algorithm, so elements the comparison does not order end
        /// up where that implementation leaves them.
        /// </summary>
        public static void QuickSort<T>(Span<T> items, Comparison<T> compare)
        {
            const int Cutoff = 8;

            if (items.Length < 2)
            {
                return;
            }

            Span<int> lowStack = stackalloc int[64];
            Span<int> highStack = stackalloc int[64];
            var stackDepth = 0;
            var lo = 0;
            var hi = items.Length - 1;

            while (true)
            {
                var size = hi - lo + 1;

                if (size <= Cutoff)
                {
                    ShortSort(items, lo, hi, compare);
                }
                else
                {
                    var mid = lo + size / 2;

                    if (compare(items[lo], items[mid]) > 0)
                    {
                        (items[lo], items[mid]) = (items[mid], items[lo]);
                    }

                    if (compare(items[lo], items[hi]) > 0)
                    {
                        (items[lo], items[hi]) = (items[hi], items[lo]);
                    }

                    if (compare(items[mid], items[hi]) > 0)
                    {
                        (items[mid], items[hi]) = (items[hi], items[mid]);
                    }

                    var loGuy = lo;
                    var hiGuy = hi;

                    while (true)
                    {
                        if (mid > loGuy)
                        {
                            do
                            {
                                loGuy++;
                            }
                            while (loGuy < mid && compare(items[loGuy], items[mid]) <= 0);
                        }

                        if (mid <= loGuy)
                        {
                            do
                            {
                                loGuy++;
                            }
                            while (loGuy <= hi && compare(items[loGuy], items[mid]) <= 0);
                        }

                        do
                        {
                            hiGuy--;
                        }
                        while (hiGuy > mid && compare(items[hiGuy], items[mid]) > 0);

                        if (hiGuy < loGuy)
                        {
                            break;
                        }

                        (items[loGuy], items[hiGuy]) = (items[hiGuy], items[loGuy]);

                        if (mid == hiGuy)
                        {
                            mid = loGuy;
                        }
                    }

                    hiGuy++;

                    if (mid < hiGuy)
                    {
                        do
                        {
                            hiGuy--;
                        }
                        while (hiGuy > mid && compare(items[hiGuy], items[mid]) == 0);
                    }

                    if (mid >= hiGuy)
                    {
                        do
                        {
                            hiGuy--;
                        }
                        while (hiGuy > lo && compare(items[hiGuy], items[mid]) == 0);
                    }

                    if (hiGuy - lo >= hi - loGuy)
                    {
                        if (lo < hiGuy)
                        {
                            lowStack[stackDepth] = lo;
                            highStack[stackDepth] = hiGuy;
                            stackDepth++;
                        }

                        if (loGuy < hi)
                        {
                            lo = loGuy;
                            continue;
                        }
                    }
                    else
                    {
                        if (loGuy < hi)
                        {
                            lowStack[stackDepth] = loGuy;
                            highStack[stackDepth] = hi;
                            stackDepth++;
                        }

                        if (lo < hiGuy)
                        {
                            hi = hiGuy;
                            continue;
                        }
                    }
                }

                if (--stackDepth < 0)
                {
                    return;
                }

                lo = lowStack[stackDepth];
                hi = highStack[stackDepth];
            }
        }

        private static void ShortSort<T>(Span<T> items, int lo, int hi, Comparison<T> compare)
        {
            while (hi > lo)
            {
                var max = lo;

                for (var p = lo + 1; p <= hi; p++)
                {
                    if (compare(items[p], items[max]) > 0)
                    {
                        max = p;
                    }
                }

                (items[max], items[hi]) = (items[hi], items[max]);
                hi--;
            }
        }
    }
}
