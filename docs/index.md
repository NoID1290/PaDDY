---
layout: default
title: Home
---

<!-- markdownlint-disable MD033 -->
<meta name="google-site-verification" content="BWApMIBBsK_hOKJBuRTUtVKgj3wVTcayKgDoyjzTk_Q" />

<!-- Hero Section -->
<section class="hero">
  <div class="container text-center">
    <img class="hero-image" src="{{ '/assets/img/hero.png' | relative_url }}" alt="PaDDY wordmark logo" style="max-width: 400px; margin-bottom: 1.5rem;">
    <p class="tagline" style="font-size: 1.35rem; font-weight: 500; max-width: 700px; margin: 0 auto 2rem;">
      Fast voice and system-audio capture for Windows, built around a pad-based workflow for recording, organizing, monitoring, trimming, and replaying short clips.
    </p>
    <div class="cta-row" style="margin-bottom: 1rem;">
      <a class="btn btn-primary" href="https://github.com/NoID1290/PaDDY/releases" style="padding: 0.75rem 1.5rem; font-weight: bold; margin-right: 0.75rem;">📦 Download Latest</a>
      <a class="btn btn-secondary" href="https://github.com/NoID1290/PaDDY" style="padding: 0.75rem 1.5rem; font-weight: bold;">💻 View Source</a>
    </div>
    <p class="meta" style="font-family: monospace; opacity: 0.8;">v1.8.1.0712</p>
  </div>
</section>

<hr style="border: 0; border-top: 1px solid dashed; margin: 3rem 0; opacity: 0.2;">

<!-- Feature Highlights Grid -->
<section id="features" class="section">
  <div class="container">
    <h2 style="text-align: center; margin-bottom: 2.5rem;">✨ Core Engine Highlights</h2>
    <div class="grid" style="display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 1.5rem;">
      <article class="card">
        <h3>🎙️ AutoVAD Mode</h3>
        <p>Monitors incoming audio signals dynamically, cutting clips when speech begins and cleanly stopping them when silence thresholds are met.</p>
      </article>
      <article class="card">
        <h3>⚡ Key Buffer Mode</h3>
        <p>Keeps a continuous, low-overhead rolling audio buffer running silently. Smash a hotkey to save the last <em>N</em> seconds out of the past instantly.</p>
      </article>
      <article class="card">
        <h3>🎛️ App-Specific Loopback</h3>
        <p>Target a distinct, audio-producing application window, or capture microphone inputs and system-wide outputs simultaneously.</p>
      </article>
      <article class="card">
        <h3>🎹 Pad-Based Workflow</h3>
        <p>Persistent, robust SQLite database tracking favorites, clip states, and custom metadata directly from responsive dashboard pads.</p>
      </article>
      <article class="card">
        <h3>📊 Pro Level Metering</h3>
        <p>Track live inputs, playback outputs, and independent monitor endpoints with dedicated RMS meters, thresholds, and peak indicators.</p>
      </article>
      <article class="card">
        <h3>✂️ Destructive Trim & FX</h3>
        <p>Apply real-time DSP gain, fade curves, noise gates, echo, and a dedicated 5-band EQ (80Hz, 250Hz, 1kHz, 4kHz, 12kHz) inside a precise waveform editor.</p>
      </article>
    </div>
  </div>
</section>

<!-- Quick Start Guide -->
<section id="usage" class="section alt" style="padding: 4rem 0;">
  <div class="container">
    <h2>🕹️ Quick Start Workflow</h2>
    <ol style="line-height: 1.8; font-size: 1.05rem;">
      <li><strong>Select Input Target:</strong> Choose between Mic/Line, System Loopback, or Target Application.</li>
      <li><strong>Engage Capture Strategy:</strong> Choose <em>AutoVAD</em> for vocal automation, or <em>Key Buffer</em> to capture recent action retrospectively.</li>
      <li><strong>Route & Monitor:</strong> Fine-tune your recording thresholds, output devices, and live tracking meters.</li>
      <li><strong>Manage via Pads:</strong> Replay, sort, favorite, and organize your clips cleanly on the main workspace.</li>
      <li><strong>Polish & Export:</strong> Use the waveform trim interface and 5-band EQ stack to instantly output high-fidelity WAV, MP3, Opus, Ogg Vorbis, or FLAC files.</li>
    </ol>
  </div>
</section>

<!-- Screenshots Display -->
<section id="screenshots" class="section">
  <div class="container">
    <h2 style="text-align: center; margin-bottom: 1rem;">📸 Software Interface</h2>
    <p style="text-align: center; margin-bottom: 2.5rem; opacity: 0.8;">The main recording environment and wave processing editor in action.</p>
    <div class="shots" style="display: grid; grid-template-columns: repeat(auto-fit, minmax(400px, 1fr)); gap: 2rem;">
      <figure class="shot-card" style="margin: 0;">
        <img src="{{ '/assets/img/main-window.png' | relative_url }}" alt="PaDDY main window showing recording pads and meters" style="border-radius: 6px; box-shadow: 0 4px 12px rgba(0,0,0,0.15); width: 100%;">
        <figcaption style="margin-top: 0.75rem; font-style: italic; opacity: 0.85;">Main workspace: Responsive tracking pads, source selectors, and system configuration profiles.</figcaption>
      </figure>
      <figure class="shot-card" style="margin: 0;">
        <img src="{{ '/assets/img/trim-editor.png' | relative_url }}" alt="PaDDY trim editor showing waveform and effect controls" style="border-radius: 6px; box-shadow: 0 4px 12px rgba(0,0,0,0.15); width: 100%;">
        <figcaption style="margin-top: 0.75rem; font-style: italic; opacity: 0.85;">Waveform trim interface: Surgical drag-and-drop handles with inline hardware-style effects mapping.</figcaption>
      </figure>
    </div>
  </div>
</section>

<!-- Installation & Source Compiling -->
<section id="install" class="section alt" style="padding: 4rem 0;">
  <div class="container">
    <h2>💾 Deployment & Requirements</h2>
    <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 2.5rem; margin-top: 1.5rem;">
      <div>
        <h3>Binary Installation</h3>
        <p>Pre-packaged standalone release configurations for immediate execution.</p>
        <ol style="line-height: 1.6;">
          <li>Open the official <a href="https://github.com/NoID1290/PaDDY/releases">Releases portal</a>.</li>
          <li>Grab the latest <code>PaDDY_[version].zip</code> archive distribution.</li>
          <li>Extract cleanly to a folder directory and click <code>PaDDY.exe</code>.</li>
        </ol>
      </div>
      <div>
        <h3>Compile From Source</h3>
        <p>Requires an active Windows environment paired with the newer <strong>.NET 10 SDK</strong> compiler framework.</p>
        <pre style="background: #1e1e1e; color: #d4d4d4; padding: 1rem; border-radius: 6px; overflow-x: auto; font-family: monospace; font-size: 0.9rem;">
# Restore solution components
dotnet restore PaDDY.sln

# Build runtime deployment binaries
dotnet build PaDDY.csproj --configuration Release</pre>
      </div>
    </div>
  </div>
</section>

<!-- Changelog & Meta Info -->
<section id="changelog" class="section" style="padding: 3rem 0; text-align: center;">
  <div class="container">
    <h2>📄 Project Documentation</h2>
    <p>Track detailed releases, engine optimizations, framework migration commits, and historical logs inside the <a href="https://github.com/NoID1290/PaDDY/blob/main/CHANGELOG.md">CHANGELOG.md</a> profile.</p>
    <p style="margin-top: 3rem; font-size: 0.9rem; opacity: 0.6;">NoID Softwork © 2020 - 2026</p>
  </div>
</section>

<!-- markdownlint-enable MD033 -->
