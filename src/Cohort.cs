// uses dominance to allocate psn and subtract transpiration from soil water, average cohort vars over layer

using Landis.Core;
using Landis.SpatialModeling;
using Landis.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Landis.Extension.Succession.BiomassPnET
{
    public class Cohort : Landis.Library.AgeOnlyCohorts.ICohort, Landis.Library.BiomassCohorts.ICohort
    {
        public static event Landis.Library.BiomassCohorts.DeathEventHandler<Landis.Library.BiomassCohorts.DeathEventArgs> DeathEvent;
        public static event Landis.Library.BiomassCohorts.DeathEventHandler<Landis.Library.BiomassCohorts.DeathEventArgs> AgeOnlyDeathEvent;

        private const double V = 1.0;
        public byte Layer;

        public delegate void SubtractTranspiration(float transpiration, ISpeciesPNET Species);
        public delegate void AddWoodyDebris(float Litter, float KWdLit);
        public delegate void AddLitter(float AddLitter, ISpeciesPNET Species);

        private bool leaf_on = false;

        public static IEcoregionPnET ecoregion;
        public static ActiveSite site;

        public static AddWoodyDebris addwoodydebris;

        public static AddLitter addlitter;

        private float biomassmax;
        private float biomass; // root + wood
        private float fol;
        private float nsc;
        private ushort age;
        private float defolProp; //BRM
        private bool firstDefol = true; // First defoliation applied to cohort
        private bool firstAlloc = true;  //First foliage allocation applied to cohort (to avoid multiple applications for each sublayer)
        private float lastWoodySenescence; // last recorded woody senescence
        private float lastFoliageSenescence; // last recorded foliage senescence
        private float lastFRad;  //last month's average FRad
        private List<float> lastSeasonFRad;  // last growing season FRad
        private float adjFracFol;
        private bool firstYear;
        private float adjHalfSat;
        private float adjFolN;
        private int coldKill;

        public ushort index;

        private ISpeciesPNET species;
        private LocalOutput cohortoutput;

        // Leaf area index per subcanopy layer (m/m)
        public float[] LAI = null;

        // Gross photosynthesis (gC/mo)
        public float[] GrossPsn = null;

        // Foliar respiration (gC/mo)
        public float[] FolResp = null;

        // Net photosynthesis (gC/mo)
        public float[] NetPsn = null;

        // Mainenance respiration (gC/mo)
        public float[] MaintenanceRespiration = null;

        // Transpiration (mm/mo)
        public float[] Transpiration = null;

        // Reduction factor for suboptimal radiation on growth
        public float[] FRad = null;

        // Reduction factor for suboptimal or supra optimal water 
        public float[] FWater = null;

        // Actual water used to calculate FWater
        public float[] Water = null;

        // Actual pressurehead used to calculate FWater
        public float[] PressHead = null;

        // Reduction factor for ozone 
        public float[] FOzone = null;

        // Interception (mm/mo)
        public float[] Interception = null;

        // Adjustment folN based on fRad
        public float[] AdjFolN = null;

        // Adjustment fracFol based on fRad
        public float[] AdjFracFol = null;

        // Modifier of CiCa ratio based on fWater and Ozone
        public float[] CiModifier = null;

        // Adjustment to Amax based on CO2
        public float[] DelAmax = null;


        private float plantN; // non structure n in trees
        private float nRatio; // N stress indicator (1-1+FolNConRange)



        public void InitializeSubLayers()
        {
            // Initialize subcanopy layers
            index = 0;
            LAI = new float[PlugIn.IMAX];
            GrossPsn = new float[PlugIn.IMAX];
            FolResp = new float[PlugIn.IMAX];
            NetPsn = new float[PlugIn.IMAX];
            Transpiration = new float[PlugIn.IMAX];
            FRad = new float[PlugIn.IMAX];
            FWater = new float[PlugIn.IMAX];
            Water = new float[PlugIn.IMAX];
            PressHead = new float[PlugIn.IMAX];
            FOzone = new float[PlugIn.IMAX];
            MaintenanceRespiration = new float[PlugIn.IMAX];
            Interception = new float[PlugIn.IMAX];
            AdjFolN = new float[PlugIn.IMAX];
            AdjFracFol = new float[PlugIn.IMAX];
            CiModifier = new float[PlugIn.IMAX];
            DelAmax = new float[PlugIn.IMAX];
            Initialize_CN_Cohort_monthly();//Zhou
        }

        public void StoreFRad()
        {
            // Filter for growing season months only
            if (leaf_on)
            {
                lastFRad = FRad.Average();
                lastSeasonFRad.Add(lastFRad);
            }
        }

        public void CalcAdjFracFol()
        {
            if (lastSeasonFRad.Count() > 0)
            {
                float lastSeasonAvgFRad = lastSeasonFRad.ToArray().Average();
                float fracFol_slope = species.FracFolShape;
                float fracFol_int = species.MaxFracFol;
                // linear version
                //adjFracFol = (lastSeasonAvgFRad * fracFol_slope + fracFol_int) * species.FracFol;
                //exponential version
                //adjFracFol = (float)Math.Pow((lastSeasonAvgFRad + 0.2), fracFol_slope) * species.FracFol + species.FracFol * fracFol_int;
                //modified exponential version - controls lower and upper limit of function
                adjFracFol = species.FracFol + ((fracFol_int - species.FracFol) * (float)Math.Pow(lastSeasonAvgFRad, fracFol_slope)); //slope is shape parm; fracFol is minFracFol; int is maxFracFol. EJG-7-24-18

                firstYear = false;
            }
            else
                adjFracFol = species.FracFol;
            lastSeasonFRad = new List<float>();

        }

        public void NullSubLayers()
        {
            // Reset values for subcanopy layers   
            LAI = null;
            GrossPsn = null;
            FolResp = null;
            NetPsn = null;
            Transpiration = null;
            FRad = null;
            FWater = null;
            PressHead = null;
            Water = null;
            FOzone = null;
            MaintenanceRespiration = null;
            Interception = null;
            AdjFolN = null;
            AdjFracFol = null;
            CiModifier = null;
            DelAmax = null;
        }

        // Age (years)
        public ushort Age
        {
            get
            {
                return age;
            }
        }
        // Non soluble carbons
        public float NSC
        {
            get
            {
                return nsc;
            }
        }
        // Foliage (g/m2)
        public float Fol
        {
            get
            {
                return fol;
            }
            private set
            {
                fol = value;
            }
        }
        // Aboveground Biomass (g/m2)
        public int Biomass
        {
            get
            {
                return (int)((1 - species.FracBelowG) * biomass) + (int)fol;
            }
        }
        // Total Biomass (root + wood) (g/m2)
        public int TotalBiomass
        {
            get
            {
                return (int)biomass;
            }
        }
        // Wood (g/m2)
        public uint Wood
        {
            get
            {
                return (uint)((1 - species.FracBelowG) * biomass);
            }
        }
        // Root (g/m2)
        public uint Root
        {
            get
            {
                return (uint)(species.FracBelowG * biomass);
            }
        }

        // Max biomass achived in the cohorts' life time. 
        // This value remains high after the cohort has reached its 
        // peak biomass. It is used to determine canopy layers where
        // it prevents that a cohort could descent in the canopy when 
        // it declines (g/m2)
        public float BiomassMax
        {
            get
            {
                return biomassmax;
            }
        }
        /// <summary>
        /// Boolean whether cohort has been killed by cold temp relative to cold tolerance
        /// </summary>
        public int ColdKill
        {
            get
            {
                return coldKill;
            }
        }
        // Get totals for combined cohorts
        public void Accumulate(Cohort c)
        {
            biomass += c.biomass;
            biomassmax = Math.Max(biomassmax, biomass);
            fol += c.Fol;
        }

        // Add dead wood to last senescence
        public void AccumulateWoodySenescence(int senescence)
        {
            lastWoodySenescence += senescence;
        }

        // Add dead foliage to last senescence
        public void AccumulateFoliageSenescence(int senescence)
        {
            lastFoliageSenescence += senescence;
        }

        // Growth reduction factor for age
        float Fage
        {
            get
            {
                return Math.Max(0, 1 - (float)Math.Pow((age / (float)species.Longevity), species.PsnAgeRed));
            }
        }
        // NSC fraction: measure for resources
        public float NSCfrac
        {
            get
            {
                return nsc / (FActiveBiom * (biomass + fol));
            }
        }
        // Species with PnET parameter additions
        public ISpeciesPNET SpeciesPNET
        {
            get
            {
                return species;
            }
        }
        // LANDIS species (without PnET parameter additions)
        public Landis.Core.ISpecies Species
        {
            get
            {
                return PlugIn.SpeciesPnET[species];
            }
        }
        // Defoliation proportion - BRM
        public float DefolProp
        {
            get
            {
                return defolProp;
            }
        }

        // Annual Woody Senescence (g/m2)
        public int LastWoodySenescence
        {
            get
            {
                return (int)lastWoodySenescence;
            }
        }
        // Annual Foliage Senescence (g/m2)
        public int LastFoliageSenescence
        {
            get
            {
                return (int)lastFoliageSenescence;
            }
        }

        // Last average FRad
        public float LastFRad
        {
            get
            {
                return lastFRad;
            }
        }

        // Constructor
        public Cohort(ISpeciesPNET species, ushort year_of_birth, string SiteName) // : base(species, 0, (int)(1F / species.DNSC * (ushort)species.InitialNSC))
        {
            this.species = species;
            age = 1;
            coldKill = int.MaxValue;

            this.nsc = (ushort)species.InitialNSC;

            // Initialize biomass assuming fixed concentration of NSC
            this.biomass = (uint)(1F / species.DNSC * (ushort)species.InitialNSC);

            biomassmax = biomass;

            Initialize_CN_Cohort(); // ZHOU

            // Then overwrite them if you need stuff for outputs
            if (SiteName != null)
            {
                InitializeOutput(SiteName, year_of_birth);
            }

            lastSeasonFRad = new List<float>();
            firstYear = true;
        }
        public Cohort(Cohort cohort) // : base(cohort.species, new Landis.Library.BiomassCohorts.CohortData(cohort.age, cohort.Biomass))
        {
            this.species = cohort.species;
            this.age = cohort.age;
            this.nsc = cohort.nsc;
            this.biomass = cohort.biomass;
            biomassmax = cohort.biomassmax;
            this.fol = cohort.fol;
            this.lastSeasonFRad = cohort.lastSeasonFRad;

            Initialize_CN_Cohort(); // ZHOU
        }

        public Cohort(ISpeciesPNET species, ushort age, int woodBiomass, string SiteName, ushort firstYear)
        {
            InitializeSubLayers();
            this.species = species;
            this.age = age;
            //incoming biomass is aboveground wood, calculate total biomass
            int biomass = (int)(woodBiomass / (1 - species.FracBelowG));
            this.biomass = biomass;
            this.nsc = this.species.DNSC * this.FActiveBiom * this.biomass;
            this.biomassmax = biomass;
            this.lastSeasonFRad = new List<float>();
            this.adjFracFol = species.FracFol;
            this.coldKill = int.MaxValue;

            if (this.leaf_on)
            {
                this.fol = (adjFracFol * FActiveBiom * biomass);
                LAI[index] = CalculateLAI(this.species, this.fol, index);
            }

            if (SiteName != null)
            {
                InitializeOutput(SiteName, firstYear);
            }

            Initialize_CN_Cohort(); // ZHOU
        }

        // Makes sure that litters are allocated to the appropriate site
        public static void SetSiteAccessFunctions(SiteCohorts sitecohorts)
        {
            Cohort.addlitter = sitecohorts.AddLitter;
            Cohort.addwoodydebris = sitecohorts.AddWoodyDebris;
            Cohort.ecoregion = sitecohorts.Ecoregion;
            Cohort.site = sitecohorts.Site;
        }


        public void CalculateDefoliation(ActiveSite site, int SiteAboveGroundBiomass)
        {
            int abovegroundBiomass = (int)((1 - species.FracBelowG) * biomass) + (int)fol;
            //defolProp = (float)Landis.Library.Biomass.CohortDefoliation.Compute(site, species, abovegroundBiomass, SiteAboveGroundBiomass);
            defolProp = (float)Landis.Library.BiomassCohorts.CohortDefoliation.Compute(this, site, SiteAboveGroundBiomass);
        }

        // Photosynthesis by canopy layer
        public bool CalculatePhotosynthesis(float PrecInByCanopyLayer, int precipCount, float leakageFrac, IHydrology hydrology, ref float SubCanopyPar, float o3_cum, float o3_month, int subCanopyIndex, int layerCount, ref float O3Effect, float frostFreeSoilDepth, float MeltInByCanopyLayer, bool coldKillBoolean)
        {
            bool success = true;
            float lastO3Effect = O3Effect;
            O3Effect = 0;

            // permafrost
            float frostFreeProp = Math.Min(1.0F, frostFreeSoilDepth / ecoregion.RootingDepth);

            // Leaf area index for the subcanopy layer by index. Function of specific leaf weight SLWMAX and the depth of the canopy
            // Depth of the canopy is expressed by the mass of foliage above this subcanopy layer (i.e. slwdel * index/imax *fol)
            LAI[index] = CalculateLAI(species, fol, index);

            // Precipitation interception has a max in the upper canopy and decreases exponentially through the canopy
            //Interception[index] = PrecInByCanopyLayer * (float)(1 - Math.Exp(-1 * ecoregion.PrecIntConst * LAI[index]));
            //if (Interception[index] > PrecInByCanopyLayer) throw new System.Exception("Error adding water, PrecInByCanopyLayer = " + PrecInByCanopyLayer + " Interception[index] = " + Interception[index]);

            if (MeltInByCanopyLayer > 0)
            {
                // Add thawed soil water to soil moisture
                // Instantaneous runoff (excess of porosity)
                float meltrunoff = Math.Min(MeltInByCanopyLayer, Math.Max(hydrology.Water + MeltInByCanopyLayer - (ecoregion.Porosity * frostFreeProp), 0));
                Hydrology.RunOff += meltrunoff * ecoregion.RunoffFrac;

                success = hydrology.AddWater(MeltInByCanopyLayer - (meltrunoff * ecoregion.RunoffFrac));
                if (success == false) throw new System.Exception("Error adding water, MeltInByCanopyLayer = " + MeltInByCanopyLayer + "; water = " + hydrology.Water + "; meltrunoff = " + meltrunoff + "; ecoregion = " + ecoregion.Name + "; site = " + site.Location);
            }
            float precipIn = 0;
            // If more than one precip event assigned to layer, repeat precip, runoff, leakage for all events prior to respiration
            for (int p = 1; p <= precipCount; p++)
            {
                // Incoming precipitation
                //float waterIn = PrecInByCanopyLayer  - Interception[index]; //mm   
                precipIn = PrecInByCanopyLayer; //mm 

                // Instantaneous runoff (excess of porosity)
                float rainrunoff = Math.Min(precipIn, Math.Max(hydrology.Water + precipIn - (ecoregion.Porosity * frostFreeProp), 0));
                Hydrology.RunOff += rainrunoff * ecoregion.RunoffFrac;

                float waterIn = precipIn - (rainrunoff * ecoregion.RunoffFrac);

                // Add incoming precipitation to soil moisture
                success = hydrology.AddWater(waterIn);
                if (success == false) throw new System.Exception("Error adding water, waterIn = " + waterIn + "; water = " + hydrology.Water + "; rainrunoff = " + rainrunoff + "; ecoregion = " + ecoregion.Name + "; site = " + site.Location);
            }

            // Leakage only occurs following precipitation events or incoming melt water
            if (precipIn > 0 || MeltInByCanopyLayer > 0)
            {
                float leakage = Math.Max((float)leakageFrac * (hydrology.Water - (ecoregion.FieldCap * frostFreeProp)), 0);
                Hydrology.Leakage += leakage;

                // Remove fast leakage
                success = hydrology.AddWater(-1 * leakage);
                if (success == false) throw new System.Exception("Error adding water, Hydrology.Leakage = " + Hydrology.Leakage + "; water = " + hydrology.Water + "; ecoregion = " + ecoregion.Name + "; site = " + site.Location);
            }

            // Adjust soil water for freezing
            if (frostFreeProp < 1.0)
            {
                // water in frozen soil is not accessible - treat it as if it leaked out
                float frozenLimit = ecoregion.FieldCap * frostFreeProp;
                float frozenWater = hydrology.Water - frozenLimit;
                // Remove frozen water
                success = hydrology.AddWater(-1 * frozenWater);
                if (success == false) throw new System.Exception("Error adding water, frozenWater = " + frozenWater + "; water = " + hydrology.Water + "; ecoregion = " + ecoregion.Name + "; site = " + site.Location);
            }



            // Maintenance respiration depends on biomass,  non soluble carbon and temperature
            MaintenanceRespiration[index] = (1 / (float)PlugIn.IMAX) * (float)Math.Min(NSC, ecoregion.Variables[Species.Name].MaintRespFTempResp * biomass);//gC //IMAXinverse

            // Subtract mainenance respiration (gC/mo)
            nsc -= MaintenanceRespiration[index];


            // Woody decomposition: do once per year to reduce unnescessary computation time so with the last subcanopy layer 
            if (index == PlugIn.IMAX - 1)
            {

                // In the last month
                if (ecoregion.Variables.Month == (int)Constants.Months.December)
                {
                    //Check if nscfrac is below threshold to determine if cohort is alive
                    if (!this.IsAlive)
                    {
                        nsc = 0.0F;  // if cohort is dead, nsc goes to zero and becomes functionally dead even though not removed until end of timestep
                    }
                    else if (PlugIn.ModelCore.CurrentTime > 0 && this.TotalBiomass < 1.0)  //Check if biomass < 1.0 -> cohort dies
                    {
                        nsc = 0.0F;  // if cohort is dead, nsc goes to zero and becomes functionally dead even though not removed until end of timestep
                        leaf_on = false;
                        nsc = 0.0F;
                        float foliageSenescence = FoliageSenescence();
                        addlitter(foliageSenescence, SpeciesPNET);
                        lastFoliageSenescence = foliageSenescence;
                    }

                    float woodSenescence = Senescence();
                    addwoodydebris(woodSenescence, species.KWdLit);
                    lastWoodySenescence = woodSenescence;

                    // Release of nsc, will be added to biomass components next year
                    // Assumed that NSC will have a minimum concentration, excess is allocated to biomass
                    float Allocation = Math.Max(nsc - (species.DNSC * FActiveBiom * biomass), 0);
                    //   biomass += Allocation;
                    //   biomassmax = Math.Max(biomassmax, biomass);
                    //   nsc -= Allocation;
                    age++;

                    firstDefol = true;
                    firstAlloc = true;
                }

            }
            

            if (coldKillBoolean)
            {
                coldKill = (int)Math.Floor(ecoregion.Variables.Tave - (3.0 * ecoregion.WinterSTD));
                leaf_on = false;
                nsc = 0.0F;
                float foliageSenescence = FoliageSenescence();
                addlitter(foliageSenescence, SpeciesPNET);
                lastFoliageSenescence = foliageSenescence;
                FolLitM = foliageSenescence;
                CNTrans_FolTrans();//Zhou

            }
            else
            {
                // When LeafOn becomes false for the first time in a year

                if (ecoregion.Variables.Tmin <= this.SpeciesPNET.LeafOnMinT)
                {
                    if (leaf_on == true)
                    {
                        leaf_on = false;
                        float foliageSenescence = FoliageSenescence();
                        addlitter(foliageSenescence, SpeciesPNET);
                        lastFoliageSenescence = foliageSenescence;
                        FolLitM = foliageSenescence;
                        CNTrans_FolTrans();//Zhou

                    }
                }
                else
                {
                    leaf_on = true;

                }
            }
            


            /****************************** MGM's restructuring 10/25/2018 ***************************************/
            if (leaf_on)
            {
                // Foliage linearly increases with active biomass
                //float IdealFol = (species.FracFol * FActiveBiom * biomass);
                if (firstYear)
                    adjFracFol = species.FracFol;
                float IdealFol = (adjFracFol * FActiveBiom * biomass); // Using adjusted FracFol

                if (ecoregion.Variables.Month < (int)Constants.Months.June) //Growing season before defoliation outbreaks
                {
                    if (IdealFol > fol)
                    {
                        // Foliage allocation depends on availability of NSC (allows deficit at this time so no min nsc)
                        // carbon fraction of biomass to convert C to DW
                        float Folalloc = Math.Max(0, Math.Min(nsc, species.CFracBiomass * (IdealFol - fol))); // gC/mo

                        // Add foliage allocation to foliage
                        fol += Folalloc / species.CFracBiomass;// gDW


                        // Subtract from NSC
                        nsc -= Folalloc;

                        FolProdCMo = Folalloc;
                        float FolGRespMo = Folalloc * species.GRespFrac;
                        nsc -= FolGRespMo; //FolResp

                        Allocate_Root(); // Zhou
                    }
                }
                else if (ecoregion.Variables.Month == (int)Constants.Months.June) //Apply defoliation only in June
                {
                    if (firstDefol) // prevents multiple rounds of defoliation within a cohort (which shares canopy variables, like foliage)
                    {
                        ReduceFoliage(defolProp);
                        firstDefol = false;
                    }
                }
                else if (ecoregion.Variables.Month > (int)Constants.Months.June) //During and after defoliation events
                {
                    if (defolProp > 0 && firstAlloc)
                    {
                        if (defolProp > 0.60 && species.TOfol == 1)  // Refoliation at >60% reduction in foliage for deciduous trees - MGM
                        {
                            // Foliage allocation depends on availability of NSC (allows deficit at this time so no min nsc)
                            // carbon fraction of biomass to convert C to DW
                            float Folalloc = Math.Max(0f, Math.Min(nsc, species.CFracBiomass * ((0.70f * IdealFol) - fol)));  // 70% refoliation

                            float Folalloc2 = Math.Max(0f, Math.Min(nsc, species.CFracBiomass * (0.95f * IdealFol - fol)));  // cost of refol is the cost of getting to IdealFol

                            fol += Folalloc / species.CFracBiomass;// gDW

                            // Subtract from NSC
                            nsc -= Folalloc2; // resource intensive to reflush in middle of growing season

                        }
                        else //No attempted refoliation but carbon loss after defoliation
                        {
                            // Foliage allocation depends on availability of NSC (allows deficit at this time so no min nsc)
                            // carbon fraction of biomass to convert C to DW
                            float Folalloc = Math.Max(0f, Math.Min(nsc, species.CFracBiomass * (0.10f * IdealFol))); // gC/mo 10% of IdealFol to take out NSC 

                            // Subtract from NSC do not add Fol
                            nsc -= Folalloc;
                        }
                        firstAlloc = false;  // Denotes that allocation has been applied to one sublayer
                    }
                    else if (IdealFol > fol && firstAlloc)    //Non-defoliated trees refoliate 'normally', but only once per cohort
                    {
                        // Foliage allocation depends on availability of NSC (allows deficit at this time so no min nsc)
                        // carbon fraction of biomass to convert C to DW
                        float Folalloc = Math.Max(0, Math.Min(nsc, species.CFracBiomass * (IdealFol - fol))); // gC/mo

                        // Add foliage allocation to foliage
                        fol += Folalloc / species.CFracBiomass;// gDW

                        // Subtract from NSC
                        nsc -= Folalloc;
                        firstAlloc = false; // Denotes that allocation has been applied to one sublayer
                    }
                }
            }
            /*^^^^^^^^^^^^^^^^^^^^^^^^^^^^ MGM's restructuring 10/25/2018 ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^*/

            // Leaf area index for the subcanopy layer by index. Function of specific leaf weight SLWMAX and the depth of the canopy
            LAI[index] = CalculateLAI(species, fol, index);

            // Adjust HalfSat for CO2 effect
            float halfSatIntercept = species.HalfSat - 350 * species.CO2HalfSatEff;
            adjHalfSat = species.CO2HalfSatEff * ecoregion.Variables.CO2 + halfSatIntercept;
            // Reduction factor for radiation on photosynthesis
            FRad[index] = ComputeFrad(SubCanopyPar, adjHalfSat);



            // Below-canopy PAR if updated after each subcanopy layer
            SubCanopyPar *= (float)Math.Exp(-species.K * LAI[index]);

            // Get pressure head given ecoregion and soil water content (latter in hydrology)
            float PressureHead = hydrology.GetPressureHead(ecoregion);

            // Reduction water for sub or supra optimal soil water content

            float fWaterOzone = 1.0f;  //fWater for ozone functions; ignores H1 and H2 parameters because only impacts when drought-stressed
            if (PlugIn.ModelCore.CurrentTime > 0)
            {
                FWater[index] = ComputeFWater(species.H1, species.H2, species.H3, species.H4, PressureHead);
                Water[index] = hydrology.Water;
                PressHead[index] = PressureHead;
                fWaterOzone = ComputeFWater(-1, -1, species.H3, species.H4, PressureHead); // ignores H1 and H2 parameters because only impacts when drought-stressed
            }
            else // Ignore H1 and H2 parameters during spinup
            {
                FWater[index] = ComputeFWater(-1, -1, species.H3, species.H4, PressureHead);
                Water[index] = hydrology.Water;
                PressHead[index] = PressureHead;
                fWaterOzone = FWater[index];
            }



            // FoliarN adjusted based on canopy position (FRad)
            float folN_shape = species.FolNShape; //Slope for linear FolN relationship
            float maxFolN = species.MaxFolN; //Intercept for linear FolN relationship
            //adjFolN = (FRad[index] * folN_slope + folN_int) * species.FolN; // Linear reduction (with intercept) in FolN with canopy depth (FRad)
            //adjFolN = (float)Math.Pow((FRad[index]), folN_slope) * species.FolN + species.FolN * folN_int; // Expontential reduction
            // Non-Linear reduction in FolN with canopy depth (FRad)

            // species.FolN = 3.0F;
            //adjFolN = species.FolN + ((maxFolN - species.FolN) * (float)Math.Pow(FRad[index], folN_shape)); //slope is shape parm; FolN is minFolN; intcpt is max FolN. EJG-7-24-18
            adjFolN = species.FolN; //slope is shape parm; FolN is minFolN; intcpt is max FolN. EJG-7-24-18// zhou

            AdjFolN[index] = adjFolN;  // Stored for output
            AdjFracFol[index] = adjFracFol; //Stored for output


            float ciModifier = fWaterOzone; // if no ozone, ciModifier defaults to fWater
            if (o3_cum > 0)
            {
                // Regression coefs estimated from New 3 algorithm for Ozone drought.xlsx
                // https://usfs.box.com/s/eksrr4d7fli8kr9r4knfr7byfy9r5z0i
                // Uses data provided by Yasutomo Hoshika and Elena Paoletti
                float ciMod_tol = (float)(fWaterOzone + (-0.021 * fWaterOzone + 0.0087) * o3_cum);
                ciMod_tol = Math.Min(ciMod_tol, 1.0f);
                float ciMod_int = (float)(fWaterOzone + (-0.0148 * fWaterOzone + 0.0062) * o3_cum);
                ciMod_int = Math.Min(ciMod_int, 1.0f);
                float ciMod_sens = (float)(fWaterOzone + (-0.0176 * fWaterOzone + 0.0118) * o3_cum);
                ciMod_sens = Math.Min(ciMod_sens, 1.0f);
                if ((species.O3StomataSens == "Sensitive") || (species.O3StomataSens == "Sens"))
                    ciModifier = ciMod_sens;
                else if ((species.O3StomataSens == "Tolerant") || (species.O3StomataSens == "Tol"))
                    ciModifier = ciMod_tol;
                else if ((species.O3StomataSens == "Intermediate") || (species.O3StomataSens == "Int"))
                    ciModifier = ciMod_int;
                else
                {
                    throw new System.Exception("Ozone data provided, but species O3StomataSens is not set to Sensitive, Tolerant or Intermediate");
                }
            }

            CiModifier[index] = ciModifier;  // Stored for output

            // If trees are physiologically active
            if (leaf_on)
            {
                // CO2 ratio internal to the leaf versus external
                float cicaRatio = (-0.075f * adjFolN) + 0.875f;
                float modCiCaRatio = cicaRatio * ciModifier;
                // Reference co2 ratio
                float ci350 = 350 * modCiCaRatio;
                // Elevated leaf internal co2 concentration
                float ciElev = ecoregion.Variables.CO2 * modCiCaRatio;
                float Ca_Ci = ecoregion.Variables.CO2 - ciElev;

                // Franks method
                // (Franks,2013, New Phytologist, 197:1077-1094)
                float Gamma = 40; // 40; Gamma is the CO2 compensation point (the point at which photorespiration balances exactly with photosynthesis.  Assumed to be 40 based on leaf temp is assumed to be 25 C

                // Modified Gamma based on air temp
                // Tested here but removed for release v3.0
                // Bernacchi et al. 2002. Plant Physiology 130, 1992-1998
                // Gamma* = e^(13.49-24.46/RTk) [R is universal gas constant = 0.008314 kJ/J/mole, Tk is absolute temperature]
                //float Gamma_T = (float) Math.Exp(13.49 - 24.46 / (0.008314 * (ecoregion.Variables.Tday + 273)));

                float Ca0 = 350;  // 350
                float Ca0_adj = Ca0 * cicaRatio;  // Calculated internal concentration given external 350


                // Franks method (Franks,2013, New Phytologist, 197:1077-1094)
                float delamax = (ecoregion.Variables.CO2 - Gamma) / (ecoregion.Variables.CO2 + 2 * Gamma) * (Ca0 + 2 * Gamma) / (Ca0 - Gamma);
                if (delamax < 0)
                {
                    delamax = 0;
                }

                // Franks method (Franks,2013, New Phytologist, 197:1077-1094)
                // Adj Ca0
                float delamax_adj = (ecoregion.Variables.CO2 - Gamma) / (ecoregion.Variables.CO2 + 2 * Gamma) * (Ca0_adj + 2 * Gamma) / (Ca0_adj - Gamma);
                if (delamax_adj < 0)
                {
                    delamax_adj = 0;
                }

                // Modified Franks method - by M. Kubiske
                // substitute ciElev for CO2
                float delamaxCi = (ciElev - Gamma) / (ciElev + 2 * Gamma) * (Ca0 + 2 * Gamma) / (Ca0 - Gamma);
                if (delamaxCi < 0)
                {
                    delamaxCi = 0;
                }

                // Modified Franks method - by M. Kubiske
                // substitute ciElev for CO2
                // adjusted Ca0
                float delamaxCi_adj = (ciElev - Gamma) / (ciElev + 2 * Gamma) * (Ca0_adj + 2 * Gamma) / (Ca0_adj - Gamma);
                if (delamaxCi_adj < 0)
                {
                    delamaxCi_adj = 0;
                }

                // Choose between delamax methods here:
                DelAmax[index] = delamax;  // Franks
                //DelAmax[index] = delamax_adj;  // Franks with adjusted Ca0
                //DelAmax[index] = delamaxCi;  // Modified Franks
                //DelAmax[index] = delamaxCi_adj;  // Modified Franks with adjusted Ca0

                // M. Kubiske method for wue calculation:  Improved methods for calculating WUE and Transpiration in PnET.
                float V = (float)(8314.47 * (ecoregion.Variables.Tmin + 273) / 101.3);
                float JCO2 = (float)(0.139 * ((ecoregion.Variables.CO2 - ciElev) / V) * 0.00001);
                float JH2O = ecoregion.Variables[species.Name].JH2O * ciModifier;
                float wue = (JCO2 / JH2O) * (44 / 18);  //44=mol wt CO2; 18=mol wt H2O; constant =2.44444444444444

                float Amax = (float)(delamaxCi * (species.AmaxA + ecoregion.Variables[species.Name].AmaxB_CO2 * adjFolN));

                //Reference net Psn (lab conditions) in gC/g Fol/month
                float RefNetPsn = ecoregion.Variables.DaySpan * (Amax * ecoregion.Variables[species.Name].DVPD * ecoregion.Variables.Daylength * Constants.MC) / Constants.billion;

                // PSN (gC/g Fol/month) reference net psn in a given temperature
                float FTempPSNRefNetPsn = ecoregion.Variables[species.Name].FTempPSN * RefNetPsn;

                // Compute net psn from stress factors and reference net psn (gC/g Fol/month)
                // FTempPSNRefNetPsn units are gC/g Fol/mo
                float nonOzoneNetPsn = (1 / (float)PlugIn.IMAX) * FWater[index] * FRad[index] * Fage * FTempPSNRefNetPsn * fol;  // gC/m2 ground/mo

                // Convert Psn gC/m2 ground/mo to umolCO2/m2 fol/s
                // netPsn_ground = LayerNestPsn*1000000umol*(1mol/12gC) * (1/(60s*60min*14hr*30day))
                float netPsn_ground = nonOzoneNetPsn * 1000000F * (1F / 12F) * (1F / (ecoregion.Variables.Daylength * ecoregion.Variables.DaySpan));
                float netPsn_leaf_s = 0;
                if (netPsn_ground > 0 && LAI[index] > 0)
                {
                    // nesPsn_leaf_s = NetPsn_ground*(1/LAI){m2 fol/m2 ground}
                    netPsn_leaf_s = netPsn_ground * (1F / LAI[index]);
                    if (float.IsInfinity(netPsn_leaf_s))
                    {
                        netPsn_leaf_s = 0;
                    }
                }

                //Calculate water vapor conductance (gwv) from Psn and Ci; Kubiske Conductance_5.xlsx
                //gwv_mol = NetPsn_leaf_s /(Ca-Ci) {umol/mol} * 1.6(molH20/molCO2)*1000 {mmol/mol}
                float gwv_mol = (float)(netPsn_leaf_s / (Ca_Ci) * 1.6 * 1000);
                //gwv = gwv_mol / (444.5 - 1.3667*Tc)*10    {denominator is from Koerner et al. 1979 (Sheet 3),  Tc = temp in degrees C, * 10 converts from cm to mm.  
                float gwv = (float)(gwv_mol / (444.5 - 1.3667 * ecoregion.Variables.Tave) * 10);

                // Calculate gwv from Psn using Ollinger equation
                // g = -0.3133+0.8126*NetPsn_leaf_s
                //float g = (float) (-0.3133 + 0.8126 * netPsn_leaf_s);

                // Reduction factor for ozone on photosynthesis
                if (o3_month > 0)
                {
                    float o3Coeff = species.O3GrowthSens;
                    O3Effect = ComputeO3Effect_PnET(o3_month, delamaxCi, netPsn_leaf_s, subCanopyIndex, layerCount, fol, lastO3Effect, gwv, LAI[index], o3Coeff);
                }
                else
                { O3Effect = 0; }
                FOzone[index] = 1 - O3Effect;


                //Apply reduction factor for Ozone
                NetPsn[index] = nonOzoneNetPsn * FOzone[index];

                // Net foliage respiration depends on reference psn (AMAX)
                //float FTempRespDayRefResp = ecoregion.Variables[species.Name].FTempRespDay * ecoregion.Variables.DaySpan * ecoregion.Variables.Daylength * Constants.MC / Constants.billion * ecoregion.Variables[species.Name].Amax;
                //Subistitute 24 hours in place of DayLength because foliar respiration does occur at night.  FTempRespDay uses Tave temps reflecting both day and night temperatures.
                float FTempRespDayRefResp = ecoregion.Variables[species.Name].FTempRespDay * ecoregion.Variables.DaySpan * (Constants.SecondsPerHour * 24) * Constants.MC / Constants.billion * Amax;

                // Actal foliage respiration (growth respiration) 
                FolResp[index] = FWater[index] * FTempRespDayRefResp * fol / (float)PlugIn.IMAX;

                // Gross psn depends on net psn and foliage respiration
                GrossPsn[index] = NetPsn[index] + FolResp[index];

                // M. Kubiske equation for transpiration: Improved methods for calculating WUE and Transpiration in PnET.
                // JH2O has been modified by CiModifier to reduce water use efficiency
                Transpiration[index] = (float)(0.01227 * (GrossPsn[index] / (JCO2 / JH2O)));

                // It is possible for transpiration to calculate to exceed available water
                // In this case, we cap transpiration at available water, and back-calculate GrossPsn and NetPsn to downgrade those as well
                if (Transpiration[index] > hydrology.Water)
                {
                    Transpiration[index] = hydrology.Water;
                    GrossPsn[index] = (Transpiration[index] / 0.01227F) * (JCO2 / JH2O);
                    NetPsn[index] = GrossPsn[index] - FolResp[index];
                }

                // Subtract transpiration from hydrology
                if ((ecoregion.Variables.Year > 2001f) && (ecoregion.Variables.Month > 2))
                {
                    int zzhou = 1;
                    zzhou = 2;
                }
                success = hydrology.AddWater(-1 * Transpiration[index]);
                if (success == false)
                {
                    throw new System.Exception("Error adding water, Transpiration = " + Transpiration[index] + " water = " + hydrology.Water + "; ecoregion = " + ecoregion.Name + "; site = " + site.Location + "in Year\\ Month: " + ecoregion.Variables.Year + " \\" + ecoregion.Variables.Month);
                }

                // Add net psn to non soluble carbons

                nsc += NetPsn[index];

            }
            else
            {
                // Reset subcanopy layer values
                NetPsn[index] = 0;
                FolResp[index] = 0;
                GrossPsn[index] = 0;
                Transpiration[index] = 0;
                FOzone[index] = 1;

            }

            if (index < PlugIn.IMAX - 1) index++;
            return success;
        }

        // Based on Michaelis-Menten saturation curve
        // https://en.wikibooks.org/wiki/Structural_Biochemistry/Enzyme/Michaelis_and_Menten_Equation
        public static float ComputeFrad(float Radiation, float HalfSat)
        {
            // Derived from Michaelis-Menton equation
            // https://en.wikibooks.org/wiki/Structural_Biochemistry/Enzyme/Michaelis_and_Menten_Equation

            return Radiation / (Radiation + HalfSat);
        }
        public static float ComputeFWater(float H1, float H2, float H3, float H4, float pressurehead)
        {
            float minThreshold = H1;
            if (H2 <= H1)
                minThreshold = H2;
            // Compute water stress
            if (pressurehead <= minThreshold || pressurehead >= H4) return 0;
            else if (pressurehead > H3) return 1 - ((pressurehead - H3) / (H4 - H3));
            else if (pressurehead < H2) return (1.0F / (H2 - H1)) * pressurehead - (H1 / (H2 - H1));
            else return 1;
        }

        public static float ComputeO3Effect_PnET(float o3, float delAmax, float netPsn_leaf_s, int Layer, int nLayers, float FolMass, float lastO3Effect, float gwv, float layerLAI, float o3Coeff)
        {
            float currentO3Effect = 1.0F;
            float droughtO3Frac = 1.0F; // Not using droughtO3Frac from PnET code per M. Kubiske and A. Chappelka
            //float kO3Eff = 0.0026F;  // Generic coefficient from Ollinger
            float kO3Eff = 0.0026F * o3Coeff;  // Scaled by species using input parameters


            float O3Prof = (float)(0.6163 + (0.00105 * FolMass));
            float RelLayer = (float)Layer / (float)nLayers;
            float relO3 = Math.Min(1, 1 - (RelLayer * O3Prof) * (RelLayer * O3Prof) * (RelLayer * O3Prof));
            // Kubiske method (using gwv in place of conductance
            currentO3Effect = (float)Math.Min(1, (lastO3Effect * droughtO3Frac) + (kO3Eff * gwv * o3 * relO3));

            // Ollinger method
            // Calculations for gsSlope and gsInt could be moved back to EcoregionPnETVariables since they only depend on delamax
            //float gsSlope=(float)((-1.1309*delAmax)+1.9762);
            //float gsInt = (float)((0.4656 * delAmax) - 0.9701);
            //float conductance = Math.Max(0, (gsInt + (gsSlope * netPsn_leaf_s)) * (1 - lastO3Effect));
            //float currentO3Effect_conductance = (float)Math.Min(1, (lastO3Effect * droughtO3Frac) + (kO3Eff * conductance * o3 * relO3));

            // Tested here but removed for release v3.0
            //string OzoneConductance = ((Parameter<string>)PlugIn.GetParameter(Names.OzoneConductance)).Value;
            //if (OzoneConductance == "Kubiske")
            //    return currentO3Effect;
            //else if (OzoneConductance == "Ollinger")
            //    return currentO3Effect_conductance;
            //else
            //{
            //    System.Console.WriteLine("OzoneConductance is not Kubiske or Ollinger.  Using Kubiske by default");
            //    return currentO3Effect;
            //}

            return currentO3Effect;

        }
        public int ComputeNonWoodyBiomass(ActiveSite site)
        {
            return (int)(fol);
        }
        public static Percentage ComputeNonWoodyPercentage(Cohort cohort, ActiveSite site)
        {
            return new Percentage(cohort.fol / (cohort.Wood + cohort.Fol));
        }
        public void InitializeOutput(string SiteName, ushort YearOfBirth)
        {
            cohortoutput = new LocalOutput(SiteName, "Cohort_" + Species.Name + "_" + YearOfBirth + ".csv", OutputHeader);

        }

        public void InitializeOutput(string SiteName)
        {
            cohortoutput = new LocalOutput(SiteName, "Cohort_" + Species.Name + ".csv", OutputHeader);

        }

        public float SumLAI
        {
            get {
                return LAI.Sum();
            }

        }
        public void UpdateCohortData(IEcoregionPnETVariables monthdata)
        {
            float netPsnSum = NetPsn.Sum();
            float transpirationSum = Transpiration.Sum();
            float JCO2_JH2O = 0;
            if (transpirationSum > 0)
                JCO2_JH2O = (float)(0.01227 * (netPsnSum / transpirationSum));
            float WUE = JCO2_JH2O * ((float)44 / (float)18); //44=mol wt CO2; 18=mol wt H2O; constant =2.44444444444444

            // determine the limiting factor 
            float fWaterAvg = FWater.Average();
            float PressHeadAvg = PressHead.Average();
            float fRadAvg = FRad.Average();
            float fOzoneAvg = FOzone.Average();
            float fTemp = monthdata[Species.Name].FTempPSN;
            string limitingFactor = "NA";
            if (coldKill < int.MaxValue)
            {
                limitingFactor = "ColdTol (" + coldKill.ToString() + ")";
            }
            else
            {
                List<float> factorList = new List<float>(new float[] { fWaterAvg, fRadAvg, fOzoneAvg, Fage, fTemp });
                float minFactor = factorList.Min();
                if (minFactor == fTemp)
                    limitingFactor = "fTemp";
                else if (minFactor == Fage)
                    limitingFactor = "fAge";
                else if (minFactor == fWaterAvg)
                {
                    if (PressHeadAvg > this.SpeciesPNET.H3)
                    {
                        limitingFactor = "Too dry";
                    }
                    else if (PressHeadAvg < this.SpeciesPNET.H2)
                    {
                        limitingFactor = "Too wet";
                    }
                    else
                        limitingFactor = "fWater";
                }
                else if (minFactor == fRadAvg)
                    limitingFactor = "fRad";
                else if (minFactor == fOzoneAvg)
                    limitingFactor = "fOzone";
            }

            // Cohort output file
            string s = Math.Round(monthdata.Year, 2) + "," +
                        Age + "," +
                        Layer + "," +
                       SumLAI + "," +
                       GrossPsn.Sum() + "," +
                       FolResp.Sum() + "," +
                       MaintenanceRespiration.Sum() + "," +
                       netPsnSum + "," +                  // Sum over canopy layers
                       transpirationSum + "," +
                       WUE.ToString() + "," +
                       fol + "," +
                       Root + "," +
                       Wood + "," +
                       NSC + "," +
                       NSCfrac + "," +
                       fWaterAvg + "," +
                       Water.Average() + "," +
                       PressHead.Average() + "," +
                       fRadAvg + "," +
                       fOzoneAvg + "," +
                       DelAmax.Average() + "," +
                       monthdata[Species.Name].FTempPSN + "," +
                       monthdata[Species.Name].FTempRespWeightedDayAndNight + "," +
                       Fage + "," +
                       leaf_on + "," +
                       FActiveBiom + "," +
                       AdjFolN.Average() + "," +
                       AdjFracFol.Average() + "," +
                       CiModifier.Average() + "," +
                       adjHalfSat + "," +
                       limitingFactor + "," +
                       MaxNStoreWeighted + "," +
                       PlantN + "," +
                       NRatio + ",";

            cohortoutput.Add(s);


        }

        public string OutputHeader
        {
            get
            {
                // Cohort output file header
                string hdr = OutputHeaders.Time + "," +
                            OutputHeaders.Age + "," +
                            OutputHeaders.Layer + "," +
                            OutputHeaders.LAI + "," +
                            OutputHeaders.GrossPsn + "," +
                            OutputHeaders.FolResp + "," +
                            OutputHeaders.MaintResp + "," +
                            OutputHeaders.NetPsn + "," +
                            OutputHeaders.Transpiration + "," +
                            OutputHeaders.WUE + "," +
                            OutputHeaders.Fol + "," +
                            OutputHeaders.Root + "," +
                            OutputHeaders.Wood + "," +
                            OutputHeaders.NSC + "," +
                            OutputHeaders.NSCfrac + "," +
                            OutputHeaders.fWater + "," +
                            OutputHeaders.water + "," +
                            OutputHeaders.PressureHead + "," +
                            OutputHeaders.fRad + "," +
                            OutputHeaders.FOzone + "," +
                            OutputHeaders.DelAMax + "," +
                            OutputHeaders.fTemp_psn + "," +
                            OutputHeaders.fTemp_resp + "," +
                            OutputHeaders.fage + "," +
                            OutputHeaders.LeafOn + "," +
                            OutputHeaders.FActiveBiom + "," +
                            OutputHeaders.AdjFolN + "," +
                            OutputHeaders.AdjFracFol + "," +
                            OutputHeaders.CiModifier + "," +
                            OutputHeaders.AdjHalfSat + "," +
                            OutputHeaders.LimitingFactor + "," +
                            OutputHeaders.MaxNStoreWeighted + "," +
                            OutputHeaders.PlantN + "," +
                            OutputHeaders.NRatio + ",";

                return hdr;
            }
        }
        public void WriteCohortData()
        {
            cohortoutput.Write();

        }

        public float FActiveBiom
        {
            get
            {
                return (float)Math.Exp(-species.FrActWd * biomass);
            }
        }
        public bool IsAlive
        {
            // Determine if cohort is alive. It is assumed that a cohort is dead when 
            // NSC decline below 1% of biomass
            get
            {
                return NSCfrac > 0.01F;
            }
        }
        public float FoliageSenescence()
        {
            // If it is fall 
            float Litter = species.TOfol * fol;
            // If cohort is dead, then all foliage is lost
            if (NSCfrac <= 0.01F)
                Litter = fol;
            fol -= Litter;

            return Litter;

        }

        public float Senescence()
        {
            float senescence = ((Root * species.TOroot) + Wood * species.TOwood);
            //   biomass -= senescence; //ZZX

            return senescence;
        }

        public void ReduceFoliage(double fraction)
        {
            fol *= (float)(1.0 - fraction);
        }
        public void ReduceBiomass(object sitecohorts, double fraction, ExtensionType disturbanceType)
        {
            Allocation.Allocate(sitecohorts, this, disturbanceType, fraction);

            biomass *= (float)(1.0 - fraction);
            fol *= (float)(1.0 - fraction);

        }

        public float CalculateLAI(ISpeciesPNET species, float fol, int index)
        {
            // Leaf area index for the subcanopy layer by index. Function of specific leaf weight SLWMAX and the depth of the canopy
            // Depth of the canopy is expressed by the mass of foliage above this subcanopy layer (i.e. slwdel * index/imax *fol)
            float LAISum = 0;
            for (int i = 0; i < index; i++)
            {
                LAISum += LAI[i];
            }
            float LAIlayerMax = (float)Math.Max(0.01, 25.0F - LAISum); // Cohort LAI is capped at 25; once LAI reaches 25 subsequent sublayers get LAI of 0.01
            float LAIlayer = (1 / (float)PlugIn.IMAX) * fol / (species.SLWmax - species.SLWDel * index * (1 / (float)PlugIn.IMAX) * fol);
            if (fol > 0 && LAIlayer <= 0)
            {
                PlugIn.ModelCore.UI.WriteLine("\n Warning: LAI was calculated to be negative for " + species.Name + ". This could be caused by a low value for SLWmax.  LAI applied in this case is a max of 25 for each cohort.");
                LAIlayer = LAIlayerMax / (PlugIn.IMAX - index);
            }
            else
                LAIlayer = (float)Math.Min(LAIlayerMax, LAIlayer);

            return LAIlayer;
        }
        //---------------------------------------------------------------------
        /// <summary>
        /// Raises a Cohort.AgeOnlyDeathEvent.
        /// </summary>
        public static void RaiseDeathEvent(object sender,
                                Cohort cohort,
                                ActiveSite site,
                                ExtensionType disturbanceType)
        {
            //if (AgeOnlyDeathEvent != null)
            //{
            //    AgeOnlyDeathEvent(sender, new Landis.Library.BiomassCohorts.DeathEventArgs(cohort, site, disturbanceType));
            //}
            if (DeathEvent != null)
            {
                DeathEvent(sender, new Landis.Library.BiomassCohorts.DeathEventArgs(cohort, site, disturbanceType));
            }

        }

        /// <summary>
        ///    CN functions at cohort level   //ZHOU
        /// </summary>


        public float PlantN
        { get; set; }

        public float NRatio
        { get; set; }

        public float BudN
        { get; set; }

        public float BudC
        { get; set; }

        public float NRatioNit
        { get; set; }

        public float RootNSinkEff
        { get; set; }

        public float RootMass
        { get; set; }


        public float RootMassN
        { get; set; }

        public float WoodMass
        { get
            {
                return biomass;
            }
            set
            { biomass = value; }
        }

        public float WoodMassN
        { get; set; }


        public float DeadWoodM
        { get; set; }

        public float DeadWoodN
        { get; set; }

        public float WoodDecResp
        { get; set; }

        public float FolLitM
        { get; set; }

        public float FolLitN
        { get; set; }
        public float RootC
        { get; set; }

        //public float PlantC
        //{ 
        //    get
        //    {return nsc;} 
        //    set
        //    {nsc =value;} 
        //}

        public float ExcessN
        { get; set; }

  
        public void AllocateYr_Wood()
        {
            float PlantCReserveFrac = 0.75f;
            WoodC = Math.Max(nsc - (species.DNSC * FActiveBiom * biomass), 0);  //Old
            nsc -= WoodC;  // ZZX 
 //           WoodC = (1.0f - PlantCReserveFrac) * nsc;
 //           nsc = nsc - WoodC;


        }

        public void AllocateYr_Bud()  //Annual C/N allocation for the PnET ecosystem model.
        { 

            if (PlantN > MaxNStoreWeighted)
            {
                ExcessN = (PlantN - MaxNStoreWeighted);  //
                PlantN = MaxNStoreWeighted;
            }

           // PlantN = MaxNStoreWeighted / 2.0f;  //// Test zhou



            NRatio = 1.0f + (PlantN / MaxNStoreWeighted) * species.FolNConRange;

            if (NRatio < 1.0)
            {
                NRatio = 1.0f;
            }

            if (NRatio > (1.0 + species.FolNConRange))
            {
                NRatio = 1.0f + species.FolNConRange;
            }


            BudC = (adjFracFol * FActiveBiom * biomass) * species.CFracBiomass; //  Could be replaced with PnET algorithm
            BudN = (BudC / species.CFracBiomass) * species.FLPctN * (1 / (1 - species.FolNRetrans)) * NRatio;
            if (BudN > PlantN)
            {
                if (PlantN < 0.01)
                {
                    BudC = BudC * 0.1f;
                    BudN = BudN * 0.1f;
                }

                else
                {
                    BudC = BudC * (PlantN / BudN);
                    BudN = BudN * (PlantN / BudN);
                }
            }

            CanopyTotN = fol * (species.FolN / 100) + BudN;
            CanopyTotMass = fol + (BudC / species.CFracBiomass);
            //float  folnconnew = (fol * (species.FolN / 100) + BudN) / (fol + (BudC / species.CFracBiomass)) * 100;
            float folnconnew = CanopyTotN / CanopyTotMass * 100;
            species.FolN = folnconnew;
            

            RPctN = species.RLPctN * NRatio; // decimal
            WPctN = species.WLPctN * NRatio; // decimal

            PlantN =   - BudN;
            if (PlantN < 0.0f) PlantN = 0.0f;



        }

        public float CanopyTotN
        { get; set; }
        public float CanopyTotMass
        { get; set; }

        // Root update ability for N relative to the available soil N pools (NH4+NO3), 0-1.
        public void RootNSink()  
        {  
            
            RootNSinkEff = (float)Math.Sqrt(1 - (PlantN / MaxNStoreWeighted));
            float TMult = (float)Math.Exp(0.1f * (ecoregion.Variables.Tave - 7.1f)) * 0.68f;
            RootNSinkStr = (float)(Math.Min((RootNSinkEff * TMult), 0.98));

        }


        public float BiomassWeight
        { get; set; }
        public float NitWeight
        { get; set; }
        public float RootNSinkStr
        { get; set; }

        public float RootNSinkStrWeighted
        { get; set; }

        public float MaxNStoreWeight
        { get; set; }
        public float MaxNStoreWeighted
        { get; set; }
        public float MaxNStore
        { get {return species.MaxNStore; } set {; } }
        public float RootNSinkWeight
        { get; set; }
        public float PlantNUptakeWeight
        { get; set; }

        public float PlantNUptake
        { get; set; }
        
        // nitrification rate relative to the available soil N pools (NH4), 0-1.
        public void NRatioNitEff()  //
        {
            float nr = NRatio - 1 - (species.FolNConRange / 3);
            if (nr < 0) nr = 0;

            NRatioNit = (nr / (0.6667f * species.FolNConRange)) * (nr / (0.6667f * species.FolNConRange));
            if (NRatioNit > 1) NRatioNit = 1;

        }
        public float WoodTransM  // wood litter
        { get; set; }
        public float WoodTransN
        { get; set; }
        public void CNTrans_WoodTurnover()
        {
            //
            //Carbon and nitrogen translocation routine
            //

            float WoodMassLoss, WoodLitM, WoodLitN;
           
            float WoodTurnover = 0.025f;
            float WoodLitLossRate = 0.1f; //    
            float WoodLitCLoss = 0.8f; //  


         //   WoodMass = biomass;

            
            WoodLitM = WoodMass * WoodTurnover / 12.0f; //Matlab to C
            WoodLitN = WoodMassN * WoodTurnover / 12.0f; //Matlab to C
            WoodMass = WoodMass - WoodLitM;
            WoodMassN = WoodMassN - WoodLitN;

         //   biomass = WoodMass;  // IMPROVE


            DeadWoodM = DeadWoodM + WoodLitM;
            DeadWoodN = DeadWoodN + WoodLitN;
            WoodMassLoss = DeadWoodM * WoodLitLossRate / 12.0f; //Matlab to C
            WoodTransM = WoodMassLoss * (1.0f - WoodLitCLoss);  // wood litter
            WoodDecResp = (WoodMassLoss - WoodTransM) * species.CFracBiomass; // loss as CO2

            WoodTransN = (WoodMassLoss / DeadWoodM) * DeadWoodN;
            DeadWoodM = DeadWoodM - WoodMassLoss;
            DeadWoodN = DeadWoodN - WoodTransN;

        }

        public void CNTrans_FolTrans()
        {
            //
            //Carbon and nitrogen translocation routine
            //
                       
            float FolNLoss, Retrans;

            FolNLoss = FolLitM * (species.FolN / 100.0f);
            Retrans = FolNLoss * species.FolNRetrans;
            PlantN = PlantN + Retrans;
            FolLitN = FolNLoss - Retrans;

        }
        public float RootLitM
        { get; set; }
        public float RootLitN
        { get; set; }


        public void CNTrans_RootTurnover()
        {
            //
            //Carbon and nitrogen translocation routine
            //
                      
            float RootTurnover;
            
            float RootTurnoverA = 0.789f;
            float RootTurnoverB = 0.191f;
            float RootTurnoverC = 0.021f;
            float NetNMinLastYr = 10.0f; //    gN/m2
                 
                        
            RootTurnover = RootTurnoverA - (RootTurnoverB * NetNMinLastYr) + (RootTurnoverC * (float)Math.Pow(NetNMinLastYr, 2)); //Matlab to C
            if (RootTurnover > 2.5)
            {
                RootTurnover = 2.5f;
            }
            if (RootTurnover < 0.1)
            {
                RootTurnover = 0.1f;
            }
            RootTurnover = RootTurnover /12.0f; // Yearly to monthly
            
            RootLitM = RootMass * RootTurnover;
            RootLitN = RootLitM * (RootMassN / RootMass);
            RootMass = RootMass - RootLitM;
            RootMassN = RootMassN - RootLitN;


        }


        public float FolProdCMo
        { get; set; }
        public float RPctN
        { get; set; }


        public void Allocate_Root()
        {
            //
            // Root allocation for the PnET ecosystem model.
            //

            float TMult, RootCAdd, RootAllocCMo, RootProdCMo, RootMRespMo, RootGRespMo;

            
            float RootAllocA = 0.0f;
            float RootAllocB = 2.0f;
            float RootMRespFrac = 1.0f;

            TMult = (float)Math.Exp(0.1 * (ecoregion.Variables.Tave - 7.1)) * 0.68f; // annual rate

            RootCAdd = RootAllocA /12.0f + RootAllocB * FolProdCMo;
            RootC = RootC + RootCAdd;
            RootAllocCMo =  TMult /12.0f;   //
            if (RootAllocCMo > 1.0) RootAllocCMo = 1.0f;

            RootAllocCMo = RootAllocCMo * RootC;

            RootC = RootC - RootAllocCMo;
            RootProdCMo = RootAllocCMo / (1.0f + RootMRespFrac + species.GRespFrac);

            RootMRespMo = RootProdCMo * RootMRespFrac;
            RootGRespMo = RootProdCMo * species.GRespFrac;
 

            nsc -= RootCAdd + RootMRespMo + RootGRespMo; //ZZX
            float RootProdMass = RootProdCMo / species.CFracBiomass;
            RootMass = RootMass + RootProdMass;

            float RootProdMassN = RootProdMass * RPctN;
            
            if (PlantN < RootProdMassN)
            {
                RootProdMassN = PlantN;

            }
            RootMassN = RootMassN + RootProdMassN;
            PlantN = PlantN - RootProdMassN;
            ////  share->NetCBal = share->NetPsnMo - WoodMRespMo - WoodGRespMo - share->FolGRespMo - RootMRespMo - RootGRespMo;
            // needs -share->SoilDecResp - share->WoodDecResp, and will be updated in the respective routine.



        }

        public float WPctN
        { get; set; }

        public float WoodC
        { get; set; }

        public void Allocate_Wood()
        {
            //
            // C allocation for the PnET ecosystem model.
            //

            float  WoodProdCMo, WoodGRespMo;

            //float WoodMRespMo, GDDWoodEff, delGDDWoodEff;
            //if (share->GDDTot >= veg->GDDWoodStart)
            //{
            //    GDDWoodEff = (share->GDDTot - veg->GDDWoodStart) / (veg->GDDWoodEnd - veg->GDDWoodStart);
            //    if (GDDWoodEff > 1.0) GDDWoodEff = 1;
            //    if (GDDWoodEff < 0) GDDWoodEff = 0;

            //    delGDDWoodEff = GDDWoodEff - share->OldGDDWoodEff;
            //    WoodProdCMo = share->WoodC * delGDDWoodEff;
            //    WoodGRespMo = WoodProdCMo * veg->GRespFrac;
            //    share->WoodProdCYr = share->WoodProdCYr + WoodProdCMo;
            //    share->WoodGRespYr = share->WoodGRespYr + WoodGRespMo;
            //    share->OldGDDWoodEff = GDDWoodEff;
            //}
            //else
            //{
            //    WoodProdCMo = 0;
            //    WoodGRespMo = 0;
            //}

            WoodProdCMo = WoodC ;
            WoodGRespMo = WoodProdCMo * species.GRespFrac;

            nsc -=  WoodGRespMo;   // ZZX
            float WoodProdMass = WoodProdCMo / species.CFracBiomass;
            WoodMass = WoodMass + WoodProdMass;

            float WoodProdMassN = WoodProdMass * WPctN;


            if (PlantN < WoodProdMassN)
            {
                WoodProdMassN = PlantN;

            }
            WoodMassN = WoodMassN + WoodProdMassN;
            PlantN = PlantN - WoodProdMassN;

            biomassmax = Math.Max(biomassmax, biomass); // ZZHOU
        }


        public void Allocate_CN()  // monthly
        {

            if (ecoregion.Variables.Month == (int)Constants.Months.June) Allocate_Wood();// only occur once in June

            

        }


        public void CNTrans()  // monthly
        {
            

           // CNTrans_FolTrans();
            CNTrans_RootTurnover();
            CNTrans_WoodTurnover();  // biomass and WoodMass connected

        }

        public void AllocateYr() // call at the end of year to estimate foliage and wood growth and N processes
        {
            if (ecoregion.Variables.Month == (int)Constants.Months.December)
            {

                // calculate wood growth for next year. one-year off.
                AllocateYr_Wood();

                // calculate leaf growth and N stress
                AllocateYr_Bud();

                // calculate Root N update potential
                RootNSink();

                // calculate soil Nitrification potential
                NRatioNitEff();
            }

         

        }

        public void Initialize_CN_Cohort()
        {
            RootMass = 10.0f;
            RootMassN = RootMass * species.RLPctN;
            WoodC = 0.01f * WoodMass;
            RootC = 0.005f * WoodMass;
            WoodMassN = species.WLPctN * WoodMass;

            DeadWoodM = 0.4f * WoodMass;
            DeadWoodN = species.WLPctN * DeadWoodM;
            BudC = 0.005f * WoodMass;
            BudN = BudC * 0.04f;// 6.0f;//BudC *species.FolN *2.0f;  //
            PlantN = BudN * 1.6f;//10.0f; // BudN *1.6f; 
            MaxNStoreWeighted = PlantN * 2.0f;//20f;
            RPctN = species.RLPctN * 1.3f; // decimal
            WPctN = species.WLPctN * 1.3f; // decimal
            RootNSinkStr = (float)Math.Sqrt(0.5f)*0.68f;
            // nsc = 1000f;
            FolLitM = 0.0f;  
            FolLitN = 0.0f;


        }

        public void Initialize_CN_Cohort_monthly()
        {
            FolLitM = 0.0f;
            FolLitN = 0.0f;

        }

    }
}
