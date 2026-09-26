using Microsoft.UI.Xaml.Controls;
namespace SystemCore.App.Cleaner;
public sealed partial class CleanerView : UserControl, IDisposable
{
    private readonly List<Action> categoryDetach = [];
    private readonly List<Action> previewDetach = [];
    private readonly List<RowSlot> categorySlots = [];
    private readonly List<RowSlot> previewSlots = [];
    private sealed class RowSlot
    {
        public Guid Id;
        public SystemCore.Core.Cleaner.CleanerCategory? Category;
        public readonly CheckBox Check = new();
        public readonly TextBlock Detail = new() { TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
        public readonly ListViewItem Container = new();
    }
    public CleanerViewModel ViewModel { get; }
    public CleanerView(CleanerViewModel viewModel)
    {
        ViewModel = viewModel; InitializeComponent(); Unloaded += (_, _) => ViewModel.Leave();
        ViewModel.Categories.CollectionChanged += CategoriesChanged;
        ViewModel.Preview.CollectionChanged += PreviewChanged;
#if CLEANER_VALIDATION
        ViewModel.ValidationUiDiagnostics = () => $";NativeCategorySlots:{categorySlots.Count};NativePreviewSlots:{previewSlots.Count};NativeRowHandlers:{2 * (categoryDetach.Count + previewDetach.Count)}";
        var prepare = new Button { Content = "Prepare synthetic fixtures (validation build only)" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(prepare, "CleanerValidationPrepare");
        prepare.Click += (_, _) => ViewModel.PrepareValidation();
        var status = new TextBlock { TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "CleanerValidationStatus");
        status.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Source = ViewModel, Path = new Microsoft.UI.Xaml.PropertyPath(nameof(ViewModel.ValidationStatus)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        ContentPanel.Children.Insert(0, status); ContentPanel.Children.Insert(0, prepare);
#endif
    }
    private async void ConfirmClean(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var request = ViewModel.Selection();
        if (request is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = ViewModel.ConfirmationTitle,
            Content = new TextBlock { Text = ViewModel.ConfirmationText(request), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
            PrimaryButtonText = ViewModel.CleanLabel, CloseButtonText = ViewModel.CancelLabel,
            PrimaryButtonStyle = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["DangerButtonStyle"],
            DefaultButton = ContentDialogButton.Close, FlowDirection = FlowDirection, RequestedTheme = ActualTheme
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(dialog, "CleanerConfirmation");
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.CleanAsync(request);
    }
    // Keep managed scan rows out of WinRT ItemsSource/DataContext projection. Only
    // native controls and display strings cross that boundary; callbacks retain an
    // opaque GUID/category, never a previous item/session. The model owns selection.
    private void CategoriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        { ResetRows(CategoryList, categorySlots); return; }
        if (e.NewItems is null) return;
        foreach (CleanerCategoryRow row in e.NewItems)
        {
            var index = CategoryList.Items.Count;
            if (index >= Enum.GetValues<SystemCore.Core.Cleaner.CleanerCategory>().Length) throw new InvalidOperationException("Category UI limit");
            var slot = GetSlot(categorySlots, categoryDetach, index, true);
            slot.Category = row.Result.Category;
            slot.Check.Content = row.Name; slot.Check.IsEnabled = row.CanSelect; slot.Check.IsChecked = row.Selected;
            slot.Detail.Text = row.Detail;
            CategoryList.Items.Add(slot.Container);
        }
    }
    private void PreviewChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        { ResetRows(PreviewList, previewSlots); return; }
        if (e.NewItems is null) return;
        foreach (CleanerItemRow row in e.NewItems)
        {
            var index = PreviewList.Items.Count;
            if (index >= 100) throw new InvalidOperationException("Preview UI limit");
            var slot = GetSlot(previewSlots, previewDetach, index, false);
            slot.Id = row.Item.Id; slot.Check.Content = row.Display; slot.Check.IsChecked = row.Selected;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(slot.Check, row.AutomationId);
            PreviewList.Items.Add(slot.Container);
        }
    }
    private RowSlot GetSlot(List<RowSlot> pool, List<Action> detach, int index, bool category)
    {
        if (index < pool.Count) return pool[index];
        var slot = new RowSlot();
        if (category)
        {
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(slot.Check); panel.Children.Add(slot.Detail); slot.Container.Content = panel;
        }
        else { slot.Check.FlowDirection = Microsoft.UI.Xaml.FlowDirection.LeftToRight; slot.Container.Content = slot.Check; }
        Microsoft.UI.Xaml.RoutedEventHandler changed = (_, _) =>
        {
            if (category)
            {
                var current = ViewModel.Categories.FirstOrDefault(c => c.Result.Category == slot.Category);
                if (current is not null) current.Selected = slot.Check.IsChecked == true;
            }
            else
            {
                var current = ViewModel.Preview.FirstOrDefault(r => r.Item.Id == slot.Id);
                if (current is not null) current.Selected = slot.Check.IsChecked == true;
            }
        };
        slot.Check.Checked += changed; slot.Check.Unchecked += changed;
        detach.Add(() => { slot.Check.Checked -= changed; slot.Check.Unchecked -= changed; });
        pool.Add(slot); return slot;
    }
    private static void ResetRows(ListView list, List<RowSlot> pool)
    {
        foreach (var slot in pool)
        {
            slot.Id = Guid.Empty; slot.Category = null;
            slot.Check.Content = null; slot.Check.IsChecked = false; slot.Detail.Text = "";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(slot.Check, "");
        }
        list.Items.Clear();
    }
    public void Dispose()
    {
#if CLEANER_VALIDATION
        ViewModel.ValidationUiDiagnostics = null;
#endif
        ViewModel.Categories.CollectionChanged -= CategoriesChanged;
        ViewModel.Preview.CollectionChanged -= PreviewChanged;
        foreach (var action in categoryDetach.Concat(previewDetach)) action();
        categoryDetach.Clear(); previewDetach.Clear();
        ResetRows(CategoryList, categorySlots); ResetRows(PreviewList, previewSlots);
        categorySlots.Clear(); previewSlots.Clear();
    }
}
