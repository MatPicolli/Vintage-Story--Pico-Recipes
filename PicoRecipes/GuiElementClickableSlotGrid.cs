using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PicoRecipes
{
    /// <summary>
    /// An item slot grid that never moves items around. Clicks are reported through
    /// <see cref="OnSlotClicked"/> instead (JEI style: left = recipes, right = usages),
    /// and the scroll wheel flips pages via <see cref="OnScrollPage"/>.
    /// </summary>
    public class GuiElementClickableSlotGrid : GuiElementItemSlotGrid
    {
        public delegate void SlotClickedDelegate(int slotId, EnumMouseButton button, bool shiftPressed);

        public SlotClickedDelegate OnSlotClicked;
        public System.Action<int> OnScrollPage;
        public System.Action<int> OnShiftScroll;

        readonly IInventory inv;

        public GuiElementClickableSlotGrid(ICoreClientAPI capi, IInventory inventory, int columns, ElementBounds bounds)
            : base(capi, inventory, null, columns, null, bounds)
        {
            inv = inventory;
        }

        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
        {
            if (!Bounds.ParentBounds.PointInside(args.X, args.Y)) return;

            for (int i = 0; i < SlotBounds.Length && i < renderedSlots.Count; i++)
            {
                if (!SlotBounds[i].PointInside(args.X, args.Y)) continue;

                int slotId = renderedSlots.GetKeyAtIndex(i);
                if (inv[slotId]?.Itemstack != null)
                {
                    bool shift = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft] || api.Input.KeyboardKeyState[(int)GlKeys.ShiftRight];
                    OnSlotClicked?.Invoke(slotId, args.Button, shift);
                }

                args.Handled = true;
                return;
            }
        }

        public override void SlotClick(ICoreClientAPI api, int slotId, EnumMouseButton mouseButton, bool shiftPressed, bool ctrlPressed, bool altPressed)
        {
            // Reached via keyboard navigation; never let the base implementation move items
            if (inv[slotId]?.Itemstack != null)
            {
                OnSlotClicked?.Invoke(slotId, mouseButton, shiftPressed);
            }
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
        {
            // Deliberately skip GuiElementItemSlotGridBase.OnMouseUp: it manages item
            // distribution state and network sync which do not apply to a display-only grid.
            if (IsPositionInside(args.X, args.Y))
            {
                OnMouseUpOnElement(api, args);
            }
        }

        public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
        {
            if (!Bounds.PointInside(api.Input.MouseX, api.Input.MouseY)) return;

            bool shift = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft] || api.Input.KeyboardKeyState[(int)GlKeys.ShiftRight];
            if (shift)
            {
                // Shift+wheel swaps between the different recipes shown in the hover preview.
                OnShiftScroll?.Invoke(args.delta > 0 ? -1 : 1);
            }
            else
            {
                OnScrollPage?.Invoke(args.delta > 0 ? -1 : 1);
            }
            args.SetHandled(true);
        }

        /// <summary>
        /// Track the hovered slot ourselves without calling the base implementation, which would
        /// fire TriggerOnMouseEnterSlot and make the game draw its own item tooltip on top of our
        /// hover preview. We only want our own popup, so we just update <see cref="hoverSlotId"/>
        /// (used for the slot highlight and the preview) and skip the rest.
        /// </summary>
        public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
        {
            hoverSlotId = -1;
            if (!Bounds.ParentBounds.PointInside(args.X, args.Y)) return;

            for (int i = 0; i < SlotBounds.Length && i < renderedSlots.Count; i++)
            {
                if (SlotBounds[i].PointInside(args.X, args.Y))
                {
                    hoverSlotId = renderedSlots.GetKeyAtIndex(i);
                    break;
                }
            }
        }
    }
}
