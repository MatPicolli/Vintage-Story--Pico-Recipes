using System;
using System.IO;
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
    /// Client side it provides the item list overlay and the recipe browser. The (optional)
    /// server side part only handles creative mode item spawning (JEI "cheat mode").
    /// </summary>
    public class PicoRecipesModSystem : ModSystem
    {
        public const string ChannelName = "picorecipes";
        public const string HotkeyToggleOverlay = "picorecipestoggle";
        public const string HotkeyRecipes = "picorecipesrecipes";
        public const string HotkeyUsages = "picorecipesusages";

        ICoreClientAPI capi;
        IClientNetworkChannel clientChannel;

        public RecipeIndex RecipeIndex { get; private set; }
        public GuiDialogItemList ItemListDialog { get; private set; }
        public GuiDialogRecipeBrowser BrowserDialog { get; private set; }

        // Auto show/hide state for the overlay
        bool userHidOverlay;
        bool autoOpened;
        bool programmaticChange;

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
                fromPlayer?.SendIngameError("picorecipes-notcreative", Lang.Get("picorecipes:cheat-not-creative"));
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

            RecipeIndex = new RecipeIndex(api);
            BrowserDialog = new GuiDialogRecipeBrowser(api, this);
            ItemListDialog = new GuiDialogItemList(api, this);
            api.Gui.RegisterDialog(BrowserDialog, ItemListDialog);

            api.Input.RegisterHotKey(HotkeyToggleOverlay, Lang.Get("picorecipes:hotkey-toggle-overlay"),
                GlKeys.O, HotkeyType.GUIOrOtherControls, ctrlPressed: true);
            api.Input.SetHotKeyHandler(HotkeyToggleOverlay, OnToggleOverlayHotkey);

            api.Input.RegisterHotKey(HotkeyRecipes, Lang.Get("picorecipes:hotkey-show-recipes"),
                GlKeys.R, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler(HotkeyRecipes, _ => ShowForHoveredStack(usages: false));

            api.Input.RegisterHotKey(HotkeyUsages, Lang.Get("picorecipes:hotkey-show-usages"),
                GlKeys.U, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler(HotkeyUsages, _ => ShowForHoveredStack(usages: true));

            api.Event.RegisterGameTickListener(OnClientTick, 150);
        }

        bool OnToggleOverlayHotkey(KeyCombination comb)
        {
            programmaticChange = true;
            if (ItemListDialog.IsOpened())
            {
                ItemListDialog.TryClose();
                if (autoOpened) userHidOverlay = true;
            }
            else
            {
                ItemListDialog.TryOpen();
                userHidOverlay = false;
                autoOpened = false;  // manually opened: keep it open even without an inventory
            }
            programmaticChange = false;
            return true;
        }

        bool ShowForHoveredStack(bool usages)
        {
            ItemStack stack = capi.World?.Player?.InventoryManager?.CurrentHoveredSlot?.Itemstack;
            if (stack == null) return false;

            if (usages) BrowserDialog.ShowUsagesFor(stack);
            else BrowserDialog.ShowRecipesFor(stack);
            return true;
        }

        /// <summary>
        /// JEI behavior: the item list shows up automatically whenever an inventory-like
        /// dialog is on screen, and goes away when they are all closed.
        /// </summary>
        void OnClientTick(float dt)
        {
            if (capi.World?.Player == null || ItemListDialog == null) return;

            bool inventoryOpen = capi.Gui.OpenedGuis.Any(IsInventoryLikeDialog);

            if (inventoryOpen && !ItemListDialog.IsOpened() && !userHidOverlay)
            {
                programmaticChange = true;
                autoOpened = ItemListDialog.TryOpen(false);
                programmaticChange = false;
            }
            else if (!inventoryOpen)
            {
                if (ItemListDialog.IsOpened() && autoOpened)
                {
                    programmaticChange = true;
                    ItemListDialog.TryClose();
                    programmaticChange = false;
                    autoOpened = false;
                }
                userHidOverlay = false;
            }
        }

        bool IsInventoryLikeDialog(object dlg)
        {
            if (dlg is not GuiDialog dialog) return false;
            if (dialog == ItemListDialog || dialog == BrowserDialog) return false;
            if (dialog is GuiDialogBlockEntity) return true;

            string name = dialog.GetType().Name;
            return name.Contains("Inventory") || name.Contains("Character");
        }

        /// <summary>Called when the user closed the overlay themselves (e.g. escape key).</summary>
        public void OnItemListClosedByUser()
        {
            if (!programmaticChange && autoOpened) userHidOverlay = true;
        }

        /// <summary>JEI "cheat mode": shift-click in the item list gives the item (creative mode only).</summary>
        public void RequestGiveStack(ItemStack stack)
        {
            if (stack == null) return;

            if (capi.World.Player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                capi.TriggerIngameError(this, "picorecipes-notcreative", Lang.Get("picorecipes:cheat-not-creative"));
                return;
            }

            if (clientChannel == null || !clientChannel.Connected)
            {
                capi.TriggerIngameError(this, "picorecipes-noserver", Lang.Get("picorecipes:cheat-no-server"));
                return;
            }

            ItemStack give = stack.Clone();
            give.StackSize = 1;
            clientChannel.SendPacket(new GiveStackRequest { StackBytes = give.ToBytes() });
        }

        #endregion
    }
}
