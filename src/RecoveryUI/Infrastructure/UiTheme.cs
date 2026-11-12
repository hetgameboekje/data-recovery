namespace RecoveryUI.Infrastructure;

/// <summary>Colours and control styles in one place, so every view looks the same.</summary>
public static class UiTheme
{
    public static readonly Color Accent = Color.FromArgb(0, 95, 184);
    public static readonly Color AccentHover = Color.FromArgb(0, 78, 150);
    public static readonly Color Sidebar = Color.FromArgb(243, 244, 246);
    public static readonly Color SidebarSelected = Color.FromArgb(224, 233, 245);
    public static readonly Color Surface = Color.White;
    public static readonly Color Card = Color.FromArgb(248, 249, 251);
    public static readonly Color Border = Color.FromArgb(222, 226, 232);
    public static readonly Color Text = Color.FromArgb(28, 32, 38);
    public static readonly Color Muted = Color.FromArgb(96, 104, 116);
    public static readonly Color Good = Color.FromArgb(16, 124, 65);
    public static readonly Color Warning = Color.FromArgb(157, 93, 0);

    public static Font TitleFont => new("Segoe UI Semibold", 16f);
    public static Font HeadingFont => new("Segoe UI Semibold", 11.5f);
    public static Font ValueFont => new("Segoe UI Semibold", 18f);
    public static Font SmallFont => new("Segoe UI", 8.75f);

    public static void Primary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = AccentHover;
        b.BackColor = Accent;
        b.ForeColor = Color.White;
        b.Font = new Font("Segoe UI Semibold", 10f);
        b.Cursor = Cursors.Hand;
        b.AutoSize = true;
        b.Padding = new Padding(14, 6, 14, 6);
    }

    public static void Secondary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.MouseOverBackColor = SidebarSelected;
        b.BackColor = Surface;
        b.ForeColor = Text;
        b.Cursor = Cursors.Hand;
        b.AutoSize = true;
        b.Padding = new Padding(10, 4, 10, 4);
    }

    public static void Grid(DataGridView g)
    {
        g.BackgroundColor = Surface;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.GridColor = Border;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = Card;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Card;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        g.DefaultCellStyle.SelectionBackColor = SidebarSelected;
        g.DefaultCellStyle.SelectionForeColor = Text;
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.AllowUserToResizeRows = false;
        g.ReadOnly = true;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        g.RowTemplate.Height = 28;
    }
}
