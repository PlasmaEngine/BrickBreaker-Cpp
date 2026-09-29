using PlasmaBuild.Core.Configuration;
using PlasmaBuild.Core.Rules;

public class BrickBreakerTestsTarget : TargetRules
{
    public BrickBreakerTestsTarget(BuildContext context) : base(context)
    {
        Type = TargetType.Executable;
        OutputName = "BrickBreakerTests";
        OutputDirectory = BrickBreakerSdk.BinaryDirectory(context);
        UseUnityBuild = false;
        ExtraModules.Add("BrickBreakerTestsModule");
    }
}
