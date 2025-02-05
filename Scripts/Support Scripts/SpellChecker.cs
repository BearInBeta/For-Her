using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

class SpellChecker
{
    private HashSet<string> dictionary;

    public SpellChecker(IEnumerable<string> words)
    {
        dictionary = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
    }

    public string GetBestCorrection(string word)
    {
        if (dictionary.Contains(word))
        {
            return word; // Already correct
        }

        var bestMatch = dictionary
            .Select(w => new { Word = w, Distance = LevenshteinDistance(word, w) })
            .OrderBy(x => x.Distance)
            .FirstOrDefault();

        if (bestMatch != null)
        {
            double similarity = 1.0 - (double)bestMatch.Distance / Math.Max(word.Length, bestMatch.Word.Length);
            if (similarity >= 0.7)
            {
                return bestMatch.Word;
            }
        }

        return word; // Return the same word if no close match is found
    }

    private int LevenshteinDistance(string s1, string s2)
    {
        int[,] dp = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            dp[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++)
            dp[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost
                );
            }
        }

        return dp[s1.Length, s2.Length];
    }
}
