using System.ComponentModel;
using System.Reflection;

namespace RecoveryUI.Infrastructure;

/// <summary>Marks a ViewModel property as a column that only shows in Expert mode.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ExpertOnlyAttribute : Attribute;

/// <summary>Right-aligns a column (numbers, sizes).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NumericAttribute : Attribute;

/// <summary>Binds a list of ViewModels to a grid. Column titles come from [DisplayName]; [Browsable(false)] hides.</summary>
public static class GridBinder
{
    public static void Bind<T>(DataGridView grid, IReadOnlyList<T> rows, bool expert)
    {
        grid.AutoGenerateColumns = true;
        grid.DataSource = new BindingList<T>(rows.ToList());
        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (grid.Columns[p.Name] is not { } col) continue;
            col.Visible = expert || p.GetCustomAttribute<ExpertOnlyAttribute>() == null;
            if (p.GetCustomAttribute<NumericAttribute>() != null)
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
    }

    public static void ApplyExpert<T>(DataGridView grid, bool expert)
    {
        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (grid.Columns[p.Name] is { } col && p.GetCustomAttribute<ExpertOnlyAttribute>() != null)
                col.Visible = expert;
    }

    public static T? Selected<T>(DataGridView grid) where T : class =>
        grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].DataBoundItem as T : null;
}

public static class ControlExtensions
{
    /// <summary>Runs on the UI thread (controllers report progress from worker threads).</summary>
    public static void OnUi(this Control c, Action a)
    {
        if (c.IsDisposed || !c.IsHandleCreated) return;
        if (c.InvokeRequired) c.BeginInvoke(a); else a();
    }
}
