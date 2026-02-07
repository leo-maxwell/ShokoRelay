using System.Reflection;

namespace ShokoRelay.Helpers;

internal static class PluginPaths
{
    private static readonly string _pluginDirectory = Resolve();

    public static string PluginDirectory => _pluginDirectory;

    private static string Resolve()
    {
        try
        {
            string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrWhiteSpace(assemblyDir) && Directory.Exists(assemblyDir))
                return assemblyDir;
        }
        catch
        {
            // ignore and fall back
        }

        try
        {
            if (Directory.Exists(AppContext.BaseDirectory))
                return AppContext.BaseDirectory;
        }
        catch
        {
            // ignore and use current directory
        }

        return Directory.GetCurrentDirectory();
    }
}
