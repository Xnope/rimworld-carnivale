﻿using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;
using Xnope;
using Verse.AI.Group;

namespace Carnivale
{
    public class LordToil_DefendCarnival : LordToil_Carn
    {
        private int pawnsKilled = 0;
        private int numChargers = 0;

        public override bool AllowSatisfyLongNeeds
        {
            get
            {
                return false;
            }
        }


        public LordToil_DefendCarnival() { }

        public override void Init()
        {
            if (Prefs.DevMode)
                Log.Message("[Carnivale] Carnival is in defending mode.");

            foreach (var lord in Map.lordManager.lords)
            {
                lord.ReceiveMemo("DangerPresent");
            }

            if (lord.ownedPawns.Count > 10)
            {
                var steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                steel.stackCount = 75;
                GenPlace.TryPlaceThing(steel, Info.setupCentre, Map, ThingPlaceMode.Near);

                var aveHostilePos = Map.attackTargetsCache.TargetsHostileToFaction(lord.faction)
                    .Where(targ => !targ.ThreatDisabled())
                    .Select(targ => targ.Thing.Position)
                    .Average();

                var closestPos = Info.carnivalArea.ClosestCellTo(aveHostilePos);
                var line = CellLine.Between(closestPos, aveHostilePos);

                if (line.Slope > 1f || line.Slope < -1f)
                {
                    for (int i = -3; i < 3; i++)
                    {
                        var spot = closestPos + IntVec3.West * i;
                        if (AIBlueprintsUtility.CanPlaceBlueprintAt(spot, ThingDefOf.Sandbags))
                        {
                            AIBlueprintsUtility.PlaceBlueprint(ThingDefOf.Sandbags, spot);
                        }
                    }
                }
                else
                {
                    for (int i = -3; i < 3; i++)
                    {
                        var spot = closestPos + IntVec3.North * i;
                        if (AIBlueprintsUtility.CanPlaceBlueprintAt(spot, ThingDefOf.Sandbags))
                        {
                            AIBlueprintsUtility.PlaceBlueprint(ThingDefOf.Sandbags, spot);
                        }
                    }
                }
            }
        }

        public override void Cleanup()
        {
            if (Prefs.DevMode)
                Log.Message("[Carnivale] Carnival is exiting defending mode.");
        }

        public override void UpdateAllDuties()
        {
            var nearHostiles = Map.attackTargetsCache.TargetsHostileToFaction(lord.faction)
                .Where(targ => !targ.ThreatDisabled())
                .Select(targ => targ.Thing);

            var aveHostilesPos = nearHostiles.Select(t => t.Position).Average();

            IntVec3 closestGuardSpot;
            IntVec3 bestGatherSpot;

            if (aveHostilesPos.IsValid && Info.guardPositions.Any())
            {
                closestGuardSpot = Info.guardPositions.MinBy(c => c.DistanceToSquared(aveHostilesPos));
            }
            else
            {
                closestGuardSpot = Info.bannerCell;
            }

            if (Info.Chapiteaux != null)
            {
                bestGatherSpot = Info.Chapiteaux.Position;
            }
            else
            {
                bestGatherSpot = Info.setupCentre;
            }

            foreach (var pawn in lord.ownedPawns)
            {
                if (pawn.Dead || pawn.Downed) continue;

                var distressedCarny = RandomExposedCarnyByHealth(pawn);
                var nearHost = nearHostiles.MinBy(t => pawn.Position.DistanceToSquared(t.Position));
                
                CarnivalRole role = pawn.GetCarnivalRole();

                if (role.Is(CarnivalRole.Manager))
                {
                    var guard = Info.GetBestGuard(false);

                    if (Rand.Chance(0.4f) && guard != null && !guard.Dead && !guard.Downed)
                    {
                        pawn.mindState.duty = new PawnDuty(DutyDefOf.Escort, guard, 7f)
                        {
                            locomotion = LocomotionUrgency.Jog
                        };
                    }
                    else
                    {
                        if (distressedCarny != null)
                        {
                            DutyUtility.DefendPoint(pawn, distressedCarny, null, 5f);
                        }
                        else
                        {
                            DutyUtility.DefendPoint(pawn, closestGuardSpot, nearHost);
                        }
                    }
                }
                else if (role.Is(CarnivalRole.Guard))
                {
                    if (numChargers > 0)
                    {
                        DutyUtility.ChargeHostiles(pawn);
                        numChargers--;
                    }
                    else if (distressedCarny != null && (Rand.Chance(0.33f) || pawn.mindState.enemyTarget == null))
                    {
                        DutyUtility.DefendPoint(pawn, distressedCarny, null, 5f);
                    }
                    else
                    {
                        DutyUtility.DefendPoint(pawn, closestGuardSpot, nearHost);
                    }
                }
                else if (pawn.equipment != null && pawn.equipment.Primary != null)
                {
                    if (numChargers > 0)
                    {
                        DutyUtility.ChargeHostiles(pawn);
                        numChargers--;
                    }
                    else if (pawn.health.summaryHealth.SummaryHealthPercent > 0.85f)
                    {
                        IntVec3 tentDoor;

                        if (pawnsKilled > 3 && distressedCarny != null)
                        {
                            DutyUtility.DefendPoint(pawn, distressedCarny, null, 5f);
                        }
                        else if (Rand.Bool && (tentDoor = Info.GetRandomTentDoor(true, CarnBuildingType.Attraction).Cell).IsValid)
                        {
                            DutyUtility.DefendPoint(pawn, tentDoor, null);
                        }
                        else
                        {
                            DutyUtility.DefendPoint(pawn, closestGuardSpot, nearHost);
                        }
                    }
                    else
                    {
                        DutyUtility.DefendPoint(pawn, bestGatherSpot, null, 5f);
                    }
                }
                else
                {
                    // TODO

                    pawn.mindState.duty = new PawnDuty(DutyDefOf.Travel, bestGatherSpot)
                    {
                        locomotion = LocomotionUrgency.Sprint
                    };
                }

            }
        }

        public override void Notify_PawnLost(Pawn victim, PawnLostCondition cond)
        {
            base.Notify_PawnLost(victim, cond);

            if (cond == PawnLostCondition.IncappedOrKilled)
            {
                if (victim == lord.faction.leader)
                {
                    lord.ReceiveMemo("LeaderKilled");
                    return;
                }

                pawnsKilled++;
                numChargers++;
            }

            UpdateAllDuties();
        }

        public override void LordToilTick()
        {
            base.LordToilTick();

            if (Find.TickManager.TicksGame % 223 == 0)
            {
                var hostiles = Map.attackTargetsCache.TargetsHostileToFaction(lord.faction);
                var anyHostiles = false;

                foreach (var hostile in hostiles)
                {
                    var pawn = hostile as Pawn;
                    if ((pawn != null && !pawn.Downed) || GenHostility.IsActiveThreat(hostile))
                    {
                        anyHostiles = true;
                        break;
                    }
                }

                if (!anyHostiles)
                {
                    if (pawnsKilled > lord.ownedPawns.Count / 4)
                    {
                        lord.ReceiveMemo("BattleDonePawnsLost");
                    }
                    else
                    {
                        lord.ReceiveMemo("BattleDone");
                    }
                }
            }
        }


        private Pawn RandomExposedCarnyByHealth(Pawn searcher)
        {
            Pawn pawn = null;
            lord.ownedPawns
                .Where(p => p != searcher && !p.Dead && p.IsOutdoors() && p.health.summaryHealth.SummaryHealthPercent < 0.95f)
                .TryRandomElementByWeight(p => p.Downed ? 100f : 1f / p.health.summaryHealth.SummaryHealthPercent, out pawn);

            return pawn;
        }


    }
}
