using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

/// <summary>Start page: where am I, and what is the next step. Written for someone who has never used the tool.</summary>
public partial class HomeView : UserControl, IHomeView
{
    public event EventHandler<Page>? StepClicked;
    public Control Control => this;

    public HomeView()
    {
        InitializeComponent();
        lblIntro.ForeColor = UiTheme.Muted;
        lblNext.ForeColor = UiTheme.Accent;
        lblNext.Font = UiTheme.HeadingFont;
        stepCatalog.ActionClicked += (_, _) => StepClicked?.Invoke(this, Page.Scan);
        stepReport.ActionClicked += (_, _) => StepClicked?.Invoke(this, Page.Report);
        stepCopy.ActionEnabled = false;
    }

    public void ShowSummary(HomeSummaryViewModel vm) => this.OnUi(() =>
    {
        tileDisks.Value = vm.Disks;
        tileFiles.Value = vm.Files;
        tileUnique.Value = vm.Unique;
        tileExtra.Value = vm.Extra;
        tilePending.Value = vm.Pending;
        lblNext.Text = vm.NextStep;
    });

    public void SetExpertMode(bool expert) { /* the home page is the same in both modes */ }
}
