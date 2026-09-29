// #pl-version 1

using PlasmaBuild.Core.Configuration;
using PlasmaBuild.Core.Rules;

public class BrickBreakerPluginTarget : TargetRules
{
    public BrickBreakerPluginTarget(BuildContext context) : base(context)
    {
        Type = TargetType.SharedLibrary;
        OutputName = "BrickBreakerPlugin";
        OutputDirectory = BrickBreakerSdk.BinaryDirectory(context);

        UsePCHFiles = true;
        UseUnityBuild = false;
        UseAdaptiveUnityBuild = false;
        UseIncrementalLinking = true;

        BrickBreakerActivePlugins.ApplyTargetDependencies(this, context);
        ExtraModules.Add("BrickBreakerPluginModule");
    }
}
