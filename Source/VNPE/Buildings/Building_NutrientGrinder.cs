using System.Collections.Generic;
using System.Linq;
using System.Text;
using PipeSystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace VNPE
{
    public class Building_NutrientGrinder : Building
    {
        public CompPowerTrader powerComp;
        public CompResource resourceComp;

        private const int produceTicksNeeded = 400;

        private List<Thing> cachedHoppers;
        private Effecter effecter;
        private int nextTick = -1;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextTick, "nextTick");
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            yield return BuildCopyCommandUtility.FindAllowedDesignator(ThingDefOf.Hopper);
        }

        public override string GetInspectString()
        {
            var builder = new StringBuilder();
            builder.AppendLine(base.GetInspectString());

            if (!this.IsSociallyProper(null, false))
                builder.AppendLine((string)"InPrisonCell".Translate());

            if (Prefs.DevMode && !cachedHoppers.NullOrEmpty())
            {
                builder.AppendLine($"{cachedHoppers.Count} connected hopper(s)\n");
            }

            return builder.ToString().Trim();
        }

        public void RegisterHopper(Thing hopper)
        {
            if (cachedHoppers == null)
                cachedHoppers = new List<Thing>();

            if (!cachedHoppers.Contains(hopper))
                cachedHoppers.Add(hopper);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            resourceComp = GetComp<CompResource>();

            var adjCells = GenAdj.CellsAdjacentCardinal(this).ToList();
            for (int i = 0; i < adjCells.Count; i++)
            {
                if (adjCells[i].GetFirstBuilding(map) is Building h && h.TryGetComp<CompRegisterToGrinder>() != null) RegisterHopper(h);
            }

            if (!respawningAfterLoad)
                nextTick = Find.TickManager.TicksGame + produceTicksNeeded;
        }

        protected override void Tick()
        {
            var tick = Find.TickManager.TicksGame;
            if (tick >= nextTick)
            {
                nextTick = tick + produceTicksNeeded;
                if (!powerComp.PowerOn || cachedHoppers.NullOrEmpty())
                    return;

                if (TryProducePaste() && effecter == null)
                {
                    effecter = VThingDefOf.EatVegetarian.Spawn();
                    effecter.Trigger(this, new TargetInfo(Position, Map));
                }
            }
            else if (tick >= nextTick - 150 && effecter != null)
            {
                effecter?.Cleanup();
                effecter = null;
            }

            effecter?.EffectTick(this, new TargetInfo(Position, Map));
        }

        public void UnregisterHopper(Thing hopper)
        {
            if (cachedHoppers.Contains(hopper))
                cachedHoppers.Remove(hopper);
        }

        private struct ThingWithCount
        {
            public Thing thing;
            public int count;
        }

        // This returns the amount of nutrition still required to be satisfied.
        private float FindFeedToSatisfyNutrition(float neededNutrition, List<ThingWithCount> feed)
        {
            var thingGrid = Map.thingGrid;

            foreach (var hopper in cachedHoppers)
            {
                foreach (var cell in GenAdj.CellsOccupiedBy(hopper))
                {
                    foreach (var thing in thingGrid.ThingsListAtFast(cell))
                    {
                        if (!Building_NutrientPasteDispenser.IsAcceptableFeedstock(thing.def))
                            continue;

                        var nutritionOfThing = thing.GetStatValue(StatDefOf.Nutrition);
                        var count = Mathf.Min(thing.stackCount, Mathf.Ceil(neededNutrition / nutritionOfThing));
                        var nutritionOfStack = nutritionOfThing * count;

                        feed.Add(new ThingWithCount{thing = thing, count = (int) count});

                        neededNutrition -= nutritionOfStack;

                        if (neededNutrition <= 0f)
                        {
                            return neededNutrition;
                        }
                    }
                }
            }

            return neededNutrition;
        }

        private bool TryProducePaste()
        {
            var net = resourceComp.PipeNet;

            if (net == null || net.AvailableCapacity < 1)
                return false;

            var feedStacks = new List<ThingWithCount>();

            if (FindFeedToSatisfyNutrition(def.building.nutritionCostPerDispense - 0.00001f, feedStacks) > 0f)
                return false;

            var comps = new List<CompRegisterIngredients>();
            for (int i = 0; i < net.storages.Count; i++)
            {
                var storage = net.storages[i];
                if (storage.parent.GetComp<CompRegisterIngredients>() is CompRegisterIngredients comp)
                    comps.Add(comp);
            }
            var compsCount = comps.Count;

            foreach (var feed in feedStacks)
            {
                for (int i = 0; i < compsCount; i++)
                    comps[i].RegisterIngredient(feed.thing.def);

                feed.thing.SplitOff(feed.count);
            }

            net.DistributeAmongStorage(1);
            return true;
        }
    }
}
