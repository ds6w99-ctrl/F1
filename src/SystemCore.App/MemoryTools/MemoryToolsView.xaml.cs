using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace SystemCore.App.MemoryTools;

public sealed partial class MemoryToolsView : UserControl, IDisposable
{
    private readonly MemoryToolsViewModel model;
    private readonly List<(ListViewItem Item, TextBlock Text)> rows = [];
    private bool rendering;
#if MEMORY_VALIDATION
    private readonly TextBlock validationAudit = new() { TextWrapping = TextWrapping.Wrap };
#endif
    public MemoryToolsView(MemoryToolsViewModel model)
    {
        this.model = model; InitializeComponent();
#if MEMORY_VALIDATION
        var validation = new StackPanel { Spacing = 4 };
        void Add(string id, string title, Action action)
        {
            var button = new Button { Content = title }; AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => { action(); model.Analyze(); }; validation.Children.Add(button);
        }
        Add("MemTestNormal", "TEST: Normal", () => MemoryValidationEnvironment.Current.SetState(false));
        Add("MemTestHigh", "TEST: High pressure", () => MemoryValidationEnvironment.Current.SetState(true));
        Add("MemTestStale", "TEST: Prepare stale 16 MB", () => MemoryValidationEnvironment.Current.Prepare(false));
        Add("MemTestActive", "TEST: Prepare active 16 MB", () => MemoryValidationEnvironment.Current.Prepare(true));
        AutomationProperties.SetAutomationId(validationAudit, "MemTestAudit"); validation.Children.Add(validationAudit); Sections.Children.Insert(0, validation);
#endif
        for (var index = 0; index < 10; index++) { var text = new TextBlock { TextWrapping = TextWrapping.Wrap }; var row = new ListViewItem { Content = text }; rows.Add((row, text)); Rows.Items.Add(row); }
        model.PropertyChanged += OnChanged; Render();
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs args) => Render();
    private void Render()
    {
        rendering = true;
        try
        {
            Title.Text = model.Title; Status.Text = model.Status; Analyze.Content = model.Text("MemAnalyze"); Education.Text = model.Text("MemEducation");
#if MEMORY_VALIDATION
            validationAudit.Text = MemoryValidationEnvironment.Current.Audit;
#endif
            Optimize.Content = model.Text("MemOptimize"); Optimize.IsEnabled = model.CanOptimize;
            OptimizationResult.Visibility = model.HasOptimizationResult ? Visibility.Visible : Visibility.Collapsed;
            OptimizationSummary.Text = model.OptimizationSummary; BeforeAfter.Text = model.BeforeAfter;
            Physical.Text = model.Physical; Pressure.Text = model.Pressure; Commit.Text = model.Commit; Updated.Text = model.Updated;
            Cache.Text = model.Text("MemCache") + ": " + model.Cache; PageFile.Text = model.Text("MemPageFile") + ": " + model.PageFile;
            ToolTipService.SetToolTip(Commit, model.Text("MemCommitTip")); ToolTipService.SetToolTip(Cache, model.Text("MemCacheTip"));
            History.Text = model.HistoryStatus; UsageTitle.Text = model.Text("MemUsageHistory"); AvailableTitle.Text = model.Text("MemAvailableHistory");
            UsageChart.Culture = AvailableChart.Culture = model.Culture;
            UsageChart.Points = AvailableChart.Points = model.History;
            AutomationProperties.SetName(UsageChart, model.Text("MemUsageHistory") + "; " + model.ChartDescription);
            AutomationProperties.SetName(AvailableChart, model.Text("MemAvailableHistory") + "; " + model.ChartDescription);
            var modes = new[] { model.Text("PerfTopMemory"), model.Text("PerfTopCpu") };
            if (!Mode.Items.Cast<string>().SequenceEqual(modes)) { Mode.Items.Clear(); foreach (var option in modes) Mode.Items.Add(option); }
            Mode.SelectedIndex = model.Mode; AutomationProperties.SetName(Mode, model.Text("MemConsumers"));
            Consumers.Text = model.ConsumersStatus; Details.Text = model.Details; Technical.Text = model.TechnicalDetails;
            OpenLocation.Content = model.Text("StartOpenLocation"); OpenLocation.IsEnabled = model.CanOpenLocation;
            Disk.Text = model.Disk; Network.Text = model.Network;
            var values = model.Rows;
            for (var index = 0; index < rows.Count; index++)
            {
                var value = index < values.Count ? values[index] : null; var row = rows[index];
                row.Item.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible; row.Item.Tag = value is null ? null : MemoryToolsViewModel.Key(value);
                row.Text.Text = value is null ? "" : model.RowText(value); row.Item.IsSelected = value is not null && MemoryToolsViewModel.Key(value) == model.SelectedKey;
                AutomationProperties.SetName(row.Item, value?.Name ?? "");
            }
        }
        finally { rendering = false; }
    }
    private void OnAnalyze(object sender, RoutedEventArgs args) => model.Analyze();
    private async void OnOptimize(object sender, RoutedEventArgs args) => await model.OptimizeAsync();
    private void OnMode(object sender, SelectionChangedEventArgs args) { if (!rendering) model.Mode = Mode.SelectedIndex; }
    private async void OnSelection(object sender, SelectionChangedEventArgs args) { if (!rendering) await model.SelectAsync((Rows.SelectedItem as ListViewItem)?.Tag as string ?? ""); }
    private async void OnOpenLocation(object sender, RoutedEventArgs args) => await model.OpenLocationAsync();
    public void Dispose() { model.PropertyChanged -= OnChanged; Rows.Items.Clear(); rows.Clear(); UsageChart.Points = AvailableChart.Points = null; }
}
