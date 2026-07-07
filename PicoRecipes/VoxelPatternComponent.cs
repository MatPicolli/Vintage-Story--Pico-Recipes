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

        const double CellUnscaled = 6.0;

        public VoxelPatternComponent(ICoreClientAPI capi, bool[,] grid) : base(capi)
        {
            this.capi = capi;
            this.grid = grid;
            rows = grid.GetLength(0);
            cols = grid.GetLength(1);

            double cell = GuiElement.scaled(CellUnscaled);
            BoundsPerLine = new[] { new LineRectangled(0, 0, cols * cell, rows * cell) };
            VerticalAlign = EnumVerticalAlign.Top;
        }

        public override EnumCalcBoundsResult CalcBounds(TextFlowPath[] flowPath, double currentLineHeight, double offsetX, double lineY, out double nextOffsetX)
        {
            TextFlowPath curfp = GetCurrentFlowPathSection(flowPath, lineY);
            offsetX += GuiElement.scaled(PaddingLeft);
            bool requireLinebreak = offsetX + BoundsPerLine[0].Width > curfp.X2;

            BoundsPerLine[0].X = requireLinebreak ? 0 : offsetX;
            BoundsPerLine[0].Y = lineY + (requireLinebreak ? currentLineHeight : 0);

            nextOffsetX = (requireLinebreak ? 0 : offsetX) + BoundsPerLine[0].Width;

            return requireLinebreak ? EnumCalcBoundsResult.Nextline : EnumCalcBoundsResult.Continue;
        }

        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            double cell = GuiElement.scaled(CellUnscaled);
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
