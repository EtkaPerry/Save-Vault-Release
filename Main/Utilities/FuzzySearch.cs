using System;
using System.Collections.Generic;
using System.Linq;

namespace SaveVaultApp.Utilities
{
    /// <summary>
    /// Provides fuzzy search capabilities for improved search quality
    /// </summary>
    public static class FuzzySearch
    {
        /// <summary>
        /// Match result with score and relevance information
        /// </summary>
        public class MatchResult
        {
            public bool IsMatch { get; set; }
            public int Score { get; set; }
            public MatchType MatchType { get; set; }
        }

        /// <summary>
        /// Types of matches in order of relevance
        /// </summary>
        public enum MatchType
        {
            Exact = 0,          // Exact match (highest relevance)
            StartsWith = 1,     // Starts with the search term
            Contains = 2,       // Contains the search term
            Fuzzy = 3,          // Fuzzy match (lowest relevance)
            NoMatch = 4         // No match at all
        }

        /// <summary>
        /// Searches a text for a query using fuzzy matching.
        /// </summary>
        /// <param name="text">The text to search in</param>
        /// <param name="query">The query to search for</param>
        /// <param name="fuzzyThreshold">Maximum Levenshtein distance for fuzzy matches (default: 2)</param>
        /// <returns>Match result containing score and match type information</returns>
        public static MatchResult Match(string text, string query, int fuzzyThreshold = 2)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
            {
                return new MatchResult { IsMatch = false, Score = int.MaxValue, MatchType = MatchType.NoMatch };
            }

            text = text.Trim();
            query = query.Trim();

            // Case insensitive comparison
            string textLower = text.ToLowerInvariant();
            string queryLower = query.ToLowerInvariant();

            // Check for exact match
            if (textLower == queryLower)
            {
                return new MatchResult { IsMatch = true, Score = 0, MatchType = MatchType.Exact };
            }

            // Check for starts with
            if (textLower.StartsWith(queryLower))
            {
                return new MatchResult { IsMatch = true, Score = 1, MatchType = MatchType.StartsWith };
            }

            // Check for contains
            if (textLower.Contains(queryLower))
            {
                // Score based on position of match (earlier is better)
                int position = textLower.IndexOf(queryLower);
                int score = 2 + position;
                return new MatchResult { IsMatch = true, Score = score, MatchType = MatchType.Contains };
            }

            // If we get here, we need fuzzy matching
            int distance = LevenshteinDistance(textLower, queryLower);
            
            // Normalize the distance by the length of the query for fair comparison
            double normalizedDistance = (double)distance / queryLower.Length;
            
            // Determine if it's a match based on threshold and text length
            bool isMatch = distance <= fuzzyThreshold || normalizedDistance <= 0.4;
            
            return new MatchResult
            {
                IsMatch = isMatch,
                // Higher score means lower relevance
                Score = 100 + distance,
                MatchType = isMatch ? MatchType.Fuzzy : MatchType.NoMatch
            };
        }

        /// <summary>
        /// Tokenizes the search query into individual terms for multi-term searching
        /// </summary>
        public static string[] TokenizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            // Split by whitespace and remove empty terms
            return query.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Calculates the Levenshtein distance between two strings
        /// </summary>
        private static int LevenshteinDistance(string s, string t)
        {
            // Early exit for empty strings
            if (string.IsNullOrEmpty(s))
                return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t))
                return s.Length;

            // For all i and j, d[i,j] holds the Levenshtein distance between
            // the first i characters of s and the first j characters of t.
            var d = new int[s.Length + 1, t.Length + 1];

            // Populate first row and column
            for (var i = 0; i <= s.Length; i++)
                d[i, 0] = i;
            for (var j = 0; j <= t.Length; j++)
                d[0, j] = j;

            // Compute the distance
            for (var j = 1; j <= t.Length; j++)
            {
                for (var i = 1; i <= s.Length; i++)
                {
                    var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[s.Length, t.Length];
        }
    }
}
