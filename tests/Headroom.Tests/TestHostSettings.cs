using System.Runtime.CompilerServices;

namespace Headroom.Tests;

internal static class TestHostSettings
{
    // The CI runner allows only 128 inotify instances. Config reload starts file watchers in every test host, so turn it off.
    // This cuts the peak of the test process from about 400 to about 120 instances.
#pragma warning disable CA2255 // A module initializer is the only hook that runs before any test host builds.
    [ModuleInitializer]
    internal static void DisableConfigReload() =>
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
#pragma warning restore CA2255
}
