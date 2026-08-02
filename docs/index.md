---
layout: default
title: Home
---

<!-- Hero Block -->
<section class="hero">
  <div class="container">
    <img src="{{ '/assets/img/hero.png' | relative_url }}" alt="PaDDY Logo" class="hero-logo">
    <h1>PaDDY</h1>
    <p class="hero-tagline">
      A lightweight, low-overhead Windows companion built to continuously stream, capture, manage, and process high-fidelity audio snippets instantly.
    </p>
    <div class="cta-group">
      <a class="btn btn-primary" href="https://github.com/NoID1290/PaDDY/releases">📦 Download Release</a>
      <a class="btn btn-secondary" href="https://github.com/NoID1290/PaDDY">💻 View Source</a>
    </div>
    <span class="version-badge">v2.0.0.0802</span>
  </div>
</section>

<!-- Features Grid -->
<section id="features" class="section">
  <div class="container">
    <div class="section-header">
      <h2 class="section-title">Engine Architecture</h2>
      <p class="section-subtitle">Intelligent audio capturing logic bound to high-performance local management wrappers.</p>
    </div>
    
    <div class="grid">
      <article class="card">
        <span class="card-icon">🎙️</span>
        <h3>AutoVAD Mode</h3>
        <p>Monitors system voice thresholds dynamically, launching standalone clip sequences when speech activity initiates and breaking cleanly on silence timeout windows.</p>
      </article>
      
      <article class="card">
        <span class="card-icon">🔄</span>
        <h3>Retroactive Key Buffer</h3>
        <p>Maintains a silent, zero-impact background cyclic allocation array. Instantly extract and commit the last <em>N</em> seconds of history via a global hotkey context.</p>
      </article>
      
      <article class="card">
        <span class="card-icon">🎯</span>
        <h3>Focused App Loopback</h3>
        <p>Isolate structural audio endpoints directly from specified thread windows (like Discord or specific game targets) while discarding standard system clutter.</p>
      </article>
      
      <article class="card">
        <span class="card-icon">💾</span>
        <h3>Persistent Pad Deck</h3>
        <p>Organize, favorite, cascade-sort, or queue multiple capture items using a fast SQLite data persistence backend tied into highly responsive modular structural pads.</p>
      </article>
      
      <article class="card">
        <span class="card-icon">📊</span>
        <h3>Realtime Telemetry Meters</h3>
        <p>Track hardware input lines, master loopback mixes, and target monitor streams concurrently with hardware-accurate RMS meters and clipping peak indicators.</p>
      </article>
      
      <article class="card">
        <span class="card-icon">🎛️</span>
        <h3>Inline Waveform DSP</h3>
        <p>Process operations inside a destructive local trim environment featuring dynamic gain curves, adjustable noise gating, echoing, and a fixed 5-band studio EQ array.</p>
      </article>
    </div>
  </div>
</section>

<!-- Workflow Mechanics -->
<section id="usage" class="section alt">
  <div class="container">
    <div class="section-header">
      <h2 class="section-title">Quick Start Workflow</h2>
      <p class="section-subtitle">Go from zero configuration to capturing perfect snippets in under a minute.</p>
    </div>
    
    <ul class="step-list">
      <li class="step-item">
        <div class="step-num">1</div>
        <div class="step-content">
          <strong>Select Active Hardware Route</strong>
          <p>Pick your standard microphone line-in, general Windows system mix, or pin an app-specific pipeline context.</p>
        </div>
      </li>
      <li class="step-item">
        <div class="step-num">2</div>
        <div class="step-content">
          <strong>Choose Capture Engine Profile</strong>
          <p>Toggle AutoVAD for continuous automated monitoring, or enable Key Buffer to capture past actions on command.</p>
        </div>
      </li>
      <li class="step-item">
        <div class="step-num">3</div>
        <div class="step-content">
          <strong>Track Master Gauges</strong>
          <p>Check localized decibel parameters using live VU indicators before initiating production workflow tracking.</p>
        </div>
      </li>
      <li class="step-item">
        <div class="step-num">4</div>
        <div class="step-content">
          <strong>Manipulate Saved Frames</strong>
          <p>Trigger visual deck blocks to replay audio instantly, send assets to the favorites list, or apply processing algorithms.</p>
        </div>
      </li>
    </ul>
  </div>
</section>

<!-- Workspace Interface Display -->
<section id="screenshots" class="section">
  <div class="container">
    <div class="section-header">
      <h2 class="section-title">Application Interface</h2>
      <p class="section-subtitle">Visual monitoring ecosystems constructed around native WPF layout engines.</p>
    </div>
    
    <div class="shots">
      <figure class="shot-card">
        <img src="{{ '/assets/img/main-window.png' | relative_url }}" alt="Main interface pipeline tracking dashboard">
        <figcaption>
          <strong>Dashboard Workspace</strong>
          <p>Integrated structural clip pads, input tracking knobs, operational profile tags, and independent pipeline multi-meters.</p>
        </figcaption>
      </figure>
      <figure class="shot-card">
        <img src="{{ '/assets/img/trim-editor.png' | relative_url }}" alt="Destructive linear waveform editing workspace">
        <figcaption>
          <strong>Trim Processing Deck</strong>
          <p>Fine-grained waveform timeline positioning matching inline hardware EQ, noise gate modules, and dynamic multi-format export presets.</p>
        </figcaption>
      </figure>
    </div>
  </div>
</section>

<!-- Compiling and Distribution Info -->
<section id="install" class="section alt">
  <div class="container">
    <div class="split-layout">
      <div>
        <h3>💾 Standard Installation</h3>
        <p>Pre-compiled release distributions are delivered completely self-contained — no configuration overhead or dependencies needed.</p>
        <ol>
          <li>Navigate directly to the official <a href="https://github.com/NoID1290/PaDDY/releases">Releases portal</a>.</li>
          <li>Grab the latest compressed release build: <code>PaDDY_[version].zip</code>.</li>
          <li>Extract files cleanly and launch <code>PaDDY.exe</code>.</li>
        </ol>
      </div>
      
      <div>
        <h3>🛠️ Compilation From Source</h3>
        <p>Requires an environment running Windows 10/11 along with an active <strong>.NET 10 SDK</strong> compiler profile context.</p>
        <pre><code># Pull and link project component assemblies
dotnet restore PaDDY.sln

# Target architectural optimized compilation profiles
dotnet build PaDDY.csproj --configuration Release</code></pre>
      </div>
    </div>
  </div>
</section>

<!-- Page Footer Navigation Context -->
<section id="changelog" class="section" style="text-align: center;">
  <div class="container">
    <h3 style="font-size: 1.5rem; justify-content: center; margin-bottom: 0.75rem;">Release Timeline</h3>
    <p style="max-width: 600px; margin: 0 auto 3rem auto;">
      Review full execution updates, patch items, or component version updates inside the 
      <a href="https://github.com/NoID1290/PaDDY/blob/main/CHANGELOG.md">CHANGELOG.md file</a>.
    </p>
  </div>
</section>
