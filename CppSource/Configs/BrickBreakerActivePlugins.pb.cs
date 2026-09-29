// #pl-version 2
// This file is auto-generated, do not modify.
// The plEditor may modify this file to add build configuration options.

using System;
using System.IO;
using System.Linq;
using PlasmaBuild.Core.Configuration;
using PlasmaBuild.Core.Rules;

public static class BrickBreakerActivePlugins
{
    public static readonly string[] RuntimePlugins = Array.Empty<string>();

    public static readonly string[] PackageRoots =
    {
        "Plugins/plasma.gameui/1.2.1",
    };

    public static readonly string[] PackagePluginRoots =
    {
        "Plugins/plasma.gameui/1.2.1",
    };

    public static readonly string[] PackagePlugins =
    {
        "plGameUiPlugin",
    };

    public static void Apply(ModuleRules rules, BuildContext context)
    {

        foreach (var packageRoot in PackageRoots)
        {
            BrickBreakerSdk.AddPackageIncludePath(rules, context, packageRoot);
        }

        for (var i = 0; i < PackagePlugins.Length; ++i)
        {
            BrickBreakerSdk.AddPackageImportLibrary(rules, context, PackagePluginRoots[i], PackagePlugins[i]);
        }
    }

    public static void ApplyTargetDependencies(TargetRules rules, BuildContext context)
    {
        // Engine plugins are prebuilt SDK binaries, linked via the import libraries in Apply().
        // Their build targets are not part of the project workspace, so no target dependencies are added.
    }

    public static string[] GetRuntimePluginLibraryPaths(BuildContext context)
    {
        if (context.Platform != TargetPlatform.Android)
        {
            return Array.Empty<string>();
        }

        var libraries = new string[RuntimePlugins.Length + PackagePlugins.Length];
        for (var i = 0; i < RuntimePlugins.Length; ++i)
        {
            libraries[i] = Path.Combine(BrickBreakerSdk.BinaryDirectory(context), RuntimePlugins[i] + ".so");
        }

        // A package's plugins ship in the package, not in the engine's binaries.
        for (var i = 0; i < PackagePlugins.Length; ++i)
        {
            libraries[RuntimePlugins.Length + i] = BrickBreakerSdk.PackageLibraryPath(context, PackagePluginRoots[i], PackagePlugins[i] + ".so");
        }

        return libraries;
    }
}
