using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private bool _standingWavesPlaying;
    private double _standingWaveRequestedTime;
    private double _standingWaveSpeed;
    private double _standingWaveLastRequest;

    private void StandingWaves_OnChanged(object? sender, EventArgs e)
    {
        if (_updatingUi) return;
        StopStandingWaves();
        _renderCts?.Cancel();
        WaveEditor.ShowError("");
        ScheduleRender();
    }

    private void StandingWaves_OnPlaybackRequested(object? sender, EventArgs e)
    {
        if (_standingWavesPlaying)
        {
            StopStandingWaves();
            ScheduleRender(immediate: true);
            return;
        }

        var settings = WaveEditor.Capture();
        _standingWaveRequestedTime = settings.Time >= StandingWaveSettings.MaxTime ? 0 : settings.Time;
        _standingWaveSpeed = settings.PlaybackSpeed;
        _standingWaveLastRequest = _clock.Elapsed.TotalMilliseconds;
        _standingWavesPlaying = true;
        WaveEditor.SetPlaying(true);
        WaveEditor.ShowError("");
        ScheduleRender(immediate: true);
    }

    private void AdvanceStandingWaves(double now)
    {
        if (!_standingWavesPlaying || _frameRequested) return;
        double elapsed = now - _standingWaveLastRequest;
        if (elapsed < 40) return;

        if (_standingWaveRequestedTime >= StandingWaveSettings.MaxTime)
        {
            StopStandingWaves();
            return;
        }

        _standingWaveRequestedTime = Math.Min(StandingWaveSettings.MaxTime,
            _standingWaveRequestedTime + Math.Min(elapsed / 1000, .15) * _standingWaveSpeed);
        _standingWaveLastRequest = now;
        RequestFrame(FrameQuality.Draft);
    }

    private void StopStandingWaves()
    {
        if (!_standingWavesPlaying) return;
        _standingWavesPlaying = false;
        _renderCts?.Cancel();
        WaveEditor.SetPlaying(false);
    }
}
