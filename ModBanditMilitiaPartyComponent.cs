using System.Reflection;
using BanditMilitias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using static BanditMilitias.Globals;
using static BanditMilitias.Helpers.Helper;

namespace BanditMilitias
{
    public class ModBanditMilitiaPartyComponent : WarPartyComponent
    {
        [SaveableField(1)] public readonly Banner Banner;
        [SaveableField(2)] public readonly string BannerKey;
        [SaveableField(3)] public CampaignTime LastMergedOrSplitDate = CampaignTime.Now;
        [CachedData] private TextObject cachedName;

        public override Hero PartyOwner => MobileParty.ActualClan?.Leader;
        public override Settlement HomeSettlement { get; }
        public override Hero Leader { get; }
        private static readonly MethodInfo GetLocalizedText = AccessTools.Method(typeof(MBTextManager), "GetLocalizedText");

        public override TextObject Name
        {
            get
            {
                cachedName ??= new TextObject((string)GetLocalizedText.Invoke(null, new object[] { $"{Possess(Leader.FirstName.ToString())} Bandit Militia" }));
                cachedName.SetTextVariable("IS_BANDIT", 1);
                return cachedName;
            }
        }

        public override void ChangePartyLeader(Hero newLeader)
        {
            if (Leader != newLeader)
            {
                Leader?.RemoveMilitiaHero();
            }

            Traverse.Create(this).Field<Hero>("<Leader>k__BackingField").Value = newLeader;
        }

        public ModBanditMilitiaPartyComponent(Clan heroClan)
        {
            Banner = Banners.GetRandomElement();
            BannerKey = Banner.Serialize();
            var hero = CreateHero(heroClan);
            ConfigureLeader(hero);
            HomeSettlement = hero.BornSettlement;
            Leader = hero;
            //LogMilitiaFormed(MobileParty);
        }
    }
}
