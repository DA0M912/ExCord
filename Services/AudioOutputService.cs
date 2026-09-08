using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ExCord.Services;

public sealed class AudioOutputService : IDisposable
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly object _playbackLock = new();
    private readonly object _operationLock = new();
    private readonly HashSet<Task> _activeOperations = new();
    private CancellationTokenSource? _currentPlaybackCancellation;

    public event EventHandler<string>? PlaybackIssue;

    public IReadOnlyList<string> GetAvailableOutputDeviceNames()
    {
        var deviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                deviceNames.Add(device.FriendlyName);
            }
            finally
            {
                device.Dispose();
            }
        }

        return deviceNames.ToArray();
    }

    public Task PlayAsync(byte[] pcmWaveData, string outputDeviceKeyword, bool monitorEnabled, double outputVolume, CancellationToken cancellationToken = default)
    {
        var operation = PlayCoreAsync(pcmWaveData, outputDeviceKeyword, monitorEnabled, outputVolume, cancellationToken);
        TrackOperation(operation);
        return operation;
    }

    private async Task PlayCoreAsync(byte[] pcmWaveData, string outputDeviceKeyword, bool monitorEnabled, double outputVolume, CancellationToken cancellationToken)
    {
        if (pcmWaveData is null || pcmWaveData.Length == 0)
        {
            return;
        }

        CancellationTokenSource playbackCancellation;
        lock (_playbackLock)
        {
            _currentPlaybackCancellation?.Cancel();
            playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _currentPlaybackCancellation = playbackCancellation;
        }

        try
        {
            var tasks = new List<Task>();

            var targetDevice = FindOutputDevice(outputDeviceKeyword);
            if (targetDevice is null)
            {
                PlaybackIssue?.Invoke(this, $"The specified output device could not be found: {outputDeviceKeyword}");
            }
            else
            {
                tasks.Add(PlayOnDeviceAsync(pcmWaveData, targetDevice, outputVolume, playbackCancellation.Token));
            }

            if (monitorEnabled)
            {
                var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDevice is not null)
                {
                    if (targetDevice is null || !string.Equals(targetDevice.ID, defaultDevice.ID, StringComparison.OrdinalIgnoreCase))
                    {
                        tasks.Add(PlayOnDeviceAsync(pcmWaveData, defaultDevice, outputVolume, playbackCancellation.Token));
                    }
                    else
                    {
                        defaultDevice.Dispose();
                    }
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }
        finally
        {
            lock (_playbackLock)
            {
                if (ReferenceEquals(_currentPlaybackCancellation, playbackCancellation))
                {
                    _currentPlaybackCancellation = null;
                }
            }

            playbackCancellation.Dispose();
        }
    }

    public void CancelCurrentPlayback()
    {
        lock (_playbackLock)
        {
            _currentPlaybackCancellation?.Cancel();
        }
    }

    private MMDevice? FindOutputDevice(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        MMDevice? matchingDevice = null;
        foreach (var device in _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (matchingDevice is null && device.FriendlyName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                matchingDevice = device;
                continue;
            }

            device.Dispose();
        }

        return matchingDevice;
    }

    private async Task PlayOnDeviceAsync(byte[] pcmWaveData, MMDevice device, double volume, CancellationToken cancellationToken)
    {
        if (device is null)
        {
            return;
        }

        using var ownedDevice = device;
        using var waveStream = CreateWaveStream(pcmWaveData);
        using var output = new WasapiOut(ownedDevice, AudioClientShareMode.Shared, false, 100);
        using var cancellationRegistration = cancellationToken.Register(static state => ((WasapiOut)state!).Stop(), output);
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var sampleProvider = waveStream.ToSampleProvider();
            var volumeProvider = new VolumeSampleProvider(sampleProvider)
            {
                Volume = (float)Math.Clamp(volume / 100d, 0d, 1d)
            };

            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    tcs.TrySetException(args.Exception);
                    return;
                }

                tcs.TrySetResult(null);
            };

            cancellationToken.ThrowIfCancellationRequested();
            output.Init(volumeProvider);
            output.Play();
            await tcs.Task;
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            PlaybackIssue?.Invoke(this, ex.Message);
            tcs.TrySetResult(null);
        }
    }

    private static WaveStream CreateWaveStream(byte[] data)
    {
        return new WaveFileReader(new System.IO.MemoryStream(data, writable: false));
    }

    public void Dispose()
    {
        CancelCurrentPlayback();
        lock (_playbackLock)
        {
            _currentPlaybackCancellation?.Dispose();
            _currentPlaybackCancellation = null;
        }

        _deviceEnumerator.Dispose();
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
}
