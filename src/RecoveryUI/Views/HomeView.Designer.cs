namespace RecoveryUI.Views;

partial class HomeView
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
        layout = new TableLayoutPanel();
        lblIntro = new Label();
        flowTiles = new FlowLayoutPanel();
        tileDisks = new Controls.StatTile();
        tileFiles = new Controls.StatTile();
        tileUnique = new Controls.StatTile();
        tileExtra = new Controls.StatTile();
        tilePending = new Controls.StatTile();
        lblNext = new Label();
        flowSteps = new FlowLayoutPanel();
        stepCatalog = new Controls.StepCard();
        stepReport = new Controls.StepCard();
        stepCopy = new Controls.StepCard();
        layout.SuspendLayout();
        flowTiles.SuspendLayout();
        flowSteps.SuspendLayout();
        SuspendLayout();
        //
        // layout
        //
        layout.AutoScroll = true;
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(lblIntro, 0, 0);
        layout.Controls.Add(flowTiles, 0, 1);
        layout.Controls.Add(lblNext, 0, 2);
        layout.Controls.Add(flowSteps, 0, 3);
        layout.Dock = DockStyle.Fill;
        layout.Name = "layout";
        layout.RowCount = 5;
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // lblIntro
        //
        lblIntro.AutoSize = true;
        lblIntro.MaximumSize = new Size(900, 0);
        lblIntro.Margin = new Padding(0, 0, 0, 16);
        lblIntro.Name = "lblIntro";
        lblIntro.Text = "This toolkit reads your disks, fingerprints every file, and tells you how much is unique and how much is a copy - before anything is copied. Cataloguing is read-only: nothing on your disks is changed.";
        //
        // flowTiles
        //
        flowTiles.AutoSize = true;
        flowTiles.Controls.Add(tileDisks);
        flowTiles.Controls.Add(tileFiles);
        flowTiles.Controls.Add(tileUnique);
        flowTiles.Controls.Add(tileExtra);
        flowTiles.Controls.Add(tilePending);
        flowTiles.Margin = new Padding(0);
        flowTiles.Name = "flowTiles";
        //
        // tileDisks
        //
        tileDisks.Caption = "disks in the catalog";
        tileDisks.Name = "tileDisks";
        tileDisks.Title = "Disks";
        //
        // tileFiles
        //
        tileFiles.Caption = "files found on them";
        tileFiles.Name = "tileFiles";
        tileFiles.Title = "Files";
        //
        // tileUnique
        //
        tileUnique.Caption = "space for one copy of everything";
        tileUnique.Name = "tileUnique";
        tileUnique.Title = "Unique data";
        //
        // tileExtra
        //
        tileExtra.Caption = "taken by duplicate copies";
        tileExtra.Name = "tileExtra";
        tileExtra.Title = "Extra copies";
        //
        // tilePending
        //
        tilePending.Caption = "not fingerprinted yet";
        tilePending.Name = "tilePending";
        tilePending.Title = "Not yet decided";
        //
        // lblNext
        //
        lblNext.AutoSize = true;
        lblNext.Margin = new Padding(0, 8, 0, 16);
        lblNext.MaximumSize = new Size(900, 0);
        lblNext.Name = "lblNext";
        lblNext.Text = "Next step: …";
        //
        // flowSteps
        //
        flowSteps.AutoSize = true;
        flowSteps.Controls.Add(stepCatalog);
        flowSteps.Controls.Add(stepReport);
        flowSteps.Controls.Add(stepCopy);
        flowSteps.Margin = new Padding(0);
        flowSteps.Name = "flowSteps";
        //
        // stepCatalog
        //
        stepCatalog.ActionText = "Catalog disks";
        stepCatalog.Description = "Plug in a disk and let the toolkit list and fingerprint its files. Do this for every disk you want to combine, including disks in another PC.";
        stepCatalog.Name = "stepCatalog";
        stepCatalog.Number = "1";
        stepCatalog.Title = "Catalog your disks";
        //
        // stepReport
        //
        stepReport.ActionText = "Open report";
        stepReport.Description = "See per disk and per file type how much is unique and how much is a duplicate, and how much space one clean copy needs.";
        stepReport.Name = "stepReport";
        stepReport.Number = "2";
        stepReport.Title = "See what is duplicated";
        //
        // stepCopy
        //
        stepCopy.ActionText = "Copy";
        stepCopy.Description = "Check the plan, then copy one copy of everything to your backup disks: newest version in the main folders, duplicates aside. Source disks are only read.";
        stepCopy.Name = "stepCopy";
        stepCopy.Number = "3";
        stepCopy.Title = "Copy one clean set";
        //
        // HomeView
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(layout);
        Name = "HomeView";
        Size = new Size(940, 600);
        layout.ResumeLayout(false);
        layout.PerformLayout();
        flowTiles.ResumeLayout(false);
        flowSteps.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel layout;
    private Label lblIntro;
    private FlowLayoutPanel flowTiles;
    private Controls.StatTile tileDisks;
    private Controls.StatTile tileFiles;
    private Controls.StatTile tileUnique;
    private Controls.StatTile tileExtra;
    private Controls.StatTile tilePending;
    private Label lblNext;
    private FlowLayoutPanel flowSteps;
    private Controls.StepCard stepCatalog;
    private Controls.StepCard stepReport;
    private Controls.StepCard stepCopy;
}
