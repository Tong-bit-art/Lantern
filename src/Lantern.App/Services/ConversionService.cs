// UI-facing wrapper around JobQueue: starts jobs, keeps bindable lists for the toast window and records history.
// Jobs started from the donut are first shown by its progress ring and only appear in the toast when the ring
// hands them over (long job, failure, or a new drag).

using System.Collections.ObjectModel;
using Avalonia.Threading;
using Lantern.App.ViewModels;
using Lantern.Core.Conversion;
using Lantern.Core.History;
using Lantern.Core.Localization;
using Lantern.Core.Platform;
using Lantern.Core.Policy;
using Lantern.Core.Presets;
using Lantern.Core.Settings;
using Lantern.Core.Web;
using Microsoft.Extensions.Logging;

namespace Lantern.App.Services;

public sealed class ConversionService
{
    private static readonly TimeSpan KeepSuccess = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KeepErrors = TimeSpan.FromSeconds(15);

    /// <summary>After this many fully successful conversions Lantern asks once for a star or feedback.</summary>
    public const int FeedbackAfterConversions = 3;

    private readonly JobQueue _queue;
    private readonly UserDataStore _store;
    private readonly ILocalizer _loc;
    private readonly IPlatformServices _platform;
    private readonly WebDownloader _downloader;
    private readonly PolicyStore? _policy;
    private readonly ILogger<ConversionService>? _log;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, JobViewModel> _byId = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<ConversionJob>> _waiting = new();

    /// <summary>Jobs that came from a watch folder: job id → rule id (for the history and for retrying).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _watchJobs = new();

    /// <summary>Jobs whose results go on the clipboard when they finish (the clipboard shortcut).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _clipboardJobs = new();

    public ConversionService(JobQueue queue, UserDataStore store, ILocalizer loc, IPlatformServices platform, WebDownloader downloader,
        ILogger<ConversionService>? log = null, PolicyStore? policy = null)
    {
        _log = log;
        _queue = queue;
        _store = store;
        _loc = loc;
        _platform = platform;
        _downloader = downloader;
        _policy = policy;
        _queue.JobUpdated += OnJobUpdated;
        _queue.JobFinished += OnJobFinished;
    }

    /// <summary>All jobs that are running or finished recently, newest first. UI thread only.</summary>
    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    /// <summary>The subset shown in the toast window. UI thread only.</summary>
    public ObservableCollection<JobViewModel> ToastJobs { get; } = [];

    /// <summary>
    /// Raised once (UI thread) when the user has had a few successful conversions and the toast has gone: time to
    /// ask for a GitHub star or feedback.
    /// </summary>
    public event EventHandler? FeedbackDue;

    /// <summary>
    /// Runs the user's conversion policy over a job and hands back the preset it should use.
    /// Returns a refusal reason when a rule vetoed the job.
    /// </summary>
    /// <remarks>
    /// Skips the cases a rule cannot speak about: copying to a folder is not a conversion, and a
    /// group or tool preset is bookkeeping rather than a target format. Applying rules there would
    /// change what those operations mean.
    /// </remarks>
    private string? ApplyPolicy(IReadOnlyList<string> files, ref Preset preset)
    {
        if (_policy is null || files.Count == 0)
            return null;
        if (preset.IsGroup || preset.IsCopyToFolder || preset.IsTool)
            return null;

        try
        {
            var policy = _policy.Current;
            if (policy.Rules.Count == 0)
                return null;

            var application = PolicyApplier.Apply(policy, files, preset, FindPresetById);
            if (application.RefusedBecause is { } reason)
                return reason;
            if (application.Changed)
            {
                preset = application.Preset;
                _log?.LogInformation("policy adjusted the preset:{NewLine}{Explanation}",
                    Environment.NewLine, application.Explain());
            }
            return null;
        }
        catch (Exception ex)
        {
            // A broken policy must never take the converter down with it: the job runs with the
            // preset the user chose, and the next Settings visit reports the problem.
            _log?.LogWarning(ex, "conversion policy failed; using the preset as given");
            return null;
        }
    }

    /// <summary>Resolves a preset id from the user's collection, so a rule can name one.</summary>
    private Preset? FindPresetById(string id) =>
        _store.Presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    /// <summary>Starts converting files with a preset.</summary>
    /// <param name="files">Source files.</param>
    /// <param name="preset">Preset to apply.</param>
    /// <param name="showInToast">False when another view (the donut's progress ring) displays the job first.</param>
    /// <param name="web">
    /// The browser drop <paramref name="files"/> stand for: the job saves them first, into the folder Settings ›
    /// General names for files from a browser. The job owns it from now on (disposed here when nothing starts).
    /// </param>
    /// <param name="toClipboard">The results go on the clipboard when the job has finished (the clipboard shortcut).</param>
    /// <returns>The job's view model, or null when there was nothing to convert.</returns>
    public JobViewModel? Start(IReadOnlyList<string> files, Preset preset, bool showInToast = true, WebDrop? web = null,
        bool toClipboard = false)
    {
        if (files.Count == 0 || preset.IsGroup || preset.IsTool && !preset.IsCopyToFolder || preset.IsCopyToFolder && preset.Output.AskEveryTime)
        {
            web?.Dispose();
            return null;
        }
        // A preset on "Default" saves where Settings > General says (read now, so a changed default applies at once).
        preset = _store.Settings.WithDefaultOutput(preset);
        if (preset.Translate.Enabled && string.IsNullOrWhiteSpace(preset.Translate.Model) && !string.IsNullOrWhiteSpace(_store.Settings.PreferredOllamaModel))
        {
            preset = preset.Clone();
            preset.Translate.Model = _store.Settings.PreferredOllamaModel;
        }
        // The user's conversion policy can replace or refine what the preset says. It runs after the
        // default-output fix-up so a rule sees the folder the job would actually write to, and before
        // the queue so the job carries the adjusted preset. A rule that refuses stops the job here.
        if (ApplyPolicy(files, ref preset) is { } refusal)
        {
            web?.Dispose();
            _log?.LogInformation("policy refused {Count} file(s): {Reason}", files.Count, refusal);
            return null;
        }
        WebDropFetcher? fetcher = null;
        if (web is not null)
        {
            var options = BrowserDropOptions();
            if (preset.IsCopyToFolder)
                options = options with { SaveOnlyConverted = true };
            else if (web.SaveOnlyConverted is { } only)
                options = options with { SaveOnlyConverted = only };
            fetcher = web.CreateFetcher(options, _downloader);
            // "Next to the source" is the download folder, also when the original is only kept until converted.
            preset = fetcher.Apply(preset);
        }
        var folders = files.Where(Directory.Exists).ToHashSet(Lantern.Core.Platform.FileSystemPaths.Comparer);
        var job = _queue.Enqueue(files, preset, _loc.DisplayName(preset), fetcher, folders);
        if (toClipboard)
        {
            _clipboardJobs[job.Id] = 0;
            // It may have finished already (nothing to convert): OnJobFinished found no mark then.
            if (job.State is not (JobState.Queued or JobState.Running) && _clipboardJobs.TryRemove(job.Id, out _))
                Dispatcher.UIThread.Post(() => CopyResults(job));
        }
        var vm = new JobViewModel(job, _loc, _platform, Remove, RetryFailed);
        _byId[job.Id] = vm;
        Jobs.Insert(0, vm);
        if (showInToast)
            ToastJobs.Insert(0, vm);
        return vm;
    }

    /// <summary>The existing save picker, before creating a job; cancellation releases browser data.</summary>
    internal async Task<JobViewModel?> AskThenStartAsync(IReadOnlyList<string> files, Preset preset, WebDrop? web,
        Func<Preset, bool, Task<Preset?>> choose, bool toClipboard = false)
    {
        try
        {
            var running = _store.Settings.WithDefaultOutput(preset);
            var one = AskedLocation.MakesOneResult(running, files.Count)
                && !(running.IsCopyToFolder && Directory.Exists(files[0]));
            if (await choose(running, one) is not { } asked)
                return null;
            var job = Start(files, asked, web: web, toClipboard: toClipboard);
            web = null;
            return job;
        }
        finally { web?.Dispose(); }
    }

    /// <summary>
    /// Converts a watch folder's files again (rule id, files, preset as the job ran it): WatchFolderService sets it, so
    /// a retry also does what the folder does afterwards (moving originals).
    /// </summary>
    public Func<string, IReadOnlyList<string>, Preset, Task>? WatchRetry { get; set; }

    /// <summary>
    /// Converts files that failed again with the preset their job ran with (shown in the toast). Files that are no
    /// longer there are left out. Files of a watch folder go through <see cref="WatchRetry"/>.
    /// </summary>
    /// <returns>The new job, or null when none of the files is left or the watch folder converts them.</returns>
    public JobViewModel? Retry(IEnumerable<string> sources, Preset preset, string? watchRuleId = null)
    {
        var files = sources.Where(Exists).Distinct(StringComparer.Ordinal).ToList();
        if (files.Count == 0)
            return null;
        // History keeps no PDF password: it comes from the preset when that still has one (else the job refuses).
        if (preset.Pdf.PasswordLeftOut && _store.FindPreset(preset.Id)?.Pdf.Password is { Length: > 0 } password)
        {
            preset = preset.Clone();
            preset.Pdf.Password = password;
            preset.Pdf.PasswordLeftOut = false;
        }
        if (watchRuleId is not null && WatchRetry is { } watchRetry)
        {
            _ = watchRetry(watchRuleId, files, preset);
            return null;
        }
        return Start(files, preset);
    }

    /// <summary>
    /// Saves a browser drop's files for a tool (the crop overlay) or the Home page's list, which need them on disk before
    /// anything else happens: always into the download folder, as the tool shows and keeps working on the original, and
    /// the list's conversions save next to them. Files that can't be saved are left out (<paramref name="failed"/> hears
    /// why). The drop is done afterwards.
    /// </summary>
    public async Task<IReadOnlyList<string>> SaveBrowserFilesAsync(WebDrop web, Action<string, Exception> failed)
    {
        try
        {
            var fetcher = web.CreateFetcher(BrowserDropOptions() with { SaveOnlyConverted = false }, _downloader);
            var saved = new List<string>();
            foreach (var placeholder in web.Placeholders)
            {
                try
                {
                    saved.Add(await fetcher.FetchAsync(placeholder, new Progress<TransferProgress>(), CancellationToken.None));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failed(placeholder, ex);
                }
            }
            return saved;
        }
        finally
        {
            web.Dispose();
        }
    }

    /// <summary>Where files dragged from a browser are saved, as Settings › General says now.</summary>
    public WebDropOptions BrowserDropOptions()
    {
        var settings = _store.Settings.BrowserDrops;
        var folder = string.IsNullOrWhiteSpace(settings.Folder) ? _platform.DownloadsFolder : settings.Folder.Trim();
        return new WebDropOptions(folder, settings.SaveOnlyConverted, settings.AllowLocalNetwork);
    }

    /// <summary>True when a source file (or folder) is still where it was.</summary>
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private void RetryFailed(JobViewModel vm)
    {
        var watchRuleId = _watchJobs.GetValueOrDefault(vm.Job.Id);
        var failed = vm.FailedSources.ToList();
        Remove(vm);
        Retry(failed, vm.Job.Preset, watchRuleId);
    }

    /// <summary>
    /// Converts like <see cref="Start"/> (shown in the toast and history) and completes when the job has finished.
    /// Callable from any thread; used by watch folders.
    /// </summary>
    /// <param name="watchRuleId">The watch folder the files come from; recorded in the history.</param>
    public async Task<ConversionJob?> RunAsync(IReadOnlyList<string> files, Preset preset, string? watchRuleId = null)
    {
        var vm = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var started = Start(files, preset);
            if (started is not null && watchRuleId is not null)
                _watchJobs[started.Job.Id] = watchRuleId;
            return started;
        });
        if (vm is null)
            return null;
        var finished = _waiting.GetOrAdd(vm.Job.Id, _ => new TaskCompletionSource<ConversionJob>(TaskCreationOptions.RunContinuationsAsynchronously));
        // The job may have finished before the wait was registered.
        if (vm.Job.State is not (JobState.Queued or JobState.Running))
            finished.TrySetResult(vm.Job);
        var job = await finished.Task;
        _waiting.TryRemove(job.Id, out _);
        return job;
    }

    /// <summary>Moves a job into the toast window (no-op if it is already there or gone).</summary>
    public void ShowInToast(JobViewModel vm)
    {
        if (Jobs.Contains(vm) && !ToastJobs.Contains(vm))
            ToastJobs.Insert(0, vm);
    }

    private void OnJobUpdated(object? sender, ConversionJob job)
    {
        // Called from worker threads, possibly many times per second: coalesce into one UI update.
        if (_byId.TryGetValue(job.Id, out var vm) && vm.TryMarkPending())
            Dispatcher.UIThread.Post(vm.Refresh, DispatcherPriority.Background);
    }

    private void OnJobFinished(object? sender, ConversionJob job)
    {
        var entry = HistoryEntry.From(job);
        if (string.IsNullOrWhiteSpace(job.Preset.Name) && job.Preset.NameKey is { } key && _loc[key] == job.PresetDisplayName)
            entry.PresetNameKey = key;
        entry.WatchRuleId = _watchJobs.GetValueOrDefault(job.Id);
        _store.AddHistory(entry);
        if (_waiting.TryGetValue(job.Id, out var waiting))
            waiting.TrySetResult(job);
        var toClipboard = _clipboardJobs.TryRemove(job.Id, out _);
        Dispatcher.UIThread.Post(() =>
        {
            if (toClipboard)
                CopyResults(job);
            if (!_byId.TryGetValue(job.Id, out var vm))
                return;
            vm.Refresh();
            var keep = vm.HasErrors ? KeepErrors : KeepSuccess;
            DispatcherTimer.RunOnce(() => Remove(vm), keep);
            if (!vm.HasErrors && CountSuccess(job))
                DispatcherTimer.RunOnce(() => FeedbackDue?.Invoke(this, EventArgs.Empty), keep + TimeSpan.FromSeconds(1));
        });
    }

    /// <summary>
    /// Puts what a clipboard shortcut's job made on the clipboard, in place of what it was made from. A cancelled job,
    /// or one that made nothing, leaves the clipboard alone. A partly failed job still puts its outputs on the
    /// clipboard; the log carries the failed count, so it shows why fewer files are pasted than were copied. UI thread.
    /// </summary>
    internal void CopyResults(ConversionJob job)
    {
        if (job.State == JobState.Cancelled)
            return;
        var failed = job.Files.Count(f => f.State == FileState.Failed);
        var outputs = job.Outputs.Where(File.Exists).Distinct(StringComparer.Ordinal).ToList();
        if (outputs.Count == 0)
        {
            if (failed > 0)
                _log?.LogInformation("The clipboard job made nothing to put on the clipboard; {Failed} file(s) failed", failed);
            return;
        }
        if (!_platform.CopyToClipboard(outputs))
            _log?.LogWarning("Could not put {Count} converted file(s) on the clipboard; {Failed} file(s) failed", outputs.Count, failed);
        else
            _log?.LogInformation("Put {Count} converted file(s) on the clipboard; {Failed} file(s) failed", outputs.Count, failed);
    }

    /// <summary>Counts a fully successful conversion; true when the feedback card is due now (only once).</summary>
    private bool CountSuccess(ConversionJob job)
    {
        var settings = _store.Settings;
        if (settings.FeedbackPromptShown || job.Files.Count == 0 || job.Files.Any(f => f.State != FileState.Done))
            return false;
        settings.SuccessfulConversions++;
        settings.FeedbackPromptShown = settings.SuccessfulConversions >= FeedbackAfterConversions;
        _store.SaveSettings();
        return settings.FeedbackPromptShown;
    }

    private void Remove(JobViewModel vm)
    {
        Jobs.Remove(vm);
        ToastJobs.Remove(vm);
        _byId.TryRemove(vm.Job.Id, out _);
        _watchJobs.TryRemove(vm.Job.Id, out _);
        _clipboardJobs.TryRemove(vm.Job.Id, out _);
    }
}
