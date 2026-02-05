using System.Text;

namespace ShokoRelay.Helpers
{
    public static class VfsHelper
    {
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);

            foreach (char c in name)
            {
                sb.Append(invalid.Contains(c) ? ' ' : c);
            }

            string cleaned = sb.ToString();
            while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
            cleaned = cleaned.Trim().TrimEnd('.');

            return cleaned.Length == 0 ? "Unknown" : cleaned;
        }

        public static string BuildStandardFileName(MapHelper.FileMapping mapping, int pad, string extension, int fileId, int? partIndexOverride = null, int? partCountOverride = null)
        {
            string epPart = $"S{mapping.Coords.Season:D2}E{mapping.Coords.Episode.ToString($"D{pad}")}";
            if (mapping.Coords.EndEpisode.HasValue && mapping.Coords.EndEpisode.Value != mapping.Coords.Episode)
            {
                epPart += $"-{mapping.Coords.EndEpisode.Value.ToString($"D{pad}")}";
            }

            int partCount = partCountOverride ?? mapping.PartCount;
            int? partIndex = partIndexOverride ?? mapping.PartIndex;

            if (partCount > 1 && partIndex.HasValue)
            {
                epPart += $"-pt{partIndex.Value}";
            }

            string fileIdPart = $"[{fileId}]";
            return $"{epPart} {fileIdPart}{extension}";
        }

        public static string BuildExtrasFileName(MapHelper.FileMapping mapping, (string Folder, string Prefix) extraInfo, int pad, string extension, string displaySeriesTitle, int fileId, int? partIndexOverride = null, int? partCountOverride = null)
        {
            string epPart = mapping.Coords.Episode.ToString($"D{pad}");
            int partCount = partCountOverride ?? mapping.PartCount;
            int? partIndex = partIndexOverride ?? mapping.PartIndex;
            string part = partCount > 1 && partIndex.HasValue ? $"-pt{partIndex.Value}" : string.Empty;

            string epTitle = TextHelper.ResolveEpisodeTitle(mapping.PrimaryEpisode, displaySeriesTitle);
            epTitle = TextHelper.CleanEpisodeTitleForFilename(epTitle);
            epTitle = SanitizeName(epTitle);
            string fileIdPart = $"[{fileId}]";

            return $"{epPart}{part} - {epTitle} {fileIdPart}{extension}";
        }
    }
}
