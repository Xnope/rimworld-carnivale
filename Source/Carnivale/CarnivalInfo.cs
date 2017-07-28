﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Xnope;

namespace Carnivale
{
    public sealed class CarnivalInfo : MapComponent, ILoadReferenceable
    {
        private static IntRange addToRadius = new IntRange(5, 12);

        private const int TrashRadius = 4;

        public const float MaxEntertainHour = 22f;

        public const float MinEntertainHour = 10f;

        public const float MinRadius = 15f;

        public const float MaxRadius = 35f;


        // Fields

        public Lord currentLord;

        public IntVec3 setupCentre;

        public float baseRadius;

        public CellRect carnivalArea;

        public IntVec3 bannerCell;

        private IntVec3 trashCentre; // Assigned when blueprint is placed

        private int daysPassed;

        public bool alreadyEntertainedToday;

        public bool entertainingNow;

        public int feePerColonist = -1;

        public List<Pawn> allowedColonists;

        public List<Building> carnivalBuildings;

        public Dictionary<CarnivalRole, DeepPawnList> pawnsWithRole;

        public List<IntVec3> guardPositions;

        public Dictionary<Pawn, IntVec3> rememberedPositions;

        [Unsaved]
        public List<Thing> thingsToHaul;
        [Unsaved]
        public List<Pawn> colonistsInArea;
        [Unsaved]
        public List<IntVec3> checkForCells;
        [Unsaved]
        public LocomotionUrgency leavingUrgency = LocomotionUrgency.Walk;

        [Unsaved]
        private bool checkRemoveColonists;
        [Unsaved]
        private bool anyCarnyNeedsRest;
        [Unsaved]
        private IntVec3 averageLodgeTentPosInt = IntVec3.Invalid;
        [Unsaved]
        private Building_Carn entranceInt = null;
        [Unsaved]
        private Building_Tent chapInt = null;

        // Properties:

        public bool Active { get { return currentLord != null; } }

        public IntVec3 TrashCentre
        {
            set
            {
                this.trashCentre = value;
                foreach (var trashCell in TrashCells())
                {
                    checkForCells.Remove(trashCell);
                }
            }
        }

        public bool ShouldHaulTrash { get { return Active && trashCentre.IsValid; } }

        public bool AnyCarnyNeedsRest { get { return anyCarnyNeedsRest; } }

        public bool CanEntertainNow
        {
            get
            {
                if (alreadyEntertainedToday) return false;

                float curHour = GenLocalDate.HourFloat(map);
                return curHour < MaxEntertainHour && curHour > MinEntertainHour;
            }
        }

        public IntVec3 AverageLodgeTentPos
        {
            get
            {
                if (!averageLodgeTentPosInt.IsValid)
                {
                    averageLodgeTentPosInt = carnivalBuildings.Select(b => new LocalTargetInfo(b)).Average(delegate(LocalTargetInfo target)
                    {
                        if (((Building)target.Thing).Is(CarnBuildingType.Bedroom))
                        {
                            return 15;
                        }
                        return 1;
                    }).NearestStandableCell(map, 5);
                }

                return averageLodgeTentPosInt;
            }
        }

        public Building_Carn Entrance
        {
            get
            {
                if (entranceInt == null)
                {
                    entranceInt = GetFirstBuildingOf(_DefOf.Carn_SignEntry) as Building_Carn;
                }

                return entranceInt;
            }
        }

        public Building_Tent Chapiteaux
        {
            get
            {
                if (chapInt == null)
                {
                    chapInt = GetFirstBuildingOf(_DefOf.Carn_TentChap) as Building_Tent;
                }

                return chapInt;
            }
        }

        
        // Constructor
        public CarnivalInfo(Map map) : base(map)
        {
            Cleanup();
        }


        // working lists solely for ExposeData()
        [Unsaved]
        private List<Pawn> pawnWorkingList = null;
        [Unsaved]
        private List<IntVec3> vec3WorkingList = null;

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (Active)
                {
                    // Clean up unusable elements in collections

                    carnivalBuildings.RemoveAll(b => b.DestroyedOrNull() || !b.Spawned);

                    foreach (var list in pawnsWithRole.Values)
                    {
                        for (int i = list.Count - 1; i > -1; i--)
                        {
                            var pawn = list[i];
                            if (pawn.DestroyedOrNull() || !pawn.Spawned || pawn.Dead || pawn.Faction != currentLord.faction)
                            {
                                list.RemoveAt(i);
                            }
                        }
                    }

                    IEnumerator<Pawn> en = rememberedPositions.Keys.GetEnumerator();

                    while (en.MoveNext())
                    {
                        var pawn = en.Current;
                        if (pawn.DestroyedOrNull() || !pawn.Spawned || pawn.Dead || pawn.Faction != currentLord.faction)
                        {
                            rememberedPositions.Remove(pawn);
                        }
                    }
                }
                else
                {
                    Cleanup();
                }
            }


            Scribe_References.Look(ref this.currentLord, "currentLord");

            Scribe_Values.Look(ref this.setupCentre, "setupCentre", IntVec3.Invalid);

            Scribe_Values.Look(ref this.baseRadius, "baseRadius", 0f);

            Scribe_Values.Look(ref this.carnivalArea, "carnivalArea", default(CellRect));

            Scribe_Values.Look(ref this.bannerCell, "bannerCell", IntVec3.Invalid);

            Scribe_Values.Look(ref this.trashCentre, "trashCell", IntVec3.Invalid);

            Scribe_Values.Look(ref this.alreadyEntertainedToday, "alreadyEntertainedToday", false);

            Scribe_Values.Look(ref this.entertainingNow, "entertainingNow", false);

            Scribe_Values.Look(ref this.feePerColonist, "feePerColonist", -1);

            Scribe_Collections.Look(ref this.allowedColonists, "allowedColonists", LookMode.Reference);

            Scribe_Collections.Look(ref this.carnivalBuildings, "carnivalBuildings", LookMode.Reference);

            Scribe_Collections.Look(ref this.pawnsWithRole, "pawnsWithRoles", LookMode.Value, LookMode.Deep);

            Scribe_Collections.Look(ref this.guardPositions, "guardPositions", LookMode.Value);

            Scribe_Collections.Look(ref this.rememberedPositions, "rememberedPositions", LookMode.Reference, LookMode.Value, ref pawnWorkingList, ref vec3WorkingList);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RecalculateCheckForCells();
            }
        }


        public void Cleanup()
        {
            currentLord = null;

            setupCentre = IntVec3.Invalid;

            baseRadius = 0f;

            carnivalArea = default(CellRect);

            bannerCell = IntVec3.Invalid;

            trashCentre = IntVec3.Invalid;

            daysPassed = 0;

            alreadyEntertainedToday = false;

            entertainingNow = false;

            checkRemoveColonists = false;

            anyCarnyNeedsRest = false;

            if (feePerColonist > 0)
            {
                feePerColonist = -1;
            }
            else if (feePerColonist < -1)
            {
                feePerColonist = -feePerColonist;
            }

            if (allowedColonists != null)
            {
                allowedColonists.Clear();
            }
            else
            {
                allowedColonists = new List<Pawn>();
            }

            if (thingsToHaul != null)
            {
                thingsToHaul.Clear();
            }
            else
            {
                thingsToHaul = new List<Thing>();
            }

            if (colonistsInArea != null)
            {
                colonistsInArea.Clear();
            }
            else
            {
                colonistsInArea = new List<Pawn>();
            }

            if (carnivalBuildings != null)
            {
                carnivalBuildings.Clear();
            }
            else
            {
                carnivalBuildings = new List<Building>();
            }

            if (checkForCells != null)
            {
                checkForCells.Clear();
            }
            else
            {
                checkForCells = new List<IntVec3>();
            }

            if (pawnsWithRole != null)
            {
                pawnsWithRole.Clear();
            }
            else
            {
                pawnsWithRole = new Dictionary<CarnivalRole, DeepPawnList>(9);
            }

            if (guardPositions != null)
            {
                guardPositions.Clear();
            }
            else
            {
                guardPositions = new List<IntVec3>();
            }

            if (rememberedPositions != null)
            {
                rememberedPositions.Clear();
            }
            else
            {
                rememberedPositions = new Dictionary<Pawn, IntVec3>();
            }
    }


        public string GetUniqueLoadID()
        {
            return "CarnivalInfo_" + map.uniqueID;
        }


        // Main way to initialise a carnival in town
        public CarnivalInfo ReInitWith(Lord lord, IntVec3 spawnCentre)
        {
            // MUST BE CALLED AFTER LordMaker.MakeNewLord()

            Cleanup();

            currentLord = lord;

            // Set radius for carnies to stick to
            baseRadius = lord.ownedPawns.Count + addToRadius.RandomInRange;
            baseRadius = Mathf.Clamp(baseRadius, MinRadius, MaxRadius);

            // Calculate setup centre
            setupCentre = CarnCellFinder.BestCarnivalSetupPosition(spawnCentre, map);

            // Set initial check cells
            checkForCells.AddRange(GenRadial.RadialCellsAround(setupCentre, baseRadius, true)
                .Where(c => c.InBounds(map) && c.Walkable(map)));

            // Set carnival area
            carnivalArea = CellRect.CenteredOn(setupCentre, (int)baseRadius + 10).ClipInsideMap(map).ContractedBy(10);

            // Set initial banner spot
            bannerCell = PreCalculateBannerCell();

            // Cache pawn roles to lists
            var roles = Enum.GetValues(typeof(CarnivalRole));
            for (int i = 0; i < roles.Length; i++)
            {
                var role = (CarnivalRole)roles.GetValue(i);

                List<Pawn> pawns = (from p in lord.ownedPawns
                                    where i == 0 ? p.IsOnly(role) : p.Is(role)
                                    select p).ToList();
                if (role == CarnivalRole.Vendor)
                {
                    // Will enable this when they are standing in stalls
                    pawns.ForEach(p => p.mindState.wantsToTradeWithColony = false);
                }

                pawnsWithRole.Add(role, pawns);
            }

            daysPassed = GenDate.DaysPassed;

            // The rest is assigned as the carnival goes along

            return this;
        }



        public override void MapRemoved()
        {
            this.Cleanup();

            base.MapRemoved();
        }

        public override void MapComponentTick()
        {
            if (Active)
            {
                if (Find.TickManager.TicksGame % 1009 == 0)
                {
                    // Check if there are any things needing to be hauled to carriers or trash
                    CheckForHaulables(false);

                    // Check for a new day
                    var day = GenDate.DaysPassed;
                    if (day > daysPassed)
                    {
                        daysPassed = day;
                        alreadyEntertainedToday = false;
                        // Colonists must pay every day
                        allowedColonists.Clear();
                    }
                }
                else if (Find.TickManager.TicksGame % 757 == 0)
                {
                    // Check for colonists in area. They are kept in the list for roughly an hour.
                    CheckForColonists(checkRemoveColonists);
                    checkRemoveColonists = !checkRemoveColonists;
                }
                else if (Find.TickManager.TicksGame % 499 == 0)
                {
                    // Check carny rest levels
                    this.anyCarnyNeedsRest = currentLord.ownedPawns
                        .Where(p => p.IsAny(CarnivalRole.Entertainer, CarnivalRole.Vendor))
                        .Any(c => c.needs.rest.CurCategory > RestCategory.Rested);
                }
            }
        }


        public void CheckForHaulables(bool checkForCrates)
        {
            foreach (var cell in checkForCells)
            {
                foreach (var thing in cell.GetThingList(map))
                {
                    if (HaulableValidator(thing, checkForCrates))
                    {
                        if (Prefs.DevMode)
                            Log.Message("[Carnivale] thingsToHaul : Adding " + thing + ". pos=" + thing.Position);
                        thingsToHaul.Add(thing);
                    }
                }
            }
        }

        private bool HaulableValidator(Thing thing, bool checkForCrates)
        {
            return thing.DefaultHaulLocation(checkForCrates) != HaulLocation.None
                   && !thingsToHaul.Contains(thing)
                   && thing.IsForbidden(Faction.OfPlayer); // dropped things are by default forbidden to the player
        }

        public void CheckForColonists(bool removeColonists)
        {
            if (removeColonists)
            {
                for (int i = colonistsInArea.Count - 1; i > -1; i--)
                {
                    var col = colonistsInArea[i];
                    if (!checkForCells.Contains(col.PositionHeld))
                    {
                        colonistsInArea.Remove(col);
                    }
                }
            }

            foreach (var cell in checkForCells)
            {
                var firstPawn = cell.GetFirstPawn(map);

                if (firstPawn != null
                    && firstPawn.IsColonistPlayerControlled
                    && !colonistsInArea.Contains(firstPawn))
                {
                    colonistsInArea.Add(firstPawn);
                }
            }
        }

        public void RecalculateCheckForCells()
        {
            if (!Active) return;

            if (checkForCells != null)
            {
                checkForCells.Clear();
            }
            else
            {
                checkForCells = new List<IntVec3>();
            }

            var tcellSet = new HashSet<IntVec3>();

            foreach (var tcell in TrashCells())
            {
                tcellSet.Add(tcell);
            }

            checkForCells.AddRange(GenRadial.RadialCellsAround(setupCentre, baseRadius, true)
                .Where(c => c.InBounds(map) && c.Walkable(map) && !tcellSet.Contains(c)));

            foreach (var building in carnivalBuildings)
            {
                if (building.def == _DefOf.Carn_SignTrash) continue;

                foreach (var bcell in building.OccupiedRect().ExpandedBy(1))
                {
                    if (!tcellSet.Contains(bcell) && !checkForCells.Contains(bcell))
                        checkForCells.Add(bcell);
                }
            }

        }


        public void AddBuilding(Building building)
        {
            carnivalBuildings.Add(building);

            if (building.def == _DefOf.Carn_SignTrash) return;

            guardPositions.Add(building.OccupiedRect().ExpandedBy(1).RandomCell);

            foreach (var cell in building.OccupiedRect().ExpandedBy(1))
            {
                if (!checkForCells.Contains(cell))
                {
                    checkForCells.Add(cell);
                }
            }
        }

        public Building GetFirstBuildingOf(CarnBuildingType type)
        {
            if (!Active)
            {
                Log.Error("Cannot get carnival building: carnival is not in town.");
                return null;
            }

            foreach (var building in this.carnivalBuildings)
            {
                if (building.Is(type))
                {
                    return building;
                }
            }

            if (Prefs.DevMode)
                Log.Warning("[Carnivale] Tried to find any building of type " + type + " in CarnivalInfo, but none exists.");
            return null;
        }

        public Building GetFirstBuildingOf(ThingDef def)
        {
            if (!Active)
            {
                Log.Error("Cannot get carnival building: carnival is not in town.");
                return null;
            }

            foreach (var building in this.carnivalBuildings)
            {
                if (building.def == def)
                {
                    return building;
                }
            }

            if (Prefs.DevMode)
                Log.Warning("[Debug] Tried to find any building of def " + def + " in CarnivalInfo, but none exists.");
            return null;
        }

        public Building_Carn GetRandomBuildingOf(CarnBuildingType type, bool suppressWarnings = false)
        {
            if (!Active)
            {
                Log.Error("Cannot get carnival building: carnival is not in town.");
                return null;
            }

            Building building;

            if (carnivalBuildings.Where(b => b.Is(type)).TryRandomElement(out building))
            {
                return building as Building_Carn;
            }

            if (Prefs.DevMode && !suppressWarnings)
                Log.Warning("[Carnivale] Tried to find any building of type " + type + " in CarnivalInfo, but none exists.");
            return null;
        }

        public IEnumerable<Building> GetBuildingsOf(CarnBuildingType type)
        {
            foreach (var building in carnivalBuildings)
            {
                if (building.Is(type))
                {
                    yield return building;
                }
            }
        }

        public LocalTargetInfo GetRandomTentDoor(bool suppressWarnings = false, CarnBuildingType secondaryType = CarnBuildingType.Bedroom)
        {
            var tent = GetRandomBuildingOf(CarnBuildingType.Tent | secondaryType, suppressWarnings) as Building_Tent;

            if (tent != null)
            {
                return tent.GetTentFlap();
            }

            return IntVec3.Invalid;
        }


        public IEnumerable<IntVec3> TrashCells()
        {
            if (!trashCentre.IsValid) yield break;

            foreach (var cell in GenRadial.RadialCellsAround(trashCentre, TrashRadius, false))
            {
                if (!cell.InBounds(map) || !cell.Walkable(map)) continue;

                yield return cell;
            }
        }

        public IntVec3 GetNextTrashCellFor(Thing thing, Pawn carrier = null)
        {
            if (thing != null && ShouldHaulTrash)
            {
                foreach (var cell in TrashCells())
                {
                    if (StoreUtility.IsGoodStoreCell(cell, map, thing, carrier, currentLord.faction))
                    {
                        return cell;
                    }
                }
            }
            

            Log.Error("Found no spot to put trash. Jobs will be ended. Did the trash area overflow, or was trash cell calculation bad?");
            //this.trashCentre = IntVec3.Invalid;
            return trashCentre;
        }

        public bool AnyCarriersCanCarry(Thing thing)
        {
            return pawnsWithRole[CarnivalRole.Carrier].Any(c => c.HasSpaceFor(thing));
        }

        public int UnreservedThingsToHaulOf(ThingDef def, Pawn claimant)
        {
            int num = thingsToHaul.Sum(delegate (Thing t)
            {
                if (t.def == def && claimant.CanReserve(t))
                    return t.stackCount;
                return 0;
            });

            return num;
        }


        public Pawn GetBestAnnouncer(bool withoutAssignedPostion = true)
        {
            if (!Active)
            {
                return null;
            }


            if (Entrance != null && Entrance.assignedPawn != null && (!withoutAssignedPostion || !rememberedPositions.ContainsKey(Entrance.assignedPawn)))
            {
                return Entrance.assignedPawn;
            }

            Pawn ticketTaker;

            if (!(from p in pawnsWithRole[CarnivalRole.Entertainer]
                  where p.story != null && p.story.adulthood != null
                    && p.story.adulthood.TitleShort == "Announcer"
                    && !withoutAssignedPostion || !rememberedPositions.ContainsKey(p)
                  select p).TryRandomElement(out ticketTaker))
            {
                // If no pawns have the announcer backstory
                if (!pawnsWithRole[CarnivalRole.Entertainer].Where(p => !withoutAssignedPostion || !rememberedPositions.ContainsKey(p)).TryRandomElement(out ticketTaker))
                {
                    // No entertainers either, use leader
                    return currentLord.faction.leader;
                }
            }

            return ticketTaker;
        }

        public bool AssignAnnouncerToBuilding(Pawn announcer, Building_Carn building, bool relieveExistingPawn = false)
        {
            if (announcer == null || building == null) return false;

            var cell = building.GetAnnouncerCell();

            if (cell.IsValid)
            {
                if (building.assignedPawn != null)
                {
                    if (relieveExistingPawn)
                    {
                        rememberedPositions.Remove(building.assignedPawn);
                        building.assignedPawn = announcer;
                        rememberedPositions.Add(announcer, cell);
                        guardPositions.Add(cell);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    building.assignedPawn = announcer;
                    rememberedPositions.Add(announcer, cell);
                    guardPositions.Add(cell);
                    return true;
                }
            }

            return false;
        }

        public Pawn GetBestGuard(bool withoutAssignedPosition = true)
        {
            Pawn guard = null;
            if (!pawnsWithRole[CarnivalRole.Guard]
                .Where(g => g.needs.rest.CurCategory == RestCategory.Rested
                            && (!withoutAssignedPosition || !rememberedPositions.ContainsKey(g)))
                .TryRandomElementByWeight(g => g.health.summaryHealth.SummaryHealthPercent, out guard))
            {
                if (!pawnsWithRole[CarnivalRole.Guard].TryRandomElementByWeight((Pawn p) => 1f / ((float)p.needs.rest.CurCategory + 1f), out guard))
                {
                    guard = pawnsWithRole[CarnivalRole.Any].Where(p => !p.Is(CarnivalRole.Carrier) && !p.IsOnly(CarnivalRole.None) && p.equipment.Primary != null).FirstOrDefault();
                }
            }

            if (guard == null && Prefs.DevMode)
                Log.Warning("\t[Carnivale] Got null guard from CarnivalInfo.GetBestGuard().");

            return guard;
        }


        private IntVec3 PreCalculateBannerCell()
        {
            var minDistToCentre = baseRadius / 2;
            var maxDistToCentre = baseRadius + 3f;

            IntVec3 colonistPos;

            if (!(colonistPos = CellsUtil.ApproxClosestColonistBuilding(map, setupCentre, ThingDefOf.Door)).IsValid)
            {
                colonistPos = CellsUtil.AverageColonistPosition(map);
            }
            else
            {
                colonistPos = colonistPos.AverageWith(CellsUtil.AverageColonistPosition(map));
            }

            // Initial pass
            var closestCell = CellRect.CenteredOn(setupCentre, (int)minDistToCentre).EdgeCells.ClosestCellTo(colonistPos, map);

            if (Prefs.DevMode)
                Log.Message("[Carnivale] bannerCell initial minimum pass: " + closestCell);

            // Triangular spread pass

            var candidateTri = CellTriangle.FromTarget(closestCell, colonistPos, 55, (maxDistToCentre - minDistToCentre));

            closestCell = candidateTri.B.AverageWith(candidateTri.C);

            if (Prefs.DevMode)
                Log.Message("\t[Carnivale] bannerCell closest to colony pass: " + closestCell);

            // line of sight pass

            if (!GenSight.LineOfSight(setupCentre, closestCell, map) || !GenSight.LineOfSight(closestCell, colonistPos, map))
            {
                Func<IntVec3, float> weightLoSSetupCentre = c => 2f / (setupCentre.CountObstructingCellsTo(c, map) + 1f);
                Func<IntVec3, float> weightLoSColony = c => 1f / (c.CountObstructingCellsTo(colonistPos, map) + 1f);
                Func<IntVec3, float> weightBest = c => (weightLoSSetupCentre(c) == 2f ? 2f : 0f) + (weightLoSColony(c) == 1f ? 1f : 0f);

                var candidateCells = candidateTri.Where(c => c.InBounds(map));

                IntVec3 tempCell;

                if (candidateCells.TryRandomElementByWeight(weightBest, out tempCell))
                {
                    closestCell = tempCell;
                    if (Prefs.DevMode)
                        Log.Message("\t[Carnivale] bannerCell optimal LoS pass: " + closestCell);
                }
                else if (candidateCells.TryRandomElementByWeight(weightLoSSetupCentre + weightLoSColony, out tempCell))
                {
                    closestCell = tempCell;
                    if (Prefs.DevMode)
                        Log.Message("\t[Carnivale] bannerCell sub-optimal LoS pass: " + closestCell);
                }
                else
                {
                    if (Prefs.DevMode)
                        Log.Warning("\t[Carnivale] bannerCell failed all LoS passes. Is candidateCells empty? Leaving it at: " + closestCell);
                }
            }

            // Road pass

            IntVec3 road;
            if (closestCell.TryFindNearestRoadCell(map, (int)(baseRadius), out road))
            {
                var minDistSqrdToCentre = minDistToCentre * minDistToCentre;
                var maxDistSqrdToCentre = maxDistToCentre * maxDistToCentre;
                var found = false;
                foreach (var rcell in GenRadial.RadialCellsAround(road, 7, true))
                {
                    int distSqrdToCentre = rcell.DistanceToSquared(setupCentre);
                    if (distSqrdToCentre < maxDistSqrdToCentre && distSqrdToCentre > minDistSqrdToCentre)
                    {
                        // Found the edge of a road, try to centre it
                        var adjustedCell = road;

                        if ((adjustedCell + IntVec3.East * 2).GetTerrain(map).HasTag("Road"))
                        {
                            adjustedCell += IntVec3.East * 2;
                        }
                        else if ((adjustedCell + IntVec3.West * 2).GetTerrain(map).HasTag("Road"))
                        {
                            adjustedCell += IntVec3.West * 2;
                        }
                        else
                        {
                            adjustedCell += IntVec3.North;
                        }

                        closestCell = adjustedCell;
                        found = true;

                        if (Prefs.DevMode)
                            Log.Message("\t[Carnivale] bannerCell road pass: " + closestCell);

                        break;
                    }
                }

                if (!found && Prefs.DevMode)
                {
                    Log.Warning("\t[Carnivale] bannerCell road pass failed. Reason: out of range from setupCentre. Initial road=" + road);
                }
            }
            else if (map.roadInfo.roadEdgeTiles.Any() && Prefs.DevMode)
            {
                Log.Warning("\t[Carnivale] bannerCell road pass failed. Reason: no roads found in search radius. searchRadius=" + (baseRadius));
            }

            // Mountain proximity pass

            var attempts = 0;
            IntVec3 nearestMineable;
            while (attempts < 10
                   && closestCell.DistanceSquaredToNearestMineable(map, 12, out nearestMineable) <= 36)
            {
                closestCell = CellRect.CenteredOn(closestCell, 4).FurthestCellFrom(nearestMineable);
                attempts++;

                if (Prefs.DevMode)
                    Log.Message("\t[Carnivale] bannerCell mountain proximity pass #" + attempts + ": " + closestCell);
            }

            if (attempts == 10 && Prefs.DevMode)
                Log.Warning("\t[Carnivale] bannerCell mountain proximity passes took too many tries. Leaving it at: " + closestCell);

            // End passes

            if (Prefs.DevMode)
                Log.Message("[Carnivale] bannerCell pre-buildability pass: " + closestCell);

            return closestCell;
        }

    }
}
