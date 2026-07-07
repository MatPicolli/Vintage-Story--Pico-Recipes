using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PicoRecipes
{
    /// <summary>
    /// Turns the recipes found for an itemstack into the richtext components used by both the
    /// full recipe browser and the compact hover preview. Rendering uses the same engine
    /// components the vanilla handbook uses, so wildcard / wood-typed recipes cycle correctly.
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
        /// Compact preview for the hover popup: recipe first, minimal chrome. Prefers "how to craft"
        /// (created-by); falls back to "used in" if the item is only a raw ingredient.
        /// </summary>
        public RichTextComponentBase[] BuildCompact(ItemStack stack, out int rowEstimate)
        {
            var components = new List<RichTextComponentBase>();

            // Small header just to identify the item, then straight to the recipe.
            components.Add(new ItemstackTextComponent(capi, stack, 30, 8, EnumFloat.Left, onStackClicked));
            components.Add(new RichTextComponent(capi, stack.GetName() + "\n", CairoFont.WhiteSmallishText().WithWeight(FontWeight.Bold)));
            components.Add(new ClearFloatTextComponent(capi, 4));

            // Skip the expensive process scan for the preview; grid/voxel/barrel recipes are indexed.
            RecipeSections sections = index.GetCreatedBy(stack, includeProcesses: false);
            bool usages = false;
            if (sections.IsEmpty)
            {
                sections = index.GetUsedIn(stack);
                usages = true;
            }

            if (sections.IsEmpty)
            {
                rowEstimate = 1;
                components.Add(new RichTextComponent(capi, Loc.T("no-recipes-found", "No recipes found for this item."), CairoFont.WhiteDetailText()));
                return components.ToArray();
            }

            if (usages)
            {
                components.Add(new RichTextComponent(capi, Loc.T("used-in-label", "Used in:") + "\n", CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment)));
            }

            AddAllSections(components, sections, stack, maxGridGroups: 4);
            rowEstimate = EstimateRows(sections, 4);
            return components.ToArray();
        }

        /// <summary>Rough number of recipe "rows" so the hover popup can size itself before layout.</summary>
        static int EstimateRows(RecipeSections s, int maxGridGroups)
        {
            int rows = 2; // header
            rows += (Math.Min(s.GridGroups.Count, maxGridGroups) + 1) / 2 * 2; // grids drawn two per row, ~2 lines tall
            rows += s.Smithing.Count + s.ClayForming.Count + s.Knapping.Count + s.Barrel.Count;
            rows += s.Smelting.Count + s.Grinding.Count + s.Crushing.Count;
            return rows;
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

                if (i++ % 2 == 0) components.Add(new ClearFloatTextComponent(capi, 8));

                SlideshowGridRecipeTextComponent gridComp;
                try
                {
                    gridComp = new SlideshowGridRecipeTextComponent(capi, group, 40, EnumFloat.Inline, onStackClicked, allStacks);
                }
                catch (Exception e)
                {
                    // A broken recipe from some mod should not take down the whole page
                    capi.Logger.Warning("[picorecipes] Could not display recipe group {0}: {1}", group[0]?.Name, e.Message);
                    continue;
                }
                gridComp.VerticalAlign = EnumVerticalAlign.Top;
                gridComp.PaddingRight = 8;
                gridComp.PaddingLeft = 6 + (1 - i % 2) * 20;
                components.Add(gridComp);

                components.Add(new RichTextComponent(capi, "=", CairoFont.WhiteMediumText())
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    PaddingRight = 5
                });

                ItemStack[] outputStacks = group
                    .Select(r => { try { return r.Output?.ResolvedItemStack; } catch (Exception) { return null; } })
                    .Where(s => s != null)
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

            components.Add(new ClearFloatTextComponent(capi, 8));

            if (skipped > 0)
            {
                components.Add(new RichTextComponent(capi,
                    Loc.T("more-recipes", "...and {0} more. Refine via search or drill down through an intermediate item.", skipped) + "\n",
                    CairoFont.WhiteDetailText()));
            }
        }

        void AddRecipeBaseSection(List<RichTextComponentBase> components, List<RecipeBase> recipes, string heading)
        {
            if (recipes.Count == 0) return;

            AddHeading(components, heading);

            foreach (RecipeBase recipe in recipes)
            {
                AddIngredientsToOutputLine(components, recipe, extraInfo: null);
                TryAddVoxelPattern(components, recipe);
            }

            components.Add(new ClearFloatTextComponent(capi, 4));
        }

        void AddBarrelSection(List<RichTextComponentBase> components, List<RecipeBase> recipes)
        {
            if (recipes.Count == 0) return;

            AddHeading(components, Loc.T("heading-barrel", "Barrel (mixing / aging)"));

            foreach (RecipeBase recipe in recipes)
            {
                string extraInfo = null;
                try
                {
                    // BarrelRecipe.SealHours; read reflectively to avoid a VSSurvivalMod.dll reference
                    var prop = recipe.GetType().GetProperty("SealHours");
                    if (prop?.GetValue(recipe) is double hours && hours > 0)
                    {
                        extraInfo = Loc.T("seal-for-hours", "Seal for {0} in-game hours", Math.Round(hours, 1));
                    }
                }
                catch (Exception) { }

                AddIngredientsToOutputLine(components, recipe, extraInfo);
            }

            components.Add(new ClearFloatTextComponent(capi, 4));
        }

        /// <summary>
        /// Clay forming / knapping / smithing recipes are voxel shapes rather than grids. Render a
        /// top-down silhouette of the shape (■ = filled voxel) so the "recipe" is actually explained.
        /// </summary>
        void TryAddVoxelPattern(List<RichTextComponentBase> components, RecipeBase recipe)
        {
            try
            {
                // Pattern is a public field on LayeredVoxelRecipe (clay forming / knapping / smithing).
                var patternField = recipe.GetType().GetField("Pattern");
                if (patternField?.GetValue(recipe) is not string[][] pattern || pattern.Length == 0) return;

                int rows = pattern[0]?.Length ?? 0;
                if (rows == 0) return;

                var sb = new StringBuilder();
                for (int j = 0; j < rows; j++)
                {
                    for (int z = 0; ; z++)
                    {
                        bool anyRowHasCol = false;
                        bool filled = false;
                        for (int layer = 0; layer < pattern.Length; layer++)
                        {
                            if (j >= pattern[layer].Length) continue;
                            string row = pattern[layer][j];
                            if (row == null || z >= row.Length) continue;
                            anyRowHasCol = true;
                            char c = row[z];
                            if (c != '_' && c != ' ') filled = true;
                        }
                        if (!anyRowHasCol) break;
                        sb.Append(filled ? '#' : '.'); // filled voxel vs empty
                    }
                    sb.Append('\n');
                }

                if (sb.Length == 0) return;

                components.Add(new RichTextComponent(capi, sb.ToString(),
                    CairoFont.WhiteDetailText().WithFont("monospace").WithColor(GuiStyle.ColorParchment)));
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
