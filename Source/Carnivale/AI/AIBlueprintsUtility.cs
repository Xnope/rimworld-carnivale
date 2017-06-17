﻿using RimWorld;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace Carnivale
{
    public static class AIBlueprintsUtility
    {
        private static CarnivalInfo info;

        private static List<Thing> availableCrates;

        private static List<Pawn> stallUsers;

        private static List<IntVec3> cachedPos = new List<IntVec3>();

        // The only public method; use this
        [DebuggerHidden]
        public static IEnumerable<Blueprint> PlaceCarnivalBlueprints(CarnivalInfo info)
        {
            // Assign necessary values to this singleton (is this technically a singleton?)
            AIBlueprintsUtility.info = info;
            
            if (info.currentLord.CurLordToil.data is LordToilData_Setup)
            {
                availableCrates = ((LordToilData_Setup)info.currentLord.CurLordToil.data).availableCrates;
                stallUsers = ((List<Pawn>)info.pawnsWithRole[CarnivalRole.Vendor]).ListFullCopyOrNull();
            }
            else
            {
                Log.Error("Tried to place carnival blueprints while not in setup toil.");
                yield break;
            }

            cachedPos = new List<IntVec3>(availableCrates.Count);



            // Do the blueprint thing

            if (!availableCrates.NullOrEmpty())
            {
                foreach (Blueprint tent in PlaceTentBlueprints())
                {
                    cachedPos.Add(tent.Position);
                    yield return tent;
                }

                if (!stallUsers.NullOrEmpty())
                {
                    foreach (Blueprint stall in PlaceStallBlueprints())
                    {
                        cachedPos.Add(stall.Position);
                        yield return stall;
                    }
                }

                Blueprint entrance = PlaceEntranceBlueprint();

                if (entrance != null)
                {
                    cachedPos.Add(entrance.Position);
                    yield return entrance;
                }

                Blueprint trashSign = PlaceTrashBlueprint();

                if (trashSign != null)
                {
                    yield return trashSign;
                }
            }

            cachedPos.Clear();
            cachedPos = null;
        }


        private static IEnumerable<Blueprint> PlaceTentBlueprints()
        {
            ThingDef tentDef = _DefOf.Carn_TentMedBed;
            Rot4 rot = Rot4.Random;
            IntVec3 tentSpot = FindRandomPlacementFor(tentDef, rot, true, (int)info.baseRadius / 2);

            IntVec3 lineDirection = rot.ToIntVec3(1); // shifted clockwise by 1

            // Place lodging tents (8 pawns per medium sized tent)
            int attempts = 0;
            while (attempts < 20 && availableCrates.Any(t => t.def == _DefOf.Carn_Crate_TentLodge))
            {
                // Following works as intended iff size.x == size.y

                // Distance between tents is 1 cell
                if (attempts > 0)
                {
                    tentSpot += lineDirection * ((tentDef.size.x + 1));
                }

                if (CanPlaceBlueprintAt(tentSpot, rot, tentDef))
                {
                    RemoveFirstCrateOf(_DefOf.Carn_Crate_TentLodge);
                    Utilities.ClearThingsFor(info.map, tentSpot, tentDef.size, rot, false, true);
                    yield return PlaceBlueprint(tentDef, tentSpot, rot);
                }
                else
                {
                    rot.AsByte++;
                    lineDirection = rot.ToIntVec3(1);
                }

                if (attempts > 4)
                {
                    // Find new placement if next spot doesn't work
                    tentSpot = FindRandomPlacementFor(tentDef, rot, info.carnivalArea);
                }

                attempts++;
            }

            if (attempts == 20 && availableCrates.Any(t => t.def == _DefOf.Carn_Crate_TentLodge))
            {
                Log.Error("Tried too many times to place tents. Some may not be built.");
            }

            // Place manager tent
                if (!availableCrates.Any(c => c.def == _DefOf.Carn_Crate_TentMan))
                yield break;

            rot = Rot4.Random;
            tentDef = _DefOf.Carn_TentSmallMan;

            // Try to place near other tents
            CellRect searchArea = CellRect.CenteredOn(tentSpot, 10);
            tentSpot = FindRandomPlacementFor(tentDef, rot, searchArea, tentSpot);

            if (tentSpot.IsValid)
            {
                RemoveFirstCrateOf(_DefOf.Carn_Crate_TentMan);
                Utilities.ClearThingsFor(info.map, tentSpot, tentDef.size, rot, false, true);
                yield return PlaceBlueprint(tentDef, tentSpot, rot);
            }
            else
            {
                // use suboptimal random cell
                tentSpot = FindRadialPlacementFor(tentDef, rot, info.setupCentre, (int)info.baseRadius / 2);
                if (tentSpot.IsValid)
                {
                    RemoveFirstCrateOf(_DefOf.Carn_Crate_TentMan);
                    Utilities.ClearThingsFor(info.map, tentSpot, tentDef.size, rot, false, true);
                    yield return PlaceBlueprint(tentDef, tentSpot, rot);
                }
                else
                {
                    Log.Error("Found no valid placement for manager tent.");
                }
            }

        }

        private static IEnumerable<Blueprint> PlaceStallBlueprints()
        {
            // Default stall is food for now
            ThingDef stallDef = _DefOf.Carn_StallFood;
            IntVec3 stallSpot = IntVec3.Invalid;
            Rot4 rot = default(Rot4);
            CellRect stallArea = info.carnivalArea;

            foreach (Pawn stallUser in stallUsers)
            {
                if (stallUser.TraderKind != null)
                {
                    // Handle vendor stalls

                    if (stallUser.TraderKind == _DefOf.Carn_Trader_Food)
                    {
                        stallDef = _DefOf.Carn_StallFood;
                    }
                    else if (stallUser.TraderKind == _DefOf.Carn_Trader_Surplus)
                    {
                        stallDef = _DefOf.Carn_StallSurplus;
                    }
                    else if (stallUser.TraderKind == _DefOf.Carn_Trader_Curios)
                    {
                        stallDef = _DefOf.Carn_StallCurios;
                    }
                    else
                    {
                        Log.Error("Trader " + stallUser.NameStringShort + " is not a carnival vendor and will get no stall.");
                        continue;
                    }
                }



                if (stallSpot.IsValid)
                {
                    // Next spot should be close to last spot
                    stallSpot = FindRandomPlacementFor(stallDef, rot, stallArea, stallSpot);
                }
                else
                {
                    // Find random initial spot
                    stallSpot = FindRandomPlacementFor(stallDef, rot, false, (int)info.baseRadius / 3);
                    stallArea = CellRect.CenteredOn(stallSpot, 8);
                }


                // Finally, spawn the f*cker
                if (stallSpot.IsValid)
                {
                    RemoveFirstCrateOf(_DefOf.Carn_Crate_Stall);
                    Utilities.ClearThingsFor(info.map, stallSpot, stallDef.size, rot, false, true);
                    // Add spot to stall user's spot
                    info.rememberedPositions.Add(stallUser, stallSpot);
                    yield return PlaceBlueprint(stallDef, stallSpot, rot);
                }
            }
        }

        private static Blueprint PlaceEntranceBlueprint()
        {
            if (!availableCrates.Any(c => c.def == _DefOf.Carn_Crate_Stall))
                return null;

            ThingDef bannerDef = _DefOf.Carn_SignEntry;
            Rot4 rot = default(Rot4);

            IntVec3 bannerSpot = FindRadialPlacementFor(bannerDef, rot, info.bannerCell, 16);

            if (bannerSpot.IsValid)
            {
                info.bannerCell = bannerSpot;

                if (Prefs.DevMode)
                    Log.Warning("CarnivalInfo.bannerCell final pass: " + info.bannerCell.ToString());

                RemoveFirstCrateOf(_DefOf.Carn_Crate_Stall);
                Utilities.ClearThingsFor(info.map, info.bannerCell, bannerDef.size, rot, false, true);
                return PlaceBlueprint(bannerDef, bannerSpot, rot);
            }

            // If that fails, try any spot in the carnival area (suboptimal)

            Log.Error("Couldn't find an optimum place for " + bannerDef + ". Giving random place in carnival area.");
            bannerSpot = FindRandomPlacementFor(bannerDef, rot);
            
            if (bannerSpot.IsValid)
            {
                info.bannerCell = bannerSpot;

                if (Prefs.DevMode)
                    Log.Warning("CarnivalInfo.bannerCell final pass: " + info.bannerCell.ToString());

                RemoveFirstCrateOf(_DefOf.Carn_Crate_Stall);
                Utilities.ClearThingsFor(info.map, bannerSpot, bannerDef.size, rot, false, true);
                return PlaceBlueprint(bannerDef, bannerSpot, rot);
            }

            Log.Error("Couldn't find any place for " + bannerDef + ". Not retrying.");
            return null;
        }

        private static Blueprint PlaceTrashBlueprint()
        {
            ThingDef signDef = _DefOf.Carn_SignTrash;
            IntVec3 trashPos = info.carnivalArea.ContractedBy(3).FurthestCellFrom(cachedPos.Average());
            Utilities.ClearThingsFor(info.map, trashPos, new IntVec2(4,4), default(Rot4));
            return PlaceBlueprint(signDef, trashPos, default(Rot4), ThingDefOf.WoodLog);
        }




        private static bool CanPlaceBlueprintAt(IntVec3 spot, Rot4 rot, ThingDef def)
        {
            if (!spot.IsValid) return false;

            // Cheaty cheaty
            bool isEdifice = def.IsEdifice();

            def.building.isEdifice = true;

            bool result = GenConstruct.CanPlaceBlueprintAt(def, spot, rot, info.map, false, null).Accepted;

            def.building.isEdifice = isEdifice;

            return result;
        }


        private static Blueprint PlaceBlueprint(ThingDef def, IntVec3 spot, Rot4 rotation, ThingDef stuff = null)
        {
            return GenConstruct.PlaceBlueprintForBuild(def, spot, info.map, rotation, info.currentLord.faction, stuff);
        }


        private static IntVec3 FindRandomPlacementFor(ThingDef def, Rot4 rot, bool preferFarFromColony = false, int contractedBy = 0)
        {
            CellRect noGo = CellRect.CenteredOn(info.bannerCell, info.carnivalArea.Width / 2);

            CellRect adjustedArea = info.carnivalArea.ContractedBy(contractedBy);

            for (int i = 0; i < 200; i++)
            {
                IntVec3 randomCell = adjustedArea.RandomCell;

                if (preferFarFromColony && noGo.Contains(randomCell))
                    continue;

                if (info.map.reachability.CanReach(randomCell, info.carnivalArea.CenterCell, Verse.AI.PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly))
                {
                    if (CanPlaceBlueprintAt(randomCell, rot, def))
                    {
                        return randomCell;
                    }
                }
            }
            return IntVec3.Invalid;
        }

        private static IntVec3 FindRandomPlacementFor(ThingDef def, Rot4 rot, CellRect otherArea, IntVec3 preferCardinalAdjacentTo = default(IntVec3))
        {
            for (int i = 0; i < 200; i++)
            {
                IntVec3 randomCell = otherArea.RandomCell;

                if (info.map.reachability.CanReach(randomCell, info.carnivalArea.CenterCell, Verse.AI.PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly))
                {
                    if (CanPlaceBlueprintAt(randomCell, rot, def))
                    {
                        if (preferCardinalAdjacentTo != default(IntVec3))
                        {
                            return randomCell;
                        }

                        if (randomCell.AdjacentToCardinal(preferCardinalAdjacentTo))
                        {
                            return randomCell;
                        }
                    }
                }
            }
            return IntVec3.Invalid;
        }



        private static IntVec3 FindRadialPlacementFor(ThingDef def, Rot4 rot, IntVec3 centre, int radius)
        {
            foreach (var cell in GenRadial.RadialCellsAround(centre, radius, true))
            {
                if (info.map.reachability.CanReach(cell, info.carnivalArea.CenterCell, Verse.AI.PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly))
                {
                    if (CanPlaceBlueprintAt(cell, rot, def))
                    {
                        return cell;
                    }
                }
            }

            return IntVec3.Invalid;
        }


        private static void RemoveFirstCrateOf(ThingDef def)
        {
            Thing first = availableCrates.FirstOrDefault(t => t.def == def);
            if (first != null)
            {
                availableCrates.Remove(first);
            }
        }

        
    }
}
