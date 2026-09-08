using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using Windows.Media.SpeechSynthesis;

namespace ExCord.Services;

public sealed class TtsService : IDisposable
{
    private readonly SpeechSynthesizer _speechSynthesizer;
    private readonly IReadOnlyList<VoiceInformation> _availableVoices;
    private readonly BlockingCollection<Action> _sapiWorkQueue = new();
    private readonly ManualResetEventSlim _sapiThreadReady = new();
    private readonly Thread _sapiThread;
    private System.Speech.Synthesis.SpeechSynthesizer? _sapiSynthesizer;
    private IReadOnlyList<System.Speech.Synthesis.VoiceInfo> _availableSapiVoices = Array.Empty<System.Speech.Synthesis.VoiceInfo>();
    private Exception? _sapiInitializationException;
    private readonly SemaphoreSlim _sapiLock = new(1, 1);
    private readonly object _operationLock = new();
    private readonly HashSet<Task> _activeOperations = new();
    private readonly object _sapiCompletionLock = new();
    private readonly HashSet<TaskCompletionSource<object?>> _activeSapiCompletions = new();
    private string? _preferredVoiceId;
    private bool _preferredVoiceIsOneCore;
    private bool _isDisposing;

    public event EventHandler<string>? VoiceFallback;

    public TtsService()
    {
        _speechSynthesizer = new SpeechSynthesizer();
        _availableVoices = SpeechSynthesizer.AllVoices;

        _sapiThread = new Thread(SapiThreadProc)
        {
            IsBackground = true,
            Name = "ExCord SAPI"
        };
        _sapiThread.SetApartmentState(ApartmentState.STA);
        _sapiThread.Start();
        _sapiThreadReady.Wait();

        if (_sapiInitializationException is not null)
        {
            CleanupSapiThreadResources();
            _sapiLock.Dispose();
            throw new InvalidOperationException("Failed to initialize the SAPI speech synthesizer.", _sapiInitializationException);
        }
    }

    private void SapiThreadProc()
    {
        try
        {
            _sapiSynthesizer = new System.Speech.Synthesis.SpeechSynthesizer();
            _availableSapiVoices = _sapiSynthesizer.GetInstalledVoices()
                .Where(voice => voice.Enabled)
                .Select(voice => voice.VoiceInfo)
                .ToArray();
        }
        catch (Exception ex)
        {
            _sapiInitializationException = ex;
        }
        finally
        {
            _sapiThreadReady.Set();
        }

        foreach (var action in _sapiWorkQueue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "TtsService.SapiThreadProc");
            }
        }
    }

    private bool PostSapiAction(Action action)
    {
        try
        {
            var posted = _sapiWorkQueue.TryAdd(action);
            if (!posted && !_isDisposing)
            {
                LoggingService.LogMessage("Failed to post a SAPI operation.");
            }

            return posted;
        }
        catch (InvalidOperationException)
        {
            if (!_isDisposing)
            {
                LoggingService.LogMessage("Failed to post a SAPI operation.");
            }

            return false;
        }
    }

    public void SetPreferredVoice(string? voiceId, bool isOneCore)
    {
        _preferredVoiceId = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId;
        _preferredVoiceIsOneCore = isOneCore;

        if (string.IsNullOrWhiteSpace(_preferredVoiceId))
        {
            return;
        }

        if (_preferredVoiceIsOneCore)
        {
            var voice = _availableVoices.FirstOrDefault(v => string.Equals(v.Id, _preferredVoiceId, StringComparison.OrdinalIgnoreCase));
            if (voice is not null)
            {
                _speechSynthesizer.Voice = voice;
                return;
            }
        }
        else
        {
            var voice = _availableSapiVoices.FirstOrDefault(v => string.Equals(v.Id, _preferredVoiceId, StringComparison.OrdinalIgnoreCase));
            if (voice is not null)
            {
                return;
            }
        }

        VoiceFallback?.Invoke(this, $"The selected voice ({_preferredVoiceId}) is unavailable. The default voice will be used.");
    }

    public Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
    {
        var operation = SynthesizeCoreAsync(text, cancellationToken);
        TrackOperation(operation);
        return operation;
    }

    private async Task<byte[]> SynthesizeCoreAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<byte>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var detectedLanguage = DetectLanguage(text);
        var selection = ResolveVoiceSelection(detectedLanguage);

        if (selection is null)
        {
            var fallbackMessage = $"No voice was found for {detectedLanguage}. The default voice will be used.";
            VoiceFallback?.Invoke(this, fallbackMessage);
            selection = new VoiceSelection(SpeechSynthesizer.DefaultVoice.Id, true, SpeechSynthesizer.DefaultVoice, null);
        }

        if (selection.IsOneCore)
        {
            await _sapiLock.WaitAsync(cancellationToken);
            try
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var oneCoreVoice = selection.OneCoreVoice ?? _speechSynthesizer.Voice ?? SpeechSynthesizer.DefaultVoice;
                    _speechSynthesizer.Voice = oneCoreVoice;
                    using var stream = await _speechSynthesizer.SynthesizeTextToStreamAsync(text);
                    cancellationToken.ThrowIfCancellationRequested();
                    using var memoryStream = new MemoryStream();

                    using (var inputStream = stream.AsStreamForRead())
                    {
                        await inputStream.CopyToAsync(memoryStream);
                    }

                    return memoryStream.ToArray();
                }
                catch (ObjectDisposedException)
                {
                    return Array.Empty<byte>();
                }
            }
            finally
            {
                try
                {
                    _sapiLock.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        var sapiVoice = selection.SapiVoice ?? _availableSapiVoices.FirstOrDefault();
        if (sapiVoice is null)
        {
            return await SynthesizeWithOneCoreFallbackAsync(text, detectedLanguage, cancellationToken);
        }

        using var waveStream = new MemoryStream();
        await _sapiLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SynthesizeWithSapiAsync(text, sapiVoice, waveStream, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            _sapiLock.Release();
        }

        return waveStream.ToArray();
    }

    private async Task SynthesizeWithSapiAsync(string text, System.Speech.Synthesis.VoiceInfo voice, Stream outputStream, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        TrackSapiCompletion(completion);
        EventHandler<System.Speech.Synthesis.SpeakCompletedEventArgs>? completedHandler = null;
        completedHandler = (_, args) =>
        {
            if (!PostSapiAction(() =>
            {
                _sapiSynthesizer!.SpeakCompleted -= completedHandler;
                _sapiSynthesizer.SetOutputToNull();
                CompleteSapiOperation(completion, args, cancellationToken);
            }))
            {
                CompleteSapiOperation(completion, args, cancellationToken);
            }
        };

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (!PostSapiAction(() =>
            {
                if (!completion.Task.IsCompleted)
                {
                    _sapiSynthesizer!.SpeakAsyncCancelAll();
                }
            }))
            {
                completion.TrySetCanceled(cancellationToken);
            }
        });

        if (!PostSapiAction(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _sapiSynthesizer!.SpeakCompleted += completedHandler;
                _sapiSynthesizer.SetOutputToWaveStream(outputStream);
                _sapiSynthesizer.SelectVoice(voice.Name);
                _sapiSynthesizer.SpeakAsync(text);
            }
            catch (Exception ex)
            {
                _sapiSynthesizer!.SpeakCompleted -= completedHandler;
                _sapiSynthesizer.SetOutputToNull();
                completion.TrySetException(ex);
            }
        }))
        {
            completion.TrySetCanceled(cancellationToken);
        }

        var synthesisTimeout = TimeSpan.FromSeconds(Math.Clamp(30 + text.Length / 10, 30, 300));
        if (await Task.WhenAny(completion.Task, Task.Delay(synthesisTimeout)) != completion.Task)
        {
            if (PostSapiAction(() => _sapiSynthesizer?.SpeakAsyncCancelAll()))
            {
                await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            }

            completion.TrySetCanceled();
        }

        await completion.Task;
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void CompleteSapiOperation(TaskCompletionSource<object?> completion, System.Speech.Synthesis.SpeakCompletedEventArgs args, CancellationToken cancellationToken)
    {
        if (args.Error is not null)
        {
            completion.TrySetException(args.Error);
        }
        else if (args.Cancelled)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        else
        {
            completion.TrySetResult(null);
        }
    }

    private async Task<byte[]> SynthesizeWithOneCoreFallbackAsync(string text, string detectedLanguage, CancellationToken cancellationToken)
    {
        var fallbackMessage = $"No voice was found for {detectedLanguage}. The default voice will be used.";
        VoiceFallback?.Invoke(this, fallbackMessage);

        await _sapiLock.WaitAsync(cancellationToken);
        try
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var oneCoreVoice = _speechSynthesizer.Voice ?? SpeechSynthesizer.DefaultVoice;
                _speechSynthesizer.Voice = oneCoreVoice;
                using var stream = await _speechSynthesizer.SynthesizeTextToStreamAsync(text);
                cancellationToken.ThrowIfCancellationRequested();
                using var memoryStream = new MemoryStream();
                using (var inputStream = stream.AsStreamForRead())
                {
                    await inputStream.CopyToAsync(memoryStream);
                }

                return memoryStream.ToArray();
            }
            catch (ObjectDisposedException)
            {
                return Array.Empty<byte>();
            }
        }
        finally
        {
            try
            {
                _sapiLock.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private VoiceSelection? ResolveVoiceSelection(string detectedLanguage)
    {
        if (!string.IsNullOrWhiteSpace(_preferredVoiceId))
        {
            if (_preferredVoiceIsOneCore)
            {
                var oneCoreVoice = _availableVoices.FirstOrDefault(v => string.Equals(v.Id, _preferredVoiceId, StringComparison.OrdinalIgnoreCase));
                if (oneCoreVoice is not null)
                {
                    return new VoiceSelection(oneCoreVoice.Id, true, oneCoreVoice, null);
                }
            }
            else
            {
                var sapiVoice = _availableSapiVoices.FirstOrDefault(v => string.Equals(v.Id, _preferredVoiceId, StringComparison.OrdinalIgnoreCase));
                if (sapiVoice is not null)
                {
                    return new VoiceSelection(sapiVoice.Id, false, null, sapiVoice);
                }
            }
        }

        return FindVoiceForLanguage(detectedLanguage);
    }

    private string DetectLanguage(string text)
    {
        if (Regex.IsMatch(text, "[\uAC00-\uD7A3]"))
        {
            return "ko-KR";
        }

        if (Regex.IsMatch(text, "[\u3040-\u30FF\u4E00-\u9FFF]"))
        {
            return "ja-JP";
        }

        if (Regex.IsMatch(text, "[a-zA-Z]"))
        {
            return "en-US";
        }

        return "default";
    }

    private VoiceSelection? FindVoiceForLanguage(string language)
    {
        if (string.Equals(language, "default", StringComparison.OrdinalIgnoreCase))
        {
            return new VoiceSelection(_speechSynthesizer.Voice?.Id ?? SpeechSynthesizer.DefaultVoice.Id, true, _speechSynthesizer.Voice ?? SpeechSynthesizer.DefaultVoice, null);
        }

        var oneCoreVoice = _availableVoices.FirstOrDefault(v =>
            string.Equals(v.Language, language, StringComparison.OrdinalIgnoreCase));
        if (oneCoreVoice is not null)
        {
            return new VoiceSelection(oneCoreVoice.Id, true, oneCoreVoice, null);
        }

        var sapiVoice = _availableSapiVoices.FirstOrDefault(v =>
            string.Equals(v.Culture.Name, language, StringComparison.OrdinalIgnoreCase) ||
            v.Culture.Name.StartsWith(language.Split('-')[0], StringComparison.OrdinalIgnoreCase));
        if (sapiVoice is not null)
        {
            return new VoiceSelection(sapiVoice.Id, false, null, sapiVoice);
        }

        if (language.Equals("ko-KR", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackOneCore = _availableVoices.FirstOrDefault(v => v.Language.StartsWith("ko", StringComparison.OrdinalIgnoreCase));
            if (fallbackOneCore is not null)
            {
                return new VoiceSelection(fallbackOneCore.Id, true, fallbackOneCore, null);
            }

            var fallbackSapi = _availableSapiVoices.FirstOrDefault(v => v.Culture.Name.StartsWith("ko", StringComparison.OrdinalIgnoreCase));
            if (fallbackSapi is not null)
            {
                return new VoiceSelection(fallbackSapi.Id, false, null, fallbackSapi);
            }
        }

        if (language.Equals("ja-JP", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackOneCore = _availableVoices.FirstOrDefault(v => v.Language.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
            if (fallbackOneCore is not null)
            {
                return new VoiceSelection(fallbackOneCore.Id, true, fallbackOneCore, null);
            }

            var fallbackSapi = _availableSapiVoices.FirstOrDefault(v => v.Culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
            if (fallbackSapi is not null)
            {
                return new VoiceSelection(fallbackSapi.Id, false, null, fallbackSapi);
            }
        }

        if (language.Equals("en-US", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackOneCore = _availableVoices.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (fallbackOneCore is not null)
            {
                return new VoiceSelection(fallbackOneCore.Id, true, fallbackOneCore, null);
            }

            var fallbackSapi = _availableSapiVoices.FirstOrDefault(v => v.Culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (fallbackSapi is not null)
            {
                return new VoiceSelection(fallbackSapi.Id, false, null, fallbackSapi);
            }
        }

        return new VoiceSelection(SpeechSynthesizer.DefaultVoice.Id, true, SpeechSynthesizer.DefaultVoice, null);
    }

    public void Dispose()
    {
        _isDisposing = true;
        if (!WaitForOperations(TimeSpan.FromSeconds(2)))
        {
            LoggingService.LogMessage("TTS operations did not stop within the disposal timeout.");
        }

        ForceCancelSapiOperations();
        _speechSynthesizer.Dispose();
        using var sapiDisposed = new ManualResetEventSlim();
        PostSapiAction(() =>
        {
            _sapiSynthesizer?.Dispose();
            sapiDisposed.Set();
        });
        sapiDisposed.Wait(TimeSpan.FromSeconds(2));
        CleanupSapiThreadResources();
        _sapiLock.Dispose();
    }

    private bool WaitForOperations(TimeSpan timeout)
    {
        Task[] operations;
        lock (_operationLock)
        {
            operations = _activeOperations.ToArray();
        }

        if (operations.Length == 0)
        {
            return true;
        }

        var allOperations = Task.WhenAll(operations);
        try
        {
            allOperations.Wait(timeout);
        }
        catch (AggregateException)
        {
        }

        return allOperations.IsCompleted;
    }

    private void CleanupSapiThreadResources()
    {
        _sapiWorkQueue.CompleteAdding();
        if (_sapiThread.Join(TimeSpan.FromSeconds(2)))
        {
            _sapiThreadReady.Dispose();
            _sapiWorkQueue.Dispose();
            return;
        }

        LoggingService.LogMessage("SAPI thread did not stop within the shutdown timeout; skipping work queue disposal.");
    }

    private void TrackSapiCompletion(TaskCompletionSource<object?> completion)
    {
        lock (_sapiCompletionLock)
        {
            _activeSapiCompletions.Add(completion);
        }

        _ = completion.Task.ContinueWith(_ =>
        {
            lock (_sapiCompletionLock)
            {
                _activeSapiCompletions.Remove(completion);
            }
        }, TaskScheduler.Default);
    }

    private void ForceCancelSapiOperations()
    {
        TaskCompletionSource<object?>[] completions;
        lock (_sapiCompletionLock)
        {
            completions = _activeSapiCompletions.ToArray();
        }

        if (completions.Length == 0)
        {
            return;
        }

        if (PostSapiAction(() => _sapiSynthesizer?.SpeakAsyncCancelAll()))
        {
            var allCompletions = Task.WhenAll(completions.Select(completion => completion.Task));
            try
            {
                allCompletions.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
            }
        }

        foreach (var completion in completions)
        {
            if (!completion.Task.IsCompleted)
            {
                completion.TrySetCanceled();
            }
        }
    }

    private bool HasActiveOperations()
    {
        lock (_operationLock)
        {
            return _activeOperations.Count > 0;
        }
    }

    public async Task<bool> WaitForOperationsAsync(TimeSpan timeout)
    {
        Task[] operations;
        lock (_operationLock)
        {
            operations = _activeOperations.ToArray();
        }

        if (operations.Length == 0)
        {
            return true;
        }

        var allOperations = Task.WhenAll(operations);
        if (await Task.WhenAny(allOperations, Task.Delay(timeout)) != allOperations)
        {
            return false;
        }

        try
        {
            await allOperations;
        }
        catch
        {
        }

        return true;
    }

    private void TrackOperation(Task operation)
    {
        lock (_operationLock)
        {
            _activeOperations.Add(operation);
        }

        _ = operation.ContinueWith(completedOperation =>
        {
            lock (_operationLock)
            {
                _activeOperations.Remove(completedOperation);
            }
        }, TaskScheduler.Default);
    }

    private sealed class VoiceSelection
    {
        public VoiceSelection(string voiceId, bool isOneCore, VoiceInformation? oneCoreVoice, System.Speech.Synthesis.VoiceInfo? sapiVoice)
        {
            VoiceId = voiceId;
            IsOneCore = isOneCore;
            OneCoreVoice = oneCoreVoice;
            SapiVoice = sapiVoice;
        }

        public string VoiceId { get; }
        public bool IsOneCore { get; }
        public VoiceInformation? OneCoreVoice { get; }
        public System.Speech.Synthesis.VoiceInfo? SapiVoice { get; }
    }
}
