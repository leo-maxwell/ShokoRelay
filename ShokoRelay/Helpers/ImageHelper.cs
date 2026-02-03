using Microsoft.AspNetCore.Http;
using Shoko.Plugin.Abstractions.DataModels;
using Shoko.Plugin.Abstractions.Enums;

namespace ShokoRelay.Helpers
{
    public sealed class ImageInfo
    {
        public string alt  { get; init; } = "";
        public string type { get; init; } = "";
        public string url  { get; init; } = "";
    }
    public static class ImageHelper
    {
        public static IHttpContextAccessor? HttpContextAccessor { get; set; }

        private static string GetBaseUrl()
        {
            var ctx = HttpContextAccessor?.HttpContext;
            if (ctx is not null)
                return $"{ctx.Request.Scheme}://{ctx.Request.Host}";

            return "http://localhost:8111";
        }

        public static string GetImageUrl(IImageMetadata image, string? imageTypeOverride = null)
            => $"{GetBaseUrl()}/api/v{ShokoRelayInfo.ApiVersion}/Image/{image.Source}/{imageTypeOverride ?? image.ImageType.ToString()}/{image.ID}";

        public static ImageInfo[] GenerateImageArray(IWithImages images, string title, bool addEveryImage)
        {
            IEnumerable<IImageMetadata> Filter(ImageEntityType type)
            {
                var all = images.GetImages(type);
                if (addEveryImage) return all;

                var pref = all.FirstOrDefault(i => i.IsPreferred);
                return pref is not null ? new[] { pref } : all.Take(1);
            }

            IEnumerable<ImageInfo> Project(ImageEntityType type, string kind) =>
                Filter(type).Select(i => new ImageInfo
                {
                    alt  = title,
                    type = kind,
                    url  = GetImageUrl(i)
                });

            // backgroundSquare excluded as there is no provider for them yet
            return Project(ImageEntityType.Backdrop, "background")
                .Concat(Project(ImageEntityType.Logo, "clearLogo"))
                .Concat(Project(ImageEntityType.Poster, "coverPoster"))
                .Concat(Project(ImageEntityType.Thumbnail, "snapshot"))
                .ToArray();
        }
    }
}