using System;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace PicoRecipes
{
    [ProtoContract]
    public class GiveStackRequest
    {
        [ProtoMember(1)]
        public byte[] StackBytes;
    }

    /// <summary>
    /// Pico Recipes: a "Just Enough Items" (JEI) style item and recipe browser for Vintage Story.
    ///
    /// Client side it provides the item list overlay, the recipe browser and the hover preview.
    /// The (optional) server side part only handles creative mode item spawning (JEI "cheat mode").
    /// </summary>
    public class PicoRecipesModSystem : ModSystem
    {
        public const string ChannelName = "picorecipes";
        public const string HotkeyToggle = "picorecipestoggle";
        public const string HotkeyRecipes = "picorecipesrecipes";
        public const string HotkeyUsages = "picorecipesusages";
        public const string EnabledSetting = "picorecipesEnabled";

        ICoreClientAPI capi;
        IClientNetworkChannel clientChannel;

        public RecipeIndex RecipeIndex { get; private set; }
        public GuiDialogItemList ItemListDialog { get; private set; }
        public GuiDialogRecipeBrowser BrowserDialog { get; private set; }
        public GuiDialogRecipeHover HoverDialog { get; private set; }

        /// <summary>Master on/off switch toggled with Ctrl+O and persisted across sessions.</summary>
        public bool Enabled { get; private set; } = true;

        // Minimap hiding
        GuiDialog minimapHud;
        bool minimapHiddenByUs;
        bool overlayWasVisible;

        public override void Start(ICoreAPI api)
        {
            api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<GiveStackRequest>();
        }

        #region Server: creative item spawning

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Network
                .GetChannel(ChannelName)
                .SetMessageHandler<GiveStackRequest>((fromPlayer, msg) => OnGiveStackRequest(api, fromPlayer, msg));
        }

        void OnGiveStackRequest(ICoreServerAPI sapi, IServerPlayer fromPlayer, GiveStackRequest msg)
        {
            if (fromPlayer?.WorldData?.CurrentGameMode != EnumGameMode.Creative)
            {
                fromPlayer?.SendIngameError("picorecipes-notcreative", Loc.T("cheat-not-creative", "You must be in creative mode to spawn items."));
                return;
            }
            if (msg?.StackBytes == null) return;

            try
            {
                var stack = new ItemStack(msg.StackBytes);
                if (!stack.ResolveBlockOrItem(sapi.World)) return;

                stack.StackSize = Math.Min(Math.Max(1, stack.StackSize), stack.Collectible.MaxStackSize);
                fromPlayer.InventoryManager.TryGiveItemstack(stack, true);
            }
            catch (Exception e)
            {
                sapi.Logger.Warning("[picorecipes] Failed to give itemstack: {0}", e.Message);
            }
        }

        #endregion

        #region Client

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            clientChannel = api.Network.GetChannel(ChannelName);

            Enabled = api.Settings.Bool.Get(EnabledSetting, true);

            RecipeIndex = new RecipeIndex(api);
            BrowserDialog = new GuiDialogRecipeBrowser(api, this);
            HoverDialog = new GuiDialogRecipeHover(api, this);
            ItemListDialog = new GuiDialogItemList(api, this);
            api.Gui.RegisterDialog(BrowserDialog, HoverDialog, ItemListDialog);

            api.Input.RegisterHotKey(HotkeyToggle, Loc.T("hotkey-toggle", "Pico Recipes: Enable/disable overlay"),
                GlKeys.O, HotkeyType.GUIOrOtherControls, ctrlPressed: true);
            api.Input.SetHotKeyHandler(HotkeyToggle, OnToggleHotkey);

            api.Input.RegisterHotKey(HotkeyRecipes, Loc.T("hotkey-show-recipes", "Pico Recipes: Show recipes for hovered item"),
                GlKeys.R, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler(HotkeyRecipes, _ => ShowForHoveredStack(usages: false));

            api.Input.RegisterHotKey(HotkeyUsages, Loc.T("hotkey-show-usages", "Pico Recipes: Show usages of hovered item"),
                GlKeys.U, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler(HotkeyUsages, _ => ShowForHoveredStack(usages: true));

            // Fast tick so the overlay appears in lockstep with the inventory (no visible delay).
            api.Event.RegisterGameTickListener(OnClientTick, 20);
        }

        bool OnToggleHotkey(KeyCombination comb)
        {
            Enabled = !Enabled;
            capi.Settings.Bool.Set(EnabledSetting, Enabled, false);

            if (!Enabled)
            {
                if (ItemListDialog.IsOpened()) ItemListDialog.TryClose();
                HoverDialog.Hide();
                RestoreMinimap();
            }

            capi.ShowChatMessage(Enabled
                ? Loc.T("enabled-msg", "Pico Recipes overlay enabled")
                : Loc.T("disabled-msg", "Pico Recipes overlay disabled"));
            return true;
        }

        bool ShowForHoveredStack(bool usages)
        {
            // Prefer the item hovered in our own list (which suppresses the vanilla tooltip and so
            // does not set CurrentHoveredSlot), then fall back to a real hovered inventory slot.
            ItemStack stack = ItemListDialog.GetHoveredStack()
                ?? capi.World?.Player?.InventoryManager?.CurrentHoveredSlot?.Itemstack;
            if (stack == null) return false;

            if (usages) BrowserDialog.ShowUsagesFor(stack);
            else BrowserDialog.ShowRecipesFor(stack);
            return true;
        }

        /// <summary>
        /// JEI behavior: the item list shows up automatically while the player inventory screen is
        /// open by itself (when the mod is enabled), and goes away when it closes or when any other
        /// container (chest, crate, ...) is open.
        /// </summary>
        void OnClientTick(float dt)
        {
            if (capi.World?.Player == null || ItemListDialog == null) return;

            // Start building the item index in the background as soon as the world is ready, so it
            // is done (or nearly) by the time the overlay is first opened. Idempotent.
            RecipeIndex.EnsureLoaded();

            // The overlay is only for the plain player inventory: it appears when the inventory
            // screen is open on its own, and never when a chest, crate or any other container is
            // open (even if that container also shows the backpack).
            bool playerInventoryOpen = capi.Gui.OpenedGuis.Any(IsPlayerInventoryDialog);
            bool containerOpen = capi.Gui.OpenedGuis.Any(IsExternalContainerDialog);
            bool shouldShow = Enabled && playerInventoryOpen && !containerOpen;

            if (shouldShow && !ItemListDialog.IsOpened())
            {
                ItemListDialog.TryOpen(false);
            }
            else if (!shouldShow && ItemListDialog.IsOpened())
            {
                ItemListDialog.TryClose();
            }

            // The recipe browser is always opened from the overlay, so close it whenever the overlay
            // should no longer be shown (inventory closed, or a container was opened).
            if (!shouldShow && BrowserDialog.IsOpened())
            {
                BrowserDialog.TryClose();
            }

            bool overlayVisible = ItemListDialog.IsOpened();
            UpdateMinimap(overlayVisible);

            if (overlayVisible)
            {
                ItemListDialog.RefreshIfIndexReady();
                ItemListDialog.UpdateHoverPreview(dt);
            }
            else HoverDialog.Hide();
        }

        /// <summary>
        /// True only for the player's own inventory / character screen (the dialogs opened with the
        /// inventory key, e.g. GuiDialogInventory / GuiDialogCreativeInventory / GuiDialogCharacter).
        /// These are the only dialogs that should bring up the overlay. Block-entity containers
        /// (chests, crates, campfires, ...) are excluded first, since their dialog class name also
        /// contains "Inventory" (e.g. GuiDialogBlockEntityInventory) and would otherwise match.
        /// </summary>
        bool IsPlayerInventoryDialog(object dlg)
        {
            if (dlg is not GuiDialog dialog) return false;
            if (dialog is GuiDialogBlockEntity) return false;
            if (dialog == ItemListDialog || dialog == BrowserDialog || dialog == HoverDialog) return false;

            string name = dialog.GetType().Name;
            return name.Contains("Inventory") || name.Contains("Character");
        }

        /// <summary>
        /// True for any external container dialog (chests, crates, querns, campfires, ground storage,
        /// etc.). While one of these is open the overlay stays hidden, so it never opens on top of a
        /// container. Our own dialogs and the player inventory are not containers.
        /// </summary>
        bool IsExternalContainerDialog(object dlg)
        {
            if (dlg is not GuiDialog dialog) return false;
            if (dialog == ItemListDialog || dialog == BrowserDialog || dialog == HoverDialog) return false;
            if (IsPlayerInventoryDialog(dialog)) return false;

            return dialog is GuiDialogBlockEntity;
        }

        public void OnItemListClosedByUser()
        {
            // The overlay was closed (escape, inventory closing, or the master toggle). Make sure
            // the transient preview and the minimap are restored.
            HoverDialog?.Hide();
            RestoreMinimap();
        }

        #region Minimap hiding

        void UpdateMinimap(bool overlayVisible)
        {
            // Act only on transitions so we never fight the map mod frame to frame.
            if (overlayVisible == overlayWasVisible) return;
            overlayWasVisible = overlayVisible;

            if (overlayVisible)
            {
                GuiDialog hud = FindMinimapHud();
                if (hud != null && hud.IsOpened())
                {
                    hud.TryClose();
                    minimapHiddenByUs = true;
                }
            }
            else
            {
                RestoreMinimap();
            }
        }

        void RestoreMinimap()
        {
            if (minimapHiddenByUs)
            {
                minimapHud?.TryOpen();
                minimapHiddenByUs = false;
            }
        }

        /// <summary>
        /// Locates the vanilla minimap HUD dialog at runtime by type name, so we do not need a
        /// compile time dependency on the world map mod. The minimap is the HUD-type map dialog.
        /// </summary>
        GuiDialog FindMinimapHud()
        {
            if (minimapHud != null) return minimapHud;

            foreach (GuiDialog dlg in capi.Gui.LoadedGuis)
            {
                string name = dlg.GetType().Name;
                if (dlg.DialogType == EnumDialogType.HUD &&
                    (name.Contains("WorldMap") || name.Contains("Minimap")))
                {
                    minimapHud = dlg;
                    break;
                }
            }
            return minimapHud;
        }

        #endregion

        /// <summary>JEI "cheat mode": shift-click in the item list gives the item (creative mode only).</summary>
        public void RequestGiveStack(ItemStack stack)
        {
            if (stack == null) return;

            if (capi.World.Player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                capi.TriggerIngameError(this, "picorecipes-notcreative", Loc.T("cheat-not-creative", "You must be in creative mode to spawn items."));
                return;
            }

            if (clientChannel == null || !clientChannel.Connected)
            {
                capi.TriggerIngameError(this, "picorecipes-noserver", Loc.T("cheat-no-server", "Item spawning unavailable: Pico Recipes is not installed on the server."));
                return;
            }

            ItemStack give = stack.Clone();
            give.StackSize = 1;
            clientChannel.SendPacket(new GiveStackRequest { StackBytes = give.ToBytes() });
        }

        #endregion
    }
}
