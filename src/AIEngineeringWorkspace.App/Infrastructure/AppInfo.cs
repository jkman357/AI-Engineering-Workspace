using System.Reflection;

namespace AIEngineeringWorkspace.Infrastructure;

internal static class AppInfo
{
    public static string DisplayVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                ?? "development";

            var metadataSeparator = version.IndexOf('+');
            return metadataSeparator >= 0 ? version[..metadataSeparator] : version;
        }
    }
}
