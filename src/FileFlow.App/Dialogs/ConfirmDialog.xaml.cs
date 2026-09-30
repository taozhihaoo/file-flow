using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FileFlow.App.Dialogs;

public partial class ConfirmDialogViewModel : ObservableObject
{
    public string Title { get; init; } = "Confirm";

    public string Message { get; init; } = string.Empty;

    public string? Details { get; init; }

    public string ConfirmText { get; init; } = "OK";

    public bool DangerStyle { get; init; }

    [RelayCommand]
    private void Confirm() => RequestClose?.Invoke(true);

    public event Action<bool>? RequestClose;
}

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(ConfirmDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += confirmed => DialogResult = confirmed;
    }
}
