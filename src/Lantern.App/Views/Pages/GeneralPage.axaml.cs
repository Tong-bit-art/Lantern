using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lantern.App.Services;
using Lantern.App.ViewModels.Pages;
using Lantern.Core.Localization;

namespace Lantern.App.Views.Pages;

public partial class GeneralPage : UserControl
{
    private static readonly FilePickerFileType JsonType = new("Lantern presets") { Patterns = ["*.json"] };

    public GeneralPage() => InitializeComponent();

    private GeneralPageViewModel? Vm => DataContext as GeneralPageViewModel;

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "lantern-presets.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonType],
        });
        if (file?.TryGetLocalPath() is { } path)
            Vm.Export(path);
    }

    private async void OnBrowseDefaultFolder(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            Vm.DefaultFolder = path;
    }

    private async void OnBrowseBrowserFolder(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var start = await top.StorageProvider.TryGetFolderFromPathAsync(Vm.BrowserFolderInUse);
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, SuggestedStartLocation = start });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            Vm.BrowserFolder = path;
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [JsonType] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            Vm.Import(path);
    }

    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || TopLevel.GetTopLevel(this) is not Window owner)
            return;
        var loc = AppHost.Get<ILocalizer>();
        if (await AppHost.Get<WindowService>().ConfirmAsync(owner, loc["general.reset_confirm"]))
            Vm.ResetLibrary();
    }
}
