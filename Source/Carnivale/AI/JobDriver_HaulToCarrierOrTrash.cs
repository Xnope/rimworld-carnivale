﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Carnivale
{
    public class JobDriver_HaulToCarrierOrTrash : JobDriver
    {
        private const int PlaceInInventoryDuration = 25;

        public Thing ToHaul
        {
            get
            {
                return base.CurJob.GetTarget(TargetIndex.A).Thing;
            }
        }

        private Pawn Carrier
        {
            get
            {
                return (Pawn)base.CurJob.GetTarget(TargetIndex.B).Thing;
            }
        }

        private CarnivalInfo Info
        {
            get
            {
                return Map.GetComponent<CarnivalInfo>();
            }
        }

        [DebuggerHidden]
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(delegate
            {
                return !this.pawn.Map.lordManager.lords.Contains(this.pawn.CurJob.lord);
            });

            Toil reserve = Toils_Reserve.Reserve(TargetIndex.A);

            yield return reserve;

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            yield return this.DetermineNumToHaul();

            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, true);

            //yield return this.AddCarriedThingToTransferable();

            yield return this.RemoveCarriedThingFromThingsToHaul();

            yield return Toils_Haul.CheckForGetOpportunityDuplicate(reserve, TargetIndex.A, TargetIndex.None, false, t => Info.thingsToHaul.Contains(t));

            HaulLocation destType = Utilities.GetHaulToLocation(this.ToHaul);

            if (destType == HaulLocation.ToCarriers && Info.AnyCarriersCanCarry(this.ToHaul))
            {
                Toil findCarrier = FindCarrier();

                yield return findCarrier;

                //yield return Toils_Reserve.Reserve(TargetIndex.B);

                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch)
                    .JumpIf(() => !JobDriver_PrepareCaravan_GatherItems.IsUsableCarrier(this.Carrier, this.pawn, false), findCarrier);

                yield return Toils_General.Wait(PlaceInInventoryDuration).WithProgressBarToilDelay(TargetIndex.B, false, -0.5f);

                yield return PlaceTargetInCarrierInventory();
            }
            else //if (destType == HaulLocation.ToTrash)
            {
                Toil findTrashSpot = FindTrashSpot();

                yield return findTrashSpot;

                //yield return Toils_Reserve.Reserve(TargetIndex.B);

                yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.Touch);

                yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, findTrashSpot, false);
            }
        }




        private Toil DetermineNumToHaul()
        {
            return new Toil
            {
                initAction = delegate
                {
                    int num = Info.TotalCountToHaulFor(this.ToHaul.def);

                    if (this.pawn.carryTracker.CarriedThing != null)
                    {
                        num -= this.pawn.carryTracker.CarriedThing.stackCount;
                    }

                    if (num <= 0)
                    {
                        this.pawn.jobs.EndCurrentJob(JobCondition.Succeeded, true);
                    }
                    else
                    {
                        base.CurJob.count = num;
                    }

                },
                defaultCompleteMode = ToilCompleteMode.Instant,
                atomicWithPrevious = true
            };
        }


        private Toil RemoveCarriedThingFromThingsToHaul()
        {
            return new Toil
            {
                initAction = delegate
                {
                    var haulable = this.pawn.carryTracker.CarriedThing;
                    if (haulable != null
                        && Info.thingsToHaul.Remove(haulable))
                    {
                        if (Prefs.DevMode)
                            Log.Warning("[Debug] Removing " + haulable + " from CarnivalInfo.thingsToHaul.");
                    }
                }
            };
        }


        private Toil FindCarrier()
        {
            return new Toil
            {
                initAction = delegate
                {
                    Pawn carrier = null;
                    foreach (var car in Info.pawnsWithRole[CarnivalRole.Carrier])
                    {
                        if (car.HasSpaceFor(this.ToHaul))
                        {
                            carrier = car;
                            break;
                        }
                    }

                    if (carrier == null)
                    {
                        Log.Error("Could not find a carrier to carry " + ToHaul + ". A validation step failed somewhere.");
                    }
                    else
                    {
                        base.CurJob.SetTarget(TargetIndex.B, carrier);
                    }
                }
            };
        }



        private Toil FindTrashSpot()
        {
            // Doubting that this needs to be done this way.
            // Only use case is if this step needs to ever be jumped back to.
            return new Toil
            {
                initAction = delegate
                {
                    base.CurJob.SetTarget(TargetIndex.B, Info.GetNextTrashSpotFor());
                }
            };
        }



        private Toil PlaceTargetInCarrierInventory()
        {
            return new Toil
            {
                initAction = delegate
                {
                    Pawn_CarryTracker carryTracker = this.pawn.carryTracker;
                    Thing carriedThing = carryTracker.CarriedThing;
                    //this.Transferable.AdjustTo(Mathf.Max(this.Transferable.CountToTransfer - carriedThing.stackCount, 0));
                    carryTracker.innerContainer.TryTransferToContainer(carriedThing, this.Carrier.inventory.innerContainer, carriedThing.stackCount, true);
                }
            };
        }

    }
}
