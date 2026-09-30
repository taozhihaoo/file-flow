using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Services;
using FileFlow.Core.Models;

namespace FileFlow.App.Dialogs;

public partial class TagPickerViewModel : ObservableObject
{
    public string Title { get; init; } = "Tag";

    public string Label { get; init; } = string.Empty;

    public string ConfirmText { get; init; } = "OK";

    public bool AllowCreate { get; init; } = true;

    public IReadOnlyList<Tag> Tags { get; init; } = Array.Empty<Tag>();

    [ObservableProperty]
    private Tag? selectedTag;

    [ObservableProperty]
    private bool createNew;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string newTagName = string.Empty;

    public RelayCommand ConfirmCommand { get; }

    public TagPickerViewModel()
    {
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
    }

    public event Action<TagPickResult>? Confirmed;

    private bool CanConfirm()
        => CreateNew ? AllowCreate && !string.IsNullOrWhiteSpace(NewTagName) : SelectedTag is not null;

    private void Confirm()
    {
        var name = CreateNew ? NewTagName.Trim() : SelectedTag!.Name;
        Confirmed?.Invoke(new TagPickResult(name, CreateNew));
    }

    partial void OnCreateNewChanged(bool value)
    {
        if (!AllowCreate)
        {
            CreateNew = false;
        }

        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTagChanged(Tag? value)
    {
        if (value is not null)
        {
            CreateNew = false;
        }

        ConfirmCommand.NotifyCanExecuteChanged();
    }
}

public partial class TagPickerDialog : Window
{
    public TagPickerDialog(TagPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Confirmed += _ => DialogResult = true;
    }
}
