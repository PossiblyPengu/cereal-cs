// ─── Gamepad input (XInput) ─────────────────────────────────────────────────
// Port of src/utils.ts `useGamepad` hook. Polls XInput on a background loop
// and raises normalized action strings on the Avalonia UI thread so the view
// model can react without knowing about input plumbing.
//
// Action vocabulary (matches the Electron version 1:1):
//   confirm, back, x, y, start, select, lb, rb
//   up, down, left, right           ← D-pad *or* left stick
//   r_up, r_down, r_left, r_right   ← right stick
//
// Directional buttons auto-repeat after INITIAL_DELAY ms at REPEAT_DELAY cadence.
// XInput is Windows-only; on other platforms the service silently no-ops.

using System.Runtime.InteropServices;
using Avalonia.Threading;
using Serilog;

namespace Cereal.App.Services;

public sealed class GamepadEventArgs : EventArgs
{
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
}

public sealed class GamepadService : IDisposable
{
    public event EventHandler<GamepadEventArgs>? ActionsReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    private CancellationTokenSource? _cts;
    private bool _wasConnected;

    private const double Deadzone     = 0.4;
    private const int    InitialDelay = 300;
    private const int    RepeatDelay  = 120;

    // XInput button bitmasks (see XInput.h).
    private const ushort XINPUT_GAMEPAD_DPAD_UP        = 0x0001;
    private const ushort XINPUT_GAMEPAD_DPAD_DOWN      = 0x0002;
    private const ushort XINPUT_GAMEPAD_DPAD_LEFT      = 0x0004;
    private const ushort XINPUT_GAMEPAD_DPAD_RIGHT     = 0x0008;
    private const ushort XINPUT_GAMEPAD_START          = 0x0010;
    private const ushort XINPUT_GAMEPAD_BACK           = 0x0020;
    private const ushort XINPUT_GAMEPAD_LEFT_SHOULDER  = 0x0100;
    private const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
    private const ushort XINPUT_GAMEPAD_A              = 0x1000;
    private const ushort XINPUT_GAMEPAD_B              = 0x2000;
    private const ushort XINPUT_GAMEPAD_X              = 0x4000;
    private const ushort XINPUT_GAMEPAD_Y              = 0x8000;

    public void Start()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => PollLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task PollLoop(CancellationToken ct)
    {
        var prev  = new Dictionary<string, bool>();
        var held  = new Dictionary<string, long>();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!TryReadController(out var gp))
                {
                    if (_wasConnected)
                    {
                        _wasConnected = false;
                        Dispatcher.UIThread.Post(() => Disconnected?.Invoke(this, EventArgs.Empty));
                    }
                    await Task.Delay(1000, ct);
                    continue;
                }

                if (!_wasConnected)
                {
                    _wasConnected = true;
                    Dispatcher.UIThread.Post(() => Connected?.Invoke(this, EventArgs.Empty));
                }

                var actions = new List<string>();
                var now = Environment.TickCount64;

                void Check(string name, bool pressed, bool repeatable)
                {
                    var wasPressed = prev.GetValueOrDefault(name);
                    if (pressed && !wasPressed)
                    {
                        actions.Add(name);
                        held[name] = now;
                    }
                    else if (pressed && wasPressed && repeatable)
                    {
                        var elapsed = now - held.GetValueOrDefault(name);
                        if (elapsed > InitialDelay)
                        {
                            actions.Add(name);
                            held[name] = now - (InitialDelay - RepeatDelay);
                        }
                    }
                    else if (!pressed)
                    {
                        held.Remove(name);
                    }
                    prev[name] = pressed;
                }

                var b = gp.wButtons;
                Check("confirm", (b & XINPUT_GAMEPAD_A) != 0, false);
                Check("back",    (b & XINPUT_GAMEPAD_B) != 0, false);
                Check("x",       (b & XINPUT_GAMEPAD_X) != 0, false);
                Check("y",       (b & XINPUT_GAMEPAD_Y) != 0, false);
                Check("start",   (b & XINPUT_GAMEPAD_START) != 0, false);
                Check("select",  (b & XINPUT_GAMEPAD_BACK) != 0, false);
                Check("lb",      (b & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0, false);
                Check("rb",      (b & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0, false);

                var dpadUp    = (b & XINPUT_GAMEPAD_DPAD_UP) != 0;
                var dpadDown  = (b & XINPUT_GAMEPAD_DPAD_DOWN) != 0;
                var dpadLeft  = (b & XINPUT_GAMEPAD_DPAD_LEFT) != 0;
                var dpadRight = (b & XINPUT_GAMEPAD_DPAD_RIGHT) != 0;

                // Left stick also counts as directional input (auto-repeats).
                double lx = gp.sThumbLX / 32767.0;
                double ly = -gp.sThumbLY / 32767.0; // invert so down is positive
                var leftStick = new
                {
                    Left  = lx < -Deadzone,
                    Right = lx >  Deadzone,
                    Up    = ly < -Deadzone,
                    Down  = ly >  Deadzone,
                };

                Check("up",    dpadUp    || leftStick.Up,    true);
                Check("down",  dpadDown  || leftStick.Down,  true);
                Check("left",  dpadLeft  || leftStick.Left,  true);
                Check("right", dpadRight || leftStick.Right, true);

                // Right stick emits r_* actions.
                double rx = gp.sThumbRX / 32767.0;
                double ry = -gp.sThumbRY / 32767.0;
                Check("r_left",  rx < -Deadzone, true);
                Check("r_right", rx >  Deadzone, true);
                Check("r_up",    ry < -Deadzone, true);
                Check("r_down",  ry >  Deadzone, true);

                if (actions.Count > 0)
                {
                    var dispatched = actions;
                    Dispatcher.UIThread.Post(() =>
                        ActionsReceived?.Invoke(this, new GamepadEventArgs { Actions = dispatched }));
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[gamepad] poll iteration failed"); }

            try { await Task.Delay(16, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ─── XInput P/Invoke ────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte   bLeftTrigger;
        public byte   bRightTrigger;
        public short  sThumbLX;
        public short  sThumbLY;
        public short  sThumbRX;
        public short  sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint            dwPacketNumber;
        public XINPUT_GAMEPAD  Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState14(uint dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState910(uint dwUserIndex, out XINPUT_STATE pState);

    private static bool _useLegacy;

    private static bool TryReadController(out XINPUT_GAMEPAD gp)
    {
        gp = default;
        for (uint i = 0; i < 4; i++)
        {
            try
            {
                var err = _useLegacy
                    ? XInputGetState910(i, out var st)
                    : XInputGetState14(i, out st);
                if (err == 0)
                {
                    gp = st.Gamepad;
                    return true;
                }
            }
            catch (DllNotFoundException)
            {
                if (!_useLegacy)
                {
                    _useLegacy = true;
                    return TryReadController(out gp);
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[gamepad] XInput read failed for slot {Slot}", i);
            }
        }
        return false;
    }
}
