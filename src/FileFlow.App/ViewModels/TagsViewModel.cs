using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
            "New tag",
            "Tag name:",
            string.Empty,
            value => value.Trim().Length == 0 ? "Tag name cannot be empty." : null);
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
            _dialogs.ShowError("New tag", ex.Message);
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
            "Rename tag",
            $"New name for tag '{current.Name}':",
            current.Name,
            value => value.Trim().Length == 0 ? "Tag name cannot be empty." : null);
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
            _dialogs.ShowError("Rename tag", ex.Message);
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
                "Delete tag",
                $"Delete tag '{current.Name}'?",
                $"The tag is removed from {current.FileCount:N0} file(s). The files themselves are not touched."))
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
            _dialogs.ShowError("Delete tag", "Could not delete the tag: " + ex.Message);
        }
    }
}
