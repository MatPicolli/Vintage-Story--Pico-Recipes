using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace PicoRecipes
{
    /// <summary>
    /// Everything we found out about how one particular itemstack is created (or used).
    /// </summary>
    public class RecipeSections
    {
        /// <summary>Grid crafting recipes, grouped by RecipeGroup so variants can be shown as a slideshow.</summary>
        public List<GridRecipe[]> GridGroups = new List<GridRecipe[]>();

        public List<RecipeBase> Smithing = new List<RecipeBase>();
        public List<RecipeBase> ClayForming = new List<RecipeBase>();
        public List<RecipeBase> Knapping = new List<RecipeBase>();
        public List<RecipeBase> Barrel = new List<RecipeBase>();

        /// <summary>[input, output] pairs. Ratio = how many inputs per output (smelting only).</summary>
        public List<StackProcess> Smelting = new List<StackProcess>();
        public List<StackProcess> Grinding = new List<StackProcess>();
        public List<StackProcess> Crushing = new List<StackProcess>();

        public bool IsEmpty =>
            GridGroups.Count == 0 && Smithing.Count == 0 && ClayForming.Count == 0 && Knapping.Count == 0
            && Barrel.Count == 0 && Smelting.Count == 0 && Grinding.Count == 0 && Crushing.Count == 0;
    }

    public class StackProcess
    {
        public ItemStack Input;
        public ItemStack Output;
        public int Ratio = 1;
    }

    /// <summary>
    /// Collects all recipes known to the client into a form the browser dialog can render.
    /// Grid recipes come straight from the API. Smithing/knapping/clay forming/barrel recipes
    /// are read from the survival mod's RecipeRegistrySystem via reflection (so we do not need
    /// a compile time dependency on VSSurvivalMod.dll), but each recipe object derives from the
    /// engine's RecipeBase, which is all we need to display ingredients and outputs.
    /// </summary>
    public class RecipeIndex
    {
        readonly ICoreClientAPI capi;

        public List<ItemStack> AllStacks { get; private set; } = new List<ItemStack>();
        /// <summary>Lowercased searchable text (item code) parallel to AllStacks.</summary>
        public List<string> SearchTexts { get; private set; } = new List<string>();

        List<RecipeBase> smithing = new List<RecipeBase>();
        List<RecipeBase> knapping = new List<RecipeBase>();
        List<RecipeBase> clayforming = new List<RecipeBase>();
        List<RecipeBase> barrel = new List<RecipeBase>();

        volatile bool ready;
        bool started;

        /// <summary>True once the item index has finished building on the background thread.</summary>
        public bool Ready => ready;

        public RecipeIndex(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        /// <summary>
        /// Kicks off the index build on a background thread (the way the vanilla handbook does).
        /// Building it on the main thread froze the client for minutes on a full modpack.
        /// </summary>
        public void EnsureLoaded()
        {
            if (started) return;
            started = true;

            TyronThreadPool.QueueTask(BuildInBackground, "picorecipes-index");
        }

        void BuildInBackground()
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                var stacks = BuildStackList();
                LoadSurvivalRecipes();

                // Publish the finished lists, then flag ready (volatile write orders it after the
                // list assignments, so main-thread readers that see Ready==true see full lists).
                AllStacks = stacks.stacks;
                SearchTexts = stacks.texts;
                ready = true;

                capi.Logger.Notification("[picorecipes] Indexed {0} item stacks in {1} ms", AllStacks.Count, sw.ElapsedMilliseconds);
            }
            catch (Exception e)
            {
                capi.Logger.Error("[picorecipes] Failed to build item index: {0}", e);
                ready = true; // don't get stuck on "Loading..." forever
            }
        }

        (List<ItemStack> stacks, List<string> texts) BuildStackList()
        {
            var stacks = new List<ItemStack>();
            var texts = new List<string>();

            foreach (CollectibleObject obj in capi.World.Collectibles)
            {
                if (obj?.Code == null) continue;

                List<ItemStack> objStacks;
                try
                {
                    objStacks = obj.GetHandBookStacks(capi);
                }
                catch (Exception)
                {
                    continue;
                }
                if (objStacks == null) continue;

                foreach (ItemStack stack in objStacks)
                {
                    if (stack?.Collectible == null) continue;
                    stacks.Add(stack);

                    // Search on the item code only. Resolving localized names (GetName ->
                    // Lang.GetMatching) for every one of ~30k stacks is what made the index build
                    // slow; codes contain the searchable words and match is word-based.
                    texts.Add(stack.Collectible.Code.ToString().ToLowerInvariant());
                }
            }

            return (stacks, texts);
        }

        void LoadSurvivalRecipes()
        {
            ModSystem registry = null;
            try
            {
                registry = capi.ModLoader.GetModSystem("Vintagestory.GameContent.RecipeRegistrySystem");
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[picorecipes] Could not find RecipeRegistrySystem: {0}", e.Message);
            }

            smithing = GrabRecipeList(registry, "SmithingRecipes");
            knapping = GrabRecipeList(registry, "KnappingRecipes");
            clayforming = GrabRecipeList(registry, "ClayFormingRecipes");
            barrel = GrabRecipeList(registry, "BarrelRecipes");
        }

        List<RecipeBase> GrabRecipeList(object owner, string fieldName)
        {
            try
            {
                var field = owner?.GetType().GetField(fieldName);
                if (field?.GetValue(owner) is IEnumerable list)
                {
                    return list.OfType<RecipeBase>().Where(r => r.Enabled).ToList();
                }
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[picorecipes] Could not read {0}: {1}", fieldName, e.Message);
            }
            return new List<RecipeBase>();
        }

        /// <summary>All recipes that produce the given stack ("how do I make this?").</summary>
        /// <param name="includeProcesses">
        /// When true, scans every known stack to find smelting/grinding/crushing that yields this
        /// stack (O(all items), used for the full browser). Set false for the hover preview.
        /// </param>
        public RecipeSections GetCreatedBy(ItemStack stack, bool includeProcesses = true)
        {
            EnsureLoaded();
            var result = new RecipeSections();
            if (stack == null) return result;

            // Grid crafting: same matching the vanilla handbook uses
            var grouped = new Dictionary<int, List<GridRecipe>>();
            var groupOrder = new List<int>();
            foreach (GridRecipe recipe in capi.World.GridRecipes)
            {
                if (recipe == null || !recipe.Enabled || !recipe.ShowInCreatedBy) continue;

                bool matches = false;
                try
                {
                    matches = recipe.Output?.ResolvedItemStack?.Satisfies(stack) == true;

                    if (!matches && recipe.ResolvedIngredients != null)
                    {
                        foreach (var ingred in recipe.ResolvedIngredients)
                        {
                            if (ingred?.ReturnedStack?.ResolvedItemstack is not ItemStack rstack) continue;
                            if (rstack.Satisfies(stack) && ingred.ResolvedItemStack?.Satisfies(stack) == false)
                            {
                                matches = true;
                                break;
                            }
                        }
                    }
                }
                catch (Exception) { /* Skip recipes from other mods that misbehave */ }

                if (!matches) continue;

                if (!grouped.TryGetValue(recipe.RecipeGroup, out var list))
                {
                    grouped[recipe.RecipeGroup] = list = new List<GridRecipe>();
                    groupOrder.Add(recipe.RecipeGroup);
                }
                list.Add(recipe);
            }
            foreach (int group in groupOrder) result.GridGroups.Add(grouped[group].ToArray());

            // Voxel and barrel recipes: match by output
            result.Smithing = FilterByOutput(smithing, stack);
            result.Knapping = FilterByOutput(knapping, stack);
            result.ClayForming = FilterByOutput(clayforming, stack);
            result.Barrel = FilterByOutput(barrel, stack);

            // Processes producing this stack: scan all known stacks once
            if (includeProcesses)
            {
                foreach (ItemStack input in AllStacks)
                {
                    CollectProcesses(input, onlyOutputMatching: stack, result);
                }
            }

            return result;
        }

        /// <summary>All recipes that consume the given stack ("what can I do with this?").</summary>
        public RecipeSections GetUsedIn(ItemStack stack)
        {
            EnsureLoaded();
            var result = new RecipeSections();
            if (stack == null) return result;

            // For usages we group recipes by their output, so that e.g. all wood-type variants
            // of a ladder cycle in one grid instead of flooding the page.
            var grouped = new Dictionary<string, List<GridRecipe>>();
            var groupOrder = new List<string>();
            foreach (GridRecipe recipe in capi.World.GridRecipes)
            {
                if (recipe == null || !recipe.Enabled) continue;

                bool matches = false;
                try
                {
                    if (recipe.ResolvedIngredients != null)
                    {
                        foreach (var ingred in recipe.ResolvedIngredients)
                        {
                            if (ingred?.SatisfiesAsIngredient(stack, false) == true)
                            {
                                matches = true;
                                break;
                            }
                        }
                    }
                }
                catch (Exception) { }

                if (!matches) continue;

                string groupKey = (recipe.Output?.ResolvedItemStack?.Collectible?.Code?.ToString() ?? recipe.Name?.ToString() ?? "?")
                    + "-" + recipe.RecipeGroup;
                if (!grouped.TryGetValue(groupKey, out var list))
                {
                    grouped[groupKey] = list = new List<GridRecipe>();
                    groupOrder.Add(groupKey);
                }
                list.Add(recipe);
            }
            foreach (string group in groupOrder) result.GridGroups.Add(grouped[group].ToArray());

            result.Smithing = FilterByIngredient(smithing, stack);
            result.Knapping = FilterByIngredient(knapping, stack);
            result.ClayForming = FilterByIngredient(clayforming, stack);
            result.Barrel = FilterByIngredient(barrel, stack);

            // Processes consuming this stack directly
            CollectProcesses(stack, onlyOutputMatching: null, result);

            return result;
        }

        List<RecipeBase> FilterByOutput(List<RecipeBase> recipes, ItemStack stack)
        {
            var found = new List<RecipeBase>();
            foreach (RecipeBase r in recipes)
            {
                try
                {
                    ItemStack outstack = r.RecipeOutput?.ResolvedItemStack;
                    if (outstack != null && outstack.Equals(capi.World, stack, GlobalConstants.IgnoredStackAttributes))
                    {
                        found.Add(r);
                    }
                }
                catch (Exception) { }
            }
            return found;
        }

        List<RecipeBase> FilterByIngredient(List<RecipeBase> recipes, ItemStack stack)
        {
            var found = new List<RecipeBase>();
            foreach (RecipeBase r in recipes)
            {
                try
                {
                    if (r.RecipeIngredients?.Any(ingred => ingred?.SatisfiesAsIngredient(stack, false) == true) == true)
                    {
                        found.Add(r);
                    }
                }
                catch (Exception) { }
            }
            return found;
        }

        /// <summary>
        /// Checks smelting/grinding/crushing properties of <paramref name="input"/>. If
        /// <paramref name="onlyOutputMatching"/> is set, only records processes whose output matches it.
        /// </summary>
        void CollectProcesses(ItemStack input, ItemStack onlyOutputMatching, RecipeSections result)
        {
            if (input?.Collectible == null) return;

            try
            {
                var combProps = input.Collectible.GetCombustibleProperties(capi.World, input, null);
                ItemStack smelted = combProps?.SmeltedStack?.ResolvedItemstack;
                if (smelted != null && MatchesFilter(smelted, onlyOutputMatching))
                {
                    result.Smelting.Add(new StackProcess { Input = input, Output = smelted, Ratio = Math.Max(1, combProps.SmeltedRatio) });
                }
            }
            catch (Exception) { }

            try
            {
                var grindProps = input.Collectible.GetGrindingProperties(capi.World, input);
                ItemStack ground = grindProps?.GroundStack?.ResolvedItemstack;
                if (ground != null && MatchesFilter(ground, onlyOutputMatching))
                {
                    result.Grinding.Add(new StackProcess { Input = input, Output = ground });
                }
            }
            catch (Exception) { }

            try
            {
                var crushProps = input.Collectible.GetCrushingProperties(capi.World, input);
                ItemStack crushed = crushProps?.CrushedStack?.ResolvedItemstack;
                if (crushed != null && MatchesFilter(crushed, onlyOutputMatching))
                {
                    result.Crushing.Add(new StackProcess { Input = input, Output = crushed });
                }
            }
            catch (Exception) { }
        }

        bool MatchesFilter(ItemStack output, ItemStack filter)
        {
            if (filter == null) return true;
            return output.Equals(capi.World, filter, GlobalConstants.IgnoredStackAttributes);
        }

        /// <summary>Resolves the display stacks for a recipe ingredient, expanding wildcards against all known stacks.</summary>
        public ItemStack[] ResolveIngredientStacks(IRecipeIngredient ingred, int maxVariants = 60)
        {
            if (ingred == null) return Array.Empty<ItemStack>();

            if (ingred is CraftingRecipeIngredient cingred && cingred.ResolvedItemStack != null && cingred.MatchingType == EnumRecipeMatchType.Exact)
            {
                ItemStack direct = cingred.ResolvedItemStack.Clone();
                direct.StackSize = Math.Max(1, cingred.Quantity);
                return new[] { direct };
            }

            var found = new List<ItemStack>();
            foreach (ItemStack candidate in AllStacks)
            {
                try
                {
                    if (ingred.SatisfiesAsIngredient(candidate, false))
                    {
                        ItemStack copy = candidate.Clone();
                        copy.StackSize = Math.Max(1, ingred.Quantity);
                        found.Add(copy);
                        if (found.Count >= maxVariants) break;
                    }
                }
                catch (Exception) { }
            }
            return found.ToArray();
        }
    }
}
