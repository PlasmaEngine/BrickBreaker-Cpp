// #pl-version 5

using System;
using System.IO;
using BrickBreakerAndroid;
using PlasmaBuild.Core.Configuration;
using PlasmaBuild.Core.Rules;

public static class BrickBreakerSdk
{
    public static readonly string Root = FindSdkRoot();

    private static string FindSdkRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("PLASMA_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
            return configuredRoot;
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Code", "Engine", "Foundation", "Basics.h")))
                return directory.FullName;
        }
        throw new InvalidOperationException("Set PLASMA_SDK_ROOT to the engine SDK directory, or use Build.ps1.");
    }

    public static string BinaryDirectory(BuildContext context)
    {
        return Path.Combine(Root, "Binaries", $"{context.Platform}_{context.Configuration}");
    }

    public static string AndroidPackageDirectory(BuildContext context)
    {
        return Path.Combine(ProjectDirectory(context), "Build", "AndroidPackage");
    }

    public static string AndroidStagedDataDirectory(BuildContext context)
    {
        return Path.Combine(AndroidPackageDirectory(context), "Data");
    }

    public static AndroidPackageOptions AndroidPackageOptions(BuildContext context)
    {
        return new AndroidPackageOptions
        {
            TargetName = "BrickBreakerGame",
            NativeLibraryName = "BrickBreakerGame",
            NativeLibraryPath = Path.Combine(BinaryDirectory(context), "BrickBreakerGame.so"),
            NativeLibraryPaths = BrickBreakerActivePlugins.GetRuntimePluginLibraryPaths(context),
            PlatformContentDirectory = Path.Combine(Root, "Data", "Platform", "Android"),
            ManifestTemplate = Path.Combine(Root, "Data", "Platform", "Android", "AndroidManifest.xml"),
            PackageName = "uk.co.plasmatechnology.brickbreakergame",
            AppLabel = "BrickBreakerGame"
        };
    }

    public static string ProjectDirectory(BuildContext context)
    {
        return Path.GetFullPath(Path.Combine(context.ProjectRoot, ".."));
    }

    public static string ProjectDirectoryForMacro(BuildContext context)
    {
        return ProjectDirectory(context).Replace('\\', '/');
    }

    public static void ConfigureSdkConsumer(ModuleRules rules, BuildContext context)
    {
        rules.PublicIncludePaths.Add(Path.Combine(Root, "Code", "Engine"));
        rules.PublicIncludePaths.Add(Path.Combine(Root, "Code", "EnginePlugins"));
        rules.PublicIncludePaths.Add(Path.Combine(Root, "Code", "EditorPlugins"));
        rules.PublicIncludePaths.Add(Path.Combine(Root, "Code", "ThirdParty"));
        AddSdkThirdPartyIncludePaths(rules);

        rules.PublicDefinitions.Add("BUILDSYSTEM_COMPILE_ENGINE_AS_DLL");
        rules.PublicDefinitions.Add("BUILDSYSTEM_SDKVERSION_MAJOR=0");
        rules.PublicDefinitions.Add("BUILDSYSTEM_SDKVERSION_MINOR=9");
        rules.PublicDefinitions.Add("BUILDSYSTEM_SDKVERSION_PATCH=1");
        rules.PublicDefinitions.Add("UNICODE");
        rules.PublicDefinitions.Add("_UNICODE");

        if (context.Configuration == BuildConfiguration.Debug)
        {
            rules.PublicDefinitions.Add("BUILDSYSTEM_BUILDTYPE_Debug");
            rules.PublicDefinitions.Add("BUILDSYSTEM_BUILDTYPE=\"Debug\"");
        }
        else if (context.Configuration == BuildConfiguration.Development || context.Configuration == BuildConfiguration.Release)
        {
            rules.PublicDefinitions.Add("BUILDSYSTEM_BUILDTYPE_Dev");
            rules.PublicDefinitions.Add("BUILDSYSTEM_BUILDTYPE=\"Dev\"");
        }
        else if (context.Configuration == BuildConfiguration.Shipping)
        {
            rules.PublicDefinitions.Add("BUILDSYSTEM_BUILDTYPE_Shipping");
            rules.PublicDefinitions.Add("BUILDSYSTEM_BUILDTYPE=\"Shipping\"");
        }

        AddImportLibrary(rules, context, "plFoundation");
        AddImportLibrary(rules, context, "plCore");
        AddImportLibrary(rules, context, "plTexture");
        AddImportLibrary(rules, context, "plRendererFoundation");
        AddImportLibrary(rules, context, "plRendererCore");
        AddImportLibrary(rules, context, "plAudioSystem");
        AddImportLibrary(rules, context, "plUtilities");
        AddImportLibrary(rules, context, "plGameEngine");

        PlasmaBuildAndroid.ConfigureAndroidModule(rules, context);
    }

    public static void AddImportLibrary(ModuleRules rules, BuildContext context, string outputName)
    {
        var library = Path.Combine(BinaryDirectory(context), outputName + (context.Platform == TargetPlatform.Windows ? ".lib" : ".so"));
        if (File.Exists(library))
        {
            rules.PublicLibraries.Add(library);
        }
    }

    public static void AddIncludePath(ModuleRules rules, string sdkRelativePath)
    {
        rules.PublicIncludePaths.Add(Path.Combine(Root, sdkRelativePath));
    }

    /// \brief Absolute directory of an installed package this project depends on.
    ///
    /// A package installed into the project is named relative to it, so that the project stays
    /// portable across machines and checkouts. One from the machine-wide store can only be named
    /// absolutely - it is not part of the project, and where it lives differs per user.
    public static string PackageDirectory(BuildContext context, string packageRoot)
    {
        return Path.IsPathRooted(packageRoot)
            ? packageRoot
            : Path.Combine(ProjectDirectory(context), packageRoot);
    }

    /// \brief Where an installed package keeps its binaries and import libraries.
    ///
    /// Note the layout differs from the engine's own: a package builds into
    /// Bin/{Platform}_x64_{Configuration}, which is what PlasmaPackageSdk.PackageBinaryDirectory
    /// produces and what the installer copies.
    public static string PackageBinaryDirectory(BuildContext context, string packageRoot)
    {
        return Path.Combine(PackageDirectory(context, packageRoot), "Bin",
            $"{context.Platform}_x64_{context.Configuration}");
    }

    public static string PackageLibraryPath(BuildContext context, string packageRoot, string fileName)
    {
        return Path.Combine(PackageBinaryDirectory(context, packageRoot), fileName);
    }

    /// \brief Puts a package's headers on the include path.
    ///
    /// The staged tree mirrors the package's own Source directory, so its types are reached exactly
    /// as its own code reaches them: #include &lt;PluginDirectory/Header.h&gt;. Silent when the
    /// package ships no headers - an assets-only package is a normal thing to depend on.
    public static void AddPackageIncludePath(ModuleRules rules, BuildContext context, string packageRoot)
    {
        var includeDirectory = Path.Combine(PackageDirectory(context, packageRoot), "Include");

        if (Directory.Exists(includeDirectory))
        {
            rules.PublicIncludePaths.Add(includeDirectory);
        }
    }

    /// \brief Links one plugin out of an installed package.
    ///
    /// The import library is handed to the linker whether or not it exists. A package is installed
    /// with the binaries of the configuration the editor that installed it was built in, and a
    /// configuration it was not built for then fails with a single "cannot open file
    /// Bin/<Platform>_x64_<Configuration>/<Plugin>.lib" that names exactly what is missing - instead
    /// of compiling against the package's headers and failing with one unresolved external per
    /// imported symbol. Nothing is checked here because these rules also run for every configuration
    /// when project files are generated, where a missing configuration is not an error.
    public static void AddPackageImportLibrary(ModuleRules rules, BuildContext context, string packageRoot, string outputName)
    {
        rules.PublicLibraries.Add(PackageLibraryPath(context, packageRoot,
            outputName + (context.Platform == TargetPlatform.Windows ? ".lib" : ".so")));
    }

    private static void AddSdkThirdPartyIncludePaths(ModuleRules rules)
    {
        var thirdPartyRoot = Path.Combine(Root, "Code", "ThirdParty");
        if (!Directory.Exists(thirdPartyRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(thirdPartyRoot))
        {
            rules.PublicIncludePaths.Add(directory);
        }

        foreach (var directory in Directory.EnumerateDirectories(thirdPartyRoot, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(directory);
            if (string.Equals(name, "include", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "inc", System.StringComparison.OrdinalIgnoreCase))
            {
                rules.PublicIncludePaths.Add(directory);
            }
        }
    }
}
