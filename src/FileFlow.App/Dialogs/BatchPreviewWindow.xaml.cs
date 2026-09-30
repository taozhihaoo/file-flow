using System.Windows;

namespace FileFlow.App.Dialogs;

public partial class BatchPreviewWindow : Window
{
    public BatchPreviewWindow(ViewModels.BatchPreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += confirmed => DialogResult = confirmed;
    }
}
