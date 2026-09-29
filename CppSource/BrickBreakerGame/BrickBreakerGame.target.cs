// #pl-version 1

using PlasmaBuild.Core.Configuration;
using PlasmaBuild.Core.Rules;
using BrickBreakerAndroid;

public class BrickBreakerGameTarget : TargetRules
{
    public BrickBreakerGameTarget(BuildContext context) : base(context)
    {
        Type = context.Platform == TargetPlatform.Android ? TargetType.SharedLibrary : TargetType.Executable;
        OutputName = "BrickBreakerGame";
        OutputDirectory = BrickBreakerSdk.BinaryDirectory(context);

        UsePCHFiles = true;
        UseUnityBuild = false;
        UseAdaptiveUnityBuild = false;
        UseIncrementalLinking = true;

        TargetDependencies.Add("BrickBreakerPlugin");
        BrickBreakerActivePlugins.ApplyTargetDependencies(this, context);
        ExtraModules.Add("BrickBreakerGameModule");

        PlasmaBuildAndroid.ConfigureAndroidTarget(this, context, BrickBreakerSdk.AndroidPackageOptions(context));
    }
}
