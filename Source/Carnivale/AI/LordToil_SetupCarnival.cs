﻿using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Xnope;

namespace Carnivale
{
    public class LordToil_SetupCarnival : LordToil_Carn
    {
        // FIELDS + PROPERTIES

        private IEnumerable<Frame> Frames
        {
            get
            {
                List<Thing> framesList = Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
                foreach (var thing in framesList)
                {
                    Frame frame = thing as Frame;
                    if (frame.Faction == this.lord.faction)
                        yield return frame;
                }
            }
        }


        // CONSTRUCTORS //

        public LordToil_SetupCarnival() { }

        public LordToil_SetupCarnival(CarnivalInfo info)
        {
            this.data = new LordToilData_SetupCarnival(info);
        }


        // OVERRIDE METHODS //


        public override void Init()
        {
            base.Init();

            //foreach (var pawn in lord.ownedPawns)
            //{
            //    if (!pawn.RaceProps.Humanlike) continue;
            //    Log.Warning(pawn.NameStringShort + " --> " + pawn.kindDef.defName);
            //}

            LordToilData_SetupCarnival data = (LordToilData_SetupCarnival)this.data;

            // Give em a wood for trash sign
            data.TryHaveWorkerCarry(ThingDefOf.WoodLog, 1);

            // Give chapiteau
            if (data.TryHaveWorkerCarry(_DefOf.Carn_Crate_TentHuge, 1, Utilities.RandomFabricByCheapness()) != 1)
            {
                Log.Error("Could not give " + _DefOf.Carn_Crate_TentHuge + " to carnies of faction " + lord.faction + ". It will not be built.");
            }


            // Give workers lodging tents (currently 8 carnies per lodging tent)

            int numCarnies = this.lord.ownedPawns.Count - Info.pawnsWithRole[CarnivalRole.Carrier].Count;

            int numBedTents = numCarnies > 9 ? Mathf.CeilToInt(numCarnies / 8f) : 1;

            if (data.TryHaveWorkerCarry(_DefOf.Carn_Crate_TentLodge, numBedTents, Utilities.RandomFabricByCheapness()) != numBedTents)
            {
                Log.Error("Could not give " + _DefOf.Carn_Crate_TentLodge + " to carnies of faction " + lord.faction + ". It will not be built.");
            }

            if (Info.pawnsWithRole[CarnivalRole.Manager].Any())
            {
                if (data.TryHaveWorkerCarry(_DefOf.Carn_Crate_TentMan, 1, Utilities.RandomFabricByExpensiveness()) != 1)
                {
                    Log.Error("Could not give " + _DefOf.Carn_Crate_TentMan + " to carnies of faction " + lord.faction + ". It will not be built.");
                }
            }

            // Give workers stalls + entry sign

            int numStallCrates = Info.pawnsWithRole[CarnivalRole.Vendor].Count + _DefOf.Carn_SignEntry.costList.First().count;
            data.TryHaveWorkerCarry(_DefOf.Carn_Crate_Stall, numStallCrates, ThingDefOf.WoodLog);

            // Place blueprints
            foreach (Blueprint bp in AIBlueprintsUtility.PlaceCarnivalBlueprints(Info))
            {
                data.blueprints.Add(bp);
            }

            // Find spots for carriers to chill + a guard spot
            IntVec3 guardSpot = GetCarrierSpots().Average();

            // Assign guard spot at carriers
            Pawn guard;
            if (Info.pawnsWithRole[CarnivalRole.Guard].Where(g => !Info.rememberedPositions.ContainsKey(g)).TryRandomElement(out guard))
            {
                Info.rememberedPositions.Add(guard, guardSpot);

                // if can feed animals, give em kibble
                if (!guard.story.WorkTypeIsDisabled(WorkTypeDefOf.Handling))
                {
                    Thing kib = ThingMaker.MakeThing(ThingDefOf.Kibble);
                    kib.stackCount = 75;
                    guard.inventory.TryAddItemNotForSale(kib);
                }
            }

            // Set announcer pos at entrance sign
            TryGiveAnnouncerPosition();
        }



        public override void LordToilTick()
        {
            base.LordToilTick();

            // Check if everything is setup
            if (this.lord.ticksInToil % 601 == 0)
            {
                if (!(from frame in this.Frames
                      where !frame.Destroyed
                      select frame).Any())
                {
                    LordToilData_SetupCarnival data = (LordToilData_SetupCarnival)this.data;
                    if (!(from blue in data.blueprints
                          where !blue.Destroyed
                          select blue).Any())
                    {
                        bool anyBuildings = false;
                        foreach (Building building in from b in base.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                                                      where b.Faction == this.lord.faction
                                                      select b)
                        {
                            anyBuildings = true;
                            // Add buildings to CarnivalInfo
                            if (building.GetComp<CompCarnBuilding>() != null)
                            {
                                Info.carnivalBuildings.Add(building);
                            }
                        }

                        if (anyBuildings)
                        {
                            // Buildings are there. Next toil.
                            if (Info.CanEntertainNow)
                            {
                                lord.ReceiveMemo("SetupDoneEntertain");
                            }
                            else
                            {
                                // rest the carnival
                                lord.ReceiveMemo("SetupDoneRest");
                            }
                            return;
                        }
                        else
                        {
                            // No frames, blueprints, OR buildings
                            // Nothing is buildable. Was the carnival attacked?
                            lord.ReceiveMemo("NoBuildings");
                            Log.Error("LordToil_SetupCarnival found no frames, blueprints, or buildings after " + lord.ticksInToil + " ticks.");
                            return;
                        }
                    }
                }
            }
            // End check

        }


        public override void UpdateAllDuties()
        {
            foreach (Pawn pawn in this.lord.ownedPawns)
            {
                CarnivalRole pawnRole = pawn.GetCarnivalRole();

                if (pawnRole.Is(CarnivalRole.Worker))
                {
                    DutyUtility.BuildCarnival(pawn, Info.setupCentre, Info.baseRadius);
                }
                else if (pawnRole.Is(CarnivalRole.Carrier))
                {
                    IntVec3 pos;

                    if (Info.rememberedPositions.TryGetValue(pawn, out pos))
                    {
                        DutyUtility.HitchToSpot(pawn, pos);
                    }
                    else
                    {
                        DutyUtility.HitchToSpot(pawn, pawn.Position);
                    }
                }
                else
                {
                    DutyUtility.MeanderAndHelp(pawn, Info.setupCentre, Info.baseRadius);
                }

            }
        }


        public override void Notify_PawnLost(Pawn victim, PawnLostCondition cond)
        {
            if (cond == PawnLostCondition.IncappedOrKilled
                || cond == PawnLostCondition.MadePrisoner)
            {
                // Hostile actions
                this.lord.ReceiveMemo("HostileConditions");
                return;
            }

            // Non-hostile cause of PawnLost
            this.UpdateAllDuties();
        }


        public override void Notify_ConstructionFailed(Pawn pawn, Frame frame, Blueprint_Build newBlueprint)
        {
            if (frame.Faction == this.lord.faction && newBlueprint != null)
            {
                ((LordToilData_SetupCarnival)this.data).blueprints.Add(newBlueprint);
            }
        }


        public override void Cleanup()
        {
            // Do more cleanup here?
            LordToilData_SetupCarnival data = (LordToilData_SetupCarnival)this.data;
            data.availableCrates.RemoveAll(c => c.DestroyedOrNull());
            data.blueprints.RemoveAll(blue => blue.DestroyedOrNull());
        }


        private IEnumerable<IntVec3> GetCarrierSpots()
        {
            // Should only be called right after blueprint creation

            int countCarriers = Info.pawnsWithRole[CarnivalRole.Carrier].Count;
            int countSpots = 0;
            List<IntVec3> spots = new List<IntVec3>();
            CellRect searchRect = CellRect.CenteredOn(Info.setupCentre, 8);

            for (int i = 0; i < 50; i++)
            {
                // Try to find initial spot
                IntVec3 randomCell = searchRect.RandomCell;
                if (Map.reachability.CanReach(randomCell, Info.setupCentre, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly)
                    && !randomCell.GetThingList(this.Map).Any(t => t is Blueprint))
                {
                    spots.Add(randomCell);
                    countSpots++;
                    break;
                }
            }

            if (countSpots == 0)
            {
                goto Error;
            }

            byte attempts = 0;
            byte directionalTries = 0;
            while (attempts < 75 && countSpots < countCarriers)
            {
                IntVec3 lastSpot = spots.Last();
                IntVec3 newSpot = new IntVec3(lastSpot.x, lastSpot.y, lastSpot.z);
                IntVec3 offset;

                if (countSpots % 4 == 0)
                {
                    // New row
                    directionalTries++;
                }

                switch (directionalTries)
                {
                    case 0:
                        offset = IntVec3.East * 3;
                        break;
                    case 1:
                        offset = IntVec3.North * 3;
                        break;
                    case 2:
                        offset = IntVec3.West * 3;
                        break;
                    case 3:
                        offset = IntVec3.South * 3;
                        break;
                    default:
                        directionalTries = 1;
                        offset = IntVec3.East * 3;
                        break;
                }

                newSpot += offset;

                if (!spots.Contains(newSpot)
                    && Map.reachability.CanReach(newSpot, Info.setupCentre, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly)
                    && !newSpot.GetThingList(this.Map).Any(t => t is Blueprint))
                {
                    // Spot found
                    spots.Add(newSpot);
                    if (countSpots % 4 == 0)
                    {
                        // New row
                        directionalTries++;
                    }
                    countSpots++;
                }
                else if (directionalTries < 3)
                {
                    // No spot, increment direction to try
                    directionalTries++;
                }
                else
                {
                    // No spot, reset direction to try and find new starting spot

                    if (spots.Any())
                    {
                        // Try to find next spots close to other spots
                        searchRect = CellRect.CenteredOn(spots.Average(), 8);
                    }

                    for (int i = 0; i < 50; i++)
                    {
                        IntVec3 randomCell = searchRect.RandomCell;
                        if (Map.reachability.CanReach(randomCell, Info.setupCentre, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly)
                            && !randomCell.GetThingList(this.Map).Any(t => t is Blueprint))
                        {
                            spots.Add(randomCell);
                            countSpots++;
                            break;
                        }
                    }
                    directionalTries = 0;
                }

                // Whether or not a spot was found, increment attempts to avoid infinite looping
                attempts++;
            }

            Error:

            if (countSpots == countCarriers)
            {
                for (int i = 0; i < countSpots; i++)
                {
                    // Add calculated spots to rememberedPositions
                    Info.rememberedPositions.Add(Info.pawnsWithRole[CarnivalRole.Carrier][i], spots[i]);
                    yield return spots[i];
                }
            }
            else
            {
                Log.Error("Not enough spots found for carnival carriers to chill.");
            }
        }


        private bool TryGiveAnnouncerPosition()
        {
            Pawn announcer;

            if (!(from p in lord.ownedPawns
                  where p.story != null && p.story.adulthood != null
                    && p.story.adulthood.TitleShort == "Announcer"
                    && !Info.rememberedPositions.ContainsKey(p)
                  select p).TryRandomElement(out announcer))
            {
                // If no pawns have the announcer backstory
                if (!Info.pawnsWithRole[CarnivalRole.Entertainer].Where(p => !Info.rememberedPositions.ContainsKey(p)).TryRandomElement(out announcer))
                {
                    // No entertainers either
                    return false;
                }
            }

            IntVec3 offset = new IntVec3(-1, 0, -2);

            Info.rememberedPositions.Add(announcer, Info.bannerCell + offset);

            return true;
        }



    }
}
