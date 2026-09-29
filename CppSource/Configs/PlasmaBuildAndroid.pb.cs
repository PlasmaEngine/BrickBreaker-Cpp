using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlasmaBuild.Core.Configuration;
using PlasmaBuild.Core.Rules;

namespace BrickBreakerAndroid;

public sealed class AndroidDevice
{
    public string Serial { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
}

public sealed class AndroidPackageOptions
{
    public static IReadOnlyList<string> DefaultEngineRuntimeLibraryNames { get; } = new[]
    {
        "plFoundation",
        "plTexture",
        "plCore",
        "plRendererFoundation",
        "plRendererCore",
        "plAudioSystem",
        "plUtilities",
        "plGameEngine",
        "plRendererVulkan"
    };

    public string TargetName { get; init; } = string.Empty;
    public string NativeLibraryName { get; init; } = string.Empty;
    public string NativeLibraryPath { get; init; } = string.Empty;
    public IReadOnlyList<string> NativeLibraryNames { get; init; } = DefaultEngineRuntimeLibraryNames;
    public IReadOnlyList<string> NativeLibraryPaths { get; init; } = Array.Empty<string>();
    public string StagedDataDirectory { get; init; } = string.Empty;
    public string PlatformContentDirectory { get; init; } = string.Empty;
    public string ManifestTemplate { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
    public string AppLabel { get; init; } = string.Empty;
    public string Orientation { get; init; } = "landscape";
    public int VersionCode { get; init; } = 1;
    public string VersionName { get; init; } = "1.0";
    public int MinSdkVersion { get; init; } = 29;
    public int TargetSdkVersion { get; init; } = 35;
    public bool Debuggable { get; init; } = true;
    public string SigningKey { get; init; } = string.Empty;
    public string SigningKeyAlias { get; init; } = string.Empty;
    public string SigningPassword { get; init; } = string.Empty;
    public string KeyPassword { get; init; } = string.Empty;
    public bool FinalOutput { get; init; } = false;
    public bool IncludeAllNativeLibrariesInOutputDirectory { get; init; } = false;
    public bool RequireStagedData { get; init; } = false;
    public bool RequireShaderCache { get; init; } = false;

    public static AndroidPackageOptions ForPlayer()
    {
        return new AndroidPackageOptions
        {
            TargetName = "Player",
            PackageName = "uk.co.plasmatechnology.player",
            AppLabel = "Plasma Player"
        };
    }

    public static AndroidPackageOptions ForTarget(string targetName, string packageName, string appLabel)
    {
        return new AndroidPackageOptions
        {
            TargetName = targetName,
            PackageName = packageName,
            AppLabel = appLabel
        };
    }
}

public sealed class AndroidSdk
{
    public string Root { get; private init; } = string.Empty;
    public string PlatformToolsDirectory { get; private init; } = string.Empty;
    public string BuildToolsDirectory { get; private init; } = string.Empty;
    public string PlatformDirectory { get; private init; } = string.Empty;
    public string Adb { get; private init; } = string.Empty;
    public string Aapt { get; private init; } = string.Empty;
    public string Zipalign { get; private init; } = string.Empty;
    public string Apksigner { get; private init; } = string.Empty;
    public string AndroidJar { get; private init; } = string.Empty;

    public static AndroidSdk Discover(int targetSdkVersion)
    {
        var root = Environment.GetEnvironmentVariable("ANDROID_HOME");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        }

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("ANDROID_HOME or ANDROID_SDK_ROOT must point to an Android SDK directory.");
        }

        var platformTools = Path.Combine(root, "platform-tools");
        var adb = FindTool(platformTools, "adb");
        var buildTools = FindLatestVersionDirectory(Path.Combine(root, "build-tools"));
        var aapt = FindTool(buildTools, "aapt");
        var zipalign = FindTool(buildTools, "zipalign");
        var apksigner = FindTool(buildTools, "apksigner");

        var requestedPlatform = Path.Combine(root, "platforms", $"android-{targetSdkVersion}");
        var platform = requestedPlatform;
        if (!Directory.Exists(requestedPlatform))
        {
            platform = FindLatestVersionDirectory(Path.Combine(root, "platforms"));
            var installedVersion = ParseVersion(Path.GetFileName(platform));
            if (installedVersion.Major < targetSdkVersion)
            {
                throw new DirectoryNotFoundException($"Android platform {targetSdkVersion} or newer is required. Install platform android-{targetSdkVersion}; newest installed platform is '{Path.GetFileName(platform)}'.");
            }
        }

        var androidJar = Path.Combine(platform, "android.jar");
        if (!File.Exists(androidJar))
        {
            throw new FileNotFoundException($"Missing Android platform jar '{androidJar}'. Install the Android platform SDK.", androidJar);
        }

        return new AndroidSdk
        {
            Root = root,
            PlatformToolsDirectory = platformTools,
            BuildToolsDirectory = buildTools,
            PlatformDirectory = platform,
            Adb = adb,
            Aapt = aapt,
            Zipalign = zipalign,
            Apksigner = apksigner,
            AndroidJar = androidJar
        };
    }

    private static string FindTool(string directory, string toolName)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".bat", ".cmd", string.Empty } : new[] { string.Empty };
        foreach (var extension in extensions)
        {
            var path = Path.Combine(directory, toolName + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"Missing Android SDK tool '{toolName}' in '{directory}'.");
    }

    private static string FindLatestVersionDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Missing Android SDK directory '{root}'.");
        }

        var directories = Directory.EnumerateDirectories(root)
            .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)) })
            .OrderByDescending(item => item.Version)
            .ToArray();

        if (directories.Length == 0)
        {
            throw new DirectoryNotFoundException($"No Android SDK versions found in '{root}'.");
        }

        return directories[0].Path;
    }

    private static Version ParseVersion(string value)
    {
        if (value.StartsWith("android-", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring("android-".Length);
        }

        var numericPrefix = new string(value.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray()).TrimEnd('.');
        if (int.TryParse(numericPrefix, out var majorVersion))
        {
            return new Version(majorVersion, 0);
        }

        return Version.TryParse(numericPrefix, out var version) ? version : new Version(0, 0);
    }
}

public sealed class AndroidNdk
{
    public string Root { get; private init; } = string.Empty;
    public string ToolchainBinDirectory { get; private init; } = string.Empty;
    public string SysrootDirectory { get; private init; } = string.Empty;
    public string Clang { get; private init; } = string.Empty;
    public string ClangCpp { get; private init; } = string.Empty;
    public string LlvmAr { get; private init; } = string.Empty;
    public string LlvmRanlib { get; private init; } = string.Empty;
    public string Lld { get; private init; } = string.Empty;
    public string NativeAppGlueDirectory { get; private init; } = string.Empty;

    public static AndroidNdk? TryDiscover()
    {
        try
        {
            return Discover();
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            return null;
        }
    }

    public static AndroidNdk Discover()
    {
        var root = Environment.GetEnvironmentVariable("ANDROID_NDK_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetEnvironmentVariable("ANDROID_NDK_HOME");
        }
        if (string.IsNullOrWhiteSpace(root))
        {
            var sdkRoot = Environment.GetEnvironmentVariable("ANDROID_HOME");
            if (string.IsNullOrWhiteSpace(sdkRoot))
            {
                sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            }
            if (!string.IsNullOrWhiteSpace(sdkRoot))
            {
                var ndkRoot = Path.Combine(sdkRoot, "ndk");
                if (Directory.Exists(ndkRoot))
                {
                    root = Directory.EnumerateDirectories(ndkRoot)
                        .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)) })
                        .OrderByDescending(item => item.Version)
                        .FirstOrDefault()?.Path;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("ANDROID_NDK_ROOT, ANDROID_NDK_HOME, or ANDROID_HOME/ndk must point to an Android NDK directory.");
        }

        var hostTag = OperatingSystem.IsWindows() ? "windows-x86_64" : OperatingSystem.IsMacOS() ? "darwin-x86_64" : "linux-x86_64";
        var toolchainBin = Path.Combine(root, "toolchains", "llvm", "prebuilt", hostTag, "bin");
        if (!Directory.Exists(toolchainBin))
        {
            throw new DirectoryNotFoundException($"Missing Android NDK LLVM toolchain directory '{toolchainBin}'.");
        }

        var sysroot = Path.Combine(root, "toolchains", "llvm", "prebuilt", hostTag, "sysroot");
        if (!Directory.Exists(sysroot))
        {
            throw new DirectoryNotFoundException($"Missing Android NDK sysroot directory '{sysroot}'.");
        }

        return new AndroidNdk
        {
            Root = root,
            ToolchainBinDirectory = toolchainBin,
            SysrootDirectory = sysroot,
            Clang = FindTool(toolchainBin, "clang"),
            ClangCpp = FindTool(toolchainBin, "clang++"),
            LlvmAr = FindTool(toolchainBin, "llvm-ar"),
            LlvmRanlib = FindTool(toolchainBin, "llvm-ranlib"),
            Lld = FindTool(toolchainBin, "ld.lld"),
            NativeAppGlueDirectory = Path.Combine(root, "sources", "android", "native_app_glue")
        };
    }

    public string GetTargetTriple(TargetArchitecture architecture, int minSdkVersion = 29)
    {
        if (minSdkVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSdkVersion));
        }

        return architecture.ToString().ToLowerInvariant() switch
        {
            "arm64" or "aarch64" => $"aarch64-linux-android{minSdkVersion}",
            "arm" or "armv7" => $"armv7a-linux-androideabi{minSdkVersion}",
            "x64" or "x86_64" => $"x86_64-linux-android{minSdkVersion}",
            "x86" => $"i686-linux-android{minSdkVersion}",
            _ => throw new NotSupportedException($"Android architecture '{architecture}' is not supported.")
        };
    }

    public string GetCxxSharedLibrary(TargetArchitecture architecture)
    {
        var runtimeTriple = architecture.ToString().ToLowerInvariant() switch
        {
            "arm64" or "aarch64" => "aarch64-linux-android",
            "arm" or "armv7" => "arm-linux-androideabi",
            "x64" or "x86_64" => "x86_64-linux-android",
            "x86" => "i686-linux-android",
            _ => throw new NotSupportedException($"Android architecture '{architecture}' is not supported.")
        };

        return Path.Combine(SysrootDirectory, "usr", "lib", runtimeTriple, "libc++_shared.so");
    }

    private static string FindTool(string directory, string toolName)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".cmd", ".exe", ".bat", string.Empty } : new[] { string.Empty };
        foreach (var extension in extensions)
        {
            var path = Path.Combine(directory, toolName + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"Missing Android NDK tool '{toolName}' in '{directory}'.");
    }

    private static Version ParseVersion(string value)
    {
        return Version.TryParse(value, out var version) ? version : new Version(0, 0);
    }
}

public static class AndroidToolRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(string executable, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        var output = new StringBuilder();
        var outputLock = new object();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outputLock) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (outputLock) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = timeout.HasValue ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        if (timeoutSource != null)
        {
            timeoutSource.CancelAfter(timeout.Value);
        }

        var waitToken = timeoutSource?.Token ?? cancellationToken;

        try
        {
            if (timeout.HasValue)
            {
                var waitTask = process.WaitForExitAsync(waitToken);
                var heartbeatTimer = Stopwatch.StartNew();
                while (true)
                {
                    var completedTask = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(15), waitToken));
                    if (completedTask == waitTask)
                    {
                        await waitTask;
                        break;
                    }

                    waitToken.ThrowIfCancellationRequested();

                    var heartbeat = $"[android-package] Still running after {heartbeatTimer.Elapsed.TotalSeconds:0}s: {Path.GetFileName(executable)}";
                    Console.WriteLine(heartbeat);
                    lock (outputLock)
                    {
                        output.AppendLine(heartbeat);
                    }
                }
            }
            else
            {
                await process.WaitForExitAsync(waitToken);
            }
        }
        catch (OperationCanceledException) when (timeout.HasValue && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort: the timeout result below is still the useful diagnostic.
            }

            lock (outputLock)
            {
                output.AppendLine($"Timed out after {timeout.Value.TotalMinutes:0.#} minute(s): {executable}");
                return (-1, output.ToString());
            }
        }

        lock (outputLock)
        {
            return (process.ExitCode, output.ToString());
        }
    }

    public static Task<(int ExitCode, string Output)> AdbAsync(AndroidSdk sdk, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        return RunAsync(sdk.Adb, arguments, workingDirectory, cancellationToken);
    }

    public static async Task<IReadOnlyList<AndroidDevice>> ListDevicesAsync(AndroidSdk sdk, string workingDirectory, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await AdbAsync(sdk, new[] { "devices", "-l" }, workingDirectory, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(output);
        }

        var devices = new List<AndroidDevice>();
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var model = parts.FirstOrDefault(part => part.StartsWith("model:", StringComparison.OrdinalIgnoreCase))?.Substring("model:".Length) ?? string.Empty;
            devices.Add(new AndroidDevice { Serial = parts[0], State = parts[1], Model = model });
        }

        return devices;
    }

    public static Task<(int ExitCode, string Output)> InstallAsync(AndroidSdk sdk, string deviceSerial, string apkPath, string workingDirectory, CancellationToken cancellationToken)
    {
        return AdbAsync(sdk, WithDevice(deviceSerial, "install", "-r", "-t", apkPath), workingDirectory, cancellationToken);
    }

    public static Task<(int ExitCode, string Output)> RunAsync(AndroidSdk sdk, string deviceSerial, string packageName, string activityName, string workingDirectory, CancellationToken cancellationToken)
    {
        return AdbAsync(sdk, WithDevice(deviceSerial, "shell", "am", "start", "-n", $"{packageName}/{activityName}"), workingDirectory, cancellationToken);
    }

    public static Task<(int ExitCode, string Output)> LogcatAsync(AndroidSdk sdk, string deviceSerial, string workingDirectory, CancellationToken cancellationToken)
    {
        return AdbAsync(sdk, WithDevice(deviceSerial, "logcat"), workingDirectory, cancellationToken);
    }

    private static IEnumerable<string> WithDevice(string deviceSerial, params string[] arguments)
    {
        if (!string.IsNullOrWhiteSpace(deviceSerial))
        {
            yield return "-s";
            yield return deviceSerial;
        }

        foreach (var argument in arguments)
        {
            yield return argument;
        }
    }
}

public static class PlasmaBuildAndroid
{
    private static readonly TimeSpan PackageToolTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SigningToolTimeout = TimeSpan.FromMinutes(2);

    public static void ConfigureAndroidModule(ModuleRules rules, BuildContext context)
    {
        if (context.Platform != TargetPlatform.Android)
        {
            return;
        }

        rules.PrivateDefinitions.Add("ANDROID");

        // Workspace generation instantiates Android module rules on machines without an
        // installed NDK. A missing NDK only matters once an Android build actually runs,
        // and the preflight step reports that with a proper error.
        var ndk = AndroidNdk.TryDiscover();
        if (ndk != null)
        {
            rules.PrivateIncludePaths.Add(ndk.NativeAppGlueDirectory);
        }

        rules.AdditionalCompilerFlags.Add("-fPIC");
    }

    public static string GetNativeAppGlueIncludeDirectory()
    {
        return AndroidNdk.Discover().NativeAppGlueDirectory;
    }

    public static void AddAndroidPreflightStep(TargetRules rules, BuildContext context, AndroidPackageOptions options)
    {
        if (context.Platform != TargetPlatform.Android)
        {
            return;
        }

        options = CompleteOptions(context, rules, options);
        rules.PreBuildSteps.Add(new BuildStep
        {
            Description = $"Validate Android build environment for {options.TargetName}",
            Command = $"managed:android-preflight:{options.TargetName}",
            WorkingDirectory = context.ProjectRoot,
            ManagedAction = (_, _) => Task.FromResult(ValidateBuildEnvironment(context, options))
        });
    }

    public static void AddApkPackagingStep(TargetRules rules, BuildContext context, AndroidPackageOptions options)
    {
        if (context.Platform != TargetPlatform.Android)
        {
            return;
        }

        options = CompleteOptions(context, rules, options);
        var outputApk = GetOutputApkPath(context, options.TargetName);

        rules.PostBuildSteps.Add(new BuildStep
        {
            Description = $"Package Android APK {options.TargetName}",
            Command = $"managed:android-package:{options.TargetName}",
            WorkingDirectory = context.ProjectRoot,
            Outputs = { outputApk },
            ManagedAction = (stepContext, cancellationToken) => PackageApkAsync(context, options, cancellationToken)
        });
    }

    public static void ConfigureAndroidTarget(TargetRules rules, BuildContext context, AndroidPackageOptions options)
    {
        AddAndroidPreflightStep(rules, context, options);
        AddApkPackagingStep(rules, context, options);
    }

    public static string GetOutputApkPath(BuildContext context, string targetName)
    {
        return Path.Combine(context.ProjectRoot, "Binaries", $"{context.Platform}_{context.Configuration}", $"{targetName}.apk");
    }

    public static string GetFinalOutputApkPath(BuildContext context, string targetName)
    {
        return Path.Combine(context.ProjectRoot, "Output", "Android", targetName, context.Configuration.ToString(), $"{targetName}.apk");
    }

    public static string GetPackageStagingDirectory(BuildContext context, string targetName)
    {
        return Path.Combine(context.ProjectRoot, "Intermediate", "PlasmaBuild", "Android", targetName, context.Configuration.ToString(), context.Architecture.ToString());
    }

    private static AndroidPackageOptions CompleteOptions(BuildContext context, TargetRules rules, AndroidPackageOptions options)
    {
        var targetName = string.IsNullOrWhiteSpace(options.TargetName) ? rules.Name : options.TargetName;
        var libraryName = string.IsNullOrWhiteSpace(options.NativeLibraryName) ? targetName : options.NativeLibraryName;
        var binaryDir = string.IsNullOrWhiteSpace(rules.OutputDirectory) ? Path.Combine(context.ProjectRoot, "Binaries", $"{context.Platform}_{context.Configuration}") : rules.OutputDirectory;
        var debuggable = context.Configuration != BuildConfiguration.Shipping && options.Debuggable;
        var finalOutput = options.FinalOutput || context.Configuration == BuildConfiguration.Shipping;

        var debugSigningKey = Path.Combine(context.ProjectRoot, "Data", "Platform", "Android", "debug.keystore");
        var signingKey = GetEnvironmentOverride("PL_ANDROID_KEYSTORE", options.SigningKey);
        if (string.IsNullOrWhiteSpace(signingKey) && !finalOutput)
        {
            signingKey = debugSigningKey;
        }

        var signingPassword = GetEnvironmentOverride("PL_ANDROID_KEYSTORE_PASS", options.SigningPassword);
        var keyPassword = GetEnvironmentOverride("PL_ANDROID_KEY_PASS", options.KeyPassword);
        if (!finalOutput && PathsEqual(signingKey, debugSigningKey))
        {
            signingPassword = string.IsNullOrWhiteSpace(signingPassword) ? "pass:android" : signingPassword;
        }

        signingPassword = NormalizePasswordSpec(signingPassword);
        keyPassword = NormalizePasswordSpec(keyPassword);

        return new AndroidPackageOptions
        {
            TargetName = targetName,
            NativeLibraryName = libraryName,
            NativeLibraryPath = string.IsNullOrWhiteSpace(options.NativeLibraryPath) ? Path.Combine(binaryDir, $"{libraryName}.so") : options.NativeLibraryPath,
            NativeLibraryNames = options.NativeLibraryNames ?? Array.Empty<string>(),
            NativeLibraryPaths = options.NativeLibraryPaths ?? Array.Empty<string>(),
            StagedDataDirectory = string.IsNullOrWhiteSpace(options.StagedDataDirectory) ? Environment.GetEnvironmentVariable("PL_ANDROID_STAGED_DATA_DIR") ?? string.Empty : options.StagedDataDirectory,
            PlatformContentDirectory = string.IsNullOrWhiteSpace(options.PlatformContentDirectory) ? Path.Combine(context.ProjectRoot, "Data", "Platform", "Android") : options.PlatformContentDirectory,
            ManifestTemplate = string.IsNullOrWhiteSpace(options.ManifestTemplate) ? Path.Combine(context.ProjectRoot, "Data", "Platform", "Android", "AndroidManifest.xml") : options.ManifestTemplate,
            PackageName = GetEnvironmentOverride("PL_ANDROID_PACKAGE_NAME", string.IsNullOrWhiteSpace(options.PackageName) ? $"uk.co.plasmatechnology.{SanitizePackagePart(targetName)}" : options.PackageName),
            AppLabel = GetEnvironmentOverride("PL_ANDROID_APP_LABEL", string.IsNullOrWhiteSpace(options.AppLabel) ? targetName : options.AppLabel),
            Orientation = GetEnvironmentOverride("PL_ANDROID_ORIENTATION", options.Orientation),
            VersionCode = GetEnvironmentInt("PL_ANDROID_VERSION_CODE", options.VersionCode),
            VersionName = GetEnvironmentOverride("PL_ANDROID_VERSION_NAME", options.VersionName),
            MinSdkVersion = GetEnvironmentInt("PL_ANDROID_MIN_SDK", options.MinSdkVersion),
            TargetSdkVersion = GetEnvironmentInt("PL_ANDROID_TARGET_SDK", options.TargetSdkVersion),
            Debuggable = debuggable,
            SigningKey = signingKey,
            SigningKeyAlias = GetEnvironmentOverride("PL_ANDROID_KEY_ALIAS", options.SigningKeyAlias),
            SigningPassword = signingPassword,
            KeyPassword = keyPassword,
            FinalOutput = finalOutput,
            IncludeAllNativeLibrariesInOutputDirectory = options.IncludeAllNativeLibrariesInOutputDirectory,
            RequireStagedData = options.RequireStagedData || finalOutput,
            RequireShaderCache = options.RequireShaderCache || finalOutput
        };
    }

    private static BuildStepResult ValidateBuildEnvironment(BuildContext context, AndroidPackageOptions options)
    {
        try
        {
            var sdk = AndroidSdk.Discover(options.TargetSdkVersion);
            var ndk = AndroidNdk.Discover();
            var targetTriple = ndk.GetTargetTriple(context.Architecture, options.MinSdkVersion);
            var diagnostics = new StringBuilder();

            diagnostics.AppendLine($"Android SDK: {sdk.Root}");
            diagnostics.AppendLine($"Android build-tools: {sdk.BuildToolsDirectory}");
            diagnostics.AppendLine($"Android platform: {sdk.PlatformDirectory}");
            diagnostics.AppendLine($"Android NDK: {ndk.Root}");
            diagnostics.AppendLine($"Android NDK toolchain: {ndk.ToolchainBinDirectory}");
            diagnostics.AppendLine($"Android target: {targetTriple}");

            if (!IsValidPackageName(options.PackageName))
            {
                return BuildStepResult.Failed($"Invalid Android package name '{options.PackageName}'. Use dot-separated identifiers whose segments begin with a letter.");
            }

            if (options.VersionCode <= 0 || string.IsNullOrWhiteSpace(options.VersionName))
            {
                return BuildStepResult.Failed("Android version code must be positive and version name must not be empty.");
            }

            if (options.MinSdkVersion < 29 || options.TargetSdkVersion < options.MinSdkVersion)
            {
                return BuildStepResult.Failed($"Android SDK range is invalid. Plasma requires minSdkVersion 29 or newer, and targetSdkVersion ({options.TargetSdkVersion}) must be at least minSdkVersion ({options.MinSdkVersion}).");
            }

            if (string.IsNullOrWhiteSpace(options.SigningKey) || !File.Exists(options.SigningKey))
            {
                return BuildStepResult.Failed($"Missing Android signing key '{options.SigningKey}'. Configure PL_ANDROID_KEYSTORE or restore the debug keystore.");
            }

            var debugSigningKey = Path.Combine(context.ProjectRoot, "Data", "Platform", "Android", "debug.keystore");
            if (options.FinalOutput && PathsEqual(options.SigningKey, debugSigningKey))
            {
                return BuildStepResult.Failed("Shipping Android packages must not use Data/Platform/Android/debug.keystore. Configure PL_ANDROID_KEYSTORE and PL_ANDROID_KEYSTORE_PASS with a release/upload key.");
            }

            if (options.FinalOutput && string.IsNullOrWhiteSpace(options.SigningPassword))
            {
                return BuildStepResult.Failed("Shipping Android packages require PL_ANDROID_KEYSTORE_PASS (raw password or an apksigner pass:/env:/file: password specification).");
            }

            var stagedDataValidation = ValidateStagedData(options);
            if (stagedDataValidation != null)
            {
                return BuildStepResult.Failed(stagedDataValidation);
            }

            if (OperatingSystem.IsWindows())
            {
                foreach (var tool in new[] { "clang.cmd", "clang++.cmd", "llvm-ar.cmd", "llvm-ranlib.cmd", "ld.lld.cmd" })
                {
                    var path = Path.Combine(ndk.ToolchainBinDirectory, tool);
                    if (!File.Exists(path))
                    {
                        diagnostics.AppendLine($"Warning: '{path}' was not found. The current shipped PlasmaBuild Android toolchain expects Windows .cmd wrappers for NDK tools.");
                    }
                }
            }

            return BuildStepResult.Succeeded(diagnostics.ToString());
        }
        catch (Exception ex)
        {
            return BuildStepResult.Failed(ex.Message);
        }
    }

    private static async Task<BuildStepResult> PackageApkAsync(BuildContext context, AndroidPackageOptions options, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var packageTimer = Stopwatch.StartNew();

        void LogPhase(string message)
        {
            var line = $"[android-package] {DateTime.Now:HH:mm:ss} {message}";
            Console.WriteLine(line);
            output.AppendLine(line);
        }

        try
        {
            LogPhase($"Starting {options.TargetName} ({context.Configuration}, {context.Architecture}).");
            var sdk = AndroidSdk.Discover(options.TargetSdkVersion);
            var stagingRoot = GetPackageStagingDirectory(context, options.TargetName);
            var contentRoot = Path.Combine(stagingRoot, "content");
            var manifestPath = Path.Combine(stagingRoot, "AndroidManifest.xml");
            var unalignedApk = Path.Combine(stagingRoot, $"{options.TargetName}.unaligned.apk");
            var buildApk = GetOutputApkPath(context, options.TargetName);
            var finalApk = options.FinalOutput ? GetFinalOutputApkPath(context, options.TargetName) : buildApk;
            var abi = GetAndroidAbi(context.Architecture.ToString());

            var stagedDataValidation = ValidateStagedData(options);
            if (stagedDataValidation != null)
            {
                return BuildStepResult.Failed(stagedDataValidation);
            }

            Directory.CreateDirectory(stagingRoot);
            LogPhase($"Preparing staging directory: {stagingRoot}");
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
            Directory.CreateDirectory(contentRoot);
            DeleteIfExists(unalignedApk);
            DeleteIfExists(buildApk);
            if (!string.Equals(buildApk, finalApk, StringComparison.OrdinalIgnoreCase))
            {
                DeleteIfExists(finalApk);
            }

            var platformCopy = CopyDirectory(options.PlatformContentDirectory, contentRoot, path => !Path.GetFileName(path).Equals("AndroidManifest.xml", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(path).Equals("debug.keystore", StringComparison.OrdinalIgnoreCase), "Android platform", LogPhase);
            LogPhase($"Copied {platformCopy.CopiedFiles} Android platform file(s), {FormatBytes(platformCopy.CopiedBytes)}.");

            var nativeLibraries = GetNativeLibraries(options, context).ToArray();
            if (nativeLibraries.Length == 0)
            {
                return BuildStepResult.Failed("No Android native libraries were configured for packaging.");
            }

            var nativeLibraryBytes = nativeLibraries.Where(File.Exists).Sum(path => new FileInfo(path).Length);
            LogPhase($"Staging {nativeLibraries.Length} native librar{(nativeLibraries.Length == 1 ? "y" : "ies")} for ABI {abi}, {FormatBytes(nativeLibraryBytes)}.");
            var stagedNativeLibraries = 0;
            foreach (var nativeLibrary in nativeLibraries)
            {
                if (!File.Exists(nativeLibrary))
                {
                    return BuildStepResult.Failed($"Missing Android native library '{nativeLibrary}'.");
                }

                var nativeLibraryFileName = IsPrimaryNativeLibrary(options, nativeLibrary) ? GetAndroidNativeLibraryFileName(options.NativeLibraryName) : Path.GetFileName(nativeLibrary);
                var nativeLibraryTarget = Path.Combine(contentRoot, "lib", abi, nativeLibraryFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(nativeLibraryTarget)!);
                File.Copy(nativeLibrary, nativeLibraryTarget, overwrite: true);
                ++stagedNativeLibraries;
                LogPhase($"Staged native library {stagedNativeLibraries}/{nativeLibraries.Length}: {Path.GetFileName(nativeLibraryTarget)} ({FormatBytes(new FileInfo(nativeLibraryTarget).Length)}).");
            }

            if (!string.IsNullOrWhiteSpace(options.StagedDataDirectory) && Directory.Exists(options.StagedDataDirectory))
            {
                LogPhase($"Copying staged project data: {options.StagedDataDirectory}");
                var stagedDataCopy = CopyDirectory(options.StagedDataDirectory, Path.Combine(contentRoot, "assets", "Data"), path => ShouldPackageStagedAsset(options.StagedDataDirectory, path), "staged project data", LogPhase);
                LogPhase($"Copied {stagedDataCopy.CopiedFiles} staged data file(s), {FormatBytes(stagedDataCopy.CopiedBytes)}. Skipped {stagedDataCopy.SkippedFiles} file(s), {FormatBytes(stagedDataCopy.SkippedBytes)}.");
                if (options.RequireStagedData && stagedDataCopy.CopiedFiles == 0)
                {
                    return BuildStepResult.Failed($"Staged Android data directory '{options.StagedDataDirectory}' did not contain any packageable files.");
                }
            }
            else
            {
                LogPhase("No staged project data directory was configured.");
            }

            WriteManifest(options, manifestPath);
            LogPhase($"Wrote manifest: {manifestPath}");

            Directory.CreateDirectory(Path.GetDirectoryName(finalApk)!);

            var packageArgs = new List<string>
            {
                "package", "--debug-mode", "-f", "-M", manifestPath, "-S", Path.Combine(contentRoot, "res"), "-I", sdk.AndroidJar, "-F", unalignedApk, contentRoot
            };
            if (!options.Debuggable)
            {
                packageArgs.Remove("--debug-mode");
            }

            var packageInput = GetDirectoryStats(contentRoot);
            LogPhase($"Package input contains {packageInput.FileCount} file(s), {FormatBytes(packageInput.TotalBytes)}.");
            var toolTimer = Stopwatch.StartNew();
            LogPhase("Running aapt package.");
            var result = await AndroidToolRunner.RunAsync(sdk.Aapt, packageArgs, context.ProjectRoot, cancellationToken, PackageToolTimeout);
            output.Append(result.Output);
            LogPhase($"aapt package finished with exit code {result.ExitCode} after {toolTimer.Elapsed.TotalSeconds:0.0}s.");
            if (result.ExitCode != 0)
            {
                return BuildStepResult.Failed(output.ToString());
            }
            LogPhase($"Unaligned APK size: {FormatBytes(new FileInfo(unalignedApk).Length)}.");

            Directory.CreateDirectory(Path.GetDirectoryName(buildApk)!);
            toolTimer.Restart();
            LogPhase("Running zipalign.");
            result = await AndroidToolRunner.RunAsync(sdk.Zipalign, new[] { "-f", "4", unalignedApk, buildApk }, context.ProjectRoot, cancellationToken, SigningToolTimeout);
            output.Append(result.Output);
            LogPhase($"zipalign finished with exit code {result.ExitCode} after {toolTimer.Elapsed.TotalSeconds:0.0}s.");
            if (result.ExitCode != 0)
            {
                return BuildStepResult.Failed(output.ToString());
            }
            LogPhase($"Aligned APK size: {FormatBytes(new FileInfo(buildApk).Length)}.");

            var signArgs = new List<string> { "sign", "--ks", options.SigningKey };
            if (!string.IsNullOrWhiteSpace(options.SigningKeyAlias))
            {
                signArgs.Add("--ks-key-alias");
                signArgs.Add(options.SigningKeyAlias);
            }
            if (!string.IsNullOrWhiteSpace(options.SigningPassword))
            {
                signArgs.Add("--ks-pass");
                signArgs.Add(options.SigningPassword);
            }
            if (!string.IsNullOrWhiteSpace(options.KeyPassword))
            {
                signArgs.Add("--key-pass");
                signArgs.Add(options.KeyPassword);
            }
            signArgs.Add(buildApk);

            toolTimer.Restart();
            LogPhase("Running apksigner.");
            result = await AndroidToolRunner.RunAsync(sdk.Apksigner, signArgs, context.ProjectRoot, cancellationToken, SigningToolTimeout);
            output.Append(RedactSecrets(result.Output, options));
            LogPhase($"apksigner finished with exit code {result.ExitCode} after {toolTimer.Elapsed.TotalSeconds:0.0}s.");
            if (result.ExitCode != 0)
            {
                return BuildStepResult.Failed(output.ToString());
            }

            toolTimer.Restart();
            LogPhase("Verifying APK alignment.");
            result = await AndroidToolRunner.RunAsync(sdk.Zipalign, new[] { "-c", "-v", "4", buildApk }, context.ProjectRoot, cancellationToken, SigningToolTimeout);
            output.Append(result.Output);
            LogPhase($"zipalign verification finished with exit code {result.ExitCode} after {toolTimer.Elapsed.TotalSeconds:0.0}s.");
            if (result.ExitCode != 0)
            {
                return BuildStepResult.Failed(output.ToString());
            }

            toolTimer.Restart();
            LogPhase("Verifying APK signature.");
            result = await AndroidToolRunner.RunAsync(sdk.Apksigner, new[] { "verify", "--verbose", buildApk }, context.ProjectRoot, cancellationToken, SigningToolTimeout);
            output.Append(result.Output);
            LogPhase($"apksigner verification finished with exit code {result.ExitCode} after {toolTimer.Elapsed.TotalSeconds:0.0}s.");
            if (result.ExitCode != 0)
            {
                return BuildStepResult.Failed(output.ToString());
            }

            if (!string.Equals(buildApk, finalApk, StringComparison.OrdinalIgnoreCase))
            {
                LogPhase($"Copying final APK: {finalApk}");
                Directory.CreateDirectory(Path.GetDirectoryName(finalApk)!);
                File.Copy(buildApk, finalApk, overwrite: true);
            }

            LogPhase($"Built APK: {finalApk} ({FormatBytes(new FileInfo(finalApk).Length)}).");
            LogPhase($"Completed in {packageTimer.Elapsed.TotalSeconds:0.0}s.");
            return BuildStepResult.Succeeded(output.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildStepResult.Failed(ex.Message);
        }
    }

    private static void WriteManifest(AndroidPackageOptions options, string targetPath)
    {
        if (!File.Exists(options.ManifestTemplate))
        {
            throw new FileNotFoundException($"Missing Android manifest template '{options.ManifestTemplate}'.", options.ManifestTemplate);
        }

        var text = File.ReadAllText(options.ManifestTemplate)
            .Replace("${PACKAGE_NAME}", EscapeXmlAttributeValue(options.PackageName))
            .Replace("${VERSION_CODE}", options.VersionCode.ToString())
            .Replace("${VERSION_NAME}", EscapeXmlAttributeValue(options.VersionName))
            .Replace("${MIN_SDK_VERSION}", options.MinSdkVersion.ToString())
            .Replace("${TARGET_SDK_VERSION}", options.TargetSdkVersion.ToString())
            .Replace("${DEBUGGABLE}", options.Debuggable ? "true" : "false")
            .Replace("${ORIENTATION}", EscapeXmlAttributeValue(options.Orientation))
            .Replace("${APP_LABEL}", EscapeXmlAttributeValue(options.AppLabel))
            .Replace("${TARGET_NAME}", EscapeXmlAttributeValue(options.NativeLibraryName));

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, text, Encoding.UTF8);
    }

    private static string EscapeXmlAttributeValue(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string GetAndroidAbi(string architecture)
    {
        return architecture.ToLowerInvariant() switch
        {
            "arm64" or "aarch64" => "arm64-v8a",
            "arm" or "armv7" => "armeabi-v7a",
            "x64" or "x86_64" => "x86_64",
            "x86" => "x86",
            _ => architecture
        };
    }

    private static string SanitizePackagePart(string value)
    {
        var chars = value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '.').ToArray();
        return new string(chars).Trim('.');
    }

    private static IEnumerable<string> GetNativeLibraries(AndroidPackageOptions options, BuildContext context)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cxxShared = AndroidNdk.Discover().GetCxxSharedLibrary(context.Architecture);
        if (File.Exists(cxxShared))
        {
            libraries.Add(cxxShared);
        }

        if (!string.IsNullOrWhiteSpace(options.NativeLibraryPath))
        {
            libraries.Add(options.NativeLibraryPath);
        }

        var nativeOutputDirectory = Path.GetDirectoryName(options.NativeLibraryPath);
        if (!string.IsNullOrWhiteSpace(nativeOutputDirectory))
        {
            foreach (var libraryName in options.NativeLibraryNames ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(libraryName))
                {
                    libraries.Add(Path.Combine(nativeOutputDirectory, EnsureSharedLibraryExtension(libraryName)));
                }
            }
        }

        foreach (var library in options.NativeLibraryPaths ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(library))
            {
                libraries.Add(library);
            }
        }

        if (options.IncludeAllNativeLibrariesInOutputDirectory && !string.IsNullOrWhiteSpace(options.NativeLibraryPath))
        {
            var outputDirectory = Path.GetDirectoryName(options.NativeLibraryPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory))
            {
                foreach (var library in Directory.EnumerateFiles(outputDirectory, "*.so", SearchOption.TopDirectoryOnly))
                {
                    libraries.Add(library);
                }
            }
        }

        return libraries;
    }

    private static bool IsPrimaryNativeLibrary(AndroidPackageOptions options, string nativeLibrary)
    {
        return !string.IsNullOrWhiteSpace(options.NativeLibraryPath) &&
               string.Equals(Path.GetFullPath(nativeLibrary), Path.GetFullPath(options.NativeLibraryPath), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAndroidNativeLibraryFileName(string libraryName)
    {
        return libraryName.StartsWith("lib", StringComparison.Ordinal) ? $"{libraryName}.so" : $"lib{libraryName}.so";
    }

    private static string RedactSecrets(string output, AndroidPackageOptions options)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        foreach (var secret in new[] { options.SigningPassword, options.KeyPassword })
        {
            if (!string.IsNullOrWhiteSpace(secret))
            {
                output = output.Replace(secret, "<redacted>", StringComparison.Ordinal);
            }
        }

        return output;
    }

    private static string? ValidateStagedData(AndroidPackageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.StagedDataDirectory))
        {
            return options.RequireStagedData ? "A staged Android data directory is required. Export through the editor or set PL_ANDROID_STAGED_DATA_DIR." : null;
        }

        if (!Directory.Exists(options.StagedDataDirectory))
        {
            return $"Configured staged Android data directory does not exist: '{options.StagedDataDirectory}'.";
        }

        if (options.RequireShaderCache)
        {
            var shaderCache = Path.Combine(options.StagedDataDirectory, "project", "ShaderCache", "VULKAN");
            if (!Directory.Exists(shaderCache) || !Directory.EnumerateFiles(shaderCache, "*", SearchOption.AllDirectories).Any())
            {
                return $"Shipping Android data is missing compiled Vulkan shaders in '{shaderCache}'. Run the editor's Compile shaders export stage before packaging.";
            }
        }

        return null;
    }

    private static string GetEnvironmentOverride(string variableName, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int GetEnvironmentInt(string variableName, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, out var parsedValue))
        {
            throw new InvalidOperationException($"Environment variable {variableName} must be an integer, but was '{value}'.");
        }

        return parsedValue;
    }

    private static string NormalizePasswordSpec(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("pass:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("env:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || value.Equals("stdin", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return $"pass:{value}";
    }

    private static bool IsValidPackageName(string value)
    {
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts.All(part => char.IsLetter(part[0]) && part.All(ch => char.IsLetterOrDigit(ch) || ch == '_'));
    }

    private static bool PathsEqual(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string EnsureSharedLibraryExtension(string libraryName)
    {
        return libraryName.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ? libraryName : $"{libraryName}.so";
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool ShouldPackageStagedAsset(string stagedDataDirectory, string file)
    {
        var relative = Path.GetRelativePath(stagedDataDirectory, file).Replace('\\', '/');
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < parts.Length; ++i)
        {
            if (!string.Equals(parts[i], "AssetCache", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 2 < parts.Length && string.Equals(parts[i + 2], "tmp", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (i + 1 < parts.Length && string.Equals(parts[i + 1], "tmp", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class DirectoryStats
    {
        public int FileCount { get; init; }
        public long TotalBytes { get; init; }
    }

    private sealed class CopyDirectoryResult
    {
        public int CopiedFiles { get; set; }
        public int SkippedFiles { get; set; }
        public long CopiedBytes { get; set; }
        public long SkippedBytes { get; set; }
    }

    private static CopyDirectoryResult CopyDirectory(string source, string target, Func<string, bool> includeFile, string description, Action<string> logProgress)
    {
        var result = new CopyDirectoryResult();
        if (!Directory.Exists(source))
        {
            return result;
        }

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .ToArray();
        var includedFiles = files.Where(file => includeFile(file.FullName)).ToArray();
        var totalIncludedBytes = includedFiles.Sum(file => file.Length);
        result.SkippedFiles = files.Length - includedFiles.Length;
        result.SkippedBytes = files.Where(file => !includeFile(file.FullName)).Sum(file => file.Length);

        logProgress($"Preparing to copy {includedFiles.Length} {description} file(s), {FormatBytes(totalIncludedBytes)}.");

        var progressTimer = Stopwatch.StartNew();
        foreach (var file in includedFiles)
        {
            var targetFile = Path.Combine(target, Path.GetRelativePath(source, file.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            file.CopyTo(targetFile, overwrite: true);
            ++result.CopiedFiles;
            result.CopiedBytes += file.Length;

            if (result.CopiedFiles == includedFiles.Length ||
                result.CopiedFiles % 250 == 0 ||
                progressTimer.Elapsed.TotalSeconds >= 5.0)
            {
                logProgress($"Copying {description}: {result.CopiedFiles}/{includedFiles.Length} file(s), {FormatBytes(result.CopiedBytes)}/{FormatBytes(totalIncludedBytes)}.");
                progressTimer.Restart();
            }
        }

        return result;
    }

    private static DirectoryStats GetDirectoryStats(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new DirectoryStats();
        }

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .ToArray();

        return new DirectoryStats
        {
            FileCount = files.Length,
            TotalBytes = files.Sum(file => file.Length)
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024.0 && unit < units.Length - 1)
        {
            value /= 1024.0;
            ++unit;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }
}
