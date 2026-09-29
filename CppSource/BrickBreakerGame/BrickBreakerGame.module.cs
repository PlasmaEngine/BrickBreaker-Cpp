// #pl-version 1

using PlasmaBuild.Core.Configuration;
using PlasmaBuild.Core.Rules;

public class BrickBreakerGameModule : ModuleRules
{
    public BrickBreakerGameModule(BuildContext context) : base(context)
    {
        Type = ModuleType.Runtime;
        SourceDirectory = "BrickBreakerGame";
        PCHUsage = PCHUsage.UseExplicitOrShared;
        SharedPCHHeaderFile = null;

        PublicIncludePaths.Add("..");
        PrivateDefinitions.Add("GAME_PROJECT_FOLDER=" + BrickBreakerSdk.ProjectDirectoryForMacro(context));

        BrickBreakerSdk.ConfigureSdkConsumer(this, context);

        CStandard = CStandard.C17;
        CppStandard = CppStandard.Cpp20;
        TreatWarningsAsErrors = false;
    }
}
