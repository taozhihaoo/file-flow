using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Localization;
using FileFlow.App.Services;
using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace FileFlow.App.ViewModels;

/// <summary>Tags page: create, rename and delete tags; shows per-tag file counts.</summary>
public partial class TagsViewModel : ObservableObject, IRefreshable
{
    private readonly ITagRepository _tags;
    private readonly IDialogService _dialogs;
    private readonly ILogger<TagsViewModel> _logger;

    public TagsViewModel(ITagRepository tags, IDialogService dialogs, ILogger<TagsViewModel> logger)
    {
        _tags = tags;
        _dialogs = dialogs;
        _logger = logger;
    }

    public ObservableCollection<Tag> Tags { get; } = new();

    [ObservableProperty]
    private Tag? selectedTag;

    public bool HasSelection => SelectedTag is not null;

    public async Task RefreshAsync()
    {
        try
        {
            var tags = await _tags.GetAllAsync();
            var selectedId = SelectedTag?.Id;
            Tags.Clear();
            foreach (var tag in tags)
            {
                Tags.Add(tag);
            }

            SelectedTag = Tags.FirstOrDefault(t => t.Id == selectedId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load tags");
        }
    }

    partial void OnSelectedTagChanged(Tag? value) => OnPropertyChanged(nameof(HasSelection));

    [RelayCommand]
    private async Task NewAsync()
    {
        var name = _dialogs.PromptText(
            Loc.Instance["Tags_NewTitle"],
            Loc.Instance["Tags_NamePrompt"],
            string.Empty,
            value => value.Trim().Length == 0 ? Loc.Instance["Tags_NameEmpty"] : null);
        if (name is null)
        {
            return;
        }

        try
        {
            var tag = await _tags.CreateAsync(name);
            await RefreshAsync();
            SelectedTag = Tags.FirstOrDefault(t => t.Id == tag.Id);
        }
        catch (FileFlowException ex)
        {
            _dialogs.ShowError(Loc.Instance["Tags_NewTitle"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedTag is null)
        {
            return;
        }

        var current = SelectedTag;
        var name = _dialogs.PromptText(
            Loc.Instance["Tags_RenameTitle"],
            Loc.Instance.F("Tags_RenamePrompt", current.Name),
            current.Name,
            value => value.Trim().Length == 0 ? Loc.Instance["Tags_NameEmpty"] : null);
        if (name is null || name.Equals(current.Name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await _tags.RenameAsync(current.Id, name);
            await RefreshAsync();
        }
        catch (FileFlowException ex)
        {
            _dialogs.ShowError(Loc.Instance["Tags_RenameTitle"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedTag is null)
        {
            return;
        }

        var current = SelectedTag;
        if (!_dialogs.Confirm(
                Loc.Instance["Tags_DeleteTitle"],
                Loc.Instance.F("Tags_DeleteMessage", current.Name),
                Loc.Instance.F("Tags_DeleteDetails", current.FileCount)))
        {
            return;
        }

        try
        {
            await _tags.DeleteAsync(current.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not delete tag {TagId}", current.Id);
            _dialogs.ShowError(Loc.Instance["Tags_DeleteTitle"], Loc.Instance.F("Tags_DeleteError", ex.Message));
        }
    }
}
