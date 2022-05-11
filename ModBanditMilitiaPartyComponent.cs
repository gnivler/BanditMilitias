using System;
using System.Reflection;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
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
        public override Hero PartyOwner => MobileParty.ActualClan?.Leader;
        public override Settlement HomeSettlement { get; }
        public override Hero Leader => leader;
        [SaveableField(1)] private Hero leader;
        [SaveableField(2)] internal readonly Banner Banner;
        [SaveableField(3)] internal readonly string BannerKey;
        [SaveableField(4)] internal CampaignTime LastMergedOrSplitDate = CampaignTime.Now;

        [CachedData] private TextObject cachedName;
        private static readonly MethodInfo GetLocalizedText = AccessTools.Method(typeof(MBTextManager), "GetLocalizedText");

        public override TextObject Name
        {
            get
            {
                cachedName ??= new TextObject((string)GetLocalizedText.Invoke(null, new object[] { $"{Possess(MobileParty.LeaderHero.FirstName.ToString())} Bandit Militia" }));
                cachedName.SetTextVariable("IS_BANDIT", 1);
                return cachedName;
            }
        }

        public override void ChangePartyLeader(Hero newLeader)
        {
            if (leader != newLeader)
            {
                leader?.RemoveMilitiaHero();
            }

            leader = newLeader;
        }

        //private ModBanditMilitiaPartyComponent()
        //{
        //    Banner = Banners.GetRandomElement();
        //    BannerKey = Banner.Serialize();
        //    leader = CreateHero();
        //}
        //
        //private ModBanditMilitiaPartyComponent(Hero hero) : this()
        //{
        //    leader = hero;
        //    HomeSettlement = hero.HomeSettlement;
        //}

        public ModBanditMilitiaPartyComponent()
        {
            Banner = Banners.GetRandomElement();
            BannerKey = Banner.Serialize();
            leader = CreateHero();
            ConfigureLeader(leader);
            //LogMilitiaFormed(MobileParty);
        }
    }
}
