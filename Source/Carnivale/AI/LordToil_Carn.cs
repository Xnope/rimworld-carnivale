﻿using Verse;
using Verse.AI.Group;

namespace Carnivale
{
    public abstract class LordToil_Carn : LordToil
    {
        // FIELDS + PROPERTIES //

        [Unsaved]
        private LordToilData_Carnival dataInt;

        public LordToilData_Carnival Data
        {
            get
            {
                if (dataInt == null)
                {
                    dataInt = (LordToilData_Carnival)this.data;
                }
                return dataInt;
            }
        }

        public override IntVec3 FlagLoc
        {
            get
            {
                return Data.setupCentre;
            }
        }


        // CONSTRUCTORS //

        public LordToil_Carn() { }


        // OVERRIDE METHODS //

        
    }
}
