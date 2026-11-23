using System.Windows;
using System.Windows.Controls;

namespace Core.Views;

internal sealed class MenuColumns : Panel
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows), typeof(int), typeof(MenuColumns),
        new FrameworkPropertyMetadata(int.MaxValue, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CellsProperty = DependencyProperty.Register(nameof(Cells), typeof(int), typeof(MenuColumns),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int Rows { get => (int)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }

    public int Cells { get => (int)GetValue(CellsProperty); set => SetValue(CellsProperty, value); }

    private int CellCount => Math.Min(Cells, InternalChildren.Count);

    private int RowCount => Math.Min(Math.Max(1, Rows), Math.Max(1, CellCount));

    private int ColumnCount => (CellCount + RowCount - 1) / RowCount;

    protected override Size MeasureOverride(Size available)
    {
        double cellWidth = 0, cellHeight = 0, restWidth = 0, restHeight = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (i < CellCount)
            {
                cellWidth = Math.Max(cellWidth, child.DesiredSize.Width);
                cellHeight = Math.Max(cellHeight, child.DesiredSize.Height);
            }
            else
            {
                restWidth = Math.Max(restWidth, child.DesiredSize.Width);
                restHeight += child.DesiredSize.Height;
            }
        }
        return new Size(Math.Max(cellWidth * ColumnCount, restWidth), cellHeight * RowCount + restHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        int columns = Math.Max(1, ColumnCount), rows = RowCount, cells = CellCount;
        double cellHeight = 0;
        for (int i = 0; i < cells; i++) cellHeight = Math.Max(cellHeight, InternalChildren[i].DesiredSize.Height);
        double cellWidth = final.Width / columns;
        for (int i = 0; i < cells; i++)
            InternalChildren[i].Arrange(new Rect(i / rows * cellWidth, i % rows * cellHeight, cellWidth, cellHeight));
        double y = cells > 0 ? rows * cellHeight : 0;
        for (int i = cells; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
        return final;
    }
}
