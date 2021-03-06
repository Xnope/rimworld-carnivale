﻿using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI.Group;

namespace Carnivale
{
    class IncidentWorker_CarnivalArrives : IncidentWorker_NeutralGroup
    {
        protected override PawnGroupKindDef PawnGroupKindDef
        { get { return _DefOf.Carnival; } }


        protected override bool CanFireNowSub(IIncidentTarget target)
        {
            Map map = (Map)target;
            if (map.GetComponent<CarnivalInfo>().Active)
            {
                // only one carnival per map
                return false;
            }

            // check bad game conditions
            foreach (var condition in map.GameConditionManager.ActiveConditions)
            {
                if (condition.def == GameConditionDefOf.PsychicSoothe
                    || condition.def == GameConditionDefOf.ToxicFallout)
                {
                    return false;
                }
            }

            return true;
        }


        protected override bool FactionCanBeGroupSource(Faction f, Map map, bool desperate = false)
        {
            return !f.IsPlayer
                   && f.IsCarnival()
                   && !f.defeated
                   && !f.HostileTo(Faction.OfPlayer)
                   && (desperate
                      || (f.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.OutdoorTemp)
                   && f.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.SeasonalTemp)));
        }


        protected List<Pawn> SpawnPawns(IncidentParms parms, int spawnPointSpread)
        {
            Map map = (Map)parms.target;

            if (parms.spawnRotation != Rot4.East)
            {
                if (Prefs.DevMode)
                    Log.Message("[Carnivale] Spawn centre for CarnivalArrives was not precomputed. Resolving now.");

                IntVec3 tempSpot = parms.spawnCenter;

                if (CarnCellFinder.BestCarnivalSpawnSpot(map, out tempSpot))
                    parms.spawnCenter = tempSpot;
                else if (Prefs.DevMode)
                    Log.Warning("[Carnivale] Failed to resolve spawn center for CarnivalArrives. Defaulting.");

                int feePerColonist = CarnUtils.CalculateFeePerColonist(parms.points);
                CarnUtils.Info.feePerColonist = -feePerColonist;
            }
            
            PawnGroupMakerParms defaultMakerParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(parms);

            List<Pawn> list = PawnGroupMakerUtility.GeneratePawns(this.PawnGroupKindDef, defaultMakerParms, false).ToList();

            foreach (Pawn p in list)
            {
                IntVec3 spawnPoint = CellFinder.RandomClosewalkCellNear(parms.spawnCenter, map, spawnPointSpread, null);
                GenSpawn.Spawn(p, spawnPoint, map);
            }

            parms.faction.leader.skills.GetSkill(SkillDefOf.Shooting).levelInt = 9;

            return list;
        }


        public override bool TryExecute(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            // Cheaty:
            int durationDays = parms.raidPodOpenDelay == 140 ? 3 : parms.raidPodOpenDelay;
            // End cheaty.

            // Resolve parms (currently counting on parent class to handle this)
            if (!base.TryResolveParms(parms))
            {
                if (Prefs.DevMode)
                    Log.Warning("[Carnivale] Could not execute CarnivalArrives: the spawn point calculated yesterday is probably no longer valid.");
                return false;
            }

            // Spawn pawns. Counting on you, IncidentWorker_NeutralGroup.
            List<Pawn> pawns = this.SpawnPawns(parms, 17);
            if (pawns.Count < 3)
            {
                if (Prefs.DevMode)
                    Log.Warning("[Carnivale] Could not execute CarnivalArrives: could not generate enough valid pawns.");
                return false;
            }

            List<Pawn> vendors = new List<Pawn>();
            foreach (Pawn p in pawns)
            {
                if (p.TraderKind != null)
                    // Get list of vendors
                    vendors.Add(p);
                if (p.needs != null && p.needs.food != null)
                    // Also feed the carnies
                    p.needs.food.CurLevel = p.needs.food.MaxLevel;
            }

            string label = "LetterLabelCarnivalArrival".Translate();

            string text = "LetterCarnivalArrival".Translate(parms.faction.Name, durationDays);

            if (vendors.Count > 0)
            {
                text += "CarnivalArrivalVendorsList".Translate();
                foreach (Pawn vendor in vendors)
                {
                    text += "\n  " + vendor.NameStringShort + ", " + vendor.TraderKind.label.CapitalizeFirst();
                }
            }

            PawnRelationUtility.Notify_PawnsSeenByPlayer(pawns, ref label, ref text, "LetterRelatedPawnsNeutralGroup".Translate(), true);
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.Good, parms.faction.leader, null);

            //LordJob_EntertainColony lordJob = new LordJob_EntertainColony(durationDays);
            //Lord lord = LordMaker.MakeNewLord(parms.faction, lordJob, map, pawns);

            //CarnivalUtils.Info.ReInitWith(lord, parms.spawnCenter);

            CarnUtils.MakeNewCarnivalLord(parms.faction, map, parms.spawnCenter, durationDays, pawns);

            return true;
        }
        
    }
}
