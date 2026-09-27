using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SystemCore.Core.Tools;

namespace SystemCore.App.Tools;

public sealed partial class ToolsView : UserControl, IDisposable
{
    private readonly ToolsViewModel model;
    private readonly Dictionary<WindowsToolId, (TextBlock Name, TextBlock State, Button Open)> rows = [];
    private readonly Dictionary<ToolGroup, TextBlock> headings = [];
    public ToolsView(ToolsViewModel model)
    { this.model = model; InitializeComponent(); model.PropertyChanged += OnChanged; Loaded += OnLoaded; Render(); }
    private async void OnLoaded(object sender, RoutedEventArgs args) => await model.InitializeAsync();
    private void OnChanged(object? sender, PropertyChangedEventArgs args) => Render();
    private void Render()
    {
        Title.Text = model.Title; Description.Text = model.Text("ToolsDescription"); Status.Text = model.Status;
        Refresh.Content = model.Text("ActionRefresh"); Refresh.IsEnabled = !model.Busy;
        if (rows.Count == 0 && model.Items.Count > 0)
        {
            foreach (var group in Enum.GetValues<ToolGroup>())
            {
                var heading = new TextBlock { FontSize = 20, Margin = new Thickness(0, 12, 0, 0) }; headings[group] = heading; Cards.Children.Add(heading);
                var groupCards = new DesignSystem.F1AdaptivePanel(); Cards.Children.Add(groupCards);
                foreach (var tool in model.Items.Where(t => t.Group == group))
                {
                    var grid = new Grid { RowSpacing = 16 }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    var icon = new FontIcon { Glyph = tool.Icon, FontSize = 28, FontFamily = new FontFamily("Segoe Fluent Icons"), HorizontalAlignment = HorizontalAlignment.Left };
                    AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw); grid.Children.Add(icon);
                    var text = new StackPanel { Spacing = 8 }; Grid.SetRow(text, 1); grid.Children.Add(text);
                    var name = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap }; var state = new TextBlock { TextWrapping = TextWrapping.Wrap };
                    text.Children.Add(name); text.Children.Add(state);
                    var button = new Button { Tag = tool.Id, HorizontalAlignment = HorizontalAlignment.Stretch, Style = (Style)Application.Current.Resources["F1PrimaryButtonStyle"] }; button.Click += OnOpen; Grid.SetRow(button, 2); grid.Children.Add(button);
                    AutomationProperties.SetAutomationId(button, "ToolOpen" + tool.Id); AutomationProperties.SetAutomationId(state, "ToolAvailability" + tool.Id);
                    rows.Add(tool.Id, (name, state, button)); groupCards.Children.Add(new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = grid });
                }
            }
        }
        foreach (var pair in headings) pair.Value.Text = model.Text("ToolGroup" + pair.Key);
        foreach (var tool in model.Items)
        {
            var row = rows[tool.Id]; row.Name.Text = model.Text(tool.NameKey);
            row.State.Text = model.Text("ToolAvailability" + tool.Availability) + (tool.RequiresElevation ? " · " + model.Text("ToolAdministrative") : "");
            row.Open.Content = model.Text("ActionOpen"); row.Open.IsEnabled = !model.Busy && tool.Availability == ToolAvailability.Available;
            AutomationProperties.SetName(row.Open, model.Text("ActionOpen") + " " + row.Name.Text);
        }
    }
    private async void OnOpen(object sender, RoutedEventArgs args) { if (sender is Button { Tag: WindowsToolId id }) await model.OpenAsync(id); }
    private async void OnRefresh(object sender, RoutedEventArgs args) => await model.RefreshAsync();
    public void Dispose() { Loaded -= OnLoaded; model.PropertyChanged -= OnChanged; foreach (var row in rows.Values) row.Open.Click -= OnOpen; rows.Clear(); Cards.Children.Clear(); }
}
