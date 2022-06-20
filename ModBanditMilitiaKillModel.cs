using SandBox;
using SandBox.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace BanditMilitias
{
    public class ModBanditMilitiaKillModel : SandboxAgentDecideKilledOrUnconsciousModel
    {
        public override float GetAgentStateProbability(Agent affectorAgent, Agent effectedAgent, DamageTypes damageType, out float useSurgeryProbability)
        {
            useSurgeryProbability = 1f;
            if (((CharacterObject)effectedAgent.Character).StringId.Contains("Bandit_Militia"))
            {
                return 1f;
            }
            if (effectedAgent.IsHuman)
            {
                CharacterObject characterObject = (CharacterObject)effectedAgent.Character;
                if (Campaign.Current != null)
                {
                    if (characterObject.IsHero && !characterObject.HeroObject.CanDie(KillCharacterAction.KillCharacterActionDetail.DiedInBattle))
                    {
                        return 0f;
                    }
                    PartyBase party = effectedAgent.GetComponent<CampaignAgentComponent>()?.OwnerParty;
                    if (affectorAgent.IsHuman)
                    {
                        PartyBase enemyParty = affectorAgent.GetComponent<CampaignAgentComponent>()?.OwnerParty;
                        return 1f - Campaign.Current.Models.PartyHealingModel.GetSurvivalChance(party, characterObject, damageType, enemyParty);
                    }
                    return 1f - Campaign.Current.Models.PartyHealingModel.GetSurvivalChance(party, characterObject, damageType);
                }
            }
            return 1f;
        }
    }
}
