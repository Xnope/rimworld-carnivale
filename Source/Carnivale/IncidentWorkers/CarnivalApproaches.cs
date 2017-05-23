﻿using Carnivale.Defs;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace Carnivale.IncidentWorkers
{
    public class CarnivalApproaches : IncidentWorker
    {
        private const int acceptanceBonus = 5;
        private const int rejectionPenalty = -10;

        public override float AdjustedChance
        {
            get
            {
                // TODO: Adjust based off proximity to roads
                return base.AdjustedChance;
            }
        }


        public override bool TryExecute(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            IntVec3 spawnSpot;
            int durationDays = Mathf.RoundToInt(this.def.durationDays.RandomInRange);

            // Attempt to find a spawn spot.
            if (!FindSpawnSpot(map, out spawnSpot))
            {
                if (CarnivaleMod.debugLog)
                    Log.Warning("Tried to execute incident CarnivalApproaches, failed to find reachable spawn spot.");
                return false;
            }

            if (!FindCarnivalFaction(out parms.faction))
            {
                if (CarnivaleMod.debugLog)
                    Log.Warning("Tried to execute incident CarnivalApproaches, failed to find valid faction.");
                return false;
            }

            // Main dialog node
            string title = "CarnivalApproachesTitle".Translate(parms.faction.Name);
            DiaNode initialNode = new DiaNode("CarnivalApproachesInitial".Translate(new object[]
            {
                parms.faction.Name,
                durationDays,
                map.info.parent.Label == "Colony" ? "your colony" : map.info.parent.Label
            }));

            // Accept button
            DiaOption acceptOption = new DiaOption("CarnivalApproachesAccept".Translate());
            acceptOption.action = delegate
            {
                // Do accept action
                parms.faction.AffectGoodwillWith(Faction.OfPlayer, acceptanceBonus);

                IncidentParms arrivalParms = StorytellerUtility.DefaultParmsNow(Find.Storyteller.def, IncidentCategory.AllyArrival, map);
                arrivalParms.forced = true;
                arrivalParms.faction = parms.faction;
                arrivalParms.spawnCenter = spawnSpot;
                arrivalParms.points = parms.points; // Do this?

                QueuedIncident qi = new QueuedIncident(new FiringIncident(_IncidentDefOf.CarnivalArrives, null, arrivalParms), Find.TickManager.TicksGame + GenDate.TicksPerDay);
                Find.Storyteller.incidentQueue.Add(qi);
            };
            initialNode.options.Add(acceptOption);

            // Accept thank you message
            DiaNode acceptedMessage = new DiaNode("CarnivalApproachesAcceptMessage".Translate(new object[]
            {
                parms.faction.leader.Name.ToStringFull
            }));
            DiaOption ok = new DiaOption("OK".Translate());
            ok.resolveTree = true;
            acceptedMessage.options.Add(ok);
            acceptOption.link = acceptedMessage;

            // Reject button
            DiaOption rejectOption = new DiaOption("CarnivalApproachesReject".Translate());
            rejectOption.action = delegate
            {
                // Do reject action
                parms.faction.AffectGoodwillWith(Faction.OfPlayer, rejectionPenalty);
            };
            initialNode.options.Add(rejectOption);

            // Reject fuck you message (TODO: randomise response)
            DiaNode rejectedMessage = new DiaNode("CarnivalApproachesRejectMessage".Translate(new object[]
            {
                parms.faction.leader.Name.ToStringShort
            }));
            DiaOption hangup = new DiaOption("HangUp".Translate());
            hangup.resolveTree = true;
            rejectedMessage.options.Add(hangup);
            rejectOption.link = rejectedMessage;

            // Draw dialog
            Find.WindowStack.Add(new Dialog_NodeTree(initialNode, true, true, title));

            return true;
        }


        private static bool FindCarnivalFaction(out Faction faction)
        {
            faction = (from f in Find.FactionManager.AllFactionsListForReading
                       where f.IsCarnival() && !f.HostileTo(Faction.OfPlayer)
                       select f).RandomElement();

            if (faction == null) return false;
            return true;
        }

        private static bool FindSpawnSpot(Map map, out IntVec3 spot)
        {
            return CellFinder.TryFindRandomEdgeCellWith(
                (IntVec3 c) => map.reachability.CanReachColony(c),
                map,
                CellFinder.EdgeRoadChance_Always,
                out spot);
        }

    }
}