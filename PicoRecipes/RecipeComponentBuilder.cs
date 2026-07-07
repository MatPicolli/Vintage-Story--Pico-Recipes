using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PicoRecipes
{
    /// <summary>
    /// Turns the recipes found for an itemstack into the richtext components used by both the
    /// full recipe browser and the hover popup. Rendering uses the same engine components the
    /// vanilla handbook uses, so wildcard / wood-typed recipes cycle correctly.
    /// </summary>
    public class RecipeComponentBuilder
    {
        readonly ICoreClientAPI capi;
        readonly RecipeIndex index;
        readonly Action<ItemStack> onStackClicked;

        public RecipeComponentBuilder(ICoreClientAPI capi, RecipeIndex index, Action<ItemStack> onStackClicked)
        {
            this.capi = capi;
            this.index = index;
            this.onStackClicked = onStackClicked;
        }

        /// <summary>Full page: item header + every recipe section for created-by or usages.</summary>
        public RichTextComponentBase[] BuildFull(ItemStack stack, bool usages)
        {
            var components = new List<RichTextComponentBase>();

            components.Add(new ItemstackTextComponent(capi, stack, 44, 10, EnumFloat.Left, onStackClicked));
            components.Add(new RichTextComponent(capi, stack.GetName() + "\n", CairoFont.WhiteSmallishText().WithWeight(FontWeight.Bold)));
            components.Add(new RichTextComponent(capi, stack.Collectible.Code?.ToString() + "\n", CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment)));
            components.Add(new ClearFloatTextComponent(capi, 10));

            RecipeSections sections = usages ? index.GetUsedIn(stack) : index.GetCreatedBy(stack);

            if (sections.IsEmpty)
            {
                components.Add(new RichTextComponent(capi,
                    usages ? Loc.T("no-usages-found", "No known usages for this item.")
                           : Loc.T("no-recipes-found", "No recipes found for this item."),
                    CairoFont.WhiteSmallText()));
                return components.ToArray();
            }

            AddAllSections(components, sections, stack, maxGridGroups: 50);
            return components.ToArray();
        }

        /// <summary>
        /// One self-contained page per created-by recipe, for the swappable hover popup. Each page
        /// is a heading + a single recipe (grid, voxel or barrel). Variants inside a page still
        /// auto-cycle on their own. Never includes usages.
        /// </summary>
        public List<RichTextComponentBase[]> BuildCreatedByPages(ItemStack stack)
        {
            var pages = new List<RichTextComponentBase[]>();

            // Skip the expensive process scan; grid/voxel/barrel recipes are indexed.
            RecipeSections s = index.GetCreatedBy(stack, includeProcesses: false);
            ItemStack[] all = index.AllStacks.ToArray();

            foreach (GridRecipe[] group in s.GridGroups) pages.Add(BuildGridPage(group, stack, all));
            foreach (RecipeBase r in s.Smithing) pages.Add(BuildBaseRecipePage(r, Loc.T("heading-smithing", "Smithing"), barrel: false));
            foreach (RecipeBase r in s.ClayForming) pages.Add(BuildBaseRecipePage(r, Loc.T("heading-clayforming", "Clay forming"), barrel: false));
            foreach (RecipeBase r in s.Knapping) pages.Add(BuildBaseRecipePage(r, Loc.T("heading-knapping", "Knapping"), barrel: false));
            foreach (RecipeBase r in s.Barrel) pages.Add(BuildBaseRecipePage(r, Loc.T("heading-barrel", "Barrel (mixing / aging)"), barrel: true));

            return pages;
        }

        RichTextComponentBase[] BuildGridPage(GridRecipe[] group, ItemStack fallbackOutput, ItemStack[] allStacks)
        {
            var c = new List<RichTextComponentBase>();
            AddHeading(c, Loc.T("heading-crafting", "Crafting"));
            c.Add(new ClearFloatTextComponent(capi, 8));
            AppendGridGroup(c, group, fallbackOutput, allStacks, padLeft: 6);
            return c.ToArray();
        }

        RichTextComponentBase[] BuildBaseRecipePage(RecipeBase recipe, string heading, bool barrel)
        {
            var c = new List<RichTextComponentBase>();
            AddHeading(c, heading);
            AddIngredientsToOutputLine(c, recipe, barrel ? GetSealInfo(recipe) : null);
            AddVoxelPatterns(c, recipe);
            return c.ToArray();
        }

        void AddAllSections(List<RichTextComponentBase> components, RecipeSections sections, ItemStack fallbackOutput, int maxGridGroups)
        {
            AddGridRecipeSection(components, sections.GridGroups, fallbackOutput, maxGridGroups);
            AddRecipeBaseSection(components, sections.Smithing, Loc.T("heading-smithing", "Smithing"));
            AddRecipeBaseSection(components, sections.ClayForming, Loc.T("heading-clayforming", "Clay forming"));
            AddRecipeBaseSection(components, sections.Knapping, Loc.T("heading-knapping", "Knapping"));
            AddBarrelSection(components, sections.Barrel);
            AddProcessSection(components, sections.Smelting, Loc.T("heading-smelting", "Smelting / cooking"), showRatio: true);
            AddProcessSection(components, sections.Grinding, Loc.T("heading-grinding", "Grinding"), showRatio: false);
            AddProcessSection(components, sections.Crushing, Loc.T("heading-crushing", "Crushing"), showRatio: false);
        }

        void AddHeading(List<RichTextComponentBase> components, string text)
        {
            components.Add(new ClearFloatTextComponent(capi, 14));
            components.Add(new RichTextComponent(capi, text + "\n", CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold)));
        }

        void AddGridRecipeSection(List<RichTextComponentBase> components, List<GridRecipe[]> groups, ItemStack fallbackOutput, int maxGroups)
        {
            if (groups.Count == 0) return;

            AddHeading(components, Loc.T("heading-crafting", "Crafting"));

            ItemStack[] allStacks = index.AllStacks.ToArray();

            // Very common ingredients (sticks, planks...) can appear in hundreds of recipes;
            // building richtext for all of them at once would stall the client for seconds.
            int skipped = Math.Max(0, groups.Count - maxGroups);

            int i = 0;
            foreach (GridRecipe[] group in groups.Take(maxGroups))
            {
                if (group.Length == 0) continue;

                if (i % 2 == 0) components.Add(new ClearFloatTextComponent(capi, 8));
                AppendGridGroup(components, group, fallbackOutput, allStacks, padLeft: 6 + (1 - i % 2) * 20);
                i++;
            }

            components.Add(new ClearFloatTextComponent(capi, 8));

            if (skipped > 0)
            {
                components.Add(new RichTextComponent(capi,
                    Loc.T("more-recipes", "...and {0} more. Refine via search or drill down through an intermediate item.", skipped) + "\n",
                    CairoFont.WhiteDetailText()));
            }
        }

        /// <summary>Appends one grid recipe as: [grid] = [output]. Returns false if it could not be built.</summary>
        void AppendGridGroup(List<RichTextComponentBase> components, GridRecipe[] group, ItemStack fallbackOutput, ItemStack[] allStacks, double padLeft)
        {
            if (group.Length == 0) return;

            SlideshowGridRecipeTextComponent gridComp;
            try
            {
                gridComp = new SlideshowGridRecipeTextComponent(capi, group, 40, EnumFloat.Inline, onStackClicked, allStacks);
            }
            catch (Exception e)
            {
                // A broken recipe from some mod should not take down the whole page
                capi.Logger.Warning("[picorecipes] Could not display recipe group {0}: {1}", group[0]?.Name, e.Message);
                return;
            }
            gridComp.VerticalAlign = EnumVerticalAlign.Top;
            gridComp.PaddingRight = 8;
            gridComp.PaddingLeft = padLeft;
            components.Add(gridComp);

            components.Add(new RichTextComponent(capi, "=", CairoFont.WhiteMediumText())
            {
                VerticalAlign = EnumVerticalAlign.Middle,
                PaddingRight = 5
            });

            ItemStack[] outputStacks = group
                .Select(r => { try { return r.Output?.ResolvedItemStack; } catch (Exception) { return null; } })
                .Where(st => st != null)
                .ToArray();
            if (outputStacks.Length == 0) outputStacks = new[] { fallbackOutput };

            var outComp = new SlideshowItemstackTextComponent(capi, outputStacks, 40, EnumFloat.Inline, onStackClicked)
            {
                VerticalAlign = EnumVerticalAlign.Middle,
                ShowStackSize = true
            };
            outComp.overrideCurrentItemStack = gridComp.GenerateCurrentVisibleOutputStack;
            components.Add(outComp);
        }

        void AddRecipeBaseSection(List<RichTextComponentBase> components, List<RecipeBase> recipes, string heading)
        {
            if (recipes.Count == 0) return;

            AddHeading(components, heading);

            foreach (RecipeBase recipe in recipes)
            {
                AddIngredientsToOutputLine(components, recipe, extraInfo: null);
                AddVoxelPatterns(components, recipe);
            }

            components.Add(new ClearFloatTextComponent(capi, 4));
        }

        void AddBarrelSection(List<RichTextComponentBase> components, List<RecipeBase> recipes)
        {
            if (recipes.Count == 0) return;

            AddHeading(components, Loc.T("heading-barrel", "Barrel (mixing / aging)"));

            foreach (RecipeBase recipe in recipes)
            {
                AddIngredientsToOutputLine(components, recipe, GetSealInfo(recipe));
            }

            components.Add(new ClearFloatTextComponent(capi, 4));
        }

        string GetSealInfo(RecipeBase recipe)
        {
            try
            {
                // BarrelRecipe.SealHours; read reflectively to avoid a VSSurvivalMod.dll reference
                var prop = recipe.GetType().GetProperty("SealHours");
                if (prop?.GetValue(recipe) is double hours && hours > 0)
                {
                    return Loc.T("seal-for-hours", "Seal for {0} in-game hours", Math.Round(hours, 1));
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// Clay forming / knapping / smithing recipes are voxel shapes rather than grids. We show a
        /// single representative layer (the one with the most voxels) as one small pixel grid, rather
        /// than every layer — multi-layer rendering was cluttered and slow.
        /// </summary>
        void AddVoxelPatterns(List<RichTextComponentBase> components, RecipeBase recipe)
        {
            try
            {
                // Pattern is a public field on LayeredVoxelRecipe (clay forming / knapping / smithing).
                var patternField = recipe.GetType().GetField("Pattern");
                if (patternField?.GetValue(recipe) is not string[][] pattern || pattern.Length == 0) return;

                // Pick the fullest layer as the single representative shape.
                string[] best = null;
                int bestFilled = -1;
                foreach (string[] layer in pattern)
                {
                    if (layer == null) continue;
                    int filled = 0;
                    foreach (string row in layer)
                        if (row != null)
                            foreach (char ch in row)
                                if (ch != '_' && ch != ' ') filled++;
                    if (filled > bestFilled) { bestFilled = filled; best = layer; }
                }
                if (best == null) return;

                int rows = best.Length;
                int cols = 0;
                foreach (string row in best)
                    if (row != null && row.Length > cols) cols = row.Length;
                if (rows == 0 || cols == 0) return;

                var grid = new bool[rows, cols];
                for (int j = 0; j < rows; j++)
                {
                    string row = best[j];
                    if (row == null) continue;
                    for (int z = 0; z < cols && z < row.Length; z++)
                    {
                        char ch = row[z];
                        grid[j, z] = ch != '_' && ch != ' ';
                    }
                }

                components.Add(new ClearFloatTextComponent(capi, 4));
                components.Add(new VoxelPatternComponent(capi, grid) { PaddingLeft = 6 });
                components.Add(new ClearFloatTextComponent(capi, 4));
            }
            catch (Exception) { }
        }

        /// <summary>Renders one recipe as: [ingredient] + [ingredient] = [output]  (extra info)</summary>
        void AddIngredientsToOutputLine(List<RichTextComponentBase> components, RecipeBase recipe, string extraInfo)
        {
            components.Add(new ClearFloatTextComponent(capi, 4));

            bool first = true;
            foreach (IRecipeIngredient ingred in recipe.RecipeIngredients ?? Enumerable.Empty<IRecipeIngredient>())
            {
                ItemStack[] variants = index.ResolveIngredientStacks(ingred);
                if (variants.Length == 0) continue;

                if (!first)
                {
                    components.Add(new RichTextComponent(capi, " + ", CairoFont.WhiteMediumText())
                    {
                        VerticalAlign = EnumVerticalAlign.Middle,
                        PaddingLeft = 6
                    });
                }
                first = false;

                components.Add(new SlideshowItemstackTextComponent(capi, variants, 40, EnumFloat.Inline, onStackClicked)
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    ShowStackSize = true
                });
            }

            components.Add(new RichTextComponent(capi, " = ", CairoFont.WhiteMediumText())
            {
                VerticalAlign = EnumVerticalAlign.Middle,
                PaddingLeft = 6
            });

            ItemStack outStack = null;
            try { outStack = recipe.RecipeOutput?.ResolvedItemStack; } catch (Exception) { }

            if (outStack != null)
            {
                components.Add(new ItemstackTextComponent(capi, outStack, 40, 0, EnumFloat.Inline, onStackClicked)
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    ShowStacksize = true
                });
            }

            if (extraInfo != null)
            {
                components.Add(new RichTextComponent(capi, "  " + extraInfo, CairoFont.WhiteDetailText())
                {
                    VerticalAlign = EnumVerticalAlign.Middle
                });
            }

            components.Add(new ClearFloatTextComponent(capi, 4));
        }

        void AddProcessSection(List<RichTextComponentBase> components, List<StackProcess> processes, string heading, bool showRatio)
        {
            if (processes.Count == 0) return;

            AddHeading(components, heading);

            foreach (StackProcess process in processes)
            {
                components.Add(new ClearFloatTextComponent(capi, 4));

                ItemStack inStack = process.Input.Clone();
                inStack.StackSize = process.Ratio;

                components.Add(new ItemstackTextComponent(capi, inStack, 40, 0, EnumFloat.Inline, onStackClicked)
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    ShowStacksize = showRatio && process.Ratio > 1
                });

                components.Add(new RichTextComponent(capi, " = ", CairoFont.WhiteMediumText())
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    PaddingLeft = 6
                });

                components.Add(new ItemstackTextComponent(capi, process.Output, 40, 0, EnumFloat.Inline, onStackClicked)
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    ShowStacksize = true
                });

                components.Add(new ClearFloatTextComponent(capi, 4));
            }

            components.Add(new ClearFloatTextComponent(capi, 4));
        }
    }
}
