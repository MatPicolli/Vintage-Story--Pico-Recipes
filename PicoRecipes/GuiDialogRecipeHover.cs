using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace PicoRecipes
{
    /// <summary>
    /// A small HUD popup that appears next to the item you are hovering in the item list and shows
    /// its recipe at a glance — no click required. Left/right click and the R/U hotkeys still open
    /// the full browser. This is a display-only overlay: it never grabs the mouse.
    /// </summary>
    public class GuiDialogRecipeHover : HudElement
    {
        readonly PicoRecipesModSystem mod;
        readonly RecipeComponentBuilder builder;

        ItemStack currentStack;

        const int Width = 350;
        const int MaxHeight = 320;
        const int MinHeight = 70;

        public override string ToggleKeyCombinationCode => null;
        public override bool Focusable => false;
        public override double DrawOrder => 0.29; // above the browser's inset but below tooltips/held stack

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
            Compose(anchorScreenX, anchorScreenY);

            if (!IsOpened()) TryOpen();
        }

        public void Hide()
        {
            currentStack = null;
            if (IsOpened()) TryClose();
        }

        void Compose(double anchorScreenX, double anchorScreenY)
        {
            mod.RecipeIndex.EnsureLoaded();

            RichTextComponentBase[] components = builder.BuildCompact(currentStack, out _);

            float scale = Math.Max(0.1f, RuntimeEnv.GUIScale);
            double contentWidth = Width - 2 * GuiStyle.ElementToDialogPadding;

            // First pass at max height, then shrink to fit the actual content.
            ComposeAt(components, contentWidth, MaxHeight, 0, 0);
            double contentHeight = SingleComposer.GetRichtext("richtext").Bounds.fixedHeight;
            int height = (int)GameMath.Clamp(contentHeight + 2 * GuiStyle.ElementToDialogPadding, MinHeight, MaxHeight);

            // Position: prefer to the left of the cursor, flip to the right if there is no room.
            double ax = anchorScreenX / scale;
            double ay = anchorScreenY / scale;
            double screenW = capi.Render.FrameWidth / scale;
            double screenH = capi.Render.FrameHeight / scale;

            double x = ax - Width - 20;
            if (x < 10) x = ax + 26;
            x = GameMath.Clamp(x, 10, Math.Max(10, screenW - Width - 10));

            double y = GameMath.Clamp(ay - height / 2.0, 10, Math.Max(10, screenH - height - 10));

            ComposeAt(components, contentWidth, height, x, y);
        }

        void ComposeAt(RichTextComponentBase[] components, double contentWidth, int height, double x, double y)
        {
            ElementBounds textBounds = ElementBounds.Fixed(0, 0, contentWidth, height - 2 * GuiStyle.ElementToDialogPadding);
            ElementBounds clipBounds = textBounds.ForkBoundingParent();
            ElementBounds bgBounds = clipBounds.ForkBoundingParent(GuiStyle.ElementToDialogPadding, GuiStyle.ElementToDialogPadding, GuiStyle.ElementToDialogPadding, GuiStyle.ElementToDialogPadding);

            ElementBounds dialogBounds = bgBounds.ForkBoundingParent()
                .WithAlignment(EnumDialogArea.None)
                .WithFixedPosition(x, y);

            SingleComposer?.Dispose();
            SingleComposer = capi.Gui
                .CreateCompo("picorecipes-hover", dialogBounds)
                .AddGameOverlay(bgBounds, new double[] { 0, 0, 0, 0.72 })
                .BeginChildElements(bgBounds)
                    .BeginClip(clipBounds)
                        .AddRichtext(components, textBounds, "richtext")
                    .EndClip()
                .EndChildElements()
                .Compose();
        }

        // Never dim the screen or pause: this is a passive overlay.
        public override bool ShouldReceiveRenderEvents() => IsOpened();
    }
}
