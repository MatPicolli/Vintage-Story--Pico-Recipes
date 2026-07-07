using System;
using Cairo;
using Vintagestory.API.Client;

namespace PicoRecipes
{
    /// <summary>
    /// Draws a single voxel recipe layer (clay forming / knapping / smithing) as a small pixel grid,
    /// inline in the richtext flow. Filled cells are the recipe's voxels; empty cells are faint so the
    /// overall footprint reads as a shape. An optional step number is drawn above the grid so a series
    /// of these components reads as a "build layer by layer" sequence. Because each component wraps
    /// itself onto the next line when it does not fit, a whole multi-layer recipe fits both the wide
    /// recipe browser and the narrow hover tooltip without any per-window tuning.
    /// </summary>
    public class VoxelPatternComponent : RichTextComponentBase
    {
        readonly ICoreClientAPI capi;
        readonly bool[,] grid;
        readonly int rows;
        readonly int cols;
        readonly double cellUnscaled;

        readonly string label;
        readonly CairoFont labelFont;
        readonly double labelHeightUnscaled;

        // Keep the whole grid within this unscaled width so it can never be wider than the panel it
        // is rendered in (which would otherwise make the richtext layout loop forever trying to wrap).
        const double DefaultMaxTotalUnscaled = 90.0;
        const double DefaultMaxCellUnscaled = 6.0;

        /// <param name="label">Optional step number drawn above the grid (e.g. "1"). Null hides it.</param>
        /// <param name="maxCellUnscaled">Largest a single voxel cell may be, in unscaled pixels.</param>
        /// <param name="maxTotalUnscaled">Largest the whole grid may be across, in unscaled pixels.</param>
        public VoxelPatternComponent(ICoreClientAPI capi, bool[,] grid, string label = null,
            double maxCellUnscaled = DefaultMaxCellUnscaled, double maxTotalUnscaled = DefaultMaxTotalUnscaled) : base(capi)
        {
            this.capi = capi;
            this.grid = grid;
            this.label = string.IsNullOrEmpty(label) ? null : label;
            rows = grid.GetLength(0);
            cols = grid.GetLength(1);

            cellUnscaled = cols > 0 ? Math.Min(maxCellUnscaled, maxTotalUnscaled / cols) : maxCellUnscaled;

            if (this.label != null)
            {
                labelFont = CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment);
                labelFont.UnscaledFontSize = 12;
                labelHeightUnscaled = 13;
            }

            double cell = GuiElement.scaled(cellUnscaled);
            double gridW = cols * cell;
            double gridH = rows * cell;
            double labelH = GuiElement.scaled(labelHeightUnscaled);
            // Reserve enough width for a two-digit step number over a very narrow grid.
            double width = Math.Max(gridW, this.label != null ? GuiElement.scaled(this.label.Length * 7 + 2) : 0);

            BoundsPerLine = new[] { new LineRectangled(0, 0, width, gridH + labelH) };
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
            double labelH = GuiElement.scaled(labelHeightUnscaled);

            if (label != null)
            {
                labelFont.SetupContext(ctx);
                ctx.SetSourceRGBA(labelFont.Color[0], labelFont.Color[1], labelFont.Color[2], labelFont.Color[3]);
                ctx.MoveTo(x0, y0 + labelH - GuiElement.scaled(3));
                ctx.ShowText(label);
            }

            double gridY = y0 + labelH;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    ctx.Rectangle(x0 + c * cell + gap, gridY + r * cell + gap, cell - 2 * gap, cell - 2 * gap);
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
