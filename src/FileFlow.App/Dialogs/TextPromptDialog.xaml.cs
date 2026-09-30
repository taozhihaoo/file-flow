using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FileFlow.App.Dialogs;

public partial class TextPromptViewModel : ObservableObject
{
    private readonly Func<string, string?>? _validate;

    public TextPromptViewModel(string title, string label, string? defaultValue = null, Func<string, string?>? validate = null)
    {
        Title = title;
        Label = label;
        Value = defaultValue ?? string.Empty;
        _validate = validate;
        ConfirmCommand = new RelayCommand(Confirm, () => ValidationError is null);
    }

    public string Title { get; }

    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmCommand))]
    private string value;

    [ObservableProperty]
    private string? validationError;

    public IRelayCommand ConfirmCommand { get; }

    public event Action<string>? Confirmed;

    partial void OnValueChanged(string value) => RunValidation();

    private void Confirm()
    {
        RunValidation();
        if (ValidationError is null)
        {
            Confirmed?.Invoke(Value.Trim());
        }
    }

    private void RunValidation()
    {
        ValidationError = _validate?.Invoke(Value);
    }
}

public partial class TextPromptDialog : Window
{
    public TextPromptDialog(TextPromptViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Confirmed += _ => DialogResult = true;
        Loaded += (_, _) => FocusTextBox();
    }

    private void FocusTextBox()
    {
        var textBox = FindName("ValueBox") as System.Windows.Controls.TextBox;
        textBox?.Focus();
        textBox?.SelectAll();
    }
}
