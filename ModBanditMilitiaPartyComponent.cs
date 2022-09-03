using System.Collections.Generic;
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

// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class ModBanditMilitiaPartyComponent : WarPartyComponent
    {
        [SaveableField(1)] public readonly Banner Banner;
        [SaveableField(2)] public readonly string BannerKey;
        [SaveableField(3)] public CampaignTime LastMergedOrSplitDate = CampaignTime.Now;
        [SaveableField(4)] public Dictionary<Hero, float> Avoidance = new();
        [SaveableField(5)] private Hero leader;
        [SaveableField(6)] private Settlement homeSettlement;
        [CachedData] public TextObject cachedName;

        public override Hero Leader => leader;
        public override Hero PartyOwner => MobileParty?.ActualClan?.Leader; // clan is null during nuke  
        public override Settlement HomeSettlement => homeSettlement;
        private static readonly MethodInfo GetLocalizedText = AccessTools.Method(typeof(MBTextManager), "GetLocalizedText");


        public override TextObject Name
        {
            get
            {
                cachedName ??= new TextObject((string)GetLocalizedText.Invoke(
                    null, new object[] { $"{Possess(Leader.FirstName.ToString())} {Globals.Settings.BanditMilitiaString}" }));
                cachedName.SetTextVariable("IS_BANDIT", 1);
                return cachedName;
            }
        }

        public override void ChangePartyLeader(Hero newLeader)
        {
            Traverse.Create(this).Field<Hero>("<Leader>k__BackingField").Value = newLeader;
            if (newLeader != null && Leader != newLeader && !Leader.IsDead)
                Leader?.RemoveMilitiaHero();
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            if (!IsBandit(MobileParty))
                IsBandit(MobileParty) = true;
        }

        public ModBanditMilitiaPartyComponent(Clan heroClan)
        {
            Banner = Banners.GetRandomElement();
            BannerKey = Banner.Serialize();
            var hero = CreateHero(heroClan);
            if (hero.HomeSettlement is null)
                _homeSettlement(hero) = hero.BornSettlement;
            HiddenInEncyclopedia(hero.CharacterObject) = true;
            homeSettlement = hero.BornSettlement;
            leader = hero;
        }
    }
}
