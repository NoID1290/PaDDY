---
layout: default
title: Home
---

<!-- markdownlint-disable MD033 -->
<meta name="google-site-verification" content="BWApMIBBsK_hOKJBuRTUtVKgj3wVTcayKgDoyjzTk_Q" />

<style>
  :root {
    --bg-main: #0d1117;
    --bg-dark-alt: #161b22;
    --accent-color: #8a63d2;
    --accent-hover: #704cb5;
    --text-primary: #f0f6fc;
    --text-muted: #8b949e;
    --border-smooth: rgba(255, 255, 255, 0.08);
    --card-bg: rgba(255, 255, 255, 0.02);
  }
  
  body {
    background-color: var(--bg-main) !important;
    color: var(--text-primary) !important;
  }

  .paddy-hero {
    padding: 6rem 0 4rem 0;
    text-align: center;
    background: radial-gradient(circle at top, rgba(138, 99, 210, 0.1) 0%, transparent 60%);
  }
  .paddy-tagline {
    font-size: 1.4rem;
    font-weight: 400;
    line-height: 1.6;
    max-width: 720px;
    margin: 1.5rem auto 2.5rem auto;
    color: #e6edf3;
  }
  .paddy-btn {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.8rem 1.8rem;
    font-weight: 600;
    font-size: 1rem;
    border-radius: 8px;
    transition: all 0.2s ease-in-out;
    text-decoration: none;
  }
  .paddy-btn-primary {
    background-color: var(--accent-color);
    color: #ffffff !important;
  }
  .paddy-btn-primary:hover {
    background-color: var(--accent-hover);
    transform: translateY(-2px);
  }
  .paddy-btn-secondary {
    background-color: rgba(255, 255, 255, 0.05);
    color: #c9d1d9 !important;
    border: 1px solid var(--border-smooth);
  }
  .paddy-btn-secondary:hover {
    background-color: rgba(255, 255, 255, 0.1);
    transform: translateY(-2px);
  }
  
  .paddy-section-title {
    text-align: center;
    font-size: 2rem;
    font-weight: 700;
    margin-bottom: 0.5rem;
    letter-spacing: -0.03em;
    color: var(--text-primary);
  }
  .paddy-section-subtitle {
    text-align: center;
    color: var(--text-muted);
    margin-bottom: 3.5rem;
    font-size: 1.05rem;
  }

  .paddy-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
    gap: 1.75rem;
    margin-bottom: 2rem;
  }
  .paddy-card {
    background: var(--card-bg);
    border: 1px solid var(--border-smooth);
    border-radius: 12px;
    padding: 2rem;
    transition: transform 0.2s ease, box-shadow 0.2s ease;
  }
  .paddy-card:hover {
    transform: translateY(-4px);
    box-shadow: 0 12px 24px rgba(0, 0, 0, 0.4);
    border-color: rgba(138, 99, 210, 0.4);
    background: rgba(255, 255, 255, 0.04);
  }
  .paddy-card h3 {
    font-size: 1.25rem;
    font-weight: 600;
    margin-top: 0;
    margin-bottom: 0.75rem;
    display: flex;
    align-items: center;
    gap: 0.6rem;
    color: #ffffff;
  }
  .paddy-card p {
    font-size: 0.95rem;
    line-height: 1.6;
    margin: 0;
    color: #afb8c1;
  }

  .paddy-step-list {
    list-style: none;
    padding: 0;
    max-width: 800px;
    margin: 0 auto;
  }
  .paddy-step-item {
    display: flex;
    gap: 1.5rem;
    margin-bottom: 1.75rem;
    align-items: flex-start;
  }
  .paddy-step-num {
    background: rgba(138, 99, 210, 0.2);
    color: #b794f4;
    font-weight: 700;
    min-width: 36px;
    height: 36px;
    border-radius: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 0.95rem;
    border: 1px solid rgba(138, 99, 210, 0.3);
  }
  .paddy-step-content strong {
    display: block;
    font-size: 1.1rem;
    margin-bottom: 0.25rem;
    color: #f0f6fc;
  }

  .paddy-shots-container {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(420px, 1fr));
    gap: 2.5rem;
  }
  .paddy-figure {
    margin: 0;
    background: #090d12;
    border: 1px solid var(--border-smooth);
    border-radius: 14px;
    padding: 0.5rem;
    box-shadow: 0 20px 40px rgba(0,0,0,0.5);
  }
  .paddy-img {
    border-radius: 10px;
    display: block;
    width: 100%;
    height: auto;
    opacity: 0.9;
    transition: opacity 0.2s ease;
  }
  .paddy-img:hover {
    opacity: 1;
  }
  .paddy-caption {
    padding: 1.25rem;
    font-size: 0.9rem;
    color: var(--text-muted);
    line-height: 1.5;
    border-top: 1px solid var(--border-smooth);
    margin-top: 0.5rem;
  }

  .paddy-split-layout {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
    gap: 3.5rem;
  }
  .paddy-pre {
    background: #070a0e !important;
    border: 1px solid var(--border-smooth);
    border-radius: 10px;
    padding: 1.25rem !important;
    overflow-x: auto;
    font-family: ui-monospace, SFMono-Regular, SF Mono, Menlo, Consolas, Liberation Mono, monospace;
    font-size: 0.875rem;
    line-height: 1.6;
    color: #e6edf3 !important;
  }
</style>

<!-- Hero Block -->
<section class="paddy-hero">
  <div class="container">
    <img src="{{ '/assets/img/hero.png' | relative_url }}" alt="PaDDY Logo" style="max-width: 360px; height: auto;">
    <p class="paddy-tagline">
      A lightweight, low-overhead Windows companion built to continuously stream, capture, manage, and process high-fidelity audio snippets instantly.
    </p>
    <div style="display: flex; justify-content: center; gap: 1rem; margin-bottom: 1.5rem;">
      <a class="paddy-btn paddy-btn-primary" href="https://github.com/NoID1290/PaDDY/releases">📦 Download Release</a>
      <a class="paddy-btn paddy-btn-secondary" href="https://github.com/NoID1290/PaDDY">💻 View Source</a>
    </div>
    <span style="font-family: monospace; font-size: 0.9rem; color: var(--text-muted); background: rgba(255,255,255,0.04); padding: 0.25rem 0.75rem; border-radius: 20px; border: 1px solid var(--border-smooth);">v1.8.1.0712</span>
  </div>
</section>

<!-- Features Grid -->
<section id="features" style="padding: 5rem 0;">
  <div class="container">
    <h2 class="paddy-section-title">Engine Architecture</h2>
    <p class="paddy-section-subtitle">Intelligent audio capturing logic bound to high-performance local management wrappers.</p>
    
    <div class="paddy-grid">
      <article class="paddy-card">
        <h3>🎙️ AutoVAD Mode</h3>
        <p>Monitors system voice thresholds dynamically, launching standalone clip sequences when speech activity initiates and breaking cleanly on silence timeout windows.</p>
      </article>
      
      <article class="paddy-card">
        <h3>🔄 Retroactive Key Buffer</h3>
        <p>Maintains a silent, zero-impact background cyclic allocation array. Instantly extract and commit the last <em>N</em> seconds of history via a global hotkey context.</p>
      </article>
      
      <article class="paddy-card">
        <h3>🎯 Focused App Loopback</h3>
        <p>Isolate structural audio endpoints directly from specified thread windows (like Discord or specific game targets) while discarding standard system clutter.</p>
      </article>
      
      <article class="paddy-card">
        <h3>💾 Persistent Pad Deck</h3>
        <p>Organize, favorite, cascade-sort, or queue multiple capture items using a fast SQLite data persistence backend tied into highly responsive modular structural pads.</p>
      </article>
      
      <article class="paddy-card">
        <h3>📊 Realtime Telemetry Meters</h3>
        <p>Track hardware input lines, master loopback mixes, and target monitor streams concurrently with hardware-accurate RMS meters and clipping peak indicators.</p>
      </article>
      
      <article class="paddy-card">
        <h3>🎛️ Inline Waveform DSP</h3>
        <p>Process operations inside a destructive local trim environment featuring dynamic gain curves, adjustable noise gating, echoing, and a fixed 5-band studio EQ array.</p>
      </article>
    </div>
  </div>
</section>

<!-- Workflow Mechanics -->
<section id="usage" style="padding: 5rem 0; background: var(--bg-dark-alt); border-top: 1px solid var(--border-smooth); border-bottom: 1px solid var(--border-smooth);">
  <div class="container">
    <h2 class="paddy-section-title">Quick Start Workflow</h2>
    <p class="paddy-section-subtitle">Go from zero configuration to capturing perfect snippets in under a minute.</p>
    
    <ul class="paddy-step-list">
      <li class="paddy-step-item">
        <div class="paddy-step-num">1</div>
        <div class="paddy-step-content">
          <strong>Select Active Hardware Route</strong>
          <span style="color: var(--text-muted);">Pick your standard microphone line-in, general Windows system mix, or pin an app-specific pipeline context.</span>
        </div>
      </li>
      <li class="paddy-step-item">
        <div class="paddy-step-num">2</div>
        <div class="paddy-step-content">
          <strong>Choose Capture Engine Profile</strong>
          <span style="color: var(--text-muted);">Toggle AutoVAD for continuous automated monitoring, or enable Key Buffer to capture past actions on command.</span>
        </div>
      </li>
      <li class="paddy-step-item">
        <div class="paddy-step-num">3</div>
        <div class="paddy-step-content">
          <strong>Track Master Gauges</strong>
          <span style="color: var(--text-muted);">Check localized decibel parameters using live VU indicators before initiating production workflow tracking.</span>
        </div>
      </li>
      <li class="paddy-step-item">
        <div class="paddy-step-num">4</div>
        <div class="paddy-step-content">
          <strong>Manipulate Saved Frames</strong>
          <span style="color: var(--text-muted);">Trigger visual deck blocks to replay audio instantly, send assets to the favorites list, or apply processing algorithms.</span>
        </div>
      </li>
    </ul>
  </div>
</section>

<!-- Workspace Interface Display -->
<section id="screenshots" style="padding: 5rem 0;">
  <div class="container">
    <h2 class="paddy-section-title">Application Interface</h2>
    <p class="paddy-section-subtitle">Visual monitoring ecosystems constructed around native WPF layout engines.</p>
    
    <div class="paddy-shots-container">
      <figure class="paddy-figure">
        <img class="paddy-img" src="{{ '/assets/img/main-window.png' | relative_url }}" alt="Main interface pipeline tracking dashboard">
        <figcaption class="paddy-caption">
          <strong>Dashboard Workspace:</strong> Integrated structural clip pads, input tracking knobs, operational profile tags, and independent pipeline multi-meters.
        </figcaption>
      </figure>
      <figure class="paddy-figure">
        <img class="paddy-img" src="{{ '/assets/img/trim-editor.png' | relative_url }}" alt="Destructive linear waveform editing workspace">
        <figcaption class="paddy-caption">
          <strong>Trim Processing Deck:</strong> Fine-grained waveform timeline positioning matching inline hardware EQ, noise gate modules, and dynamic multi-format export presets.
        </figcaption>
      </figure>
    </div>
  </div>
</section>

<!-- Compiling and Distribution Info -->
<section id="install" style="padding: 5rem 0; background: var(--bg-dark-alt); border-top: 1px solid var(--border-smooth);">
  <div class="container">
    <div class="paddy-split-layout">
      <div>
        <h3 style="font-size: 1.5rem; font-weight: 600; margin-top: 0; margin-bottom: 1rem; letter-spacing: -0.02em; color: #ffffff;">💾 Standard Installation</h3>
        <p style="color: var(--text-muted); line-height: 1.6; margin-bottom: 1.5rem;">Pre-compiled release distributions are delivered completely self-contained—no configuration overhead or dependencies needed.</p>
        <ol style="padding-left: 1.2rem; line-height: 1.8; color: #e6edf3;">
          <li>Navigate directly to the official <a href="https://github.com/NoID1290/PaDDY/releases" style="color: #b794f4; text-decoration: none; font-weight: 500;">Releases portal</a>.</li>
          <li>Grab the latest compressed release build: <code>PaDDY_[version].zip</code>.</li>
          <li>Extract files cleanly and launch <code>PaDDY.exe</code>.</li>
        </ol>
      </div>
      
      <div>
        <h3 style="font-size: 1.5rem; font-weight: 600; margin-top: 0; margin-bottom: 1rem; letter-spacing: -0.02em; color: #ffffff;">🛠️ Compilation From Source</h3>
        <p style="color: var(--text-muted); line-height: 1.6; margin-bottom: 1rem;">Requires an environment running Windows 10/11 along with an active <strong>.NET 10 SDK</strong> compiler profile context.</p>
        <pre class="paddy-pre"><code># Pull and link project component assemblies
dotnet restore PaDDY.sln

# Target architectural optimized compilation profiles
dotnet build PaDDY.csproj --configuration Release</code></pre>
      </div>
    </div>
  </div>
</section>

<!-- Page Footer Navigation Context -->
<section id="changelog" style="padding: 4rem 0 3rem 0; text-align: center; border-top: 1px solid var(--border-smooth);">
  <div class="container">
    <h3 style="font-size: 1.25rem; font-weight: 600; margin-bottom: 0.5rem; color: #ffffff;">Release Timeline</h3>
    <p style="color: var(--text-muted); margin-bottom: 4rem;">Review full execution updates, patch items, or component version updates inside the <a href="https://github.com/NoID1290/PaDDY/blob/main/CHANGELOG.md" style="color: #b794f4; text-decoration: none;">CHANGELOG.md file</a>.</p>
    <p style="font-size: 0.85rem; color: #57606a; font-family: monospace; letter-spacing: 0.05em; text-transform: uppercase;">NoID Softwork &copy; 2020 - 2026</p>
  </div>
</section>

<!-- markdownlint-enable MD033 -->
