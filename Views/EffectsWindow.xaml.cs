using System.Runtime.Versioning;
using System.Windows;
using NoIDSoftwork.EffectProcessor;
using NoIDSoftwork.EffectProcessor.Effects;

namespace PaDDY.Views;

/// <summary>
/// Effect chain editor window.
/// When <paramref name="isPerClip"/> is true the Fade section is shown and a
/// "Use global defaults" toggle is available. In global mode both are hidden.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class EffectsWindow : Window
{
    private readonly IEffectChain _chain;
    private readonly bool _isPerClip;

    private FadeEffect? _fade;
    private NoiseGateEffect? _gate;
    private EchoEffect? _echo;
    private EqualizerEffect? _eq;

    // Suppresses slider-changed callbacks while loading initial values
    private bool _loading;

    public EffectsWindow(IEffectChain chain, bool isPerClip)
    {
        _loading = true;      // suppress ValueChanged events fired during XAML init
        InitializeComponent();

        _chain = chain;
        _isPerClip = isPerClip;

        // Resolve typed effect references from the chain
        foreach (var effect in chain.Effects)
        {
            switch (effect)
            {
                case FadeEffect f: _fade = f; break;
                case NoiseGateEffect g: _gate = g; break;
                case EchoEffect e: _echo = e; break;
                case EqualizerEffect q: _eq = q; break;
            }
        }

        // Hide Fade section when editing the global chain
        FadeSection.Visibility = isPerClip ? Visibility.Visible : Visibility.Collapsed;

        if (isPerClip)
            TitleText.Text = "Effects — Per-Clip";
        else
            TitleText.Text = "Effects — Global Defaults";

        LoadValues();
    }

    // ── Load / Save ───────────────────────────────────────────────────────────

    private void LoadValues()
    {
        _loading = true;
        try
        {
            // Fade
            if (_fade != null)
            {
                FadeEnabledCheck.IsChecked = _fade.IsEnabled;
                FadeInSlider.Value = _fade.FadeInDurationMs;
                FadeOutSlider.Value = _fade.FadeOutDurationMs;
                UpdateFadeLabels();
            }

            // Gate
            if (_gate != null)
            {
                GateEnabledCheck.IsChecked = _gate.IsEnabled;
                GateThresholdSlider.Value = _gate.ThresholdDb;
                GateAttackSlider.Value = _gate.AttackMs;
                GateReleaseSlider.Value = _gate.ReleaseMs;
                UpdateGateLabels();
            }

            // Echo
            if (_echo != null)
            {
                EchoEnabledCheck.IsChecked = _echo.IsEnabled;
                EchoDelaySlider.Value = _echo.DelayMs;
                EchoFeedbackSlider.Value = _echo.Feedback;
                EchoMixSlider.Value = _echo.Mix;
                UpdateEchoLabels();
            }

            // EQ
            if (_eq != null)
            {
                EqEnabledCheck.IsChecked = _eq.IsEnabled;
                EqSubBassSlider.Value = _eq.SubBassDb;
                EqBassSlider.Value = _eq.BassDb;
                EqMidSlider.Value = _eq.MidDb;
                EqPresenceSlider.Value = _eq.PresenceDb;
                EqTrebleSlider.Value = _eq.TrebleDb;
                UpdateEqLabels();
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void CommitValues()
    {
        if (_fade != null)
        {
            _fade.IsEnabled = FadeEnabledCheck.IsChecked == true;
            _fade.FadeInDurationMs = FadeInSlider.Value;
            _fade.FadeOutDurationMs = FadeOutSlider.Value;
        }

        if (_gate != null)
        {
            _gate.IsEnabled = GateEnabledCheck.IsChecked == true;
            _gate.ThresholdDb = GateThresholdSlider.Value;
            _gate.AttackMs = GateAttackSlider.Value;
            _gate.ReleaseMs = GateReleaseSlider.Value;
        }

        if (_echo != null)
        {
            _echo.IsEnabled = EchoEnabledCheck.IsChecked == true;
            _echo.DelayMs = EchoDelaySlider.Value;
            _echo.Feedback = EchoFeedbackSlider.Value;
            _echo.Mix = EchoMixSlider.Value;
        }

        if (_eq != null)
        {
            _eq.IsEnabled = EqEnabledCheck.IsChecked == true;
            _eq.SubBassDb = EqSubBassSlider.Value;
            _eq.BassDb = EqBassSlider.Value;
            _eq.MidDb = EqMidSlider.Value;
            _eq.PresenceDb = EqPresenceSlider.Value;
            _eq.TrebleDb = EqTrebleSlider.Value;
        }
    }

    // ── Label updaters ────────────────────────────────────────────────────────

    private void UpdateFadeLabels()
    {
        FadeInLabel.Text = $"{(int)FadeInSlider.Value}";
        FadeOutLabel.Text = $"{(int)FadeOutSlider.Value}";
    }

    private void UpdateGateLabels()
    {
        GateThresholdLabel.Text = $"{(int)GateThresholdSlider.Value}";
        GateAttackLabel.Text = $"{(int)GateAttackSlider.Value}";
        GateReleaseLabel.Text = $"{(int)GateReleaseSlider.Value}";
    }

    private void UpdateEchoLabels()
    {
        EchoDelayLabel.Text = $"{(int)EchoDelaySlider.Value}";
        EchoFeedbackLabel.Text = $"{EchoFeedbackSlider.Value:F2}";
        EchoMixLabel.Text = $"{EchoMixSlider.Value:F2}";
    }

    private void UpdateEqLabels()
    {
        EqSubBassLabel.Text = $"{(int)EqSubBassSlider.Value:+#;-#;0} dB";
        EqBassLabel.Text = $"{(int)EqBassSlider.Value:+#;-#;0} dB";
        EqMidLabel.Text = $"{(int)EqMidSlider.Value:+#;-#;0} dB";
        EqPresenceLabel.Text = $"{(int)EqPresenceSlider.Value:+#;-#;0} dB";
        EqTrebleLabel.Text = $"{(int)EqTrebleSlider.Value:+#;-#;0} dB";
    }

    // ── Slider event handlers ─────────────────────────────────────────────────

    private void FadeInSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateFadeLabels();
    }

    private void FadeOutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateFadeLabels();
    }

    private void GateThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateGateLabels();
    }

    private void GateAttackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateGateLabels();
    }

    private void GateReleaseSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateGateLabels();
    }

    private void EchoDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateEchoLabels();
    }

    private void EchoFeedbackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateEchoLabels();
    }

    private void EchoMixSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateEchoLabels();
    }

    private void EqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateEqLabels();
    }

    // ── Buttons ───────────────────────────────────────────────────────────────


    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            FadeEnabledCheck.IsChecked = false;
            FadeInSlider.Value = 500;
            FadeOutSlider.Value = 500;

            GateEnabledCheck.IsChecked = false;
            GateThresholdSlider.Value = -40;
            GateAttackSlider.Value = 10;
            GateReleaseSlider.Value = 100;

            EchoEnabledCheck.IsChecked = false;
            EchoDelaySlider.Value = 200;
            EchoFeedbackSlider.Value = 0.3;
            EchoMixSlider.Value = 0.4;

            EqEnabledCheck.IsChecked = false;
            EqSubBassSlider.Value = 0;
            EqBassSlider.Value = 0;
            EqMidSlider.Value = 0;
            EqPresenceSlider.Value = 0;
            EqTrebleSlider.Value = 0;
        }
        finally
        {
            _loading = false;
        }

        UpdateFadeLabels();
        UpdateGateLabels();
        UpdateEchoLabels();
        UpdateEqLabels();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitValues();
        DialogResult = true;
    }

    private void ChromeClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void FadeChevron_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        bool expand = FadeContent.Visibility == Visibility.Collapsed;
        FadeContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        FadeChevron.Text = expand ? "\u25BC" : "\u25BA";
    }

    private void GateChevron_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        bool expand = GateContent.Visibility == Visibility.Collapsed;
        GateContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        GateChevron.Text = expand ? "\u25BC" : "\u25BA";
    }

    private void EchoChevron_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        bool expand = EchoContent.Visibility == Visibility.Collapsed;
        EchoContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        EchoChevron.Text = expand ? "\u25BC" : "\u25BA";
    }

    private void EqChevron_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        bool expand = EqContent.Visibility == Visibility.Collapsed;
        EqContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        EqChevron.Text = expand ? "\u25BC" : "\u25BA";
    }
}
