using Microsoft.AspNetCore.Mvc;
using Shoko.Plugin.Abstractions.DataModels;
using Shoko.Plugin.Abstractions.DataModels.Shoko;
using Shoko.Plugin.Abstractions.Enums;
using Shoko.Plugin.Abstractions.Services;
using ShokoRelay.AnimeThemes;
using ShokoRelay.Helpers;
using ShokoRelay.Meta;
using static ShokoRelay.Helpers.MapHelper;
using static ShokoRelay.Meta.PlexMapping;

namespace ShokoRelay.Controllers
{
    # region Models
    public record PlexMatchBody(string? Filename);

    public record SeriesContext(ISeries Series, string ApiUrl, (string DisplayTitle, string SortTitle, string? OriginalTitle) Titles, string ContentRating, SeriesFileData FileData);

    #endregion

    [ApiVersion("3.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class ShokoRelayController : ControllerBase
    {
        private readonly IVideoService _videoService;
        private readonly IMetadataService _metadataService;
        private readonly PlexMetadata _mapper;
        private readonly VfsBuilder _vfsBuilder;
        private readonly AnimeThemesGenerator _animeThemesGenerator;
        private readonly AnimeThemesMapping _animeThemesMapping;

        private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

        private const string SeasonPrefix = PlexConstants.SeasonPrefix;
        private const string EpisodePrefix = PlexConstants.EpisodePrefix;
        private const string PartPrefix = PlexConstants.PartPrefix;

        public ShokoRelayController(
            IVideoService videoService,
            IMetadataService metadataService,
            PlexMetadata mapper,
            VfsBuilder vfsBuilder,
            AnimeThemesGenerator animeThemeGenerator,
            AnimeThemesMapping animeThemesMapping
        )
        {
            _videoService = videoService;
            _metadataService = metadataService;
            _mapper = mapper;
            _vfsBuilder = vfsBuilder;
            _animeThemesGenerator = animeThemeGenerator;
            _animeThemesMapping = animeThemesMapping;
        }

        [Route("match")]
        [HttpPost]
        [HttpGet]
        public IActionResult Match([FromQuery] string? name, [FromBody] PlexMatchBody? body = null)
        {
            string? rawPath = name ?? body?.Filename;
            if (string.IsNullOrWhiteSpace(rawPath))
                return EmptyMatch();

            int? fileId = TextHelper.ExtractFileId(rawPath);
            if (!fileId.HasValue)
                return EmptyMatch();

            var video = _videoService.GetVideoByID(fileId.Value);
            var series = video?.Series?.FirstOrDefault();

            if (series == null)
                return EmptyMatch();

            var poster = (series as IWithImages)?.GetImages(ImageEntityType.Poster).FirstOrDefault();

            return Ok(
                new
                {
                    MediaContainer = new
                    {
                        size = 1,
                        identifier = ShokoRelayInfo.AgentScheme,
                        Metadata = new[]
                        {
                            new
                            {
                                guid = _mapper.GetGuid("show", series.ID),
                                title = series.PreferredTitle,
                                year = series.AirDate?.Year,
                                score = 100,
                                thumb = poster != null ? ImageHelper.GetImageUrl(poster) : null,
                            },
                        },
                    },
                }
            );
        }

        private SeriesContext? GetSeriesContext(string ratingKey)
        {
            int seriesId;

            if (ratingKey.StartsWith(EpisodePrefix))
            {
                var epPart = ratingKey.Substring(EpisodePrefix.Length);
                if (epPart.Contains(PartPrefix))
                    epPart = epPart.Split(PartPrefix)[0];

                var ep = _metadataService.GetShokoEpisodeByID(int.Parse(epPart));
                if (ep?.Series == null)
                    return null;
                seriesId = ep.Series.ID;
            }
            else if (ratingKey.Contains(SeasonPrefix))
            {
                if (!int.TryParse(ratingKey.Split(SeasonPrefix)[0], out seriesId))
                    return null;
            }
            else
            {
                if (!int.TryParse(ratingKey, out seriesId))
                    return null;
            }

            var series = _metadataService.GetShokoSeriesByID(seriesId);
            if (series == null)
                return null;

            return new SeriesContext(series, BaseUrl, TextHelper.ResolveFullSeriesTitles(series), RatingHelper.GetContentRatingAndAdult(series).Rating ?? "", GetSeriesFileData(series));
        }

        private IActionResult WrapInContainer(object metadata) =>
            Ok(
                new
                {
                    MediaContainer = new
                    {
                        size = 1,
                        totalSize = 1,
                        offset = 0,
                        identifier = ShokoRelayInfo.AgentScheme,
                        Metadata = new[] { metadata },
                    },
                }
            );

        private IActionResult EmptyMatch() => Ok(new { MediaContainer = new { size = 0, Metadata = Array.Empty<object>() } });

        private IActionResult WrapInPagedContainer(IEnumerable<object> metadataList)
        {
            int start =
                int.TryParse(Request.Headers["X-Plex-Container-Start"], out var s) ? s
                : int.TryParse(Request.Query["X-Plex-Container-Start"], out var sq) ? sq
                : 0;

            int size =
                int.TryParse(Request.Headers["X-Plex-Container-Size"], out var z) ? z
                : int.TryParse(Request.Query["X-Plex-Container-Size"], out var zq) ? zq
                : 50;

            var allItems = metadataList.ToList();
            var pagedData = allItems.Skip(start).Take(size).ToArray();

            return Ok(
                new
                {
                    MediaContainer = new
                    {
                        offset = start,
                        totalSize = allItems.Count,
                        identifier = ShokoRelayInfo.AgentScheme,
                        size = pagedData.Length,
                        Metadata = pagedData,
                    },
                }
            );
        }

        [HttpGet]
        public IActionResult GetMediaProvider()
        {
            var supportedTypes = new[] { PlexConstants.TypeShow, PlexConstants.TypeSeason, PlexConstants.TypeEpisode, PlexConstants.TypeCollection };

            var typePayload = supportedTypes.Select(t => new { type = t, Scheme = new[] { new { scheme = ShokoRelayInfo.AgentScheme } } });

            var featurePayload = new[] { new { type = "metadata", key = "/metadata" }, new { type = "match", key = "/matches" }, new { type = "collection", key = "/collections" } };

            return Ok(
                new
                {
                    MediaProvider = new
                    {
                        identifier = ShokoRelayInfo.AgentScheme,
                        title = ShokoRelayInfo.Name,
                        version = ShokoRelayInfo.Version,
                        Types = typePayload,
                        Feature = featurePayload,
                    },
                }
            );
        }

        [HttpGet("collections/{groupId}")]
        public IActionResult GetCollection(int groupId)
        {
            var group = _metadataService.GetShokoGroupByID(groupId);
            if (group == null)
                return NotFound();

            var primarySeries = group.MainSeries ?? group.Series?.FirstOrDefault();
            if (primarySeries == null)
                return NotFound();

            var meta = _mapper.MapCollection(group, primarySeries);

            return WrapInContainer(meta);
        }

        [HttpGet("metadata/{ratingKey}")]
        public IActionResult GetMetadata(string ratingKey, [FromQuery] int includeChildren = 0)
        {
            var ctx = GetSeriesContext(ratingKey);
            if (ctx == null)
                return NotFound();

            // --- EPISODE ---
            if (ratingKey.StartsWith(EpisodePrefix))
            {
                var epPart = ratingKey.Substring(EpisodePrefix.Length);
                int? partIndex = null;

                if (epPart.Contains(PartPrefix))
                {
                    var parts = epPart.Split(PartPrefix);
                    epPart = parts[0];
                    partIndex = int.Parse(parts[1]);
                }

                var episode = _metadataService.GetShokoEpisodeByID(int.Parse(epPart));
                if (episode == null)
                    return NotFound();

                var coords = GetPlexCoordinates(episode);
                object? tmdbEpisode = null;

                if (partIndex.HasValue && ShokoRelay.Settings.TMDBStructure && episode is IShokoEpisode shokoEp && shokoEp.TmdbEpisodes?.Any() == true)
                {
                    var tmdbEps = shokoEp.TmdbEpisodes.OrderBy(te => te.SeasonNumber ?? 0).ThenBy(te => te.EpisodeNumber).ToList();

                    int idx = partIndex.Value - 1;
                    if (idx < tmdbEps.Count)
                    {
                        var tmdbEp = tmdbEps[idx];
                        tmdbEpisode = tmdbEp;
                        if (tmdbEp.SeasonNumber.HasValue)
                            coords = new PlexCoords { Season = tmdbEp.SeasonNumber.Value, Episode = tmdbEp.EpisodeNumber };
                    }
                }

                return WrapInContainer(_mapper.MapEpisode(episode, coords, ctx.Series, ctx.Titles, partIndex, tmdbEpisode));
            }

            // --- SEASON ---
            if (ratingKey.Contains(SeasonPrefix))
            {
                int sNum = int.Parse(ratingKey.Split(SeasonPrefix)[1]);
                var seasonMeta = _mapper.MapSeason(ctx.Series, sNum, ctx.Titles.DisplayTitle);

                if (includeChildren == 1)
                {
                    var episodes = BuildEpisodeList(ctx, sNum);
                    ((IDictionary<string, object?>)seasonMeta)["Children"] = new { size = episodes.Count, Metadata = episodes };
                }

                return WrapInContainer(seasonMeta);
            }

            // --- SERIES ---
            var showMeta = _mapper.MapSeries(ctx.Series, ctx.Titles);

            if (includeChildren == 1)
            {
                var seasons = ctx.FileData.Seasons.Select(s => _mapper.MapSeason(ctx.Series, s, ctx.Titles.DisplayTitle)).ToList();

                ((IDictionary<string, object?>)showMeta)["Children"] = new { size = seasons.Count, Metadata = seasons };
            }

            return WrapInContainer(showMeta);
        }

        [HttpGet("metadata/{ratingKey}/children")]
        public IActionResult GetChildren(string ratingKey)
        {
            var ctx = GetSeriesContext(ratingKey);
            if (ctx == null)
                return NotFound();

            if (ratingKey.Contains(SeasonPrefix))
            {
                int sNum = int.Parse(ratingKey.Split(SeasonPrefix)[1]);
                return WrapInPagedContainer(BuildEpisodeList(ctx, sNum));
            }

            var seasons = ctx.FileData.Seasons.Select(s => _mapper.MapSeason(ctx.Series, s, ctx.Titles.DisplayTitle)).ToList();

            return WrapInPagedContainer(seasons);
        }

        [HttpGet("metadata/{ratingKey}/grandchildren")]
        public IActionResult GetGrandchildren(string ratingKey)
        {
            var ctx = GetSeriesContext(ratingKey);
            if (ctx == null)
                return NotFound();

            var allEpisodes = ctx
                .FileData.Mappings.OrderBy(m => m.Coords.Season)
                .ThenBy(m => m.Coords.Episode)
                .Select(m => _mapper.MapEpisode(m.PrimaryEpisode, m.Coords, ctx.Series, ctx.Titles, m.PartIndex, m.TmdbEpisode))
                .ToList();

            return WrapInPagedContainer(allEpisodes);
        }

        [HttpGet("animethemes")]
        public async Task<IActionResult> GetAnimeThemes([FromQuery] AnimeThemesQuery query, CancellationToken cancellationToken = default)
        {
            if (query.Mapping)
            {
                string defaultBase = !string.IsNullOrWhiteSpace(ShokoRelay.Settings.AnimeThemesBasePath) ? ShokoRelay.Settings.AnimeThemesBasePath : AnimeThemesConstants.BasePath;

                string root = query.TorrentRoot ?? query.Path ?? defaultBase;
                var result = await _animeThemesMapping.BuildMappingFileAsync(root, query.MapPath, cancellationToken);
                return Ok(result);
            }

            if (query.ApplyMapping)
            {
                string defaultBase = !string.IsNullOrWhiteSpace(ShokoRelay.Settings.AnimeThemesBasePath) ? ShokoRelay.Settings.AnimeThemesBasePath : AnimeThemesConstants.BasePath;

                string? sourceRoot = query.TorrentRoot ?? query.Path ?? defaultBase;
                var result = await _animeThemesMapping.ApplyMappingAsync(query.MapPath, sourceRoot, query.DryRun, cancellationToken);
                return Ok(result);
            }

            if (string.IsNullOrWhiteSpace(query.Path))
                return BadRequest(new { status = "error", message = "path is required" });

            if (query.Play && query.Batch)
                return BadRequest(new { status = "error", message = "play is not supported in batch mode" });

            if (query.Batch)
            {
                var batch = await _animeThemesGenerator.ProcessBatchAsync(query, cancellationToken);
                return Ok(batch);
            }

            if (query.Play)
            {
                var preview = await _animeThemesGenerator.PreviewAsync(query, cancellationToken);
                if (preview.Error != null)
                    return BadRequest(preview.Error);

                if (preview.Preview == null)
                    return NotFound(new { status = "error", message = "Preview failed." });

                if (!string.IsNullOrWhiteSpace(preview.Preview.Title))
                    Response.Headers["X-Theme-Title"] = preview.Preview.Title;

                return File(preview.Preview.Stream, preview.Preview.ContentType, preview.Preview.FileName, enableRangeProcessing: true);
            }

            var single = await _animeThemesGenerator.ProcessSingleAsync(query, cancellationToken);
            if (single.Status == "error")
                return BadRequest(single);

            return Ok(single);
        }

        [HttpGet("vfs")]
        public IActionResult BuildVfs([FromQuery] int? seriesId = null, [FromQuery] bool clean = true, [FromQuery] bool dryRun = false, [FromQuery] bool run = false)
        {
            if (!run)
            {
                return Ok(
                    new
                    {
                        status = "skipped",
                        message = "Set run=true to build the VFS -OR- dryRun=true to simulate without making changes",
                        seriesId,
                        clean,
                        dryRun,
                    }
                );
            }

            var result = _vfsBuilder.Build(seriesId, clean, dryRun);
            return Ok(
                new
                {
                    status = "ok",
                    root = result.RootPath,
                    seriesProcessed = result.SeriesProcessed,
                    linksCreated = result.CreatedLinks,
                    plannedLinks = result.PlannedLinks,
                    skipped = result.Skipped,
                    dryRun = result.DryRun,
                    reportPath = result.ReportPath,
                    report = result.ReportContent,
                    errors = result.Errors,
                }
            );
        }

        private List<object> BuildEpisodeList(SeriesContext ctx, int seasonNum)
        {
            var items = new List<(PlexCoords Coords, object Meta)>();

            foreach (var m in ctx.FileData.GetForSeason(seasonNum))
            {
                if (m.Episodes.Count == 1)
                {
                    items.Add((m.Coords, _mapper.MapEpisode(m.PrimaryEpisode, m.Coords, ctx.Series, ctx.Titles, m.PartIndex, m.TmdbEpisode)));
                    continue;
                }

                foreach (var ep in m.Episodes)
                {
                    var coordsEp = GetPlexCoordinates(ep);
                    if (coordsEp.Season != seasonNum)
                        continue;
                    items.Add((coordsEp, _mapper.MapEpisode(ep, coordsEp, ctx.Series, ctx.Titles)));
                }
            }

            return items.OrderBy(x => x.Coords.Episode).Select(x => x.Meta).ToList();
        }
    }
}
