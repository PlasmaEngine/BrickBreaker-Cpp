// #pl-version 1

using PlasmaBuild.Core.Configuration;
using PlasmaBuild.Core.Rules;

public class BrickBreakerWorkspace : WorkspaceRules
{
    public BrickBreakerWorkspace(BuildContext context) : base(context)
    {
        TargetNames.Add("BrickBreakerPlugin");
        TargetNames.Add("BrickBreakerGame");
        TargetNames.Add("BrickBreakerTests");
    }
}
