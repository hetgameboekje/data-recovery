using System.ComponentModel;
using RecoveryUI.Infrastructure;

namespace RecoveryUI.Views.Controls;

/// <summary>One step of the guided workflow on the Home page: number, title, explanation, button.</summary>
public partial class StepCard : UserControl
{
    public event EventHandler? ActionClicked;

    public StepCard()
    {
        InitializeComponent();
        BackColor = UiTheme.Card;
        lblNumber.BackColor = UiTheme.Accent;
        lblNumber.ForeColor = Color.White;
        lblTitle.Font = UiTheme.HeadingFont;
        lblDescription.ForeColor = UiTheme.Muted;
        UiTheme.Primary(btnAction);
        btnAction.Click += (_, _) => ActionClicked?.Invoke(this, EventArgs.Empty);
    }

    [Category("Appearance"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Number { get => lblNumber.Text; set => lblNumber.Text = value; }

    [Category("Appearance"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Title { get => lblTitle.Text; set => lblTitle.Text = value; }

    [Category("Appearance"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Description { get => lblDescription.Text; set => lblDescription.Text = value; }

    [Category("Appearance"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string ActionText { get => btnAction.Text; set => btnAction.Text = value; }

    [Category("Behavior"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool ActionEnabled
    {
        get => btnAction.Enabled;
        set
        {
            btnAction.Enabled = value;
            if (value) UiTheme.Primary(btnAction); else UiTheme.Secondary(btnAction);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(UiTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
