using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;

namespace RequiemClutterPlacementPatcher
{
    public class Program
    {

        private static string REQUIEM_ESP = "Requiem.esp";

        private static List<string> BASIC_MASTERS = new()
        {
            "Skyrim.esm",
            "Update.esm",
            "Dawnguard.esm",
            "HearthFires.esm",
            "Dragonborn.esm",
            "Unofficial Skyrim Special Edition Patch" // Requiem has this as a master
        };
        
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "RequiemClutterPlacementPatcher.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            // Take all entries from RTFI related to clutter REFs
            // For each of those refs, find the last placement which is different, and ALSO has a plugin
            // that doesn't have Requiem.esp as a master (or isn't Requiem.esp itself)

            // Go through all placed objects...
            var allPlacedObjects = state
                .LoadOrder
                .PriorityOrder
                .PlacedObject()
                .WinningContextOverrides(state.LinkCache);
            
            foreach (var placedObjectGetter in allPlacedObjects)
            {
                var mod = state.LoadOrder.GetIfEnabledAndExists(placedObjectGetter.ModKey);
                processForModContext(placedObjectGetter, mod, state);
            }
            // }
        }

        private static void processForModContext(
            IModContext<ISkyrimMod, ISkyrimModGetter, IPlacedObject, IPlacedObjectGetter> placedObjectGetter,
            ISkyrimModGetter mod,
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            
            if (placedObjectGetter.Record.Placement == null || !IsRequiemRelated(mod))
            {
                return;
            }
            printLog($"Found requiem-related mod: {mod.ModKey.FileName}");
            
            // This placed object has been modified by Requiem or one of its patches.
            // We want to determine the 'best' position records for this object. 
            // Something like JK's interiors will likely modify it before Requiem, but Requiem will
            // introduce some things like lock data etc that we want to keep.
            var linkGetter = placedObjectGetter.Record.ToLinkGetter();
            var objectRecordContexts = linkGetter.ResolveAllContexts<ISkyrimMod, ISkyrimModGetter, IPlacedObject, IPlacedObjectGetter>(state.LinkCache);
            var otherPossibleRecords = objectRecordContexts.Where(context =>
            {
                var skyrimModGetter = state.LoadOrder.GetIfEnabledAndExists(context.ModKey);
                return !IsRequiemRelated(skyrimModGetter) && !IsBasicMaster(skyrimModGetter);
            }).ToList();
            

            if (!otherPossibleRecords.Any())
            {
                return;
            }
            
            printLog("Found other possible records for object " + placedObjectGetter.Record.FormKey);
            foreach (var otherPossibleRecord in otherPossibleRecords)
            {
                printLog("Possible record: " + otherPossibleRecord.Record.FormKey);
            }
            
            var winningScale = placedObjectGetter.Record.Scale;
            var winningPosition = placedObjectGetter.Record.Placement.Position;
            var winningRotation = placedObjectGetter.Record.Placement.Rotation;

            var highestPriorityOtherRecord = otherPossibleRecords.First();
            if (highestPriorityOtherRecord.Record.Placement == null)
            {
                return;
            }
            
            // Also forward scale 
            var hporScale = highestPriorityOtherRecord.Record.Scale;
            var hporPosition = highestPriorityOtherRecord.Record.Placement.Position;
            var hporRotation = highestPriorityOtherRecord.Record.Placement.Rotation;

            // Something ahead of requiem changes the position of this object. Let's keep that positioning.
            // Note: We should also check the position against the LAST object in the overrides if this ends up
            // just pulling in placements from Skyrim.esm or something
            if (hporPosition == winningPosition && hporRotation == winningRotation)
            {
                return;
            }
            
            var newPlacedObject = placedObjectGetter.GetOrAddAsOverride(state.PatchMod);
            newPlacedObject.Placement = new Placement { Position = hporPosition, Rotation = hporRotation };

            // Don't really care about floating point comparison problems here. Usually one mod alters the scale and the other doesn't.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (hporScale != null && (winningScale == null || hporScale.Value != winningScale.Value)) 
            {
                newPlacedObject.Scale = hporScale;
            }
            
            // Check if highest priority override (hpor) disables the entry, and if so add the record flag and forward XESP
            if (newPlacedObject.Placement.Position.Z <= -30000)
            {
                IPlacedExt.Disable(newPlacedObject);
            }
            
            // If it's a BOOK, forward ragdoll data from HPOR
        }

        private static bool IsRequiemRelated(ISkyrimModGetter mod)
        {
            return mod.ModKey.FileName.Equals(REQUIEM_ESP)
                   || mod.MasterReferences.Any(reference => reference.Master.FileName.Equals(REQUIEM_ESP));
        }

        private static bool IsBasicMaster(IModKeyed mod)
        {
            return BASIC_MASTERS.Contains(mod.ModKey.FileName);
        }

        private static void printLog(string logString)
        {
            Console.WriteLine(logString);
        }

        private static void printError(string errorString)
        {
            Console.WriteLine("! ------ERROR------- !");
            Console.WriteLine(errorString);
            Console.WriteLine("! ------ERROR------- !");
        }
    }
}
