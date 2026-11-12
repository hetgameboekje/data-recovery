using System.ComponentModel;
using RecoveryUI.Infrastructure;

namespace RecoveryUI.Views.Controls;

/// <summary>A number with a title and a one-line explanation ("Unique data / 135 GB / one copy of everything").</summary>
public partial class StatTile : UserControl
{
    public StatTile()
    {
        InitializeComponent();
        BackColor = UiTheme.Card;
        lblTitle.ForeColor = UiTheme.Muted;
        lblValue.ForeColor = UiTheme.Text;
        lblValue.Font = UiTheme.ValueFont;
        lblCaption.ForeColor = UiTheme.Muted;
        lblCaption.Font = UiTheme.SmallFont;
    }

    [Category("Appearance"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Title { get => lblTitle.Text; set => lblTitle.Text = value; }

    [Category("Appearance"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Value { get => lblValue.Text; set => lblValue.Text = value; }

    [Category("Appearance"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Caption { get => lblCaption.Text; set => lblCaption.Text = value; }

    [Category("Appearance"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public Color ValueColor { get => lblValue.ForeColor; set => lblValue.ForeColor = value; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(UiTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
