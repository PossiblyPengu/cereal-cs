using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaWebView;
using Cereal.App.Services;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.Views;

public partial class OrbitView : UserControl
{
    private WebView? _webView;
    private bool _ready;

    public OrbitView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_webView is not null) return;
        try
        {
            _webView = new WebView
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            };

            _webView.NavigationCompleted += async (_, _) =>
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var overlay = this.FindControl<Border>("LoadingOverlay");
                    if (overlay is not null) overlay.IsVisible = false;
                    _ready = true;
                });
                await PushGamesAsync();
            };

            _webView.WebMessageReceived += OnWebMessageReceived;

            var host = this.FindControl<ContentControl>("WebViewHost");
            if (host is not null) host.Content = _webView;

            var density = App.Services.GetRequiredService<SettingsService>().Get().StarDensity;
            var starCount = density switch { "low" => 300, "high" => 1500, _ => 800 };
            _webView.HtmlContent = OrbitHtml.Build(starCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[orbit] Failed to initialize WebView");
            ShowError(ex.Message);
        }
    }

    public async Task RefreshGamesAsync()
    {
        if (!_ready) return;
        await PushGamesAsync();
    }

    private async Task PushGamesAsync()
    {
        try
        {
            var paths = App.Services.GetRequiredService<PathService>();
            var games = App.Services.GetRequiredService<GameService>().GetAll();
            var payload = games.Select(g =>
            {
                var localCover = paths.GetCoverPath(g.Id);
                var coverSrc = File.Exists(localCover)
                    ? "file:///" + localCover.Replace('\\', '/')
                    : (g.CoverUrl ?? "");
                return new { id = g.Id, name = g.Name, platform = g.Platform ?? "custom", playtime = g.PlaytimeMinutes ?? 0, cover = coverSrc };
            });
            var json = JsonSerializer.Serialize(payload);
            if (_webView is not null)
                await _webView.ExecuteScriptAsync($"window.loadGames({json})");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[orbit] PushGamesAsync failed");
        }
    }

    private void OnWebMessageReceived(object? sender, WebViewCore.Events.WebViewMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.Message);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            var id   = root.GetProperty("id").GetString();
            if (string.IsNullOrEmpty(id)) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is not MainViewModel vm) return;
                if (type == "select")
                    vm.SelectGameByIdCommand.Execute(id);
                else if (type == "launch")
                    vm.LaunchGameByIdCommand.Execute(id);
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[orbit] Bad web message");
        }
    }

    private void ShowError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var loading = this.FindControl<Border>("LoadingOverlay");
            var error = this.FindControl<Border>("ErrorOverlay");
            var text = this.FindControl<TextBlock>("ErrorText");
            if (loading is not null) loading.IsVisible = false;
            if (error is not null) error.IsVisible = true;
            if (text is not null) text.Text = $"Orbit view unavailable: {message}";
        });
    }
}

// ─── Orbit HTML (2D CSS-transform galaxy, matching source app) ───────────────

internal static class OrbitHtml
{
    public static string Build(int starCount = 800) =>
        BuildTemplate().Replace("__STAR_COUNT__", starCount.ToString());

    private static string BuildTemplate() => """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8"/>
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{background:#080818;overflow:hidden;font-family:system-ui,sans-serif;color:#ccd}
#viewport{position:fixed;inset:0;overflow:hidden;cursor:grab;user-select:none}
#viewport:active{cursor:grabbing}
#canvas{
  position:absolute;top:0;left:0;
  width:3000px;height:2000px;
  transform-origin:0 0;
}
#canvas.fly{transition:transform .6s cubic-bezier(.25,1,.5,1)}
.nebula{
  position:absolute;border-radius:50%;
  filter:blur(100px);pointer-events:none;
  transform:translate(-50%,-50%);
}
.orbit-ring{
  position:absolute;border-radius:50%;
  border:1px solid rgba(255,255,255,.015);
  pointer-events:none;transform:translate(-50%,-50%);
}
.galaxy-sun{position:absolute;transform:translate(-50%,-50%);z-index:5;pointer-events:none}
.galaxy-sun-corona{
  position:absolute;left:50%;top:50%;
  border-radius:50%;transform:translate(-50%,-50%);
  animation:coronaPulse 4s ease-in-out infinite alternate;
}
.galaxy-sun-core{
  position:relative;z-index:2;border-radius:50%;
  display:flex;align-items:center;justify-content:center;
  font-size:20px;font-weight:700;color:rgba(255,255,255,.9);
}
.cluster-label{
  position:absolute;font-size:80px;font-weight:100;
  letter-spacing:8px;text-transform:uppercase;
  color:rgba(255,255,255,.022);pointer-events:none;
  transform:translate(-50%,-50%);white-space:nowrap;
}
.star{position:absolute;border-radius:50%;pointer-events:none}
.orb{
  position:absolute;transform:translate(-50%,-50%);
  cursor:pointer;z-index:10;
}
.orb-body{
  border-radius:50%;overflow:hidden;
  border:2px solid transparent;
  transition:transform .2s,box-shadow .2s,border-color .2s;
  background:#1a1a3a;
}
.orb-body:hover{
  transform:scale(1.35);
  border-color:var(--orb-color,#44aaff);
  box-shadow:0 0 20px var(--orb-color,#44aaff),0 8px 30px rgba(0,0,0,.5);
  z-index:100;
}
.orb-body img{width:100%;height:100%;object-fit:cover;display:block}
.orb-fallback{
  width:100%;height:100%;display:flex;align-items:center;
  justify-content:center;font-weight:700;color:rgba(255,255,255,.7);
}
.orb-name{
  position:absolute;bottom:-24px;left:50%;transform:translateX(-50%);
  white-space:nowrap;font-size:11px;color:rgba(255,255,255,.7);
  pointer-events:none;opacity:0;transition:opacity .2s;
  text-shadow:0 1px 4px rgba(0,0,0,.9);max-width:120px;
  overflow:hidden;text-overflow:ellipsis;
}
.orb-body:hover ~ .orb-name{opacity:1}
#tooltip{
  position:fixed;pointer-events:none;
  padding:6px 12px;background:rgba(8,8,30,.9);
  border:1px solid rgba(80,150,255,.3);border-radius:8px;
  color:#cce;font-size:13px;display:none;
  white-space:nowrap;z-index:1000;
}
@keyframes coronaPulse{0%{opacity:.3;transform:translate(-50%,-50%) scale(1)}100%{opacity:.5;transform:translate(-50%,-50%) scale(1.12)}}
@keyframes twinkle{0%,100%{opacity:.7}50%{opacity:.2}}
.shooting-star{
  position:absolute;height:1px;pointer-events:none;
  background:linear-gradient(90deg,rgba(255,255,255,0),rgba(255,255,255,.8),rgba(255,255,255,0));
  border-radius:1px;opacity:0;
}
</style>
</head>
<body>
<div id="viewport">
  <div id="canvas"></div>
</div>
<div id="tooltip"></div>
<script>
const GALAXY_W = 3000, GALAXY_H = 2000;

const CLUSTER_CENTERS = {
  steam:    {x:480,  y:580},
  epic:     {x:1450, y:400},
  gog:      {x:2400, y:560},
  psn:      {x:560,  y:1400},
  xbox:     {x:1600, y:1350},
  custom:   {x:2500, y:1350},
  battlenet:{x:950,  y:900},
  ea:       {x:1450, y:950},
  ubisoft:  {x:2000, y:900},
  itchio:   {x:2000, y:1550},
};

const PLATFORM_COLORS = {
  steam:    '#64b4f5',
  epic:     '#cccccc',
  gog:      '#b36ef5',
  psn:      '#0070d1',
  xbox:     '#107c10',
  custom:   '#d4a853',
  battlenet:'#009ae5',
  ea:       '#f44040',
  ubisoft:  '#0070ff',
  itchio:   '#fa5c5c',
};

const PLATFORM_LABELS = {
  steam:'Steam', epic:'Epic', gog:'GOG', psn:'PlayStation',
  xbox:'Xbox', custom:'Custom', battlenet:'Battle.net',
  ea:'EA', ubisoft:'Ubisoft', itchio:'itch.io',
};

const viewport = document.getElementById('viewport');
const canvas   = document.getElementById('canvas');
const tooltip  = document.getElementById('tooltip');

// ── Camera state ──────────────────────────────────────────────────────────────
let cam = { x:0, y:0, zoom:1 };

function applyTransform(fly) {
  if (fly) canvas.classList.add('fly'); else canvas.classList.remove('fly');
  canvas.style.transform = `translate(${cam.x}px,${cam.y}px) scale(${cam.zoom})`;
  if (fly) setTimeout(()=>canvas.classList.remove('fly'), 650);
}

function fitAll() {
  const vw = viewport.clientWidth, vh = viewport.clientHeight;
  const zx = vw / GALAXY_W, zy = vh / GALAXY_H;
  const z  = Math.min(zx, zy) * 0.9;
  cam = { x:(vw - GALAXY_W*z)/2, y:(vh - GALAXY_H*z)/2, zoom:z };
  applyTransform(true);
}

// ── Drag / pan ────────────────────────────────────────────────────────────────
let drag = null;
let dragMoved = false;
viewport.addEventListener('mousedown', e => {
  drag = { sx:e.clientX - cam.x, sy:e.clientY - cam.y, ox:e.clientX, oy:e.clientY };
  dragMoved = false;
});
window.addEventListener('mousemove', e => {
  if (drag) {
    const dx = e.clientX - drag.ox, dy = e.clientY - drag.oy;
    if (Math.abs(dx) > 4 || Math.abs(dy) > 4) dragMoved = true;
    cam.x = e.clientX - drag.sx; cam.y = e.clientY - drag.sy; applyTransform(false);
  }
  updateTooltip(e);
});
window.addEventListener('mouseup', () => { drag = null; });

// ── Scroll zoom ───────────────────────────────────────────────────────────────
viewport.addEventListener('wheel', e => {
  e.preventDefault();
  const factor = e.deltaY < 0 ? 1.1 : 0.9;
  const newZoom = Math.max(0.15, Math.min(4, cam.zoom * factor));
  const mx = e.clientX, my = e.clientY;
  cam.x = mx - (mx - cam.x) * (newZoom / cam.zoom);
  cam.y = my - (my - cam.y) * (newZoom / cam.zoom);
  cam.zoom = newZoom;
  applyTransform(false);
}, { passive:false });

// Double-click to fit all
viewport.addEventListener('dblclick', fitAll);

// ── Background stars ──────────────────────────────────────────────────────────
function buildStars() {
  const N = __STAR_COUNT__;
  for (let i = 0; i < N; i++) {
    const s = document.createElement('div');
    s.className = 'star';
    const x = Math.random() * GALAXY_W;
    const y = Math.random() * GALAXY_H;
    const sz = Math.random() < 0.1 ? 2 : 1;
    const op = 0.2 + Math.random() * 0.6;
    const hue = Math.random() < 0.3 ? '#aaaaff' : '#ffffff';
    s.style.cssText = `left:${x}px;top:${y}px;width:${sz}px;height:${sz}px;background:${hue};opacity:${op}`;
    if (Math.random() < 0.15) {
      const dur = (3 + Math.random() * 4).toFixed(1);
      const del = (Math.random() * 6).toFixed(1);
      s.style.animation = `twinkle ${dur}s ease-in-out ${del}s infinite`;
    }
    canvas.appendChild(s);
  }
}

// ── Platform cluster visuals ──────────────────────────────────────────────────
function buildCluster(plat, cx, cy) {
  const col = PLATFORM_COLORS[plat] || '#aaaaff';
  const label = PLATFORM_LABELS[plat] || plat;

  // Nebula glows
  [[600,600,.07],[350,350,.03],[280,280,.02]].forEach(([w,h,op]) => {
    const n = document.createElement('div');
    n.className = 'nebula';
    n.style.cssText = `left:${cx}px;top:${cy}px;width:${w}px;height:${h}px;background:${col};opacity:${op}`;
    canvas.appendChild(n);
  });

  // Orbit rings
  [90,150,220,300].forEach(r => {
    const ring = document.createElement('div');
    ring.className = 'orbit-ring';
    ring.style.cssText = `left:${cx}px;top:${cy}px;width:${r*2}px;height:${r*2}px;border-color:${r<160?'rgba(255,255,255,.04)':'rgba(255,255,255,.018)'}`;
    canvas.appendChild(ring);
  });

  // Sun
  const sun = document.createElement('div');
  sun.className = 'galaxy-sun';
  sun.style.cssText = `left:${cx}px;top:${cy}px`;

  const corona = document.createElement('div');
  corona.className = 'galaxy-sun-corona';
  corona.style.cssText = `width:130px;height:130px;background:radial-gradient(circle,${col} 0%,transparent 65%);opacity:.35`;
  sun.appendChild(corona);

  const core = document.createElement('div');
  core.className = 'galaxy-sun-core';
  core.style.cssText = `width:56px;height:56px;background:radial-gradient(circle at 35% 35%,rgba(255,255,255,.2),${col});border:2px solid rgba(255,255,255,.1);box-shadow:0 0 30px ${col},0 0 60px ${col}`;
  core.textContent = label.charAt(0);
  sun.appendChild(core);
  canvas.appendChild(sun);

  // Cluster label
  const lbl = document.createElement('div');
  lbl.className = 'cluster-label';
  lbl.style.cssText = `left:${cx}px;top:${cy}px`;
  lbl.textContent = label;
  canvas.appendChild(lbl);
}

// ── Game orbs ─────────────────────────────────────────────────────────────────
let orbEls = [];

function removeOrbs() {
  orbEls.forEach(el => el.remove());
  orbEls = [];
}

// Seeded quasi-random for stable scatter without import
function hashStr(s) {
  let h = 0;
  for (let i=0;i<s.length;i++) h = Math.imul(31,h)+s.charCodeAt(i)|0;
  return h;
}

function placeOrb(game, idx, totalForPlatform) {
  const c = CLUSTER_CENTERS[game.platform] || CLUSTER_CENTERS.custom;
  const col = PLATFORM_COLORS[game.platform] || '#aaaaff';

  // Scatter around cluster center using hash for stability
  const seed = hashStr(game.id || game.name);
  const angle = ((seed & 0xfff) / 0xfff) * Math.PI * 2;
  const rings = Math.ceil(totalForPlatform / 8);
  const ring  = (idx % rings) + 1;
  const baseR = 120 + ring * 55;
  const r     = baseR + ((seed >> 12) & 0xff) / 255 * 30 - 15;
  const x     = c.x + Math.cos(angle + idx * 0.8) * r;
  const y     = c.y + Math.sin(angle + idx * 0.8) * r;

  const size = 44 + Math.min((game.playtime || 0) / 300, 20);

  const wrap = document.createElement('div');
  wrap.className = 'orb';
  wrap.style.cssText = `left:${x}px;top:${y}px`;

  const body = document.createElement('div');
  body.className = 'orb-body';
  body.style.cssText = `width:${size}px;height:${size}px;--orb-color:${col}`;
  body.dataset.name = game.name;
  body.dataset.platform = game.platform;
  body.dataset.playtime = game.playtime;

  if (game.cover) {
    const img = document.createElement('img');
    img.src = game.cover;
    img.alt = '';
    img.onerror = () => { img.remove(); showFallback(body, game, size); };
    body.appendChild(img);
  } else {
    showFallback(body, game, size);
  }

  const nameEl = document.createElement('div');
  nameEl.className = 'orb-name';
  nameEl.textContent = game.name;

  wrap.appendChild(body);
  wrap.appendChild(nameEl);
  canvas.appendChild(wrap);
  orbEls.push(wrap);

  body.addEventListener('mouseenter', e => {
    tooltip.style.display = 'block';
    tooltip.textContent = game.name + (game.platform ? ' · ' + (PLATFORM_LABELS[game.platform] || game.platform) : '') + (game.playtime ? ' · ' + Math.round(game.playtime / 60) + 'h' : '');
  });
  body.addEventListener('mouseleave', () => { tooltip.style.display = 'none'; });
  body.addEventListener('click', () => {
    if (dragMoved) return;
    if (window.chrome?.webview)
      window.chrome.webview.postMessage(JSON.stringify({type:'select', id: game.id}));
  });
  body.addEventListener('dblclick', e => {
    e.stopPropagation();
    if (window.chrome?.webview)
      window.chrome.webview.postMessage(JSON.stringify({type:'launch', id: game.id}));
  });
}

function showFallback(body, game, size) {
  const fb = document.createElement('div');
  fb.className = 'orb-fallback';
  fb.style.fontSize = Math.round(size * 0.38) + 'px';
  fb.textContent = (game.name || '?').charAt(0).toUpperCase();
  body.appendChild(fb);
}

window.addEventListener('mousemove', e => {
  if (tooltip.style.display === 'block') {
    tooltip.style.left = (e.clientX + 14) + 'px';
    tooltip.style.top  = (e.clientY - 8) + 'px';
  }
});

function updateTooltip(e) {
  // tooltip content is set on hover; just track position
  tooltip.style.left = (e.clientX + 14) + 'px';
  tooltip.style.top  = (e.clientY - 8)  + 'px';
}

window.loadGames = function(games) {
  removeOrbs();
  if (!games || !games.length) return;

  // Group by platform for ring placement
  const byPlat = {};
  games.forEach(g => { (byPlat[g.platform] = byPlat[g.platform] || []).push(g); });

  // Build clusters for active platforms
  const activePlats = Object.keys(byPlat);
  activePlats.forEach(plat => {
    if (CLUSTER_CENTERS[plat]) buildCluster(plat, CLUSTER_CENTERS[plat].x, CLUSTER_CENTERS[plat].y);
  });

  // Place orbs
  Object.entries(byPlat).forEach(([plat, platGames]) => {
    platGames.forEach((g, i) => placeOrb(g, i, platGames.length));
  });
};

// ── Shooting stars ────────────────────────────────────────────────────────────
function spawnShootingStar() {
  const el = document.createElement('div');
  el.className = 'shooting-star';
  const len  = 80 + Math.random() * 120;
  const angle = -15 + Math.random() * 30; // degrees, mostly horizontal
  const x = Math.random() * GALAXY_W;
  const y = Math.random() * GALAXY_H;
  const dur = 0.6 + Math.random() * 0.5;
  el.style.cssText = `left:${x}px;top:${y}px;width:${len}px;transform:rotate(${angle}deg);transform-origin:left center`;
  canvas.appendChild(el);
  el.animate(
    [{opacity:0,transform:`rotate(${angle}deg) translateX(0)`},
     {opacity:1,transform:`rotate(${angle}deg) translateX(${len*0.3}px)`},
     {opacity:0,transform:`rotate(${angle}deg) translateX(${len}px)`}],
    {duration: dur * 1000, easing:'ease-in'}
  ).finished.then(() => el.remove());
}

function scheduleShootingStar() {
  const delay = 4000 + Math.random() * 8000;
  setTimeout(() => { spawnShootingStar(); scheduleShootingStar(); }, delay);
}
scheduleShootingStar();

// ── Init ──────────────────────────────────────────────────────────────────────
buildStars();
fitAll();
</script>
</body>
</html>
""";
}
