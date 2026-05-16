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
    <p class="meta">Current version: <strong>1.2.4.0515</strong></p>
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
        <p>Export as WAV, MP3, Opus, or Ogg Vorbis from mic or loopback input sources.</p>
      </article>
      <article class="card">
        <h3>Clip Workflow</h3>
        <p>Trim, rename, favorite, and organize recordings from a pad-focused interface.</p>
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

<section id="screenshots" class="section">
  <div class="container">
    <h2>Screenshots</h2>
    <p>Add UI captures here for the recording pad, trim editor, and favorites panel.</p>
    <div class="shots">
      <div class="shot">Recording Pad (placeholder)</div>
      <div class="shot">Trim Editor (placeholder)</div>
      <div class="shot">Favorites Panel (placeholder)</div>
    </div>
  </div>
</section>

<section class="section alt">
  <div class="container">
    <h2>Changelog</h2>
    <p>See <a href="https://github.com/NoID1290/PaDDY/blob/main/CHANGELOG.md">CHANGELOG.md</a> for release history.</p>
  </div>
</section>

<!-- markdownlint-enable MD033 -->
