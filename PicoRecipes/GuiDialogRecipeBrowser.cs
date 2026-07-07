using System.Collections.Generic;
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
        readonly RecipeComponentBuilder builder;
        readonly Stack<BrowsePage> history = new Stack<BrowsePage>();

        const int ViewHeight = 500;
        const int ViewWidth = 500;

        public override string ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => true;
        public override bool UnregisterOnClose => false;

        // Draw above the player/chest inventory (0.2) so it is never hidden behind it,
        // and take input before the inventory (lower InputOrder = handled first).
        public override double DrawOrder => 0.26;
        public override double InputOrder => 0.3;

        public GuiDialogRecipeBrowser(ICoreClientAPI capi, PicoRecipesModSystem mod) : base(capi)
        {
            this.mod = mod;
            builder = new RecipeComponentBuilder(capi, mod.RecipeIndex, OnStackClicked);
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

            double titleH = GuiStyle.TitleBarHeight;   // 31
            const double tabH = 30;
            double contentTop = titleH + 6 + tabH + 10;  // title, gap, tabs, gap

            // The title bar is added below WITHOUT explicit bounds so it parents to the dialog root
            // (dialogBounds), exactly like every vanilla dialog. This is important: the built-in
            // "Movable" title-bar option repositions the title bar's ParentBounds. If that parent is
            // the Fill + FitToChildren background bounds, toggling movable collapses its width (the
            // tabs vanish) and the next compose crashes in GuiElementDialogTitleBar.BlurPartial with
            // "x2 must be larger than x1". Parenting to the dialog root moves the whole dialog safely.
            // The tabs still sit below the title bar: the top TitleBarHeight band is left empty here,
            // and FitToChildren measures from the top edge so the background still spans it.
            ElementBounds tabBounds = ElementBounds.Fixed(0, titleH + 6, ViewWidth + 34, tabH);

            ElementBounds textBounds = ElementBounds.Fixed(9, contentTop, ViewWidth, ViewHeight);
            ElementBounds clipBounds = textBounds.ForkBoundingParent();
            ElementBounds insetBounds = textBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = clipBounds.CopyOffsetedSibling(textBounds.fixedWidth + 7, -6, 0, 6).WithFixedWidth(20);

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

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding / 2);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(tabBounds, insetBounds, textBounds, scrollbarBounds, backButtonBounds, closeButtonBounds);

            ElementBounds dialogBounds = bgBounds.ForkBoundingParent()
                .WithAlignment(EnumDialogArea.CenterFixed)
                .WithFixedPosition(0, 70);

            GuiTab[] tabs = {
                new GuiTab { Name = Loc.T("tab-recipes", "Recipes"), DataInt = 0 },
                new GuiTab { Name = Loc.T("tab-usages", "Usages"), DataInt = 1 }
            };

            RichTextComponentBase[] components = builder.BuildFull(current.Stack, current.Usages);

            var tabFont = CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold);
            SingleComposer?.Dispose();
            SingleComposer = capi.Gui
                .CreateCompo("picorecipes-browser", dialogBounds)
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar(Loc.T("dialog-title", "Pico Recipes"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddHorizontalTabs(tabs, tabBounds, OnTabClicked, tabFont, tabFont.Clone().WithColor(GuiStyle.ActiveButtonTextColor), "tabs")
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

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            history.Clear();
        }

        void OnNewScrollbarValue(float value)
        {
            var richtext = SingleComposer?.GetRichtext("richtext");
            if (richtext == null) return;

            richtext.Bounds.fixedY = 3 - value;
            richtext.Bounds.CalcWorldBounds();

            if (history.Count > 0) history.Peek().PosY = value;
        }
    }
}
