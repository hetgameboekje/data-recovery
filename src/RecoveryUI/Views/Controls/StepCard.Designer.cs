namespace RecoveryUI.Views.Controls;

partial class StepCard
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
        lblNumber = new Label();
        lblTitle = new Label();
        lblDescription = new Label();
        btnAction = new Button();
        SuspendLayout();
        //
        // lblNumber
        //
        lblNumber.Font = new Font("Segoe UI Semibold", 12F);
        lblNumber.Location = new Point(16, 16);
        lblNumber.Name = "lblNumber";
        lblNumber.Size = new Size(32, 32);
        lblNumber.Text = "1";
        lblNumber.TextAlign = ContentAlignment.MiddleCenter;
        //
        // lblTitle
        //
        lblTitle.AutoEllipsis = true;
        lblTitle.Location = new Point(58, 18);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new Size(212, 28);
        lblTitle.Text = "Title";
        //
        // lblDescription
        //
        lblDescription.Location = new Point(16, 58);
        lblDescription.Name = "lblDescription";
        lblDescription.Size = new Size(254, 84);
        lblDescription.Text = "Description";
        //
        // btnAction
        //
        btnAction.Location = new Point(16, 150);
        btnAction.Name = "btnAction";
        btnAction.Size = new Size(120, 34);
        btnAction.Text = "Start";
        btnAction.UseVisualStyleBackColor = false;
        //
        // StepCard
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(lblNumber);
        Controls.Add(lblTitle);
        Controls.Add(lblDescription);
        Controls.Add(btnAction);
        Margin = new Padding(0, 0, 16, 16);
        Name = "StepCard";
        Size = new Size(286, 200);
        ResumeLayout(false);
    }

    #endregion

    private Label lblNumber;
    private Label lblTitle;
    private Label lblDescription;
    private Button btnAction;
}
