using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GooglePal.Components
{
    internal class Levenshtein
    {

        public static int startsWithLevenshtein(string val1, string val2)
        {
            // initialise
            int dist = int.MaxValue;

            // compare the first part of the two
            if (val1.Length > val2.Length)
                dist = ComputeDistanceLevenshtein(
                    val2,
                    val1.Substring(0, val2.Length)
                );
            else
                dist = ComputeDistanceLevenshtein(
                    val1,
                    val2.Substring(0, val1.Length)
                );

            return dist;
        }

        /// <summary>
        ///     Fuzzy searches to find if the two strings are equal.  If either string <br />
        ///     is longer, it will be trimmed (hence the "startsWith").
        /// </summary>
        /// <param name="val1">The first string to compare.</param>
        /// <param name="val2">The second string to compare.</param>
        /// <param name="similarity">The number of characters that can be different between the two strings.</param>
        /// <returns>A boolean which is true when the two strings are matching, and false if not.</returns>
        public static bool startsWithLevenshtein(string val1, string val2, int similarity)
        {
            int dist = startsWithLevenshtein(val1: val1, val2: val2);

            int len = Math.Min(val1.Length, val2.Length );

            
            return dist <= Math.Clamp(value: len - similarity, min: 0, max: similarity);
        }

        // https://www.dotnetperls.com/levenshtein
        public static int ComputeDistanceLevenshtein(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            // Verify arguments.
            if (n == 0)
            {
                return m;
            }

            if (m == 0)
            {
                return n;
            }

            // Initialize arrays.
            for (int i = 0; i <= n; d[i, 0] = i++)
            {
            }

            for (int j = 0; j <= m; d[0, j] = j++)
            {
            }

            // Begin looping.
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    // Compute cost.
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
                }
            }
            // Return cost.
            return d[n, m];
        }

    }
}
