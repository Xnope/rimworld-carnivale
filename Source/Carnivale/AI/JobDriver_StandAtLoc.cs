﻿using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Xnope;

namespace Carnivale
{
    public class JobDriver_StandAtLoc : JobDriver
    {
        private static string[] foodVendorMotes0Arg = new string[]
        {
            "YouThere",
            "FoodHere",
            "YouLookHungry"
        };

        private static string[] foodVendorMotes1Arg = new string[]
        {
            "HowAboutA",
            "GetYour"
        };

        private static string[] surplusVendorMotes0Arg = new string[]
        {
            "YouThere",
            "BargainPrices",
            "LightlyUsed",
            "ThisCouldBeYours"
        };

        private static string[] surplusVendorMotes1Arg = new string[]
        {
            "LikeNew",
            "CheckOut"
        };

        private static string[] curiosVendorMotes0Arg = new string[]
        {
            "BargainPrices",
            "ThisCouldBeYours",
            "RareItems"
        };

        private static string[] curiosVendorMotes1Arg = new string[]
        {
            "CheckOut",
            "GetItWhile"
        };

        private static string[] announcerMotes0Arg = new string[]
        {
            "StepRightUp",
            "YouThere",
            "WelcomeCarnival"
        };

        private static string[] entertainingMotes0Arg = new string[]
        {
            "Entertain1",
            "Entertain2",
            "Entertain3",
            "Entertain4",
            "Entertain5"
        };

        private static IntRange tickRange = new IntRange(650, 1350);

        [Unsaved]
        private bool moteArgs = false;

        [Unsaved]
        private CarnivalRole typeInt = 0;

        [Unsaved]
        private int tick = 650;


        private CarnivalInfo Info
        {
            get
            {
                return CarnUtils.Info;
            }
        }

        private CarnivalRole Type
        {
            get
            {
                if (typeInt == 0)
                {
                    typeInt = this.pawn.GetCarnivalRole();
                }
                return typeInt;
            }
        }


        public override string GetReport()
        {
            // TODO: translations

            if (TargetB.IsValid)
            {
                if (pawn.IsCarny())
                {
                    return "entertaining.";
                }
                else
                {
                    return "spectating.";
                }
            }

            if (Type.Is(CarnivalRole.Vendor))
            {
                return "peddling goods.";
            }
            else if (Type.Is(CarnivalRole.Entertainer))
            {
                return "announcing.";
            }
            else
            {
                return "resting in place.";
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Go to cell
            Toil gotoCell = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return gotoCell;

            Toil standToil;

            if (Type.Is(CarnivalRole.Vendor))
            {
                if (pawn.TraderKind == _DefOf.Carn_Trader_Curios)
                {
                    standToil = VendorStandWithMotes(curiosVendorMotes0Arg, curiosVendorMotes1Arg);
                }
                else if (pawn.TraderKind == _DefOf.Carn_Trader_Surplus)
                {
                    standToil = VendorStandWithMotes(surplusVendorMotes0Arg, surplusVendorMotes1Arg);
                }
                else
                {
                    standToil = VendorStandWithMotes(foodVendorMotes0Arg, foodVendorMotes1Arg);
                }
            }
            else if (Type.Is(CarnivalRole.Entertainer))
            {
                // TODO: differentiate between announcers and game masters
                if (TargetB.IsValid)
                {
                    standToil = StandWithMotes(entertainingMotes0Arg);
                }
                else
                {
                    standToil = StandWithMotes(announcerMotes0Arg);
                }
            }
            else
            {
                standToil = StaticStand();
            }

            standToil.socialMode = RandomSocialMode.SuperActive;
            standToil.defaultDuration = GenDate.TicksPerHour / 2;
            standToil.defaultCompleteMode = ToilCompleteMode.Delay;

            yield return standToil;
        }


        private Toil VendorStandWithMotes(string[] strings0Arg, string[] strings1Arg)
        {
            // Stand, no rotation, vendors want to trade, motes
            Toil toil = new Toil().FailOn(delegate(Toil t)
            {
                return t.actor.TraderKind == null;
            });

            toil.initAction = delegate
            {
                pawn.pather.StopDead();
                pawn.Rotation = Rot4.South;
                pawn.mindState.wantsToTradeWithColony = true;
                tick = tickRange.RandomInRange;
            };
            toil.tickAction = delegate
            {
                if (pawn.IsHashIntervalTick(tick)
                    && Info.colonistsInArea.Any())
                {
                    if (!moteArgs)
                    {
                        MoteMaker.ThrowText(
                            pawn.DrawPos,
                            Map,
                            strings0Arg.RandomElement().Translate(),
                            3f
                        );
                    }
                    else
                    {
                        var randomWareLabel = pawn.trader.Goods
                            .RandomElementByWeight(t => t.GetInnerIfMinified().MarketValue)
                            .GetInnerIfMinified()
                            .LabelNoCount;
                        int index = randomWareLabel.FirstIndexOf(c => c == '(') - 1;
                        if (index > 0 && index < randomWareLabel.Length - 1)
                        {
                            randomWareLabel = randomWareLabel.Substring(0, index);
                        }

                        MoteMaker.ThrowText(
                            pawn.DrawPos,
                            Map,
                            strings1Arg.RandomElement().Translate(randomWareLabel).CapitalizeFirst(),
                            5f
                        );
                    }

                    moteArgs = !moteArgs;
                }
            };
            toil.AddFinishAction(delegate
            {
                pawn.mindState.wantsToTradeWithColony = false;
            });

            return toil;
        }

        private Toil StandWithMotes(string[] strings0Arg)
        {
            // Stand, no rotation, motes
            Toil toil = new Toil();

            toil.initAction = delegate
            {
                pawn.pather.StopDead();
                tick = tickRange.RandomInRange;

                if (TargetB.IsValid)
                {
                    pawn.Rotation = TargetA.Cell.RotationFacing(TargetB.Cell);
                }
                else
                {
                    pawn.Rotation = Rot4.South;
                }
            };
            toil.tickAction = delegate
            {
                if (pawn.IsHashIntervalTick(tick)
                    && Info.colonistsInArea.Any())
                {
                    MoteMaker.ThrowText(
                        pawn.DrawPos,
                        Map,
                        strings0Arg.RandomElement().Translate(pawn.Faction),
                        3f
                    );
                }
            };

            return toil;
        }

        private Toil StaticStand()
        {
            // stand, rotate randomly
            Toil toil = new Toil();

            toil.initAction = delegate
            {
                pawn.pather.StopDead();
                tick = tickRange.RandomInRange;

                if (TargetB.IsValid)
                {
                    pawn.Rotation = TargetA.Cell.RotationFacing(TargetB.Cell);
                }
            };

            
            if (!TargetB.IsValid)
            {
                toil.tickAction = delegate
                {
                    if (pawn.IsHashIntervalTick(tick))
                    {
                        pawn.Rotation = Rot4.Random;
                    }
                };
            }

            return toil;
        }


    }
}
