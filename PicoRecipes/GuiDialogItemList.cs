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
    /// The JEI style item list overlay: a paged grid of every item and block in the game shown as
    /// a semi-transparent panel on the right of the screen, with a search box docked at the bottom
    /// center (JEI style). Left click / R shows recipes, right click / U shows usages, shift click
    /// spawns the item in creative mode, and hovering shows a compact recipe preview.
    /// </summary>
    public class GuiDialogItemList : GuiDialog
    {
        readonly PicoRecipesModSystem mod;

        const string GridKey = "picorecipes-grid";
        const string SearchKey = "picorecipes-search";

        const int Columns = 9;
        const int Rows = 12;
        const int SlotsPerPage = Columns * Rows;

        DummyInventory inv;
        GuiElementClickableSlotGrid gridElem;
        readonly List<int> filteredIndices = new List<int>();
        string searchText = "";
        int page;

        // Hover-preview tracking
        int lastHoverSlotId = -1;
        float hoverAccumSec;

        public override string ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => true;
        public override bool UnregisterOnClose => false;
        public override EnumDialogType DialogType => EnumDialogType.Dialog;
        public override bool DisableMouseGrab => false;

        // Sits above the minimap HUD (0.07) but below the inventory (0.2); it is docked right so it
        // does not overlap the centered inventory anyway.
        public override double DrawOrder => 0.13;

        public GuiDialogItemList(ICoreClientAPI capi, PicoRecipesModSystem mod) : base(capi)
        {
            this.mod = mod;
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();

            mod.RecipeIndex.EnsureLoaded();
            if (inv == null) inv = new DummyInventory(capi, SlotsPerPage);

            // Reset the search each time the overlay opens, but keep the page you were on.
            searchText = "";

            ApplyFilter();
            ComposeDialog();
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            mod.HoverDialog.Hide();
            lastHoverSlotId = -1;
            mod.OnItemListClosedByUser();
        }

        int PageCount => Math.Max(1, (filteredIndices.Count + SlotsPerPage - 1) / SlotsPerPage);

        void ComposeDialog()
        {
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            double gridWidth = Columns * slotSize;

            // ----- Right-docked, semi-transparent item grid panel -----
            ElementBounds prevBounds = ElementBounds.Fixed(0, 0, gridWidth / 2 - 4, 26);
            ElementBounds nextBounds = ElementBounds.Fixed(gridWidth / 2 + 4, 0, gridWidth / 2 - 4, 26);
            ElementBounds pageLabelBounds = ElementBounds.Fixed(0, 30, gridWidth, 20);
            ElementBounds gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 56, Columns, Rows);

            ElementBounds gridPanelBg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            gridPanelBg.BothSizing = ElementSizing.FitToChildren;
            gridPanelBg.WithChildren(prevBounds, nextBounds, pageLabelBounds, gridBounds);

            ElementBounds gridDialogBounds = gridPanelBg.ForkBoundingParent()
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            gridElem = new GuiElementClickableSlotGrid(capi, inv, Columns, gridBounds)
            {
                OnSlotClicked = OnSlotClicked,
                OnScrollPage = delta => ChangePage(delta)
            };

            Composers[GridKey]?.Dispose();
            Composers[GridKey] = capi.Gui
                .CreateCompo(GridKey, gridDialogBounds)
                .AddGameOverlay(gridPanelBg, new double[] { 0, 0, 0, 0.35 })
                .BeginChildElements(gridPanelBg)
                    .AddSmallButton("<", () => ChangePage(-1), prevBounds)
                    .AddSmallButton(">", () => ChangePage(1), nextBounds)
                    .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), pageLabelBounds, "pagelabel")
                    .AddInteractiveElement(gridElem, "slotgrid")
                .EndChildElements()
                .Compose(false);

            // ----- Bottom-center search box -----
            ElementBounds searchBounds = ElementBounds.Fixed(0, 0, 320, 30);
            ElementBounds searchPanelBg = ElementBounds.Fill.WithFixedPadding(6);
            searchPanelBg.BothSizing = ElementSizing.FitToChildren;
            searchPanelBg.WithChildren(searchBounds);

            // Centered at the bottom, lifted above the hotbar so the two do not overlap.
            ElementBounds searchDialogBounds = searchPanelBg.ForkBoundingParent()
                .WithAlignment(EnumDialogArea.CenterBottom)
                .WithFixedAlignmentOffset(0, -95);

            Composers[SearchKey]?.Dispose();
            Composers[SearchKey] = capi.Gui
                .CreateCompo(SearchKey, searchDialogBounds)
                .AddGameOverlay(searchPanelBg, new double[] { 0, 0, 0, 0.45 })
                .BeginChildElements(searchPanelBg)
                    .AddTextInput(searchBounds, OnSearchTextChanged, CairoFont.WhiteSmallishText(), "searchbox")
                .EndChildElements()
                .Compose(false);

            var searchbox = Composers[SearchKey].GetTextInput("searchbox");
            searchbox.SetPlaceHolderText(Loc.T("search-placeholder", "Search... (@mod filters by mod)"));
            searchbox.SetValue(searchText, false);
            // Do not steal keyboard focus on open: the search box only accepts typing once clicked.
            Composers[SearchKey].UnfocusOwnElements();

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
            if (inv == null || !Composers.ContainsKey(GridKey)) return;

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

            Composers[GridKey].GetDynamicText("pagelabel")?.SetNewText(
                $"{page + 1} / {PageCount}  ({filteredIndices.Count})"
            );

            // The visible items changed; drop any stale hover preview.
            lastHoverSlotId = -1;
            mod.HoverDialog.Hide();
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

        /// <summary>Called every frame by the mod system to drive the hover preview popup.</summary>
        public void UpdateHoverPreview(float dt)
        {
            if (gridElem == null || !IsOpened()) return;

            int hoverId = gridElem.hoverSlotId;
            ItemStack stack = hoverId >= 0 && hoverId < inv.Count ? inv[hoverId]?.Itemstack : null;

            if (hoverId != lastHoverSlotId)
            {
                lastHoverSlotId = hoverId;
                hoverAccumSec = 0;
                mod.HoverDialog.Hide();
                return;
            }

            if (stack == null) return;

            // Small delay so quick mouse sweeps do not spam recipe lookups.
            hoverAccumSec += dt;
            if (hoverAccumSec < 0.25f) return;

            double anchorX, anchorY;
            if (hoverId < gridElem.SlotBounds.Length)
            {
                var sb = gridElem.SlotBounds[hoverId];
                anchorX = sb.renderX;
                anchorY = sb.renderY + sb.OuterHeight / 2;
            }
            else
            {
                anchorX = capi.Input.MouseX;
                anchorY = capi.Input.MouseY;
            }

            mod.HoverDialog.ShowFor(stack, anchorX, anchorY);
        }
    }
}
