using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SystemCore.App.ViewModels;

public sealed class MetricCardViewModel(string id) : ObservableObject
{
    public string Id { get; } = id;
    private string title = "", value = "", detail = "";
    private double progress;
    private Visibility progressVisibility = Visibility.Collapsed;
    private TextAlignment alignment;
    public string Title { get => title; set => SetProperty(ref title, value); }
    public string Value { get => value; set => SetProperty(ref this.value, value); }
    public string Detail { get => detail; set => SetProperty(ref detail, value); }
    public double Progress { get => progress; set => SetProperty(ref progress, value); }
    public Visibility ProgressVisibility { get => progressVisibility; set => SetProperty(ref progressVisibility, value); }
    public TextAlignment ValueAlignment { get => alignment; set => SetProperty(ref alignment, value); }
}
