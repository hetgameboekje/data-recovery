namespace RecoveryUI.Views.Controls;

partial class StatTile
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    #region Component Designer generated code

    private void InitializeComponent()
    {
        lblTitle = new Label();
        lblValue = new Label();
        lblCaption = new Label();
        SuspendLayout();
        //
        // lblTitle
        //
        lblTitle.AutoSize = true;
        lblTitle.Location = new Point(14, 10);
        lblTitle.Name = "lblTitle";
        lblTitle.Text = "Title";
        //
        // lblValue
        //
        lblValue.AutoSize = true;
        lblValue.Location = new Point(12, 30);
        lblValue.Name = "lblValue";
        lblValue.Text = "—";
        //
        // lblCaption
        //
        lblCaption.AutoEllipsis = true;
        lblCaption.Location = new Point(14, 68);
        lblCaption.Name = "lblCaption";
        lblCaption.Size = new Size(150, 34);
        lblCaption.Text = "Caption";
        //
        // StatTile
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(lblTitle);
        Controls.Add(lblValue);
        Controls.Add(lblCaption);
        Margin = new Padding(0, 0, 12, 12);
        Name = "StatTile";
        Size = new Size(172, 108);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblTitle;
    private Label lblValue;
    private Label lblCaption;
}
