using PlasmaBuild.Core.Configuration;
using PlasmaBuild.Core.Rules;

public class BrickBreakerTestsModule : ModuleRules
{
    public BrickBreakerTestsModule(BuildContext context) : base(context)
    {
        Type = ModuleType.Runtime;
        SourceDirectory = "BrickBreakerTests";
        PublicIncludePaths.Add("..");
        BrickBreakerSdk.ConfigureSdkConsumer(this, context);
        CppStandard = CppStandard.Cpp20;
    }
}
