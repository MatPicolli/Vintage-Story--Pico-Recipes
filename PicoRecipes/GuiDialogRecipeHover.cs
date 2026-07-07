using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace PicoRecipes
{
    /// <summary>
    /// A small popup that appears next to the item you are hovering in the item list and shows its
    /// recipe at a glance — no click required. When the item has several ways to be crafted it shows
    /// one at a time; Shift+MouseWheel cycles between them (variants inside one recipe auto-cycle on
    /// their own). Left/right click and the R/U hotkeys still open the full browser.
    ///
    /// This is a regular dialog (not a HUD element) on purpose: HUD elements render in an earlier
    /// pass than dialogs, so a HUD popup ends up underneath the inventory and its scrollbar no
    /// matter how high its DrawOrder is. As a dialog with a high DrawOrder it sits on top of the
    /// inventory (0.2) and the recipe browser (0.26). It is display-only: it never grabs the mouse,
    /// takes focus, or consumes the escape key.
    /// </summary>
    public class GuiDialogRecipeHover : GuiDialog
    {
        readonly PicoRecipesModSystem mod;
        readonly RecipeComponentBuilder builder;

        ItemStack currentStack;
        List<RichTextComponentBase[]> pages = new List<RichTextComponentBase[]>();
        int pageIndex;
        double lastAnchorX, lastAnchorY;

        const int Width = 360;
        const int MaxHeight = 340;
        const int MinHeight = 60;
        const int Pad = 8;

        public override string ToggleKeyCombinationCode => null;
        public override bool Focusable => false;
        public override bool PrefersUngrabbedMouse => true;
        public override bool UnregisterOnClose => false;

        // Draw above the inventory (0.2) and the recipe browser (0.26) so nothing vanilla covers it.
        public override double DrawOrder => 0.5;

        // Don't swallow escape: let it close the inventory, which hides us via the mod tick.
        public override bool OnEscapePressed() => false;

        public GuiDialogRecipeHover(ICoreClientAPI capi, PicoRecipesModSystem mod) : base(capi)
        {
            this.mod = mod;
            // Clicking a stack inside the preview opens the full browser for it
            builder = new RecipeComponentBuilder(capi, mod.RecipeIndex, OnStackClicked);
        }

        void OnStackClicked(ItemStack stack)
        {
            mod.BrowserDialog.ShowRecipesFor(stack);
        }

        /// <summary>Show the preview for a stack, anchored near the given screen pixel position.</summary>
        public void ShowFor(ItemStack stack, double anchorScreenX, double anchorScreenY)
        {
            if (stack == null)
            {
                Hide();
                return;
            }

            bool sameStack = currentStack != null
                && currentStack.Equals(capi.World, stack, GlobalConstants.IgnoredStackAttributes);
            if (sameStack && IsOpened()) return;

            currentStack = stack.Clone();
            lastAnchorX = anchorScreenX;
            lastAnchorY = anchorScreenY;

            mod.RecipeIndex.EnsureLoaded();
            pages = builder.BuildCreatedByPages(currentStack);
            pageIndex = 0;

            Compose();
            if (!IsOpened()) TryOpen();
        }

        /// <summary>Cycle to another crafting recipe for the current item (Shift+MouseWheel).</summary>
        public void CycleRecipe(int dir)
        {
            if (!IsOpened() || pages.Count < 2) return;
            pageIndex = GameMath.Mod(pageIndex + dir, pages.Count);
            Compose();
        }

        public void Hide()
        {
            currentStack = null;
            if (IsOpened()) TryClose();
        }

        void Compose()
        {
            var components = new List<RichTextComponentBase>();

            // Header: item icon + name
            components.Add(new ItemstackTextComponent(capi, currentStack, 30, 8, EnumFloat.Left, OnStackClicked));
            components.Add(new RichTextComponent(capi, currentStack.GetName() + "\n", CairoFont.WhiteSmallishText().WithWeight(FontWeight.Bold)));

            if (pages.Count == 0)
            {
                components.Add(new ClearFloatTextComponent(capi, 4));
                components.Add(new RichTextComponent(capi, Loc.T("no-crafting-recipe", "No crafting recipe."), CairoFont.WhiteDetailText()));
            }
            else
            {
                if (pages.Count > 1)
                {
                    components.Add(new RichTextComponent(capi,
                        Loc.T("recipe-page-hint", "◄ Recipe {0}/{1} ►  (Shift+Scroll)", pageIndex + 1, pages.Count) + "\n",
                        CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment)));
                }
                components.Add(new ClearFloatTextComponent(capi, 2));
                components.AddRange(pages[pageIndex]);
            }

            ComposeAndPlace(components.ToArray());
        }

        void ComposeAndPlace(RichTextComponentBase[] components)
        {
            double contentWidth = Width - 2 * Pad;
            int maxInner = MaxHeight - 2 * Pad;
            int minInner = MinHeight - 2 * Pad;

            // Compose once at full height to measure the content, then shrink + reposition in place
            // via ReCompose (no second CreateCompo, so we never get a duplicate popup).
            ElementBounds textBounds = ElementBounds.Fixed(0, 0, contentWidth, maxInner);
            ElementBounds clipBounds = textBounds.ForkBoundingParent();
            ElementBounds bgBounds = clipBounds.ForkBoundingParent(Pad, Pad, Pad, Pad);
            ElementBounds dialogBounds = bgBounds.ForkBoundingParent()
                .WithAlignment(EnumDialogArea.None)
                .WithFixedPosition(0, 0);

            SingleComposer?.Dispose();
            SingleComposer = capi.Gui
                .CreateCompo("picorecipes-hover", dialogBounds)
                // Semi-transparent panel. It is the topmost dialog, so nothing draws over it; the
                // world/inventory behind only shows faintly through.
                .AddGameOverlay(bgBounds, new double[] { 0, 0, 0, 0.8 })
                .BeginChildElements(bgBounds)
                    .BeginClip(clipBounds)
                        .AddRichtext(components, textBounds, "richtext")
                    .EndClip()
                .EndChildElements()
                .Compose(false);

            double contentHeight = SingleComposer.GetRichtext("richtext").Bounds.fixedHeight;
            int innerH = (int)GameMath.Clamp(contentHeight, minInner, maxInner);
            double totalH = innerH + 2 * Pad;

            float scale = Math.Max(0.1f, RuntimeEnv.GUIScale);
            double ax = lastAnchorX / scale;
            double ay = lastAnchorY / scale;
            double screenW = capi.Render.FrameWidth / scale;
            double screenH = capi.Render.FrameHeight / scale;

            // Prefer the left of the cursor; flip to the right if there is no room.
            double x = ax - Width - 20;
            if (x < 10) x = ax + 26;
            x = GameMath.Clamp(x, 10, Math.Max(10, screenW - Width - 10));
            double y = GameMath.Clamp(ay - totalH / 2, 10, Math.Max(10, screenH - totalH - 10));

            textBounds.fixedHeight = innerH;
            dialogBounds.fixedX = x;
            dialogBounds.fixedY = y;
            SingleComposer.ReCompose();
        }
    }
}
