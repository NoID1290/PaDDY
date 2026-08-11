using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using System.Windows.Media.Animation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NoIDSoftwork.AudioProcessor;
using PaDDY.Models;
using PaDDY.Services;
using PaDDY.Helpers;
using NoIDSoftwork.EffectProcessor;
using NoIDSoftwork.EffectProcessor.Effects;

namespace PaDDY.Controls
{
    [SupportedOSPlatform("windows")]
    public partial class RecordingPadButton : WpfControl
    {
        static RecordingPadButton()
        {
        }

        /// <summary>
        /// When >0 the entrance animation is suppressed (bulk load in progress).
        /// Increment before loading a batch, decrement after.
        /// </summary>
        public static int SuppressEntranceAnimation;

        public RecordingEntry? Entry { get; private set; }

        /// <summary>
        /// Reference to RecordingStore used for on-demand materialization when PreloadAudioCache is false.
        /// </summary>
        public RecordingStore? Store { get; set; }

        /// <summary>
        /// Ensures the audio file is materialized on disk. If in Low RAM mode (file not pre-materialized),
        /// extracts the audio BLOB from <see cref="Store"/> on demand.
        /// </summary>
        public string EnsureMaterialized(RecordingStore? store = null)
        {
            if (Entry == null) return string.Empty;
            if (!string.IsNullOrEmpty(Entry.FilePath) && File.Exists(Entry.FilePath))
                return Entry.FilePath;

            var targetStore = store ?? Store;
            if (targetStore != null && !string.IsNullOrEmpty(Entry.RecordingId))
            {
                try
                {
                    string codec = !string.IsNullOrEmpty(Entry.Codec) ? Entry.Codec : "wav";
                    Entry.FilePath = targetStore.MaterializeToTemp(Entry.RecordingId, codec);
                }
                catch { }
            }
            return Entry.FilePath;
        }

        // Device routing injected from MainWindow
        public int OutputDeviceIndex { get; set; } = 0;

        // Listen device: -2 = disabled, -1 = default, 0..N = specific
        public int ListenDeviceIndex { get; set; } = -2;

        // 0 = default, 1..N = devices 0..N-1
        public int TrimEditorOutputDeviceIndex { get; set; } = 0;

        // Volume controls (0.0–1.0)
        public float OutputVolume { get; set; } = 1.0f;
        public float ListenVolume { get; set; } = 1.0f;

        // ── Global Effects (injected from MainWindow per AppSettings) ─────────
        /// <summary>Whether the global Auto Fade In/Out effect is enabled.</summary>
        public bool GlobalFadeEnabled { get; set; } = false;
        /// <summary>Global fade-in duration in milliseconds.</summary>
        public double GlobalFadeInDurationMs { get; set; } = 500.0;
        /// <summary>Global fade-out duration in milliseconds.</summary>
        public double GlobalFadeOutDurationMs { get; set; } = 500.0;

        /// <summary>Fired with (left, right) normalised 0-100 values during playback on the main output.</summary>
        public event Action<double, double>? PlaybackRmsChanged;
        /// <summary>Fired with (left, right) normalised 0-100 values during playback on the monitor output.</summary>
        public event Action<double, double>? ListenPlaybackRmsChanged;

        /// <summary>Global event fired when any pad starts or stops playback.</summary>
        public static event Action<string, bool>? GlobalPadPlaybackStateChanged;
        /// <summary>Global event fired with main output RMS values from any active pad.</summary>
        public static event Action<double, double>? GlobalPlaybackRmsChanged;
        /// <summary>Global event fired with listen output RMS values from any active pad.</summary>
        public static event Action<double, double>? GlobalListenPlaybackRmsChanged;

        private static readonly HashSet<RecordingPadButton> _activePlayingPads = new();
        private static readonly object _activePadsLock = new();

        /// <summary>Whether multi-pad playback is allowed (polyphonic mode).</summary>
        public static bool AllowMultiPadPlayback { get; set; } = true;

        public static string? ActivePlayingRecordingId
        {
            get
            {
                lock (_activePadsLock)
                {
                    return _activePlayingPads.FirstOrDefault()?.Entry?.RecordingId;
                }
            }
        }

        public static RecordingPadButton? ActivePlayingPadInstance
        {
            get
            {
                lock (_activePadsLock)
                {
                    return _activePlayingPads.FirstOrDefault();
                }
            }
        }

        public static bool IsAnyPadPlaying
        {
            get
            {
                lock (_activePadsLock)
                {
                    return _activePlayingPads.Count > 0;
                }
            }
        }

        public static bool IsRecordingPlaying(string? recordingId)
        {
            if (string.IsNullOrEmpty(recordingId)) return false;
            lock (_activePadsLock)
            {
                return _activePlayingPads.Any(p => p.Entry?.RecordingId == recordingId && p._isPlaying);
            }
        }

        public static void StopAllPlayback()
        {
            List<RecordingPadButton> playing;
            lock (_activePadsLock)
            {
                playing = _activePlayingPads.ToList();
            }
            foreach (var pad in playing)
            {
                pad.StopPlayback();
            }
        }

        public static void StopRecordingPlayback(string recordingId)
        {
            if (string.IsNullOrEmpty(recordingId)) return;
            List<RecordingPadButton> toStop;
            lock (_activePadsLock)
            {
                toStop = _activePlayingPads.Where(p => p.Entry?.RecordingId == recordingId).ToList();
            }
            foreach (var pad in toStop)
            {
                pad.StopPlayback();
            }
        }

        public double CurrentRmsL { get; private set; }
        public double CurrentRmsR { get; private set; }
        public double CurrentListenRmsL { get; private set; }
        public double CurrentListenRmsR { get; private set; }

        private static void BroadcastGlobalRms()
        {
            double sumL2 = 0;
            double sumR2 = 0;
            double sumListenL2 = 0;
            double sumListenR2 = 0;
            bool anyPlaying = false;

            lock (_activePadsLock)
            {
                foreach (var pad in _activePlayingPads)
                {
                    anyPlaying = true;
                    sumL2 += pad.CurrentRmsL * pad.CurrentRmsL;
                    sumR2 += pad.CurrentRmsR * pad.CurrentRmsR;
                    sumListenL2 += pad.CurrentListenRmsL * pad.CurrentListenRmsL;
                    sumListenR2 += pad.CurrentListenRmsR * pad.CurrentListenRmsR;
                }
            }

            if (!anyPlaying)
            {
                GlobalPlaybackRmsChanged?.Invoke(0, 0);
                GlobalListenPlaybackRmsChanged?.Invoke(0, 0);
                return;
            }

            double combinedL = Math.Min(100.0, Math.Sqrt(sumL2));
            double combinedR = Math.Min(100.0, Math.Sqrt(sumR2));
            double combinedListenL = Math.Min(100.0, Math.Sqrt(sumListenL2));
            double combinedListenR = Math.Min(100.0, Math.Sqrt(sumListenR2));

            GlobalPlaybackRmsChanged?.Invoke(combinedL, combinedR);
            GlobalListenPlaybackRmsChanged?.Invoke(combinedListenL, combinedListenR);
        }

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                _isFavorite = value;
                FavBtn.Content = value ? "★" : "☆";
                
                if (value)
                    FavBtn.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AccentAmberBrush");
                else
                    FavBtn.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "SubtleTextBrush");

                if (!_visualIsPlaying)
                {
                    if (value)
                        TileBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "AccentAmberBrush");
                    else
                        TileBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
                }
            }
        }

        private IWavePlayer? _player;
        private IUnifiedAudioReader? _reader;
        private PlaybackMeterProvider? _meterProvider;
        private VolumeSampleProvider? _outputVolumeProvider;
        private IWavePlayer? _listenPlayer;
        private IUnifiedAudioReader? _listenReader;
        private VolumeSampleProvider? _listenVolumeProvider;
        private PlaybackMeterProvider? _listenMeterProvider;
        private bool _isPlaying;
        private bool _visualIsPlaying;
        private Stopwatch? _playbackStopwatch;
        private bool _countdownActive;
        private DispatcherTimer? _perfCountdownTimer;
        private TimeSpan _playbackTotalDuration;

        /// <summary>Fired when the user clicks the inline delete (âœ•) or menu Delete.</summary>
        public event EventHandler? DeleteRequested;

        /// <summary>Fired when the user toggles the favorite (★) button.</summary>
        public event EventHandler? FavoriteToggled;
        /// <summary>Fired after a successful rename; args are (entry, newDisplayName).</summary>
        public event Action<RecordingEntry, string>? RecordingRenamed;
        /// <summary>Fired after an in-place editor save; arg is the updated entry.</summary>
        public event Action<RecordingEntry>? RecordingEdited;
        /// <summary>Fired when "Save as Copy" produces a new file; args are (newFilePath, addToFavorite).</summary>
        public event Action<string, bool>? RecordingCopied;
        /// <summary>Fired when the user changes the pad color; args are (entry, newHexColor).</summary>
        public event Action<RecordingEntry, string>? PadColorChanged;
        public RecordingPadButton()
        {
            InitializeComponent();

            // Re-apply custom colors if the global theme is modified/re-evaluated.
            ThemeManager.ThemeChanged += () =>
            {
                if (Entry != null)
                {
                    ApplyPadColor(Entry.PadColor);
                }
            };

            // Global pad state listener for cross-window and favorites/main synchronization
            GlobalPadPlaybackStateChanged += OnGlobalPadStateChanged;
            Unloaded += (_, _) => GlobalPadPlaybackStateChanged -= OnGlobalPadStateChanged;

            Loaded += (_, _) =>
            {
                if (Entry != null && IsRecordingPlaying(Entry.RecordingId) && !_isPlaying)
                {
                    StartVisualSlaveTimer();
                }

                // In performance mode, strip all hover/click EventTriggers from the tile
                // and skip the entrance animation entirely.
                if (ThemeManager.PerformanceMode)
                {
                    TileBorder.Triggers.Clear();
                    return;
                }

                if (SuppressEntranceAnimation > 0) return;
                try
                {
                    var entrance = (Storyboard)FindResource("EntranceAnimation");
                    entrance.Begin(this);
                }
                catch { }
            };

            MouseLeftButtonUp += (_, e) =>
            {
                // Don't trigger playback if the click was on an overlay button
                if (_dragInProgress)
                {
                    _dragInProgress = false;
                    return;
                }
                if (e.OriginalSource is not FrameworkElement fe ||
                    (!IsOverlayButton(fe)))
                {
                    TogglePlay();
                }
            };

            PreviewMouseLeftButtonDown += (_, e) =>
            {
                _dragInProgress = false;
                _dragStartPoint = e.GetPosition(null);
            };

            PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _dragInProgress)
                    return;
                if (Entry == null || string.IsNullOrEmpty(Entry.RecordingId))
                    return;
                if (e.OriginalSource is FrameworkElement fe && IsOverlayButton(fe))
                    return;

                var pos = e.GetPosition(null);
                if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;

                _dragInProgress = true;
                CurrentDraggedPad = this;
                DragGrabOffset = e.GetPosition(this);
                try
                {
                    DragStarting?.Invoke(this);
                    var data = new System.Windows.DataObject();
                    data.SetData(PadDragFormat, Entry?.RecordingId ?? string.Empty);
                    data.SetData(typeof(RecordingPadButton), this);
                    System.Windows.DragDrop.DoDragDrop(this, data, System.Windows.DragDropEffects.Move);
                }
                catch { }
                finally
                {
                    CurrentDraggedPad = null;
                    DragFinished?.Invoke(this);
                }
            };
        }

        /// <summary>DataObject format used when dragging a pad between panels/pages.</summary>
        public const string PadDragFormat = "PaddyRecordingPad";

        /// <summary>Currently active pad being dragged, available across window boundaries.</summary>
        public static RecordingPadButton? CurrentDraggedPad { get; set; }

        public static RecordingPadButton? GetDraggedPad(System.Windows.DragEventArgs e)
        {
            if (CurrentDraggedPad != null) return CurrentDraggedPad;
            if (e.Data.GetDataPresent(typeof(RecordingPadButton)) && e.Data.GetData(typeof(RecordingPadButton)) is RecordingPadButton pad)
                return pad;
            if (e.Data.GetDataPresent(PadDragFormat) && e.Data.GetData(PadDragFormat) is RecordingPadButton padFormat)
                return padFormat;
            return null;
        }

        /// <summary>Mouse offset within the pad where the drag began (used to position the drag ghost).</summary>
        public System.Windows.Point DragGrabOffset { get; private set; }

        /// <summary>Raised just before the drag-drop loop begins (host sets up the drag visual).</summary>
        public event Action<RecordingPadButton>? DragStarting;

        /// <summary>Raised after the drag-drop loop completes (host tears down the visual and commits).</summary>
        public event Action<RecordingPadButton>? DragFinished;

        private System.Windows.Point _dragStartPoint;
        private bool _dragInProgress;

        private static bool IsOverlayButton(FrameworkElement el)
        {
            DependencyObject? current = el;
            while (current != null)
            {
                if (current is WpfButton) return true;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        public void SetEntry(RecordingEntry entry)
        {
            Entry = entry;
            NameLabel.Text = entry.FileName;
            DurationLabel.Text = entry.DurationLabel;
            if (!string.IsNullOrWhiteSpace(entry.Transcription))
            {
                ToolTip = $"{entry.FileName}\n\n🤖 Transcription: \"{entry.Transcription}\"\n🏷️ Tags: {entry.Tags}";
            }
            else
            {
                ToolTip = entry.FileName;
            }
            if (NdIndicator != null)
                NdIndicator.Visibility = entry.IsNonDestructive ? Visibility.Visible : Visibility.Collapsed;

            ApplyPadColor(entry.PadColor);
        }

        // â”€â”€ Overlay button handlers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void FavBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            IsFavorite = !IsFavorite;
            if (Entry != null) Entry.IsFavorite = IsFavorite;
            FavoriteToggled?.Invoke(this, EventArgs.Empty);
        }

        private void DelBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            MenuDelete_Click(sender, e);
        }

        private void RenameBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            OpenRename();
        }

        private void ColorBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            OpenColorPicker();
        }

        public void OpenColorPicker()
        {
            if (Entry == null) return;
            var dialog = new Views.PadColorPickerDialog(Entry.PadColor, Entry.FileName)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() == true)
            {
                string newColor = dialog.SelectedHexColor;
                Entry.PadColor = newColor;
                ApplyPadColor(newColor);
                PadColorChanged?.Invoke(Entry, newColor);
            }
        }

        private void TrimBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            OpenAudioEditor();
        }

        private AudioEditorWindow? _activeAudioEditorWindow;

        public void OpenAudioEditor()
        {
            EnsureMaterialized();
            if (Entry == null || !File.Exists(Entry.FilePath)) return;
            StopPlayback();

            if (_activeAudioEditorWindow != null && _activeAudioEditorWindow.IsLoaded)
            {
                if (_activeAudioEditorWindow.WindowState == WindowState.Minimized)
                    _activeAudioEditorWindow.WindowState = WindowState.Normal;
                _activeAudioEditorWindow.Activate();
                _activeAudioEditorWindow.Focus();
                return;
            }

            var editor = new AudioEditorWindow(
                Entry.FilePath,
                Entry.RecordingId,
                TrimEditorOutputDeviceIndex - 1,
                Entry.DisplayName,
                Entry.IsNonDestructive,
                Entry.TrimStartMs,
                Entry.TrimEndMs,
                Entry.GainDb);
            _activeAudioEditorWindow = editor;
            editor.Closed += (s, args) =>
            {
                _activeAudioEditorWindow = null;
                if (editor.DialogResult == true)
                {
                    if (editor.CopyFilePath != null)
                    {
                        // "Save as Copy" — fire event so MainWindow adds a new pad
                        RecordingCopied?.Invoke(editor.CopyFilePath, editor.ShouldSaveToFavorite);
                    }
                    else
                    {
                        Entry.IsNonDestructive = editor.OutIsNonDestructive;
                        if (Entry.IsNonDestructive)
                        {
                            try
                            {
                                using var reader = AudioReaderFactory.Open(Entry.FilePath);
                                double totalSec = reader.TotalTime.TotalSeconds;
                                Entry.TrimStartMs = (long)(editor.OutTrimStartFraction * totalSec * 1000);
                                Entry.TrimEndMs = (long)(editor.OutTrimEndFraction * totalSec * 1000);
                                Entry.GainDb = editor.OutGainDb;
                                Entry.Duration = TimeSpan.FromSeconds((editor.OutTrimEndFraction - editor.OutTrimStartFraction) * totalSec);
                            }
                            catch { }
                        }
                        else
                        {
                            Entry.TrimStartMs = 0;
                            Entry.TrimEndMs = 0;
                            Entry.GainDb = 0.0;
                            try
                            {
                                using var reader = AudioReaderFactory.Open(Entry.FilePath);
                                Entry.Duration = reader.TotalTime;
                            }
                            catch { }
                        }
                        SetEntry(Entry);
                        RecordingEdited?.Invoke(Entry);

                        if (editor.ShouldSaveToFavorite && !IsFavorite)
                        {
                            IsFavorite = true;
                            Entry.IsFavorite = true;
                            FavoriteToggled?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
            };
            editor.Show();
        }

        // ── Right-click: play on listen/monitor device only ───────────────────────────────────
        private void OnMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsOverlayButton(e.OriginalSource as FrameworkElement ?? this)) return;
            e.Handled = true;
            StartPlaybackListenOnly();
        }

        // â”€â”€ Playback â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public void TogglePlay()
        {
            if (Entry == null || string.IsNullOrEmpty(Entry.RecordingId)) return;

            if (_isPlaying || IsRecordingPlaying(Entry.RecordingId))
            {
                StopRecordingPlayback(Entry.RecordingId);
            }
            else
            {
                if (!AllowMultiPadPlayback)
                {
                    StopAllPlayback();
                }
                StartPlayback();
            }
        }

        private void OnGlobalPadStateChanged(string recId, bool isPlaying)
        {
            if (Entry == null || Entry.RecordingId != recId) return;
            if (_isPlaying) return;

            Dispatcher.InvokeAsync(() =>
            {
                if (isPlaying)
                {
                    StartVisualSlaveTimer();
                }
                else
                {
                    StopCountdownTimer();
                    SetPlayingVisual(false);
                }
            });
        }

        private void StartVisualSlaveTimer()
        {
            if (Entry == null) return;

            if (Entry.IsNonDestructive && Entry.TrimEndMs > Entry.TrimStartMs)
            {
                _playbackTotalDuration = TimeSpan.FromMilliseconds(Entry.TrimEndMs - Entry.TrimStartMs);
            }
            else
            {
                _playbackTotalDuration = Entry.Duration;
            }

            RecordingPadButton? master;
            lock (_activePadsLock)
            {
                master = _activePlayingPads.FirstOrDefault(p => p.Entry?.RecordingId == Entry.RecordingId);
            }

            if (master?._playbackStopwatch != null)
                _playbackStopwatch = master._playbackStopwatch;
            else
                _playbackStopwatch = Stopwatch.StartNew();

            _countdownActive = true;

            if (Helpers.ThemeManager.PerformanceMode)
            {
                _perfCountdownTimer?.Stop();
                _perfCountdownTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _perfCountdownTimer.Tick += OnCountdownTick;
                _perfCountdownTimer.Start();
            }
            else
            {
                CompositionTarget.Rendering -= OnCountdownTick;
                CompositionTarget.Rendering += OnCountdownTick;
            }

            SetPlayingVisual(true);
        }

        private ISampleProvider BuildNonDestructiveSource(IUnifiedAudioReader reader)
        {
            ISampleProvider sp = reader.AsSampleProvider();
            if (Entry == null) return sp;

            if (Entry.IsNonDestructive)
            {
                double startSec = Entry.TrimStartMs / 1000.0;
                double endSec = Entry.TrimEndMs / 1000.0;

                // Seek reader to the start offset
                if (startSec > 0.001)
                {
                    reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                }

                // Apply gain
                if (Math.Abs(Entry.GainDb) > 0.01)
                {
                    sp = new VolumeSampleProvider(sp)
                    {
                        Volume = (float)Math.Pow(10.0, Entry.GainDb / 20.0)
                    };
                }

                // Apply trim end (Take)
                if (endSec > 0.001 && endSec > startSec)
                {
                    sp = new OffsetSampleProvider(sp)
                    {
                        Take = TimeSpan.FromSeconds(endSec - startSec)
                    };
                }

                // Apply per-clip effects
                var effectChain = EffectChainFactory.CreatePerClip();
                var settings = EffectSettingsManager.Load();
                if (settings.PerClipChains.TryGetValue(Entry.RecordingId, out var cfg))
                {
                    EffectSettingsManager.ApplyConfig(effectChain, cfg);
                }

                // Prepare effect chain (prime FadeEffect with total frames)
                double durationSec = (endSec > startSec) ? (endSec - startSec) : (Entry.Duration.TotalSeconds);
                effectChain.Reset();
                long totalFrames = (long)(durationSec * sp.WaveFormat.SampleRate);
                foreach (var effect in effectChain.Effects)
                {
                    if (effect is FadeEffect fade)
                    {
                        fade.TotalFrames = totalFrames;
                        break;
                    }
                }

                sp = new EffectSampleProvider(sp, effectChain);
            }

            // ── Global Effects (non-destructive, applied to every pad playback) ─
            if (GlobalFadeEnabled)
            {
                var globalFadeChain = new EffectChain();
                var globalFade = new FadeEffect
                {
                    IsEnabled = true,
                    FadeInDurationMs = GlobalFadeInDurationMs,
                    FadeOutDurationMs = GlobalFadeOutDurationMs
                };

                // Prime TotalFrames so fade-out knows when to start
                double durationSec = (Entry != null && Entry.IsNonDestructive)
                    ? (Entry.TrimEndMs > Entry.TrimStartMs
                        ? (Entry.TrimEndMs - Entry.TrimStartMs) / 1000.0
                        : Entry.Duration.TotalSeconds)
                    : Entry?.Duration.TotalSeconds ?? 0.0;

                if (durationSec > 0)
                    globalFade.TotalFrames = (long)(durationSec * sp.WaveFormat.SampleRate);

                globalFadeChain.Add(globalFade);
                globalFadeChain.Reset();
                sp = new EffectSampleProvider(sp, globalFadeChain);
            }

            return sp;
        }

        private void StartPlayback()
        {
            EnsureMaterialized();
            if (Entry == null || !File.Exists(Entry.FilePath)) return;
            StopPlayback();

            try
            {
                _isPlaying = true;
                lock (_activePadsLock)
                {
                    _activePlayingPads.Add(this);
                }

                _reader = AudioReaderFactory.Open(Entry.FilePath);
                ISampleProvider rawSource = BuildNonDestructiveSource(_reader);
                ISampleProvider playbackSource = BuildPlaybackSource(rawSource);
                _outputVolumeProvider = new VolumeSampleProvider(playbackSource)
                {
                    Volume = Math.Clamp(OutputVolume, 0.0f, 1.0f)
                };
                _meterProvider = new PlaybackMeterProvider(_outputVolumeProvider);
                _meterProvider.RmsLevelChanged += (l, r) =>
                {
                    CurrentRmsL = l;
                    CurrentRmsR = r;
                    PlaybackRmsChanged?.Invoke(l, r);
                    BroadcastGlobalRms();
                };
                _player = AudioOutputDeviceResolver.CreateWasapiPlayer(OutputDeviceIndex, 100);
                _player.Init(_meterProvider.ToWaveProvider16());
                _player.Volume = 1.0f;
                _player.PlaybackStopped += (_, _) => Dispatcher.Invoke(StopPlayback);
                _player.Play();

                // Start the countdown timer using the main reader
                StartCountdownTimer(_reader);

                if (ListenDeviceIndex >= -1)
                {
                    _listenReader = AudioReaderFactory.Open(Entry.FilePath);
                    ISampleProvider rawListen = BuildNonDestructiveSource(_listenReader);
                    ISampleProvider listenSource = BuildPlaybackSource(rawListen);
                    _listenVolumeProvider = new VolumeSampleProvider(listenSource)
                    {
                        Volume = Math.Clamp(ListenVolume, 0.0f, 1.0f)
                    };
                    _listenMeterProvider = new PlaybackMeterProvider(_listenVolumeProvider);
                    _listenMeterProvider.RmsLevelChanged += (l, r) =>
                    {
                        CurrentListenRmsL = l;
                        CurrentListenRmsR = r;
                        ListenPlaybackRmsChanged?.Invoke(l, r);
                        BroadcastGlobalRms();
                    };
                    _listenPlayer = AudioOutputDeviceResolver.CreateWasapiPlayer(ListenDeviceIndex, 120);
                    _listenPlayer.Init(_listenMeterProvider.ToWaveProvider16());
                    _listenPlayer.Volume = 1.0f;
                    _listenPlayer.Play();
                }

                SetPlayingVisual(true);
                GlobalPadPlaybackStateChanged?.Invoke(Entry.RecordingId, true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Playback error:\n{ex.Message}", "PaDDY",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                StopPlayback();
            }
        }

        /// <summary>Plays audio only on the listen/monitor device (right-click behaviour).</summary>
        private void StartPlaybackListenOnly()
        {
            EnsureMaterialized();
            if (Entry == null || !File.Exists(Entry.FilePath)) return;
            if (ListenDeviceIndex < -1) return; // listen disabled

            StopPlayback();
            try
            {
                _isPlaying = true;
                lock (_activePadsLock)
                {
                    _activePlayingPads.Add(this);
                }

                _listenReader = AudioReaderFactory.Open(Entry.FilePath);
                ISampleProvider rawListen = BuildNonDestructiveSource(_listenReader);
                ISampleProvider listenSource = BuildPlaybackSource(rawListen);
                _listenVolumeProvider = new VolumeSampleProvider(listenSource)
                {
                    Volume = Math.Clamp(ListenVolume, 0.0f, 1.0f)
                };
                _listenMeterProvider = new PlaybackMeterProvider(_listenVolumeProvider);
                _listenMeterProvider.RmsLevelChanged += (l, r) =>
                {
                    CurrentListenRmsL = l;
                    CurrentListenRmsR = r;
                    ListenPlaybackRmsChanged?.Invoke(l, r);
                    BroadcastGlobalRms();
                };
                _listenPlayer = AudioOutputDeviceResolver.CreateWasapiPlayer(ListenDeviceIndex, 120);
                _listenPlayer.Init(_listenMeterProvider.ToWaveProvider16());
                _listenPlayer.Volume = 1.0f;
                _listenPlayer.PlaybackStopped += (_, _) => Dispatcher.Invoke(StopPlayback);
                _listenPlayer.Play();

                // Start the countdown timer using the listen reader
                StartCountdownTimer(_listenReader);
                SetPlayingVisual(true);
                GlobalPadPlaybackStateChanged?.Invoke(Entry.RecordingId, true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Playback error:\n{ex.Message}", "PaDDY",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                StopPlayback();
            }
        }

        public void StopPlayback()
        {
            string? stoppedId = Entry?.RecordingId;
            bool wasPlaying = _isPlaying;
            _isPlaying = false;

            lock (_activePadsLock)
            {
                _activePlayingPads.Remove(this);
            }

            StopCountdownTimer();
            _player?.Stop();
            _player?.Dispose();
            _player = null;
            _meterProvider = null;
            _outputVolumeProvider = null;
            _reader?.Dispose();
            _reader = null;
            _listenPlayer?.Stop();
            _listenPlayer?.Dispose();
            _listenPlayer = null;
            _listenReader?.Dispose();
            _listenReader = null;
            _listenVolumeProvider = null;
            _listenMeterProvider = null;

            CurrentRmsL = 0;
            CurrentRmsR = 0;
            CurrentListenRmsL = 0;
            CurrentListenRmsR = 0;

            PlaybackRmsChanged?.Invoke(0, 0);
            ListenPlaybackRmsChanged?.Invoke(0, 0);
            BroadcastGlobalRms();

            if (wasPlaying && !string.IsNullOrEmpty(stoppedId))
            {
                if (!IsRecordingPlaying(stoppedId))
                {
                    GlobalPadPlaybackStateChanged?.Invoke(stoppedId, false);
                }
            }

            SetPlayingVisual(false);
        }

        public void RefreshLiveVolumes()
        {
            if (_outputVolumeProvider != null)
                _outputVolumeProvider.Volume = Math.Clamp(OutputVolume, 0.0f, 1.0f);
            if (_listenVolumeProvider != null)
                _listenVolumeProvider.Volume = Math.Clamp(ListenVolume, 0.0f, 1.0f);

            if (_player != null)
                _player.Volume = 1.0f;
            if (_listenPlayer != null)
                _listenPlayer.Volume = 1.0f;
        }

        private void SetPlayingVisual(bool playing)
        {
            _visualIsPlaying = playing;
            IconText.Text = playing ? "⏹" : "🎤";

            if (playing)
            {
                TileBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "AccentGreenBrush");
            }
            else
            {
                if (_isFavorite)
                    TileBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "AccentAmberBrush");
                else
                {
                    if (Entry != null && !string.IsNullOrWhiteSpace(Entry.PadColor))
                    {
                        try
                        {
                            var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(Entry.PadColor);
                            TileBorder.BorderBrush = new SolidColorBrush(c);
                        }
                        catch
                        {
                            TileBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
                        }
                    }
                    else
                    {
                        TileBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
                    }
                }
            }
        }

        public void ApplyPadColor(string? hexColor)
        {
            if (string.IsNullOrWhiteSpace(hexColor))
            {
                TileBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "CardBgBrush");
                if (!_visualIsPlaying)
                {
                    if (_isFavorite)
                        TileBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "AccentAmberBrush");
                    else
                        TileBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
                }

                // Restore default theme-based text and icon brushes
                NameLabel.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
                DurationLabel.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
                IconText.SetResourceReference(TextBlock.ForegroundProperty, "ControlTextBrush");

                FavBtn.SetResourceReference(WpfButton.ForegroundProperty, _isFavorite ? "AccentAmberBrush" : "SubtleTextBrush");
                ColorBtn.SetResourceReference(WpfButton.ForegroundProperty, "SubtleTextBrush");
                DelBtn.SetResourceReference(WpfButton.ForegroundProperty, "SubtleTextBrush");
                ExportBtn.SetResourceReference(WpfButton.ForegroundProperty, "SubtleTextBrush");
                RenameBtn.SetResourceReference(WpfButton.ForegroundProperty, "SubtleTextBrush");
                TrimBtn.SetResourceReference(WpfButton.ForegroundProperty, "SubtleTextBrush");
            }
            else
            {
                try
                {
                    var baseColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);

                    // Darken background slightly for rich tone & high text readability
                    var bg = Color.FromArgb(0xEE, (byte)(baseColor.R * 0.45), (byte)(baseColor.G * 0.45), (byte)(baseColor.B * 0.45));
                    TileBorder.Background = new SolidColorBrush(bg);

                    if (!_isPlaying)
                    {
                        if (_isFavorite)
                            TileBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "AccentAmberBrush");
                        else
                            TileBorder.BorderBrush = new SolidColorBrush(baseColor);
                    }

                    // Calculate perceived brightness of the resulting background color (YIQ formula)
                    double brightness = (bg.R * 299 + bg.G * 587 + bg.B * 114) / 1000.0;

                    // If background is dark, force light text colors for perfect readability
                    System.Windows.Media.Brush textBrush = brightness < 128 ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black;
                    System.Windows.Media.Brush subtleBrush = brightness < 128 
                        ? new SolidColorBrush(Color.FromRgb(0xBB, 0xCC, 0xEE)) 
                        : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x55));

                    NameLabel.Foreground = textBrush;
                    DurationLabel.Foreground = subtleBrush;
                    IconText.Foreground = textBrush;

                    if (!_isFavorite)
                        FavBtn.Foreground = subtleBrush;
                    else
                        FavBtn.SetResourceReference(WpfButton.ForegroundProperty, "AccentAmberBrush");

                    ColorBtn.Foreground = subtleBrush;
                    DelBtn.Foreground = subtleBrush;
                    ExportBtn.Foreground = subtleBrush;
                    RenameBtn.Foreground = subtleBrush;
                    TrimBtn.Foreground = subtleBrush;
                }
                catch
                {
                    TileBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "CardBgBrush");
                }
            }
        }

        // ── Real-time countdown ────────────────────────────────────────────
        // Normal mode: Stopwatch + CompositionTarget.Rendering (~60 fps)
        // Performance mode: Stopwatch + DispatcherTimer @ 200 ms
        private void StartCountdownTimer(IUnifiedAudioReader? reader)
        {
            if (reader == null || Entry == null) return;

            // Calculate the effective playback duration
            if (Entry.IsNonDestructive && Entry.TrimEndMs > Entry.TrimStartMs)
            {
                _playbackTotalDuration = TimeSpan.FromMilliseconds(Entry.TrimEndMs - Entry.TrimStartMs);
            }
            else
            {
                _playbackTotalDuration = Entry.Duration;
            }

            _playbackStopwatch = Stopwatch.StartNew();
            _countdownActive = true;

            if (Helpers.ThemeManager.PerformanceMode)
            {
                _perfCountdownTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _perfCountdownTimer.Tick += OnCountdownTick;
                _perfCountdownTimer.Start();
            }
            else
            {
                CompositionTarget.Rendering += OnCountdownTick;
            }
        }

        private void StopCountdownTimer()
        {
            if (_countdownActive)
            {
                if (_perfCountdownTimer != null)
                {
                    _perfCountdownTimer.Stop();
                    _perfCountdownTimer.Tick -= OnCountdownTick;
                    _perfCountdownTimer = null;
                }
                else
                {
                    CompositionTarget.Rendering -= OnCountdownTick;
                }
                _countdownActive = false;
            }
            _playbackStopwatch?.Stop();
            _playbackStopwatch = null;

            // Restore the static duration label
            if (Entry != null)
                DurationLabel.Text = Entry.DurationLabel;
        }

        private void OnCountdownTick(object? sender, EventArgs e)
        {
            if (_playbackStopwatch == null || Entry == null)
            {
                StopCountdownTimer();
                return;
            }

            TimeSpan elapsed = _playbackStopwatch.Elapsed;
            TimeSpan remaining = _playbackTotalDuration - elapsed;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            // Format: show tenths-of-a-second for real-time feel
            if (remaining.TotalMinutes >= 1)
            {
                DurationLabel.Text = $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}.{remaining.Milliseconds / 100}";
            }
            else
            {
                DurationLabel.Text = $"{remaining.Seconds}.{remaining.Milliseconds / 100}s";
            }
        }

        private static ISampleProvider BuildPlaybackSource(ISampleProvider source)
        {
            if (source.WaveFormat.Channels == 1)
            {
                return source.ToStereo();
            }

            if (source.WaveFormat.Channels > 2)
            {
                // Route front-left/front-right to stereo for robust device compatibility.
                var mux = new MultiplexingSampleProvider(new[] { source }, 2);
                mux.ConnectInputToOutput(0, 0);
                mux.ConnectInputToOutput(1, 1);
                return mux;
            }

            return source;
        }

        // â”€â”€ Context menu handlers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void MenuRename_Click(object sender, RoutedEventArgs e)
        {
            OpenRename();
        }

        public void OpenRename()
        {
            if (Entry == null) return;

            var dialog = new RenameDialog(Entry.FileName)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true) return;

            string newName = dialog.NewName.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;

            // Preserve original extension from the display name if user omits it.
            string originalExt = Path.GetExtension(
                string.IsNullOrEmpty(Entry.DisplayName) ? Entry.FilePath : Entry.DisplayName);
            if (!Path.HasExtension(newName))
                newName += originalExt;

            // Update the display name in memory and notify MainWindow to persist to DB.
            Entry.DisplayName = newName;
            SetEntry(Entry);
            RecordingRenamed?.Invoke(Entry, newName);
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Entry == null) return;
            StopPlayback();
            // Temp file cleanup and DB deletion are handled by MainWindow via DeleteRequested.
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            EnsureMaterialized();
            if (Entry == null || !File.Exists(Entry.FilePath)) return;

            string ext = Path.GetExtension(Entry.FilePath);
            string defaultName = string.IsNullOrEmpty(Entry.DisplayName)
                ? Path.GetFileName(Entry.FilePath)
                : Path.ChangeExtension(Entry.DisplayName, ext);

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export recording",
                FileName = defaultName,
                DefaultExt = ext,
                Filter = $"Audio (*{ext})|*{ext}|All files (*.*)|*.*"
            };

            if (dlg.ShowDialog() != true) return;
            File.Copy(Entry.FilePath, dlg.FileName, overwrite: true);
        }

        private void OnPadMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Entry == null) return;
            StartPlaybackListenOnly();
            e.Handled = true;
        }

        public void NormalizeLoudness()
        {
            EnsureMaterialized();
            if (Entry == null || !File.Exists(Entry.FilePath)) return;

            try
            {
                double measuredLufs = LoudnessNormalizer.MeasureIntegratedLoudness(Entry.FilePath);
                bool success = LoudnessNormalizer.NormalizeWavFile(Entry.FilePath, Entry.FilePath, -14.0);
                if (success)
                {
                    double newLufs = LoudnessNormalizer.MeasureIntegratedLoudness(Entry.FilePath);
                    Entry.LufsValue = newLufs;
                    System.Windows.MessageBox.Show($"Pad normalized successfully!\nOriginal Loudness: {measuredLufs:F1} LUFS\nNormalized Loudness: {newLufs:F1} LUFS", "LUFS Loudness Normalization", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to normalize pad: {ex.Message}", "Normalization Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        public async void TranscribePad()
        {
            EnsureMaterialized();
            if (Entry == null || !File.Exists(Entry.FilePath)) return;

            try
            {
                using var service = new SpeechRecognitionService();
                string text = await service.TranscribeAsync(Entry.FilePath, "tiny", "Auto");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    string tags = SpeechRecognitionService.ExtractTags(text);
                    Entry.Transcription = text;
                    Entry.Tags = tags;
                    ToolTip = $"{Entry.FileName}\n\n🤖 Transcription: \"{text}\"\n🏷️ Tags: {tags}";
                    System.Windows.MessageBox.Show($"Transcription: \"{text}\"\nGenerated Tags: {tags}", "Speech Transcription", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("No speech was recognized in this pad.", "Speech Transcription", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Transcription error: {ex.Message}", "Speech Recognition", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }
}
