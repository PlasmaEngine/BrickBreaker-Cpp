// #pl-version 1

using PlasmaBuild.Core.Configuration;
using PlasmaBuild.Core.Rules;

public class BrickBreakerPluginModule : ModuleRules
{
    public BrickBreakerPluginModule(BuildContext context) : base(context)
    {
        Type = ModuleType.Runtime;
        SourceDirectory = "BrickBreakerPlugin";
        PCHUsage = PCHUsage.UseExplicitOrShared;
        SharedPCHHeaderFile = "BrickBreakerPluginPCH.h";

        PublicIncludePaths.Add("..");
        PrivateDefinitions.Add("BUILDSYSTEM_BUILDING_BRICKBREAKERPLUGIN_LIB");

        BrickBreakerSdk.ConfigureSdkConsumer(this, context);
        BrickBreakerActivePlugins.Apply(this, context);

        CStandard = CStandard.C17;
        CppStandard = CppStandard.Cpp20;
        TreatWarningsAsErrors = false;
    }
}
