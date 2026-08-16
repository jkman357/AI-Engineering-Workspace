using System.Reflection;

namespace AIEngineeringWorkspace.Infrastructure;

internal static class AppInfo
{
    public static string DisplayVersion
        => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? "development";
}
