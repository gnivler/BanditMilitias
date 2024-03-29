﻿using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Roster;
using static BanditMilitias.Globals;

// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public static class Hacks
    {
        // throws during nuke (apparently not in 3.9)
        // parameters are included for debugging
        [HarmonyPatch(typeof(TroopRoster), "ClampXp")]
        public static class TroopRosterClampXpPatch
        {
            public static Exception Finalizer(Exception __exception, TroopRoster __instance)
            {
                if (__exception is not null)
                    Log.Debug?.Log(__exception);

                return null;
            }
        }
    }
}
