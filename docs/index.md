---
layout: default
title: Home
---

<!-- markdownlint-disable MD033 -->

<section class="hero">
  <div class="container">
    <img class="hero-image" src="{{ '/assets/img/hero.png' | relative_url }}" alt="PaDDY hero logo">
    <p class="tagline">Windows recording pad for quickly capturing, organizing, and replaying short audio clips.</p>
    <div class="cta-row">
      <a class="btn btn-primary" href="https://github.com/NoID1290/PaDDY/releases">Download Latest</a>
      <a class="btn btn-secondary" href="https://github.com/NoID1290/PaDDY">View Source</a>
    </div>
    <p class="meta">1.5.1.0629</p>
  </div>
</section>

<section id="features" class="section">
  <div class="container">
    <h2>Feature Highlights</h2>
    <div class="grid">
      <article class="card">
        <h3>AutoVAD Mode</h3>
        <p>Automatically starts and stops clips based on voice activity, sensitivity, and silence timeout.</p>
      </article>
      <article class="card">
        <h3>Key Buffer Mode</h3>
        <p>Capture the last N seconds with a global hotkey so you never miss what just happened.</p>
      </article>
      <article class="card">
        <h3>Flexible Outputs</h3>
        <p>Export as WAV, MP3, Opus, Ogg Vorbis, or FLAC from mic, system loopback, or app loopback sources.</p>
      </article>
      <article class="card">
        <h3>Clip Workflow</h3>
        <p>Trim, rename, favorite, and organize recordings from a pad-focused interface.</p>
      </article>
      <article class="card">
        <h3>Monitoring & Meters</h3>
        <p>Track input, playback, and monitor levels with dedicated RMS meters and threshold indicators.</p>
      </article>
      <article class="card">
        <h3>Effects & Trim Editor</h3>
        <p>Apply gain, fade, noise gate, echo, and 5-band EQ while editing clips with waveform-based trimming.</p>
      </article>
    </div>
  </div>
</section>

<section id="install" class="section alt">
  <div class="container">
    <h2>Install</h2>
    <ol>
      <li>Open the <a href="https://github.com/NoID1290/PaDDY/releases">latest release</a>.</li>
      <li>Download the zip artifact.</li>
      <li>Extract files and run <code>PaDDY.exe</code>.</li>
    </ol>
    <h3>Build from Source</h3>
    <p>Requires Windows 10/11 and .NET 10 SDK.</p>
<pre><code>dotnet restore PaDDY.sln
dotnet build PaDDY.csproj --configuration Release</code></pre>
  </div>
</section>

<section id="usage" class="section">
  <div class="container">
    <h2>Quick Start</h2>
    <ol>
      <li>Select your input source (mic, system loopback, or app loopback).</li>
      <li>Choose <strong>AutoVAD</strong> for automatic capture or <strong>Key Buffer</strong> for hotkey-based capture.</li>
      <li>Start monitoring and recording from the main window.</li>
      <li>Use recording pads to replay, trim, favorite, rename, and organize clips.</li>
      <li>Open Settings to configure codec, naming, cleanup, and hotkey behavior.</li>
    </ol>
  </div>
</section>

<section id="screenshots" class="section">
  <div class="container">
    <h2>Screenshots</h2>
    <p>Main recording workflow and trim editor in action.</p>
    <div class="shots">
      <figure class="shot-card">
        <img src="{{ '/assets/img/main-window.png' | relative_url }}" alt="PaDDY main window with recording pads and meters">
        <figcaption>Main window: recording pads, mode controls, and meters.</figcaption>
      </figure>
      <figure class="shot-card">
        <img src="{{ '/assets/img/trim-editor.png' | relative_url }}" alt="PaDDY trim editor with waveform and effect controls">
        <figcaption>Trim editor: waveform trimming with effect processing tools.</figcaption>
      </figure>
    </div>
  </div>
</section>

<section id="changelog" class="section alt">
  <div class="container">
    <h2>Changelog</h2>
    <p>See <a href="https://github.com/NoID1290/PaDDY/blob/main/CHANGELOG.md">CHANGELOG.md</a> for release history.</p>
  </div>
</section>

<!-- markdownlint-enable MD033 -->

