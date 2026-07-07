using System;
using Cairo;
using Vintagestory.API.Client;

namespace PicoRecipes
{
    /// <summary>
    /// Draws a voxel recipe shape (clay forming / knapping / smithing) as a small pixel grid,
    /// inline in the richtext flow. Filled cells are the recipe's voxels; empty cells are faint
    /// so the overall footprint reads as a shape. This replaces the old monospace-text rendering,
    /// which misaligned because the game font is not fixed-width.
    /// </summary>
    public class VoxelPatternComponent : RichTextComponentBase
    {
        readonly ICoreClientAPI capi;
        readonly bool[,] grid;
        readonly int rows;
        readonly int cols;
        readonly double cellUnscaled;

        // Keep the whole grid within this unscaled width so it can never be wider than the panel it
        // is rendered in (which would otherwise make the richtext layout loop forever trying to wrap).
        const double MaxTotalUnscaled = 90.0;
        const double MaxCellUnscaled = 6.0;

        public VoxelPatternComponent(ICoreClientAPI capi, bool[,] grid) : base(capi)
        {
            this.capi = capi;
            this.grid = grid;
            rows = grid.GetLength(0);
            cols = grid.GetLength(1);

            cellUnscaled = cols > 0 ? Math.Min(MaxCellUnscaled, MaxTotalUnscaled / cols) : MaxCellUnscaled;

            double cell = GuiElement.scaled(cellUnscaled);
            BoundsPerLine = new[] { new LineRectangled(0, 0, cols * cell, rows * cell) };
            VerticalAlign = EnumVerticalAlign.Top;
        }

        public override EnumCalcBoundsResult CalcBounds(TextFlowPath[] flowPath, double currentLineHeight, double offsetX, double lineY, out double nextOffsetX)
        {
            TextFlowPath curfp = GetCurrentFlowPathSection(flowPath, lineY);
            offsetX += GuiElement.scaled(PaddingLeft);

            double width = BoundsPerLine[0].Width;

            // Only wrap to the next line if we are NOT already at the start of a line. If the grid is
            // wider than the whole line we place it anyway (it may clip) rather than wrapping forever.
            bool atLineStart = offsetX <= curfp.X1 + GuiElement.scaled(1);
            bool requireLinebreak = !atLineStart && (offsetX + width > curfp.X2);

            BoundsPerLine[0].X = requireLinebreak ? curfp.X1 : offsetX;
            BoundsPerLine[0].Y = lineY + (requireLinebreak ? currentLineHeight : 0);

            nextOffsetX = BoundsPerLine[0].X + width;

            return requireLinebreak ? EnumCalcBoundsResult.Nextline : EnumCalcBoundsResult.Continue;
        }

        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            double cell = GuiElement.scaled(cellUnscaled);
            double x0 = BoundsPerLine[0].X;
            double y0 = BoundsPerLine[0].Y;
            double gap = GuiElement.scaled(1);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    ctx.Rectangle(x0 + c * cell + gap, y0 + r * cell + gap, cell - 2 * gap, cell - 2 * gap);
                    if (grid[r, c])
                    {
                        ctx.SetSourceRGBA(0.86, 0.76, 0.56, 1);   // parchment: filled voxel
                    }
                    else
                    {
                        ctx.SetSourceRGBA(1, 1, 1, 0.05);          // faint: empty cell
                    }
                    ctx.Fill();
                }
            }
        }

        public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
        {
        }
    }
}
