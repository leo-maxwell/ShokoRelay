using System.Text.RegularExpressions;
using Shoko.Plugin.Abstractions.DataModels;

namespace ShokoRelay.Helpers
{
    public static class TagHelper
    {
        // https://github.com/ShokoAnime/ShokoServer/blob/9c0ae9208479420dea3b766156435d364794e809/Shoko.Server/Utilities/TagFilter.cs#L37
        private static readonly IReadOnlySet<string> TagBlacklistAniDBHelpers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "asia",
            "awards",
            "body and host",
            "breasts",
            "cast missing",
            "cast",
            "complete manga adaptation",
            "content indicators",
            "delayed 16-9 broadcast",
            "description missing",
            "description needs improvement",
            "development hell",
            "dialogue driven",
            "dynamic",
            "earth",
            "elements",
            "ending",
            "ensemble cast",
            "family life",
            "fast-paced",
            "fetishes",
            "maintenance tags",
            "meta tags",
            "motifs",
            "no english subs available",
            "origin",
            "pic needs improvement",
            "place",
            "pornography",
            "season",
            "setting",
            "some weird shit goin' on",
            "source material",
            "staff missing",
            "storytelling",
            "tales",
            "target audience",
            "technical aspects",
            "themes",
            "time",
            "to be moved to character",
            "to be moved to episode",
            "translation convention",
            "tropes",
            "unsorted",
        };
        private static readonly Regex _wordRegex = new(@"[\'\w\d-]+\b", RegexOptions.Compiled);
        private static readonly IReadOnlySet<string> _forceLower = new HashSet<string>(
            [
                "a",
                "an",
                "the",
                "and",
                "but",
                "or",
                "nor",
                "at",
                "by",
                "for",
                "from",
                "in",
                "into",
                "of",
                "off",
                "on",
                "onto",
                "out",
                "over",
                "per",
                "to",
                "up",
                "with",
                "as",
                "4-koma",
                "-hime",
                "-kei",
                "-kousai",
                "-sama",
                "-warashi",
                "no",
                "vs",
                "x",
            ],
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly IReadOnlySet<string> _forceUpper = new HashSet<string>(
            ["3d", "bdsm", "cg", "cgi", "ed", "fff", "ffm", "ii", "milf", "mmf", "mmm", "npc", "op", "rpg", "tbs", "tv"],
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly IReadOnlyDictionary<string, string> _forceSpecial = new Dictionary<string, string>
        {
            { "comicfesta", "ComicFesta" },
            { "d'etat", "d'Etat" },
            { "noitamina", "noitaminA" },
        };

        public static object[] GetFilteredTags(ISeries series)
        {
            if (series.Tags == null)
                return Array.Empty<object>();

            var userBlacklist = ShokoRelay.Settings.TagBlacklist.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            return series
                .Tags.Select(t => t.Name)
                .Where(tagName => !string.IsNullOrWhiteSpace(tagName) && !TagBlacklistAniDBHelpers.Contains(tagName) && !userBlacklist.Contains(tagName, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(tagName => new { tag = TitleCase(tagName) })
                .ToArray<object>();
        }

        public static string TitleCase(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Primary Pass: Capitalize words and apply Upper/Lower lists
            string result = _wordRegex.Replace(
                text.ToLower(),
                m =>
                {
                    string word = m.Value;
                    if (_forceLower.Contains(word))
                        return word.ToLower(); // Convert words from force_lower to lowercase (follows AniDB capitalisation rules: https://wiki.anidb.net/Capitalisation)
                    if (_forceUpper.Contains(word))
                        return word.ToUpper(); // Convert words from force_upper to uppercase (abbreviations or acronyms that should be fully capitalised)

                    // Capitalise all words accounting for apostrophes first
                    return char.ToUpper(word[0]) + word.Substring(1);
                }
            );

            // Force capitalise the first character no matter what
            result = char.ToUpper(result[0]) + result.Substring(1);

            // Force capitalise the first character of the last word no matter what
            int lastSpaceIndex = result.LastIndexOf(' ');
            if (lastSpaceIndex >= 0 && lastSpaceIndex < result.Length - 1)
            {
                result = result.Substring(0, lastSpaceIndex + 1) + char.ToUpper(result[lastSpaceIndex + 1]) + result.Substring(lastSpaceIndex + 2);
            }

            // Apply special cases as a last step (where a specific capitalisation style is preferred)
            result = _wordRegex.Replace(
                result,
                m =>
                {
                    if (_forceSpecial.TryGetValue(m.Value, out var special))
                        return special;
                    return m.Value;
                }
            );

            return result;
        }
    }
}
