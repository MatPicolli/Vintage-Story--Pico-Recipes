using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace PicoRecipes
{
    /// <summary>
    /// The JEI style item list overlay: a searchable, paged grid of every item and block
    /// in the game, docked to the right edge of the screen while an inventory is open.
    /// Left click / R shows recipes, right click / U shows usages,
    /// shift click spawns the item when playing in creative mode.
    /// </summary>
    public class GuiDialogItemList : GuiDialog
    {
        readonly PicoRecipesModSystem mod;

        const int Columns = 9;
        const int Rows = 11;
        const int SlotsPerPage = Columns * Rows;

        DummyInventory inv;
        readonly List<int> filteredIndices = new List<int>();
        string searchText = "";
        int page;

        public override string ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => true;
        public override bool UnregisterOnClose => false;
        public override EnumDialogType DialogType => EnumDialogType.Dialog;
        public override bool DisableMouseGrab => false;

        public GuiDialogItemList(ICoreClientAPI capi, PicoRecipesModSystem mod) : base(capi)
        {
            this.mod = mod;
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();

            mod.RecipeIndex.EnsureLoaded();
            if (inv == null) inv = new DummyInventory(capi, SlotsPerPage);

            ApplyFilter();
            ComposeDialog();
        }

        int PageCount => Math.Max(1, (filteredIndices.Count + SlotsPerPage - 1) / SlotsPerPage);

        void ComposeDialog()
        {
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            double gridWidth = Columns * slotSize;

            ElementBounds searchBounds = ElementBounds.Fixed(0, 30, gridWidth - 2 * 34 - 12, 30);
            ElementBounds prevBounds = ElementBounds.Fixed(gridWidth - 2 * 34 - 4, 30, 30, 30);
            ElementBounds nextBounds = ElementBounds.Fixed(gridWidth - 34, 30, 30, 30);
            ElementBounds pageLabelBounds = ElementBounds.Fixed(0, 68, gridWidth, 20);
            ElementBounds gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 94, Columns, Rows);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(searchBounds, prevBounds, nextBounds, pageLabelBounds, gridBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            var gridElem = new GuiElementClickableSlotGrid(capi, inv, Columns, gridBounds)
            {
                OnSlotClicked = OnSlotClicked,
                OnScrollPage = delta => ChangePage(delta)
            };

            SingleComposer?.Dispose();
            SingleComposer = capi.Gui
                .CreateCompo("picorecipes-itemlist", dialogBounds)
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar(Lang.Get("picorecipes:itemlist-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddTextInput(searchBounds, OnSearchTextChanged, CairoFont.WhiteSmallishText(), "searchbox")
                    .AddSmallButton("<", () => ChangePage(-1), prevBounds)
                    .AddSmallButton(">", () => ChangePage(1), nextBounds)
                    .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), pageLabelBounds, "pagelabel")
                    .AddInteractiveElement(gridElem, "slotgrid")
                .EndChildElements()
                .Compose();

            var searchbox = SingleComposer.GetTextInput("searchbox");
            searchbox.SetPlaceHolderText(Lang.Get("picorecipes:search-placeholder"));
            searchbox.SetValue(searchText, false);

            FillPage();
        }

        void OnSearchTextChanged(string text)
        {
            text ??= "";
            if (text == searchText) return;

            searchText = text;
            page = 0;
            ApplyFilter();
            FillPage();
        }

        void ApplyFilter()
        {
            filteredIndices.Clear();

            var index = mod.RecipeIndex;
            string needle = searchText.Trim().ToLowerInvariant();

            // JEI style @mod filter: "@game axe" or "@game" alone
            string domainFilter = null;
            if (needle.StartsWith("@"))
            {
                int space = needle.IndexOf(' ');
                domainFilter = space > 0 ? needle.Substring(1, space - 1) : needle.Substring(1);
                needle = space > 0 ? needle.Substring(space + 1).Trim() : "";
            }

            for (int i = 0; i < index.AllStacks.Count; i++)
            {
                if (domainFilter != null && domainFilter.Length > 0)
                {
                    string domain = index.AllStacks[i].Collectible.Code.Domain;
                    if (domain == null || !domain.StartsWithOrdinal(domainFilter)) continue;
                }

                if (needle.Length == 0 || index.SearchTexts[i].Contains(needle))
                {
                    filteredIndices.Add(i);
                }
            }
        }

        bool ChangePage(int delta)
        {
            int newPage = GameMath.Clamp(page + delta, 0, PageCount - 1);
            if (newPage != page)
            {
                page = newPage;
                FillPage();
            }
            return true;
        }

        void FillPage()
        {
            if (inv == null || SingleComposer == null) return;

            page = GameMath.Clamp(page, 0, PageCount - 1);
            int start = page * SlotsPerPage;

            var index = mod.RecipeIndex;
            for (int i = 0; i < SlotsPerPage; i++)
            {
                int filteredPos = start + i;
                ItemStack wanted = filteredPos < filteredIndices.Count
                    ? index.AllStacks[filteredIndices[filteredPos]]
                    : null;

                if (inv[i].Itemstack != wanted)
                {
                    inv[i].Itemstack = wanted;
                    inv.DirtySlots.Add(i);  // so the grid refreshes the slot's overlays
                }
            }

            SingleComposer.GetDynamicText("pagelabel")?.SetNewText(
                $"{page + 1} / {PageCount}  ({filteredIndices.Count})"
            );
        }

        void OnSlotClicked(int slotId, EnumMouseButton button, bool shiftPressed)
        {
            ItemStack stack = inv[slotId]?.Itemstack;
            if (stack == null) return;

            if (shiftPressed && button == EnumMouseButton.Left)
            {
                mod.RequestGiveStack(stack);
                return;
            }

            if (button == EnumMouseButton.Left)
            {
                mod.BrowserDialog.ShowRecipesFor(stack);
            }
            else if (button == EnumMouseButton.Right)
            {
                mod.BrowserDialog.ShowUsagesFor(stack);
            }
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            // Covers escape, the title bar close button and hotkeys alike; the mod system
            // ignores this while it is opening/closing the overlay programmatically.
            mod.OnItemListClosedByUser();
        }
    }
}
