﻿using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System.Diagnostics;

namespace SpellModCompatibilityPatcher {
    public class Program {
        private static Lazy<SpellPatchSettings>? Settings;

        public static async Task<int> Main(string[] args) {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings(
                    nickname: "Spell Patch Settings",
                    path: "spellPatchSettings.json",
                    out Settings, true)
                .SetTypicalOpen(GameRelease.SkyrimSE, "SpellModCompatibilityPatches.esp")
                .Run(args);
        }

        private static async Task RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) {
            var vanillaCaches = GetVanillaLinkCaches(state.LoadOrder);
            var vanillaKeys = new HashSet<ModKey>(Settings.Value.BaseMods);
            var spells = state.LoadOrder.PriorityOrder.Spell().WinningContextOverrides(true);
            var books = state.LoadOrder.PriorityOrder.Book().WinningContextOverrides(true);
            var spellOverrides = GetOverrides(vanillaCaches, vanillaKeys, spells);
            var bookOverrides = GetOverrides(vanillaCaches, vanillaKeys, books, 
                (x)=>x is IBookSpellGetter); // filter for only spell tomes            

            Console.WriteLine("BOOK OVERRIDES ====================");
            PrintOverrides(bookOverrides);
            Console.WriteLine("SPELL OVERRIDES ====================");
            PrintOverrides(spellOverrides);

            var updatedBookOverrides = UpdateOverrides<IBook, IBookGetter>(state.LoadOrder, Settings.Value.PreferredOverrideOrder);
            var updatedSpellOverrides = UpdateOverrides<ISpell, ISpellGetter>(state.LoadOrder, Settings.Value.PreferredOverrideOrder);
            updatedBookOverrides = WeedAlreadyWinningOverrides<IBook, IBookGetter>(state.LinkCache, updatedBookOverrides);
            updatedSpellOverrides = WeedAlreadyWinningOverrides<ISpell, ISpellGetter>(state.LinkCache, updatedSpellOverrides);

            var updatedBookOverridesList = updatedBookOverrides.ToList();
            var updatedSpellOverridesList = updatedSpellOverrides.ToList();

            Console.WriteLine("Finished forwarding overrides based on priority");

            Console.WriteLine("BOOK OVERRIDES ====================");
            PrintOverrides(updatedBookOverridesList);
            Console.WriteLine("SPELL OVERRIDES ====================");
            PrintOverrides(updatedSpellOverridesList);

            if (!Debugger.IsAttached)
                Debugger.Launch();

            ApplyOverride<Book, IBookGetter>(state, updatedBookOverridesList);
            ApplyOverride<Spell, ISpellGetter>(state, updatedSpellOverrides);
        }

        private static void ApplyOverride<TMajor, TMajorGetter>(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IEnumerable<Override<TMajorGetter>> overrides) where TMajor : class, IMajorRecord, IMajorRecordQueryable, TMajorGetter where TMajorGetter : class, IMajorRecordGetter, IMajorRecordQueryableGetter {
            var group = state.PatchMod.GetTopLevelGroup<TMajor>();
            foreach(var @override in overrides) {
                var copy = @override.OverridingRecord.DeepCopy() as TMajor;
                group.Add(copy);
            }
        }

        private static string GetName(IMajorRecordGetter majorRecord) {
            if (majorRecord is null)
                return "null record";

            string name = majorRecord.EditorID ?? majorRecord.FormKey.ToString();
            if (majorRecord is INamed named)
                name = $"{name} (a.k.a {named.Name})";
            return name;
        }

        private static void PrintOverrides<T>(List<Override<T>> overrides) where T: IMajorRecordGetter {
            if (overrides.Count != 0)
                Console.WriteLine($"Found {overrides.Count} {typeof(T).Name} overrides");
            foreach (var @override in overrides) {

                Console.WriteLine($"{typeof(T).Name} '{GetName(@override.OriginalRecord)}' from [{@override.OriginalMod.Name}] has been overriden by '{GetName(@override.OverridingRecord)}' from [@{@override.OverridingMod.Name}]");
            }
        }

        private static IEnumerable<Override<TMajorGetter>> UpdateOverrides<TMajor, TMajorGetter>(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder, List<ModKey> perferredOverrideOrder) where TMajor : class, IMajorRecord, IMajorRecordQueryable, TMajorGetter where TMajorGetter : class, IMajorRecordGetter, IMajorRecordQueryableGetter {
            var overridingMods = GetRecordAddingMods<TMajor, TMajorGetter>(loadOrder);
            Dictionary<string, Override<TMajorGetter>> overrides = [];

            foreach(var modKey in perferredOverrideOrder) {
                if (!loadOrder.TryGetValue(modKey, out var listing))
                    continue;
                if (!overridingMods.TryGetValue(listing, out var records))
                    continue;
                foreach(var record in records) {
                    if (record.Record.EditorID is null)
                        continue;
                    if (overrides.ContainsKey(record.Record.EditorID))
                        continue;
                    var @override = new Override<TMajorGetter>(null, modKey, null, record.Record);
                    overrides[record.Record.EditorID] = @override;
                }
            }

            return overrides.Values;
        }

        private static List<Override<TMajorGetter>> WeedAlreadyWinningOverrides<TMajor, TMajorGetter>(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, IEnumerable<Override<TMajorGetter>> overrides) where TMajor : class, IMajorRecord, IMajorRecordQueryable, TMajorGetter where TMajorGetter : class, IMajorRecordGetter, IMajorRecordQueryableGetter {
            List<Override<TMajorGetter>> weededOverrides = [];
            foreach(var @override in overrides) {
                var record = @override.OverridingRecord;
                if (record.EditorID is null)
                    continue;
                if (!linkCache.TryResolve<TMajor>(record.EditorID, out var originalRecord))
                    continue;
                if (record.FormKey == originalRecord.FormKey)
                    continue;
                weededOverrides.Add(new Override<TMajorGetter>(originalRecord.FormKey.ModKey, record.FormKey.ModKey, originalRecord, record));
            }

            return weededOverrides;
        }

        private static Dictionary<IModListing<ISkyrimModGetter>, IEnumerable<IModContext<TMajor>>> GetRecordAddingMods<TMajor, TMajorGetter>(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder) where TMajor : class, IMajorRecord, IMajorRecordQueryable, TMajorGetter where TMajorGetter : class, IMajorRecordGetter, IMajorRecordQueryableGetter {
            Dictionary<IModListing<ISkyrimModGetter>, IEnumerable<IModContext<TMajor>>> dict = [];
            foreach (var mod in loadOrder) {
                if (mod is null || mod.Value is null || mod.Value.Mod is null)
                    continue;
                var records = mod.Value.Mod.EnumerateMajorRecordSimpleContexts<TMajor>();
                if (records is null)
                    continue;
                if (!records.Any())
                    continue;
                dict.Add(mod.Value, records);
            }

            return dict;
        }

        private record Override<T>(ModKey OriginalMod, ModKey OverridingMod, T OriginalRecord, T OverridingRecord) where T: IMajorRecordGetter;

        private static List<Override<TMajorRecordGetter>> GetOverrides<TMajorRecord, TMajorRecordGetter>(
            IEnumerable<ImmutableModLinkCache<ISkyrimMod, ISkyrimModGetter>> originals,
            HashSet<ModKey> originalsKeys,
            IEnumerable<IModContext<ISkyrimMod, ISkyrimModGetter, TMajorRecord, TMajorRecordGetter>> records,
            Predicate<TMajorRecordGetter>? filter = null
            ) where TMajorRecord : class, ISkyrimMajorRecord, TMajorRecordGetter where TMajorRecordGetter : class, ISkyrimMajorRecordGetter {

            List<Override<TMajorRecordGetter>> overrides = [];

            foreach(var contextAndRecord in records) {
                if (originalsKeys.Contains(contextAndRecord.ModKey))
                    continue;
                var record = contextAndRecord.Record;
                if (filter is not null && !filter(record))
                    continue;
                foreach(var originalCaches in originals) {
                    if (originalCaches.TryResolveContext<TMajorRecord, TMajorRecordGetter>(record.EditorID, out var originalRecord)) {
                        overrides.Add(new(originalRecord.ModKey, contextAndRecord.ModKey, originalRecord.Record, record));
                    }
                }
            }

            return overrides;
        }

        private static List<ImmutableModLinkCache<ISkyrimMod, ISkyrimModGetter>> GetVanillaLinkCaches(
                ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder) {
            if (!TryGetLinkCache("Skyrim.esm", loadOrder, out var skyrim))
                throw new Exception();
            List<ImmutableModLinkCache<ISkyrimMod, ISkyrimModGetter>> caches = [skyrim];
            foreach(var baseMod in Settings?.Value.BaseMods ?? []) {
                TryGetLinkCache(baseMod, loadOrder, out var cache);
                caches.Add(cache);
            }

            return caches
                .Where((m) => m is not null)
                .ToList();
        }

        private static bool TryGetLinkCache(string fileName, ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder, out ImmutableModLinkCache<ISkyrimMod, ISkyrimModGetter> linkCache) {
            linkCache = null;
            if (!ModKey.TryFromNameAndExtension(fileName, out var modKey))
                return false;
            return TryGetLinkCache(modKey, loadOrder, out linkCache);
        }

        private static bool TryGetLinkCache(ModKey key, ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder, out ImmutableModLinkCache<ISkyrimMod, ISkyrimModGetter> linkCache) {
            linkCache = null;
            if (!loadOrder.TryGetValue(key, out var mod))
                return false;
            linkCache = mod?.Mod.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();
            return linkCache is not null;
        }
    }
}
