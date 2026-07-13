---
layout: default
title: Home
---

<!-- markdownlint-disable MD033 -->
<meta name="google-site-verification" content="BWApMIBBsK_hOKJBuRTUtVKgj3wVTcayKgDoyjzTk_Q" />

<style>
  /* Force global theme overrides inside the wrapper container */
  .paddy-dark-theme-wrapper {
    background-color: #0d1117 !important;
    color: #f0f6fc !important;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif;
    padding: 1px 0; /* Prevents margin collapsing */
  }

  /* Force background matching across all containing elements */
  .paddy-dark-theme-wrapper section,
  .paddy-dark-theme-wrapper div,
  .paddy-dark-theme-wrapper article {
    background-color: transparent !important;
  }

  /* Typography color overrides */
  .paddy-dark-theme-wrapper h1,
  .paddy-dark-theme-wrapper h2,
  .paddy-dark-theme-wrapper h3,
  .paddy-dark-theme-wrapper h4,
  .paddy-dark-theme-wrapper strong {
    color: #ffffff !important;
  }

  .paddy-dark-theme-wrapper p,
  .paddy-dark-theme-wrapper li,
  .paddy-dark-theme-wrapper figcaption {
    color: #c9d1d9 !important;
  }

  /* Hero Module definitions */
  .paddy-hero {
    padding: 6rem 0 4rem 0;
    text-align: center;
    background: radial-gradient(circle at top, rgba(138, 99, 210, 0.15) 0%, transparent 60%) !important;
  }
  .paddy-tagline {
    font-size: 1.4rem !important;
    font-weight: 400 !important;
    line-height: 1.6 !important;
    max-width: 720px;
    margin: 1.5rem auto 2.5rem auto !important;
    color: #e6edf3 !important;
  }

  /* Buttons */
  .paddy-btn {
    display: inline-flex !important;
    align-items: center;
    gap: 0.5rem;
    padding: 0.8rem 1.8rem !important;
    font-weight: 600 !important;
    font-size: 1rem !important;
    border-radius: 8px !important;
    transition: all 0.2s ease-in-out !important;
    text-decoration: none !important;
  }
  .paddy-btn-primary {
    background-color: #8a63d2 !important;
    color: #ffffff !important;
  }
  .paddy-btn-primary:hover {
    background-color: #704cb5 !important;
    transform: translateY(-2px);
  }
  .paddy-btn-secondary {
    background-color: rgba(255, 255, 255, 0.05) !important;
    color: #c9d1d9 !important;
    border: 1px solid rgba(255, 255, 255, 0.08) !important;
  }
  .paddy-btn-secondary:hover {
    background-color: rgba(255, 255, 255, 0.1) !important;
    transform: translateY(-2px);
  }
  
  /* Sections Layout */
  .paddy-section-title {
    text-align: center !important;
    font-size: 2rem !important;
    font-weight: 700 !important;
    margin-bottom: 0.5rem !important;
    letter-spacing: -0.03em !important;
  }
  .paddy-section-subtitle {
    text-align: center !important;
    color: #8b949e !important;
    margin-bottom: 3.5rem !important;
    font-size: 1.05rem !important;
  }

  /* Grid Layout and Cards */
  .paddy-grid {
    display: grid !important;
    grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)) !important;
    gap: 1.75rem !important;
    margin-bottom: 2rem !important;
  }
  .paddy-card {
    background: rgba(255, 255, 255, 0.03) !important;
    border: 1px solid rgba(255, 255, 255, 0.08) !important;
    border-radius: 12px !important;
    padding: 2rem !important;
    transition: transform 0.2s ease, box-shadow 0.2s ease !important;
  }
  .paddy-card:hover {
    transform: translateY(-4px) !important;
    box-shadow: 0 12px 24px rgba(0, 0, 0, 0.5) !important;
    border-color: rgba(138, 99, 210, 0.4) !important;
    background: rgba(255, 255, 255, 0.05) !important;
  }
  .paddy-card h3 {
    font-size: 1.25rem !important;
    font-weight: 600 !important;
    margin-top: 0 !important;
    margin-bottom: 0.75rem !important;
    display: flex !important;
    align-items: center !important;
    gap: 0.6rem !important;
  }

  /* Step Workflow Lists */
  .paddy-step-list {
    list-style: none !important;
    padding: 0 !important;
    max-width: 800px;
    margin: 0 auto !important;
  }
  .paddy-step-item {
    display: flex !important;
    gap: 1.5rem !important;
    margin-bottom: 1.75rem !important;
    align-items: flex-start !important;
  }
  .paddy-step-num {
    background: rgba(138, 99, 210, 0.2) !important;
    color: #b794f4 !important;
    font-weight: 700 !important;
    min-width: 36px !important;
    height: 36px !important;
    border-radius: 50% !important;
    display: flex !important;
    align-items: center !important;
    justify-content: center !important;
    font-size: 0.95rem !important;
    border: 1px solid rgba(138, 99, 210, 0.3) !important;
  }
  .paddy-step-content strong {
    display: block !important;
    font-size: 1.1rem !important;
    margin-bottom: 0.25rem !important;
  }

  /* Image Containers and Screenshots */
  .paddy-shots-container {
    display: grid !important;
    grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)) !important;
    gap: 2.5rem !important;
  }
  .paddy-figure {
    margin: 0 !important;
    background: #090d12 !important;
    border: 1px solid rgba(255, 255, 255, 0.08) !important;
    border-radius: 14px !important;
    padding: 0.5rem !important;
    box-shadow: 0 20px 40px rgba(0, 0, 0, 0.6) !important;
  }
  .paddy-img {
    border-radius: 10px !important;
    display: block !important;
    width: 100% !important;
    height: auto !important;
    opacity: 0.95 !important;
  }
  .paddy-caption {
    padding: 1.25rem !important;
    font-size: 0.9rem !important;
    line-height: 1.5 !important;
    border-top: 1px solid rgba(255, 255, 255, 0.08) !important;
    margin-top: 0.5rem !important;
  }

  /* Two Column Split Panels */
  .paddy-split-layout {
    display: grid !important;
    grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)) !important;
    gap: 3.5rem !important;
  }
  .paddy-pre {
    background: #070a0e !important;
    border: 1px solid rgba(255, 255, 255, 0.08) !important;
    border-radius: 10px !important;
    padding: 1.25rem !important;
    overflow-x: auto !important;
    font-family: ui-monospace, SFMono-Regular, SF Mono, Menlo, Consolas, monospace !important;
    font-size: 0.875rem !important;
    line-height: 1.6 !important;
    color: #e6edf3 !important;
    text-align: left !important;
  }
  
  .paddy-pre code {
    background: transparent !important;
    color: inherit !important;
    padding: 0 !important;
  }

  .paddy-link {
    color: #b794f4 !important;
    text-decoration: none !important;
    font-weight: 500 !important;
  }
  .paddy-link:hover {
    text-decoration: underline !important;
  }
</style>

<!-- Main Wrapper Layer to isolate theme engine from parent templates -->
<div class="paddy-dark-theme-wrapper">

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
      <span style="font-family: monospace; font-size: 0.9rem; color: #8b949e; background: rgba(255,255,255,0.04); padding: 0.25rem 0.75rem; border-radius: 20px; border: 1px solid rgba(255,255,255,0.08);">v1.8.1.0712</span>
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
  <section id="usage" style="padding: 5rem 0; background: #161b22 !important; border-top: 1px solid rgba(255,255,255,0.08); border-bottom: 1px solid rgba(255,255,255,0.08);">
    <div class="container">
      <h2 class="paddy-section-title">Quick Start Workflow</h2>
      <p class="paddy-section-subtitle">Go from zero configuration to capturing perfect snippets in under a minute.</p>
      
      <ul class="paddy-step-list">
        <li class="paddy-step-item">
          <div class="paddy-step-num">1</div>
          <div class="paddy-step-content">
            <strong>Select Active Hardware Route</strong>
            <p>Pick your standard microphone line-in, general Windows system mix, or pin an app-specific pipeline context.</p>
          </div>
        </li>
        <li class="paddy-step-item">
          <div class="paddy-step-num">2</div>
          <div class="paddy-step-content">
            <strong>Choose Capture Engine Profile</strong>
            <p>Toggle AutoVAD for continuous automated monitoring, or enable Key Buffer to capture past actions on command.</p>
          </div>
        </li>
        <li class="paddy-step-item">
          <div class="paddy-step-num">3</div>
          <div class="paddy-step-content">
            <strong>Track Master Gauges</strong>
            <p>Check localized decibel parameters using live VU indicators before initiating production workflow tracking.</p>
          </div>
        </li>
        <li class="paddy-step-item">
          <div class="paddy-step-num">4</div>
          <div class="paddy-step-content">
            <strong>Manipulate Saved Frames</strong>
            <p>Trigger visual deck blocks to replay audio instantly, send assets to the favorites list, or apply processing algorithms.</p>
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
  <section id="install" style="padding: 5rem 0; background: #161b22 !important; border-top: 1px solid rgba(255,255,255,0.08);">
    <div class="container">
      <div class="paddy-split-layout">
        <div>
          <h3 style="font-size: 1.5rem; font-weight: 600; margin-top: 0; margin-bottom: 1rem; letter-spacing: -0.02em;">💾 Standard Installation</h3>
          <p style="line-height: 1.6; margin-bottom: 1.5rem;">Pre-compiled release distributions are delivered completely self-contained—no configuration overhead or dependencies needed.</p>
          <ol style="padding-left: 1.2rem; line-height: 1.8;">
            <li>Navigate directly to the official <a class="paddy-link" href="https://github.com/NoID1290/PaDDY/releases">Releases portal</a>.</li>
            <li>Grab the latest compressed release build: <code>PaDDY_[version].zip</code>.</li>
            <li>Extract files cleanly and launch <code>PaDDY.exe</code>.</li>
          </ol>
        </div>
        
        <div>
          <h3 style="font-size: 1.5rem; font-weight: 600; margin-top: 0; margin-bottom: 1rem; letter-spacing: -0.02em;">🛠️ Compilation From Source</h3>
          <p style="line-height: 1.6; margin-bottom: 1rem;">Requires an environment running Windows 10/11 along with an active <strong>.NET 10 SDK</strong> compiler profile context.</p>
          <pre class="paddy-pre"><code># Pull and link project component assemblies
dotnet restore PaDDY.sln

# Target architectural optimized compilation profiles
dotnet build PaDDY.csproj --configuration Release</code></pre>
        </div>
      </div>
    </div>
  </section>

  <!-- Page Footer Navigation Context -->
  <section id="changelog" style="padding: 4rem 0 3rem 0; text-align: center; border-top: 1px solid rgba(255,255,255,0.08);">
    <div class="container">
      <h3 style="font-size: 1.25rem; font-weight: 600; margin-bottom: 0.5rem;">Release Timeline</h3>
      <p style="margin-bottom: 4rem;">Review full execution updates, patch items, or component version updates inside the <a class="paddy-link" href="https://github.com/NoID1290/PaDDY/blob/main/CHANGELOG.md">CHANGELOG.md file</a>.</p>
      <p style="font-size: 0.85rem; color: #57606a; font-family: monospace; letter-spacing: 0.05em; text-transform: uppercase;">NoID Softwork &copy; 2020 - 2026</p>
    </div>
  </section>

</div>
<!-- markdownlint-enable MD033 -->
