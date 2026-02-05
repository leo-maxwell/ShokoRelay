using System.Collections.Generic;
using System.Linq;
using Shoko.Plugin.Abstractions.DataModels.Shoko;
using Shoko.Plugin.Abstractions.DataModels;

namespace ShokoRelay.Meta
{
    public static class PlexMapping
    {
        // Extra buckets used only when no TMDB match is present.
        private static readonly IReadOnlyDictionary<int, (string Folder, string Prefix)> ExtraSeasons =
            new Dictionary<int, (string Folder, string Prefix)>
            {
                { -1, ("Shorts", "Credits") },
                { -2, ("Trailers", "Trailers") },
                { -3, ("Scenes", "Parody") },
                { -4, ("Featurettes", "Other") },
                { -99, ("Other", "Unknown") }
            };

        public struct PlexCoords 
        { 
            public int Season; 
            public int Episode; 
            public int? EndEpisode; 
        }

        public static bool TryGetExtraSeason(int seasonNumber, out (string Folder, string Prefix) info)
        {
            return ExtraSeasons.TryGetValue(seasonNumber, out info);
        }

        public static string GetSeasonFolderName(int seasonNumber)
        {
            if (TryGetExtraSeason(seasonNumber, out var special))
                return special.Folder;

            if (seasonNumber == 0) return "Specials";
            return $"Season {seasonNumber}";
        }

        public static string GetSeasonTitle(int seasonNumber)
        {
            if (TryGetExtraSeason(seasonNumber, out var special))
                return special.Prefix;

            if (seasonNumber == 0) return "Specials";
            return $"Season {seasonNumber}";
        }

        public static PlexCoords GetPlexCoordinates(IEpisode e)
        {
            if (ShokoRelay.Settings.TMDBStructure && e is IShokoEpisode shokoEpisode)
            {
                var tmdbEpisodes = shokoEpisode.TmdbEpisodes
                    .OrderBy(te => te.EpisodeNumber)
                    .ToList();

                if (tmdbEpisodes.Count > 0)
                {
                    var first = tmdbEpisodes.First();
                    if (first.SeasonNumber.HasValue)
                    {
                        return new PlexCoords 
                        { 
                            Season = first.SeasonNumber.Value, 
                            Episode = first.EpisodeNumber,
                            EndEpisode = tmdbEpisodes.Count > 1 ? tmdbEpisodes.Last().EpisodeNumber : null
                        };
                    }
                }
            }

            int epNum = e.EpisodeNumber;
            int seasonNum = ResolveSeasonNumber(e);

            return e.Type switch
            {
                EpisodeType.Other   => new PlexCoords { Season = -4, Episode = epNum },
                EpisodeType.Credits => new PlexCoords { Season = -1, Episode = epNum },
                EpisodeType.Trailer => new PlexCoords { Season = -2, Episode = epNum },
                EpisodeType.Parody  => new PlexCoords { Season = -3, Episode = epNum },
                _                   => new PlexCoords { Season = seasonNum, Episode = epNum }
            };
        }

        public static PlexCoords GetPlexCoordinatesForFile(IEnumerable<IEpisode> episodes, int? fileIndexWithinEpisode = null)
        {
            var eps = (episodes ?? Enumerable.Empty<IEpisode>()).ToList();
            if (!eps.Any()) return new PlexCoords { Season = 1, Episode = 1, EndEpisode = null };

            if (ShokoRelay.Settings.TMDBStructure)
            {
                var tmdbEntries = eps
                    .OfType<IShokoEpisode>()
                    .Where(se => se.TmdbEpisodes != null && se.TmdbEpisodes.Any())
                    .SelectMany(se => se.TmdbEpisodes)
                    .OrderBy(te => te.SeasonNumber ?? 0)
                    .ThenBy(te => te.EpisodeNumber)
                    .ToList();

                if (tmdbEntries.Any())
                {
                    var first = tmdbEntries.First();
                    if (first.SeasonNumber.HasValue)
                    {
                        // If fileIndex is provided and within range, pick the specific offset
                        // Otherwise, return the full range (for single files mapping to multiple TMDB episodes)
                        if (fileIndexWithinEpisode.HasValue && fileIndexWithinEpisode.Value < tmdbEntries.Count)
                        {
                            var tmdbEp = tmdbEntries[fileIndexWithinEpisode.Value];
                            return new PlexCoords
                            {
                                Season = tmdbEp.SeasonNumber ?? first.SeasonNumber.Value,
                                Episode = tmdbEp.EpisodeNumber,
                                EndEpisode = null
                            };
                        }
                        else if (!fileIndexWithinEpisode.HasValue || tmdbEntries.Count == 1)
                        {
                            // No fileIndex provided (single file) or only one TMDB entry: return the range
                            var last = tmdbEntries.Last();
                            int? endEpisode = (tmdbEntries.Count > 1 && last.SeasonNumber == first.SeasonNumber) ? last.EpisodeNumber : (int?)null;
                            return new PlexCoords
                            {
                                Season = first.SeasonNumber.Value,
                                Episode = first.EpisodeNumber,
                                EndEpisode = endEpisode
                            };
                        }
                    }
                }
            }

            if (eps.Count == 1)
            {
                return GetPlexCoordinates(eps[0]);
            }

            var start = GetPlexCoordinates(eps.First());
            var end = GetPlexCoordinates(eps.Last());
            int? endEpisodeFinal = start.Season == end.Season ? end.Episode : (int?)null;

            return new PlexCoords
            {
                Season = start.Season,
                Episode = start.Episode,
                EndEpisode = endEpisodeFinal
            };
        }

        private static int ResolveSeasonNumber(IEpisode e)
        {
            // Prefer provider season numbers when available (covers regular episodes and specials).
            if (e.SeasonNumber.HasValue)
                return e.SeasonNumber.Value;

            return e.Type switch
            {
                EpisodeType.Episode =>  1,
                EpisodeType.Special =>  0,
                EpisodeType.Credits => -1,
                EpisodeType.Trailer => -2,
                EpisodeType.Parody  => -3,
                EpisodeType.Other   => -4,
                _                   => -99
            };
        }
    }
}