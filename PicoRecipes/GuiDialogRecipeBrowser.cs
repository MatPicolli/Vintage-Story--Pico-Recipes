using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace PicoRecipes
{
    /// <summary>
    /// The JEI style recipe viewer: shows every way to create an item (Recipes tab) or every
    /// way it is consumed (Usages tab). Clicking any itemstack inside the view drills down into
    /// that item (left click = its recipes, right click = its usages), with full back-history.
    /// </summary>
    public class GuiDialogRecipeBrowser : GuiDialog
    {
        class BrowsePage
        {
            public ItemStack Stack;
            public bool Usages;
            public float PosY;
        }

        readonly PicoRecipesModSystem mod;
        readonly Stack<BrowsePage> history = new Stack<BrowsePage>();

        const int ViewHeight = 500;
        const int ViewWidth = 500;

        public override string ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => true;
        public override bool UnregisterOnClose => false;

        public GuiDialogRecipeBrowser(ICoreClientAPI capi, PicoRecipesModSystem mod) : base(capi)
        {
            this.mod = mod;
        }

        public void ShowRecipesFor(ItemStack stack) => Show(stack, usages: false);
        public void ShowUsagesFor(ItemStack stack) => Show(stack, usages: true);

        void Show(ItemStack stack, bool usages)
        {
            if (stack == null) return;
            mod.RecipeIndex.EnsureLoaded();

            if (history.Count > 0)
            {
                BrowsePage top = history.Peek();
                bool samePage = top.Usages == usages
                    && top.Stack.Equals(capi.World, stack, GlobalConstants.IgnoredStackAttributes);
                if (samePage)
                {
                    if (!IsOpened()) TryOpen();
                    return;
                }
            }

            history.Push(new BrowsePage { Stack = stack.Clone(), Usages = usages });

            if (IsOpened()) ComposePage();
            else TryOpen();
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            if (history.Count > 0) ComposePage();
        }

        void OnStackClicked(ItemStack stack)
        {
            Show(stack, usages: capi.Input.MouseButton.Right);
        }

        void ComposePage()
        {
            BrowsePage current = history.Peek();

            ElementBounds textBounds = ElementBounds.Fixed(9, 45, ViewWidth, 30 + ViewHeight + 17);
            ElementBounds clipBounds = textBounds.ForkBoundingParent();
            ElementBounds insetBounds = textBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = clipBounds.CopyOffsetedSibling(textBounds.fixedWidth + 7, -6, 0, 6).WithFixedWidth(20);

            ElementBounds tabBounds = ElementBounds.Fixed(-1, -16, 300, 25);

            ElementBounds backButtonBounds = ElementBounds
                .FixedSize(0, 0)
                .FixedUnder(clipBounds, 2 * 5 + 5)
                .WithAlignment(EnumDialogArea.LeftFixed)
                .WithFixedPadding(20, 4)
                .WithFixedAlignmentOffset(4, 1);
            ElementBounds closeButtonBounds = ElementBounds
                .FixedSize(0, 0)
                .FixedUnder(clipBounds, 2 * 5 + 5)
                .WithAlignment(EnumDialogArea.RightFixed)
                .WithFixedPadding(20, 4)
                .WithFixedAlignmentOffset(-11, 1);

            ElementBounds bgBounds = insetBounds.ForkBoundingParent(5, 40, 36, 52).WithFixedPadding(GuiStyle.ElementToDialogPadding / 2);
            bgBounds.WithChildren(insetBounds, textBounds, scrollbarBounds, backButtonBounds, closeButtonBounds);

            ElementBounds dialogBounds = bgBounds.ForkBoundingParent()
                .WithAlignment(EnumDialogArea.CenterFixed)
                .WithFixedPosition(0, 70);

            GuiTab[] tabs = {
                new GuiTab { Name = Lang.Get("picorecipes:tab-recipes"), DataInt = 0 },
                new GuiTab { Name = Lang.Get("picorecipes:tab-usages"), DataInt = 1 }
            };

            RichTextComponentBase[] components = BuildPageComponents(current);

            SingleComposer?.Dispose();
            SingleComposer = capi.Gui
                .CreateCompo("picorecipes-browser", dialogBounds)
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar(Lang.Get("picorecipes:dialog-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddHorizontalTabs(tabs, tabBounds, OnTabClicked, CairoFont.WhiteSmallText(), CairoFont.WhiteSmallText().WithColor(GuiStyle.ActiveButtonTextColor), "tabs")
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddRichtext(components, textBounds, "richtext")
                    .EndClip()
                    .AddVerticalScrollbar(OnNewScrollbarValue, scrollbarBounds, "scrollbar")
                    .AddSmallButton(Lang.Get("general-back"), OnButtonBack, backButtonBounds)
                    .AddSmallButton(Lang.Get("general-close"), () => TryClose(), closeButtonBounds)
                .EndChildElements()
                .Compose();

            SingleComposer.GetHorizontalTabs("tabs").activeElement = current.Usages ? 1 : 0;

            var richtext = SingleComposer.GetRichtext("richtext");
            SingleComposer.GetScrollbar("scrollbar").SetHeights(
                ViewHeight, (float)richtext.Bounds.fixedHeight
            );
            SingleComposer.GetScrollbar("scrollbar").CurrentYPosition = current.PosY;
            OnNewScrollbarValue(current.PosY);
        }

        void OnTabClicked(int index)
        {
            BrowsePage current = history.Peek();
            bool usages = index == 1;
            if (current.Usages == usages) return;

            current.Usages = usages;
            current.PosY = 0;
            ComposePage();
        }

        bool OnButtonBack()
        {
            if (history.Count > 1)
            {
                history.Pop();
                ComposePage();
            }
            else
            {
                TryClose();
            }
            return true;
        }

        void OnNewScrollbarValue(float value)
        {
            var richtext = SingleComposer?.GetRichtext("richtext");
            if (richtext == null) return;

            richtext.Bounds.fixedY = 3 - value;
            richtext.Bounds.CalcWorldBounds();

            if (history.Count > 0) history.Peek().PosY = value;
        }

        #region Page content generation

        RichTextComponentBase[] BuildPageComponents(BrowsePage page)
        {
            var components = new List<RichTextComponentBase>();
            var index = mod.RecipeIndex;

            // Header: the item itself + its name
            var titleStack = new ItemstackTextComponent(capi, page.Stack, 44, 10, EnumFloat.Left, OnStackClicked);
            components.Add(titleStack);
            components.Add(new RichTextComponent(capi, page.Stack.GetName() + "\n", CairoFont.WhiteSmallishText().WithWeight(FontWeight.Bold)));
            components.Add(new RichTextComponent(capi, page.Stack.Collectible.Code?.ToString() + "\n", CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment)));
            components.Add(new ClearFloatTextComponent(capi, 10));

            RecipeSections sections = page.Usages ? index.GetUsedIn(page.Stack) : index.GetCreatedBy(page.Stack);

            if (sections.IsEmpty)
            {
                components.Add(new RichTextComponent(capi,
                    Lang.Get(page.Usages ? "picorecipes:no-usages-found" : "picorecipes:no-recipes-found"),
                    CairoFont.WhiteSmallText()));
                return components.ToArray();
            }

            AddGridRecipeSection(components, sections.GridGroups);
            AddRecipeBaseSection(components, sections.Smithing, "picorecipes:heading-smithing");
            AddRecipeBaseSection(components, sections.ClayForming, "picorecipes:heading-clayforming");
            AddRecipeBaseSection(components, sections.Knapping, "picorecipes:heading-knapping");
            AddBarrelSection(components, sections.Barrel);
            AddProcessSection(components, sections.Smelting, "picorecipes:heading-smelting", showRatio: true);
            AddProcessSection(components, sections.Grinding, "picorecipes:heading-grinding", showRatio: false);
            AddProcessSection(components, sections.Crushing, "picorecipes:heading-crushing", showRatio: false);

            return components.ToArray();
        }

        void AddHeading(List<RichTextComponentBase> components, string langKey)
        {
            components.Add(new ClearFloatTextComponent(capi, 14));
            components.Add(new RichTextComponent(capi, Lang.Get(langKey) + "\n",
                CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold)));
        }

        void AddGridRecipeSection(List<RichTextComponentBase> components, List<GridRecipe[]> groups)
        {
            if (groups.Count == 0) return;

            AddHeading(components, "picorecipes:heading-crafting");

            ItemStack[] allStacks = mod.RecipeIndex.AllStacks.ToArray();

            // Very common ingredients (sticks, planks...) can appear in hundreds of recipes;
            // building richtext for all of them at once would stall the client for seconds.
            const int maxGroups = 50;
            int skipped = Math.Max(0, groups.Count - maxGroups);

            int i = 0;
            foreach (GridRecipe[] group in groups.Take(maxGroups))
            {
                if (group.Length == 0) continue;

                if (i++ % 2 == 0) components.Add(new ClearFloatTextComponent(capi, 8));

                SlideshowGridRecipeTextComponent gridComp;
                try
                {
                    gridComp = new SlideshowGridRecipeTextComponent(capi, group, 40, EnumFloat.Inline, OnStackClicked, allStacks);
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

                var eqComp = new RichTextComponent(capi, "=", CairoFont.WhiteMediumText())
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    PaddingRight = 5
                };
                components.Add(eqComp);

                ItemStack[] outputStacks = group
                    .Select(r => { try { return r.Output?.ResolvedItemStack; } catch (Exception) { return null; } })
                    .Where(s => s != null)
                    .ToArray();
                if (outputStacks.Length == 0) outputStacks = new[] { history.Peek().Stack };

                var outComp = new SlideshowItemstackTextComponent(capi, outputStacks, 40, EnumFloat.Inline, OnStackClicked)
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
                    Lang.Get("picorecipes:more-recipes", skipped) + "\n", CairoFont.WhiteDetailText()));
            }
        }

        void AddRecipeBaseSection(List<RichTextComponentBase> components, List<RecipeBase> recipes, string headingLangKey)
        {
            if (recipes.Count == 0) return;

            AddHeading(components, headingLangKey);

            foreach (RecipeBase recipe in recipes)
            {
                AddIngredientsToOutputLine(components, recipe, extraInfo: null);
            }

            components.Add(new ClearFloatTextComponent(capi, 4));
        }

        void AddBarrelSection(List<RichTextComponentBase> components, List<RecipeBase> recipes)
        {
            if (recipes.Count == 0) return;

            AddHeading(components, "picorecipes:heading-barrel");

            foreach (RecipeBase recipe in recipes)
            {
                string extraInfo = null;
                try
                {
                    // BarrelRecipe.SealHours; read reflectively to avoid a VSSurvivalMod.dll reference
                    var prop = recipe.GetType().GetProperty("SealHours");
                    if (prop?.GetValue(recipe) is double hours && hours > 0)
                    {
                        extraInfo = Lang.Get("picorecipes:seal-for-hours", Math.Round(hours, 1));
                    }
                }
                catch (Exception) { }

                AddIngredientsToOutputLine(components, recipe, extraInfo);
            }

            components.Add(new ClearFloatTextComponent(capi, 4));
        }

        /// <summary>Renders one recipe as: [ingredient] + [ingredient] = [output]  (extra info)</summary>
        void AddIngredientsToOutputLine(List<RichTextComponentBase> components, RecipeBase recipe, string extraInfo)
        {
            components.Add(new ClearFloatTextComponent(capi, 4));

            bool first = true;
            foreach (IRecipeIngredient ingred in recipe.RecipeIngredients ?? Enumerable.Empty<IRecipeIngredient>())
            {
                ItemStack[] variants = mod.RecipeIndex.ResolveIngredientStacks(ingred);
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

                var ingredComp = new SlideshowItemstackTextComponent(capi, variants, 40, EnumFloat.Inline, OnStackClicked)
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    ShowStackSize = true
                };
                components.Add(ingredComp);
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
                var outComp = new ItemstackTextComponent(capi, outStack, 40, 0, EnumFloat.Inline, OnStackClicked)
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    ShowStacksize = true
                };
                components.Add(outComp);
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

        void AddProcessSection(List<RichTextComponentBase> components, List<StackProcess> processes, string headingLangKey, bool showRatio)
        {
            if (processes.Count == 0) return;

            AddHeading(components, headingLangKey);

            foreach (StackProcess process in processes)
            {
                components.Add(new ClearFloatTextComponent(capi, 4));

                ItemStack inStack = process.Input.Clone();
                inStack.StackSize = process.Ratio;

                components.Add(new ItemstackTextComponent(capi, inStack, 40, 0, EnumFloat.Inline, OnStackClicked)
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    ShowStacksize = showRatio && process.Ratio > 1
                });

                components.Add(new RichTextComponent(capi, " = ", CairoFont.WhiteMediumText())
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    PaddingLeft = 6
                });

                components.Add(new ItemstackTextComponent(capi, process.Output, 40, 0, EnumFloat.Inline, OnStackClicked)
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                    ShowStacksize = true
                });

                components.Add(new ClearFloatTextComponent(capi, 4));
            }

            components.Add(new ClearFloatTextComponent(capi, 4));
        }

        #endregion
    }
}
