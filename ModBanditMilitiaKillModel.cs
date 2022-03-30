using SandBox.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace Bandit_Militias
{
    public class ModBanditMilitiaKillModel : DefaultAgentDecideKilledOrUnconsciousModel
    {
        public override float GetAgentStateProbability(Agent affectorAgent, Agent effectedAgent, DamageTypes damageType, out float useSurgeryProbability)
        {
            useSurgeryProbability = 1f;
            if (((CharacterObject)effectedAgent.Character).StringId.Contains("Bandit_Militia"))
            {
                return 1f;
            }
            return base.GetAgentStateProbability(affectorAgent, effectedAgent, damageType, out useSurgeryProbability);
        }
    }
}
