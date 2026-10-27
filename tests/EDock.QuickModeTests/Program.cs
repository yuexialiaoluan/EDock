using EDock.Interop;
using EDock.Models;
using EDock.Services;
using Microsoft.UI.Dispatching;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
Fixture New(bool enable = true, bool companionCallback = true) => new(Check, enable, companionCallback);

using (var f = New(false))
{
    Check(!f.Queue.Timer.IsRunning && !f.Mode.IsActive && NativeMethods.KeyboardHook is null && NativeMethods.MouseHook is null, "disabled default has no timer or hooks");
    Check(!f.Down(NativeMethods.VkLeftControl) && !f.Up(NativeMethods.VkLeftControl), "disabled Ctrl passes through");
    Check(!NativeMethods.SendWheel(120), "disabled wheel passes through");
    f.Mode.Configure(true, "Left");
    Check(f.Queue.Timer.IsRunning && f.Queue.Timer.Interval == TimeSpan.FromMilliseconds(100) && NativeMethods.KeyboardHook is not null && NativeMethods.MouseHook is not null, "enabling installs both hooks with idle reconciliation timer");
    f.Down(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "Ctrl down does not commit a tap");
    for (int i = 0; i < 20; i++) f.Down(NativeMethods.VkLeftControl);
    f.Flush(); Check(!f.Mode.IsActive, "Ctrl repeats do not commit");
    f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(f.Mode.IsActive, "Ctrl release commits standalone tap");
    for (int i = 0; i < 50; i++) f.Tick();
    Check(f.Mode.IsActive && f.Active.SequenceEqual(new[] { true }), "quick mode persists without Ctrl");
    f.Tap(); Check(!f.Mode.IsActive && f.Active.SequenceEqual(new[] { true, false }), "second Ctrl tap hides");
    f.Mode.Configure(false, "Left");
    Check(!f.Queue.Timer.IsRunning && NativeMethods.KeyboardHook is null && NativeMethods.MouseHook is null, "disable removes both hooks and timer");
}
foreach (int ctrl in new[] { NativeMethods.VkControl, NativeMethods.VkLeftControl, NativeMethods.VkRightControl })
using (var f = New())
{
    f.Tap(ctrl); Check(f.Mode.IsActive, "either Ctrl can enter");
    f.Tap(ctrl); Check(!f.Mode.IsActive, "same Ctrl can exit");
    Check(NativeMethods.Events.All(e => !e.Suppressed), "Ctrl is never swallowed");
}
foreach (int chord in new[] { 0x43, 0x41, 0x09, 0x1B, NativeMethods.VkW, NativeMethods.VkS, NativeMethods.VkSpace,
    NativeMethods.VkShift, NativeMethods.VkLeftShift, NativeMethods.VkRightShift, NativeMethods.VkMenu, NativeMethods.VkLeftMenu,
    NativeMethods.VkRightMenu, NativeMethods.VkLeftWindows, NativeMethods.VkRightWindows })
foreach (bool releaseCtrlFirst in new[] { false, true })
using (var f = New())
{
    f.Down(NativeMethods.VkLeftControl); f.Down(chord);
    if (releaseCtrlFirst) { f.Up(NativeMethods.VkLeftControl); f.Up(chord); }
    else { f.Up(chord); f.Up(NativeMethods.VkLeftControl); }
    f.Flush(); Check(!f.Mode.IsActive && f.Launches == 0 && f.Moves.Count == 0, "Ctrl chord must not toggle or launch");
    Check(NativeMethods.Events.All(e => !e.Suppressed), "inactive application chord passes through");
    f.Tap(); Check(f.Mode.IsActive, "fresh tap works after chord");
}
using (var f = New())
{
    f.Down(0x43); f.Down(NativeMethods.VkLeftControl); f.Up(0x43); f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "key held before Ctrl prevents tap");
    f.Down(NativeMethods.VkLeftControl); f.Down(NativeMethods.VkRightControl); f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "releasing one of two Ctrl keys does not toggle");
    f.Up(NativeMethods.VkRightControl); f.Flush(); Check(!f.Mode.IsActive, "overlapping Ctrl keys do not count as standalone tap");
    f.Tap(); f.Down(NativeMethods.VkRightControl); f.Down(NativeMethods.VkLeftControl); f.Up(NativeMethods.VkRightControl); f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(f.Mode.IsActive, "overlapping Ctrl keys cannot accidentally hide active mode");
    f.Tap(NativeMethods.VkRightControl); Check(!f.Mode.IsActive, "fresh right Ctrl tap exits after overlap");
}
foreach (string app in new[] { "CabinetWClass", "Shell_TrayWnd", "Notepad", "Chrome_WidgetWin_1" })
using (var f = New())
{
    NativeMethods.ForegroundClass = app; f.Down(NativeMethods.VkLeftControl);
    NativeMethods.ForegroundClass = "Progman"; f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "Ctrl begun in an application cannot toggle on desktop release");
    f.Tap(); NativeMethods.ForegroundClass = app; f.Tick(); Check(!f.Mode.IsActive, "foreground loss cancels persistent mode");
    NativeMethods.ForegroundClass = "Progman"; f.Tick(); Check(!f.Mode.IsActive, "returning to desktop does not reactivate");
}
using (var f = New())
{
    NativeMethods.ForegroundClass = "WorkerW"; f.Tap(); Check(f.Mode.IsActive, "WorkerW desktop accepted"); f.Mode.Exit();
    f.Down(NativeMethods.VkLeftControl); NativeMethods.ForegroundClass = "Notepad"; f.Tick();
    NativeMethods.ForegroundClass = "Progman"; f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "leaving and returning during tap invalidates candidate");
    NativeMethods.Minimized = true; f.Tap(); Check(!f.Mode.IsActive, "minimized foreground rejected"); NativeMethods.Minimized = false;
    NativeMethods.HasForeground = false; f.Tap(); Check(!f.Mode.IsActive, "missing foreground rejected"); NativeMethods.HasForeground = true;
    f.Busy = true; f.Tap(); Check(!f.Mode.IsActive, "busy windows prevent entry"); f.Busy = false;
    f.Tap(); f.Busy = true; f.Tick(); Check(!f.Mode.IsActive, "opening busy window cancels active mode");
}
foreach (int modifier in new[] { NativeMethods.VkShift, NativeMethods.VkLeftShift, NativeMethods.VkRightShift, NativeMethods.VkMenu,
    NativeMethods.VkLeftMenu, NativeMethods.VkRightMenu, NativeMethods.VkLeftWindows, NativeMethods.VkRightWindows })
using (var f = New())
{
    f.Tap(); Check(!f.Down(modifier), "modifier input passes through");
    Check(!f.Down(NativeMethods.VkSpace), "modified Space passes through");
    f.Flush(); Check(!f.Mode.IsActive && f.Launches == 0, "modifier chord cannot launch and cancels mode");
    f.Release(); f.Tap(); Check(f.Mode.IsActive, "fresh tap works after modifier release");
}
using (var f = New())
{
    f.Tap(); f.Down(NativeMethods.VkLeftControl); Check(!f.Down(0x43), "Ctrl+C while active passes through");
    f.Release(); Check(!f.Mode.IsActive && f.Launches == 0, "Ctrl+C exits without toggling back");
    f.Tap(); f.Down(NativeMethods.VkRightControl); Check(!f.Down(NativeMethods.VkSpace), "Ctrl+Space while active passes through");
    f.Release(); Check(!f.Mode.IsActive && f.Launches == 0, "IME chord does not launch");
}
using (var f = New())
{
    f.Tap(); Check(f.Down(NativeMethods.VkW), "active W down swallowed"); f.Flush();
    for (int i = 0; i < 50; i++) Check(f.Down(NativeMethods.VkW), "W repeats remain swallowed");
    f.Flush(); Check(f.Moves.Count == 51 && f.Moves.All(direction => direction == -1), "W hold follows every system repeat");
    Check(f.Up(NativeMethods.VkW), "matching W release swallowed");
    Check(f.Down(NativeMethods.VkS) && f.Up(NativeMethods.VkS), "active S pair swallowed"); f.Flush();
    Check(f.Moves.Count == 52 && f.Moves[^1] == 1, "S selects next without Ctrl");
    f.Down(NativeMethods.VkW); f.Down(NativeMethods.VkS); f.Flush();
    Check(f.Moves.Count == 52, "simultaneous opposite keys do not queue conflicting moves");
    f.Release(); Check(f.Moves.Count == 52, "releases do not select");
    f.Down(NativeMethods.VkW); f.Flush(); f.Mode.Exit();
    Check(f.Down(NativeMethods.VkW) && f.Up(NativeMethods.VkW), "suppressed repeat and release remain swallowed after explicit exit");
    Check(!f.Down(NativeMethods.VkW) && !f.Up(NativeMethods.VkW), "new W after exit passes through");
}
using (var f = New())
{
    // No timer or dispatcher runs between the tap, navigation and launch.
    f.Down(NativeMethods.VkLeftControl); f.Up(NativeMethods.VkLeftControl);
    Check(f.Down(NativeMethods.VkS) && f.Up(NativeMethods.VkS), "fast navigation after tap swallowed immediately");
    Check(f.Down(NativeMethods.VkSpace), "fast Space after tap swallowed immediately");
    Check(f.Launches == 0 && f.Active.Count == 0, "hook callbacks never execute UI events inline");
    f.Flush(); Check(f.Launches == 1 && !f.Mode.IsActive && f.Moves.SequenceEqual(new[] { 1 }), "fast tap and Space launches once in order");
    Check(f.Order.SequenceEqual(new[] { "active:True", "move:1", "launch", "active:False" }), "launch callback reads selection before exit");
    for (int i = 0; i < 20; i++) Check(f.Down(NativeMethods.VkSpace), "Space repeats swallowed after launch");
    Check(f.Up(NativeMethods.VkSpace), "Space keyup swallowed after launch");
    f.Flush(); Check(f.Launches == 1, "holding Space cannot launch repeatedly");
    Check(!f.Down(NativeMethods.VkSpace) && !f.Up(NativeMethods.VkSpace), "fresh Space after exit passes through");
    f.Tap(); f.ExitInsideLaunch = true; f.Down(NativeMethods.VkSpace); f.Flush();
    Check(f.Launches == 2 && !f.Mode.IsActive, "launch handler may synchronously Exit"); f.Up(NativeMethods.VkSpace);
}
using (var f = New())
{
    Check(!NativeMethods.SendWheel(120), "inactive wheel untouched");
    f.Tap(); Check(NativeMethods.SendWheel(120) && NativeMethods.SendWheel(-120), "active vertical wheel swallowed"); f.Flush();
    Check(f.Moves.SequenceEqual(new[] { -1, 1 }), "wheel up previous and down next");
    NativeMethods.SendWheel(240); NativeMethods.SendWheel(-360); f.Flush();
    Check(f.Moves.SequenceEqual(new[] { -1, 1, -1, -1, 1, 1, 1 }), "multiple wheel notches preserved");
    int count = f.Moves.Count;
    for (int i = 0; i < 3; i++) Check(NativeMethods.SendWheel(30), "high-resolution partial wheel swallowed");
    f.Flush(); Check(f.Moves.Count == count, "partial wheel deltas accumulate");
    NativeMethods.SendWheel(30); f.Flush(); Check(f.Moves.Count == count + 1 && f.Moves[^1] == -1, "120 accumulated delta selects once");
    Check(!NativeMethods.SendWheel(120, NativeMethods.WmMouseHorizontalWheel), "horizontal wheel passes through");
    Check(!NativeMethods.SendWheel(120, code: -1), "negative hook code passes through");
    f.Down(NativeMethods.VkLeftControl); Check(!NativeMethods.SendWheel(120), "Ctrl+wheel passes through"); f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "Ctrl+wheel cancels mode without toggling on release");
    f.Tap(); NativeMethods.SendWheel(60); f.Mode.Exit(); f.Tap(); NativeMethods.SendWheel(60); f.Flush();
    Check(f.Moves.Count == count + 1, "wheel remainder resets across exit");
    NativeMethods.ForegroundClass = "Notepad"; Check(!NativeMethods.SendWheel(-120), "application wheel passes through before polling"); f.Flush();
    Check(!f.Mode.IsActive, "application wheel immediately cancels mode");
}
using (var f = New())
{
    f.Down(NativeMethods.VkLeftControl); NativeMethods.SendWheel(120); f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "Ctrl+wheel is not a standalone Ctrl tap");
    f.Down(NativeMethods.VkLeftControl); NativeMethods.SendWheel(0, NativeMethods.WmLeftButtonDown); f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "Ctrl+mouse click is not a standalone Ctrl tap");
    f.Down(NativeMethods.VkLeftControl); f.Up(NativeMethods.VkLeftControl); f.Busy = true; f.Flush();
    Check(!f.Mode.IsActive && f.Active.Count == 0, "busy state cancels deferred Enter before UI dispatch"); f.Busy = false;
    f.Tap(); f.Down(NativeMethods.VkSpace); NativeMethods.ForegroundClass = "Notepad"; f.Flush();
    Check(f.Launches == 0 && !f.Mode.IsActive, "foreground change cancels deferred launch"); f.Up(NativeMethods.VkSpace);
}
using (var f = New(false))
{
    f.Down(NativeMethods.VkLeftControl); f.Mode.Configure(true, "Left"); f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "enabling while Ctrl held ignores its release");
    f.Tap(); f.Mode.Configure(true, "left"); Check(f.Mode.IsActive, "unchanged config preserves mode");
    f.Mode.Configure(true, "Both"); Check(!f.Mode.IsActive && f.Mode.Side == "Both" && f.Mode.PreferredSide == DockSide.Left, "Both config exits and normalizes");
    f.Tap(); f.Mode.Configure(true, "RIGHT"); Check(!f.Mode.IsActive && f.Mode.Side == "Right" && f.Mode.PreferredSide == DockSide.Right, "Right config exits and normalizes");
    f.Down(NativeMethods.VkLeftControl); f.Mode.Exit(); f.Up(NativeMethods.VkLeftControl); f.Flush(); Check(!f.Mode.IsActive, "Exit invalidates in-progress tap");
    f.Mode.Configure(true, "invalid"); Check(f.Mode.Side == "Left", "unknown side defaults Left");
    f.Tap(); f.Queue.AcceptEnqueue = false; f.Tap(); f.Tick(); Check(!f.Mode.IsActive, "timer retires UI state when dispatch enqueue fails");
}
using (var f = New())
{
    f.Tap(); f.Down(NativeMethods.VkW); f.Flush();
    f.Mode.Configure(true, "Right"); f.Tap();
    Check(!f.Mode.IsActive, "side change remembers a swallowed key still held during Ctrl tap");
    Check(f.Up(NativeMethods.VkW), "setting change still swallows matching key release");
    f.Tap(); Check(f.Mode.IsActive, "fresh tap works after held key release");
    NativeMethods.Keys.Add(NativeMethods.VkControl);
    Check(!f.Down(NativeMethods.VkSpace), "unexpected async Ctrl prevents Space interception"); f.Flush();
    Check(f.Launches == 0 && !f.Mode.IsActive, "missed Ctrl event cannot cause accidental launch");
}
foreach (int failure in new[] { 0, 1, 2 })
using (var f = New(false))
{
    if (failure == 0) NativeMethods.KeyboardHookAvailable = false;
    else if (failure == 1) NativeMethods.MouseHookAvailable = false;
    else NativeMethods.ThrowInstall = true;
    f.Mode.Configure(true, "Left");
    Check(!f.Mode.IsEnabled && !f.Queue.Timer.IsRunning && !string.IsNullOrWhiteSpace(f.Mode.LastError), "hook failure disables safely and records error");
    Check(NativeMethods.KeyboardHook is null && NativeMethods.MouseHook is null, "partial installation rolls back both hooks");
    NativeMethods.KeyboardHookAvailable = NativeMethods.MouseHookAvailable = true; NativeMethods.ThrowInstall = false;
    f.Mode.Configure(true, "Left"); f.Tap(); Check(f.Mode.IsActive && f.Mode.LastError is null, "configuration can retry hook installation");
}
foreach (int held in new[] { NativeMethods.VkLeftShift, NativeMethods.VkRightShift, NativeMethods.VkLeftMenu,
    NativeMethods.VkRightMenu, NativeMethods.VkLeftWindows, NativeMethods.VkW, 0x44 })
using (var f = New(false))
{
    f.Down(held); f.Mode.Configure(true, "Left"); f.Up(held); f.Flush(); f.Tap();
    Check(f.Mode.IsActive, "held startup key and its modifier alias clear on release");
}
using (var f = New(false))
{
    f.Down(NativeMethods.VkLeftWindows); f.Down(0x44); f.Mode.Configure(true, "Left");
    f.Up(0x44); f.Up(NativeMethods.VkLeftWindows); f.Flush(); f.Tap();
    Check(f.Mode.IsActive, "Win+D started before configuration does not block later Ctrl tap");
}
foreach (int stale in new[] { 0x85, 0x87, 0x43 })
using (var f = New(false))
{
    // Some keyboard tools leave async F22 down before EDock starts, with no
    // matching event delivered to our hook. The fix must not special-case F22.
    NativeMethods.Keys.Add(stale);
    f.Mode.Configure(true, "Left");
    for (int i = 0; i < 20; i++) f.Tick();
    f.Tap(); Check(f.Mode.IsActive, "unconfirmed initial async key cannot permanently block Ctrl entry");
    f.Tap(); Check(!f.Mode.IsActive, "unconfirmed initial async key also permits Ctrl exit");
    f.Mode.Configure(true, "Right");
    f.Mode.Configure(false, "Right");
    f.Mode.Configure(true, "Left");
    f.Tap(); Check(f.Mode.IsActive, "initial async key stays unconfirmed across reconfiguration and hook reinstallation");
    f.Mode.Exit();
    Check(!f.Down(stale), "actual down of inherited key passes through and confirms its chord state");
    f.Tap(); Check(!f.Mode.IsActive, "a hook-observed inherited key blocks Ctrl while genuinely held");
    Check(!f.Up(stale), "observed inherited key retains its original release stream");
    f.Tap(); Check(f.Mode.IsActive, "Ctrl works again after confirmed inherited key release");
}
foreach (int chord in new[] { 0x85, 0x43, NativeMethods.VkW, NativeMethods.VkS, 0x41, 0x44 })
foreach (bool ctrlFirst in new[] { false, true })
using (var f = New(false))
{
    NativeMethods.Keys.Add(0x85);
    f.Mode.Configure(true, "Left");
    if (ctrlFirst) { f.Down(NativeMethods.VkLeftControl); f.Down(chord); }
    else { f.Down(chord); f.Down(NativeMethods.VkLeftControl); }
    f.Up(NativeMethods.VkLeftControl); f.Up(chord); f.Flush();
    Check(!f.Mode.IsActive && f.Launches == 0 && f.Moves.Count == 0, "real F22/C/WASD chord cannot become Ctrl tap despite inherited async state");
    Check(NativeMethods.Events.All(value => !value.Suppressed), "real Ctrl chord preserves every key event with an inherited async key");
    f.Tap(); Check(f.Mode.IsActive, "standalone Ctrl remains usable after a real chord with inherited async state");
}
foreach (int held in new[] { NativeMethods.VkShift, NativeMethods.VkLeftShift, NativeMethods.VkRightShift,
    NativeMethods.VkMenu, NativeMethods.VkLeftMenu, NativeMethods.VkRightMenu,
    NativeMethods.VkLeftWindows, NativeMethods.VkRightWindows })
using (var f = New(false))
{
    f.Down(held); f.Mode.Configure(true, "Left");
    f.Tap(); Check(!f.Mode.IsActive, "initial modifier always blocks Ctrl, even without a hook-observed down");
    f.Up(held); f.Tap(); Check(f.Mode.IsActive, "initial modifier guard clears on release");
}
foreach (int held in new[] { 0x85, 0x43 })
using (var f = New())
{
    f.Down(held);
    f.Mode.Configure(true, "Right");
    f.Mode.Configure(true, "Both");
    f.Tap(); Check(!f.Mode.IsActive, "reconfiguration cannot downgrade an observed held key into an unconfirmed initial key");
    f.Up(held); f.Tap(); Check(f.Mode.IsActive, "observed held key stops blocking after release following reconfiguration");
}
foreach (int held in new[] { NativeMethods.VkW, NativeMethods.VkS, 0x41, 0x44, 0x51, 0x45, 0x46, 0x47, NativeMethods.VkSpace, 0x09 })
using (var f = New(false))
{
    f.Down(held); f.Mode.Configure(true, "Left");
    f.Tap(); Check(f.Mode.IsActive, "unconfirmed initial command key does not block a fresh Ctrl tap");
    Check(!f.Down(held) && !f.Up(held), "key held before hook installation keeps its repeat and release stream in quick mode");
    f.Flush();
    Check(f.Moves.Count == 0 && f.HorizontalMoves.Count == 0 && f.ListMoves.Count == 0
        && f.Expansions == 0 && f.Pins == 0 && f.DockSwitches == 0 && f.Launches == 0, "preexisting command repeat cannot navigate, switch, expand, pin or launch");
    Check(f.Down(held) && f.Up(held), "fresh command press becomes usable after its initial held stream ends");
    f.Flush();
    Check(f.Moves.Count + f.HorizontalMoves.Count + f.ListMoves.Count + f.Expansions + f.Pins
        + f.DockSwitches + f.Launches == 1, "released initial command key triggers exactly one fresh action");
}
using (var f = New())
{
    f.Tap(); int events = f.Active.Count; f.Mode.Dispose();
    Check(!f.Mode.IsActive && !f.Queue.Timer.IsRunning && f.Active.Count == events + 1, "dispose stops timer and exits exactly once");
    Check(NativeMethods.KeyboardHook is null && NativeMethods.MouseHook is null && NativeMethods.UnhookCount == 2, "dispose releases both hooks");
    f.Mode.Dispose(); f.Tick(); Check(f.Active.Count == events + 1, "dispose idempotent");
    try { f.Mode.Configure(true, "Left"); throw new Exception("Disposed Configure accepted"); } catch (ObjectDisposedException) { checks++; }
}
foreach (int dropped in new[] { 0, 0x1B, 0x44, NativeMethods.VkShift, NativeMethods.VkLeftShift, NativeMethods.VkMenu, NativeMethods.VkLeftWindows })
using (var f = New())
{
    f.Down(dropped);
    // Simulate OLE consuming the release: physical async state is up, no hook callback.
    NativeMethods.Keys.Remove(dropped);
    f.Tick(); f.Tap();
    Check(f.Mode.IsActive, "missed OLE key release clears before a fresh Ctrl tap");
}
using (var f = New())
{
    f.Down(NativeMethods.VkLeftControl); f.Down(0x43);
    NativeMethods.Keys.Remove(0x43); f.Tick();
    f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "recovering a chord key must not revive its canceled Ctrl tap");
}
using (var f = New())
{
    Check(!f.Down(0), "OLE VK0 sentinel passes through");
    f.Tap(); Check(f.Mode.IsActive, "VK0 sentinel cannot block a tap before the next timer tick");
    Check(f.Down(NativeMethods.VkW), "W swallowed before recovery tick"); f.Flush();
    for (int i = 0; i < 10; i++) f.Tick();
    Check(f.Mode.IsActive && f.Moves.SequenceEqual(new[] { -1 }), "recovery polling preserves suppressed held W");
    f.Tap(); Check(!f.Mode.IsActive, "Ctrl with suppressed held W cancels rather than counting as a standalone toggle");
    Check(f.Up(NativeMethods.VkW), "suppressed W release preserved after recovery polling");
    f.Tap(); Check(f.Mode.IsActive, "Ctrl toggles after recovered suppressed key is released");
}
using (var f = New())
{
    Check(f.Queue.Timer.Interval == TimeSpan.FromMilliseconds(100), "inactive mode reconciles at 100 ms");
    int reads = f.Queue.Timer.IntervalReads, writes = f.Queue.Timer.IntervalWrites;
    for (int i = 0; i < 100; i++) f.Tick();
    Check(f.Queue.Timer.IntervalReads == reads && f.Queue.Timer.IntervalWrites == writes, "idle ticks do not cross WinRT to read or rewrite timer cadence");
    f.Down(NativeMethods.VkLeftControl);
    Check(!f.Mode.IsActive && f.Queue.Timer.Interval == TimeSpan.FromMilliseconds(16), "Ctrl candidate immediately selects fast recovery without toggling");
    f.Up(NativeMethods.VkLeftControl);
    Check(!f.Mode.IsActive && f.Queue.Timer.Interval == TimeSpan.FromMilliseconds(16), "pending entry keeps fast recovery before dispatch");
    f.Flush();
    Check(f.Mode.IsActive && f.Queue.Timer.Interval == TimeSpan.FromMilliseconds(16), "normal Ctrl enters through dispatcher without waiting for a timer tick");
    reads = f.Queue.Timer.IntervalReads; writes = f.Queue.Timer.IntervalWrites;
    for (int i = 0; i < 100; i++) f.Tick();
    Check(f.Queue.Timer.IntervalReads == reads && f.Queue.Timer.IntervalWrites == writes, "active ticks also reuse cached cadence");
    f.Tap();
    Check(!f.Mode.IsActive && f.Queue.Timer.Interval == TimeSpan.FromMilliseconds(100), "second Ctrl immediately returns to idle cadence");
    f.Down(NativeMethods.VkLeftControl); f.Down(0x43);
    Check(f.Queue.Timer.Interval == TimeSpan.FromMilliseconds(100), "canceled inactive Ctrl chord returns to idle cadence");
    f.Up(0x43); f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "canceled chord cannot enter after cadence change");
    f.Tap(); NativeMethods.ForegroundClass = "Notepad"; f.Tick();
    Check(!f.Mode.IsActive && f.Queue.Timer.Interval == TimeSpan.FromMilliseconds(100), "active foreground-loss recovery returns to idle cadence");
}
using (var f = New())
{
    f.Queue.AcceptEnqueue = false;
    f.Down(NativeMethods.VkLeftControl); f.Up(NativeMethods.VkLeftControl);
    Check(!f.Mode.IsActive && f.Queue.Timer.Interval == TimeSpan.FromMilliseconds(16), "failed enqueue retains fast pending-action recovery");
    f.Tick();
    Check(!f.Mode.IsActive && f.Queue.Timer.Interval == TimeSpan.FromMilliseconds(100), "timer clears failed entry and returns to idle cadence");
}
foreach (string foreground in new[] { "Progman", "WorkerW", "CabinetWClass", "Chrome_WidgetWin_1" })
using (var f = New())
{
    NativeMethods.ForegroundClass = foreground;
    for (int i = 0; i < 1000; i++) f.Tick();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10000; i++) f.Tick();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated == 0, $"steady inactive quick-mode polling allocates no managed objects on {foreground}: {allocated} bytes");
}
using (var f = New())
{
    f.Tap();
    for (int i = 0; i < 1000; i++) f.Tick();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10000; i++) f.Tick();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated == 0 && f.Mode.IsActive, "steady active quick mode allocates no managed objects and remains active");
}
using (var f = New())
{
    f.Tap(); f.Down(NativeMethods.VkW); f.Flush();
    for (int i = 0; i < 1000; i++) f.Tick();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10000; i++) f.Tick();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated == 0 && f.Mode.IsActive && f.Moves.SequenceEqual(new[] { -1 }), "held suppressed W remains selected without polling allocations or repeated moves");
}
using (var f = New())
{
    f.CompanionWindow = 124; // Settings may exist while the desktop stays foreground.
    f.Tap(); Check(f.Mode.IsActive, "an open settings window does not block desktop quick mode");
    Check(NativeMethods.GuiThreadQueries == 0, "desktop quick mode adds no GUI thread queries");
    f.Mode.Exit();
    NativeMethods.ForegroundClass = "WinUIDesktopWin32WindowClass";
    NativeMethods.ForegroundWindow = 124;
    f.Tap(); Check(f.Mode.IsActive, "this instance's exact settings HWND permits entry");
    Check(f.Down(NativeMethods.VkW) && f.Up(NativeMethods.VkW), "settings foreground W pair selects shortcut");
    Check(f.Down(NativeMethods.VkS) && f.Up(NativeMethods.VkS), "settings foreground S pair selects shortcut");
    Check(NativeMethods.SendWheel(120) && NativeMethods.SendWheel(-120), "settings foreground wheel selects shortcuts");
    f.Flush(); Check(f.Moves.SequenceEqual(new[] { -1, 1, -1, 1 }), "settings keyboard and wheel selection order preserved");
    f.Tap(); Check(!f.Mode.IsActive, "settings foreground second Ctrl exits");
    Check(!f.Down(NativeMethods.VkW) && !f.Up(NativeMethods.VkW), "settings normal text input stays untouched outside quick mode");
    f.Tap(); Check(f.Down(NativeMethods.VkSpace), "settings foreground Space launches selected shortcut");
    f.Flush(); f.Up(NativeMethods.VkSpace);
    Check(f.Launches == 1 && !f.Mode.IsActive, "settings launch exits exactly once");
    NativeMethods.ForegroundWindow = 125;
    f.Tap(); Check(!f.Mode.IsActive, "same WinUI class in another instance is rejected");
    Check(!f.Down(NativeMethods.VkSpace) && !f.Up(NativeMethods.VkSpace), "another instance's Space remains untouched");
    f.Down(NativeMethods.VkLeftControl); NativeMethods.ForegroundWindow = 124;
    f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive, "Ctrl begun in another instance cannot enter on settings release");
    f.Tap(); NativeMethods.ForegroundWindow = 125; f.Tick();
    Check(!f.Mode.IsActive, "moving from settings to another instance exits");
}
using (var f = New(companionCallback: false))
{
    f.CompanionWindow = 124;
    NativeMethods.ForegroundClass = "WinUIDesktopWin32WindowClass"; NativeMethods.ForegroundWindow = 124;
    f.Tap(); Check(!f.Mode.IsActive && NativeMethods.GuiThreadQueries == 0, "optional companion callback preserves desktop-only default");
}
foreach (int chord in new[] { 0x43, 0x41, NativeMethods.VkW, NativeMethods.VkS, NativeMethods.VkSpace })
foreach (bool active in new[] { false, true })
using (var f = New())
{
    f.CompanionWindow = NativeMethods.ForegroundWindow = 124; NativeMethods.ForegroundClass = "WinUIDesktopWin32WindowClass";
    if (active) f.Tap();
    int events = NativeMethods.Events.Count;
    f.Down(NativeMethods.VkLeftControl); f.Down(chord); f.Up(chord); f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(!f.Mode.IsActive && f.Moves.Count == 0 && f.Launches == 0, "settings Ctrl chord neither toggles nor launches");
    Check(NativeMethods.Events.Skip(events).All(e => !e.Suppressed), "settings Ctrl chord passes through in either mode");
}
foreach (uint flag in new uint[] { 0x2, 0x4, 0x8, 0x10 })
using (var f = New())
{
    f.CompanionWindow = NativeMethods.ForegroundWindow = 124; NativeMethods.ForegroundClass = "WinUIDesktopWin32WindowClass";
    NativeMethods.GuiFlags = flag; f.Tap(); Check(!f.Mode.IsActive, "native settings menu or move/resize prevents entry");
    NativeMethods.GuiFlags = 0; f.Tap(); Check(f.Mode.IsActive, "fresh tap works after native interaction ends");
    NativeMethods.GuiFlags = flag; Check(!f.Down(NativeMethods.VkSpace), "native settings interaction keeps Space input");
    f.Up(NativeMethods.VkSpace); f.Flush(); Check(!f.Mode.IsActive && f.Launches == 0, "native interaction cancels active mode before launch");
}
foreach (int rejection in Enumerable.Range(0, 7))
using (var f = New())
{
    f.CompanionWindow = NativeMethods.ForegroundWindow = 124; NativeMethods.ForegroundClass = "WinUIDesktopWin32WindowClass";
    void Reject()
    {
        switch (rejection)
        {
            case 0: f.CompanionWindow = 0; break;
            case 1: NativeMethods.Minimized = true; break;
            case 2: NativeMethods.WindowEnabled = false; break;
            case 3: NativeMethods.GuiQuerySuccess = false; break;
            case 4: NativeMethods.GuiActiveOverride = 125; break;
            case 5: NativeMethods.GuiCapture = 124; break;
            case 6: f.Busy = true; break;
        }
    }
    f.Tap(); Reject(); f.Tick();
    Check(!f.Mode.IsActive, "closed/minimized/disabled/busy settings or failed/racing/captured GUI exits");
    f.Tap(); Check(!f.Mode.IsActive, "invalid settings foreground cannot re-enter");
}
foreach (bool launch in new[] { false, true })
foreach (int invalidation in Enumerable.Range(0, 5))
using (var f = New())
{
    f.CompanionWindow = NativeMethods.ForegroundWindow = 124; NativeMethods.ForegroundClass = "WinUIDesktopWin32WindowClass";
    if (launch) { f.Tap(); f.Down(NativeMethods.VkSpace); }
    else { f.Down(NativeMethods.VkLeftControl); f.Up(NativeMethods.VkLeftControl); }
    switch (invalidation)
    {
        case 0: f.CompanionWindow = 0; break;
        case 1: f.CompanionWindow = 125; break;
        case 2: f.Busy = true; break;
        case 3: NativeMethods.GuiFlags = 0x4; break;
        case 4: NativeMethods.ForegroundWindow = 125; break;
    }
    f.Flush();
    Check(!f.Mode.IsActive && f.Launches == 0, "settings invalidation cancels deferred entry or launch");
    if (launch) f.Up(NativeMethods.VkSpace);
}
foreach (bool active in new[] { false, true })
using (var f = New())
{
    f.CompanionWindow = NativeMethods.ForegroundWindow = 124; NativeMethods.ForegroundClass = "WinUIDesktopWin32WindowClass";
    if (active) f.Tap();
    for (int i = 0; i < 1000; i++) f.Tick();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10000; i++) f.Tick();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated == 0 && f.Mode.IsActive == active, "settings foreground quick-mode polling retains zero managed allocation in either state");
}
using (var f = New())
{
    f.Tap();
    foreach (int key in new[] { 0x51, 0x45, 0x46, 0x47 })
    {
        Check(f.Down(key), "quick Q/E/F/G fresh down swallowed");
        for (int repeat = 0; repeat < 5; repeat++) Check(f.Down(key), "quick Q/E/F/G repeat swallowed");
        Check(f.Up(key), "quick Q/E/F/G release swallowed");
        f.Flush();
    }
    Check(f.ListMoves.SequenceEqual(new[] { -1, 1 }) && f.Expansions == 1 && f.Pins == 1 && f.Mode.IsActive, "quick Q/E changes lists and F/G expand or pin exactly once while retaining quick mode");
    f.Down(0x51); f.Down(0x45); f.Flush();
    Check(f.ListMoves.Count == 2, "opposite quick list keys held together cancel queued switches");
    f.Up(0x51); f.Up(0x45); f.Release();
}
foreach (DockSide side in Enum.GetValues<DockSide>())
using (var f = New(false))
{
    NativeMethods.ForegroundClass = "Notepad";
    f.Hover(side);
    Check(!f.Mode.IsEnabled && !f.Mode.IsActive && NativeMethods.KeyboardHook is not null && NativeMethods.MouseHook is null && f.Queue.Timer.IsRunning, "normal hover only installs temporary keyboard hook and reconciliation timer without enabling quick mode");
    var actions = new[] { DockKeyboardAction.Previous, DockKeyboardAction.Next, DockKeyboardAction.PreviousList, DockKeyboardAction.NextList, DockKeyboardAction.ToggleExpanded, DockKeyboardAction.TogglePinned, DockKeyboardAction.Launch };
    int index = 0;
    foreach (int key in new[] { NativeMethods.VkW, NativeMethods.VkS, 0x51, 0x45, 0x46, 0x47, NativeMethods.VkSpace })
    {
        Check(f.Down(key), "normal hover fresh command down swallowed");
        if (key is not (NativeMethods.VkW or NativeMethods.VkS))
            for (int repeat = 0; repeat < 3; repeat++) Check(f.Down(key), "normal single-action repeat swallowed without action");
        Check(f.Up(key), "normal hover matching release swallowed");
        f.Flush();
        Check(f.Commands.Count == ++index && f.Commands[^1] == new DockKeyboardRequest(side, actions[index - 1]), "normal command retains exact target side and action with one dispatch per press");
    }
    Check(f.Active.Count == 0 && f.Moves.Count == 0 && f.Launches == 0 && f.ListMoves.Count == 0 && f.Expansions == 0 && f.Pins == 0, "normal commands never activate quick mode or invoke its pin/reveal event path");
    Check(!NativeMethods.SendWheel(120) && !NativeMethods.SendWheel(-120), "normal hover wheel remains ordinary scrolling");
    f.Hover(null);
    Check(NativeMethods.KeyboardHook is null && NativeMethods.MouseHook is null && !f.Queue.Timer.IsRunning, "leaving normal hover releases all native hooks and stops timer");
}
foreach (int key in new[] { NativeMethods.VkW, NativeMethods.VkSpace, 0x51, 0x46, 0x47 })
using (var f = New(false))
{
    Check(!f.Down(key), "key held before hover initially passes");
    f.Hover(DockSide.Left);
    Check(!f.Down(key) && !f.Up(key), "held key entering hover keeps repeat and release stream untouched");
    f.Flush(); Check(f.Commands.Count == 0, "held key entering hover cannot act as a new command");
    Check(f.Down(key) && f.Up(key), "fresh press after held release can trigger normal command");
    f.Flush(); Check(f.Commands.Count == 1, "held-in protection ends after release");
}
foreach (bool reenter in new[] { false, true })
using (var f = New(false))
{
    f.Hover(DockSide.Left); Check(f.Down(NativeMethods.VkW), "hover command down consumed before leaving"); f.Flush();
    f.Hover(null);
    Check(NativeMethods.KeyboardHook is not null && NativeMethods.MouseHook is null && !f.Queue.Timer.IsRunning, "leaving while swallowed key is held retains only keyboard tail listener without timer");
    if (reenter) f.Hover(DockSide.Right);
    Check(f.Down(NativeMethods.VkW), "held swallowed repeat stays swallowed after leaving or reentering another side");
    Check(f.Up(NativeMethods.VkW), "held swallowed release stays swallowed after leaving or reentering");
    f.Flush(); Check(f.Commands.Count == 1, "hover transfer does not repeat held action");
    if (reenter)
    {
        Check(f.Down(NativeMethods.VkW) && f.Up(NativeMethods.VkW), "new press on second side consumed");
        f.Flush(); Check(f.Commands.Count == 2 && f.Commands[^1].Side == DockSide.Right, "fresh action after transfer targets second side");
        f.Hover(null);
    }
    Check(NativeMethods.KeyboardHook is null && !f.Queue.Timer.IsRunning, "tail listener releases immediately after final matching release");
}
foreach (int invalidation in Enumerable.Range(0, 8))
using (var f = New(false))
{
    f.Hover(DockSide.Left); f.Down(NativeMethods.VkSpace);
    switch (invalidation)
    {
        case 0: f.Hover(null); break;
        case 1: f.Hover(DockSide.Right); break;
        case 2: f.PointerSide = null; break; // Native hook/drain must not await the edge tick.
        case 3: f.PointerSide = DockSide.Right; break;
        case 4: f.Busy = true; break;
        case 5: f.Mode.Exit(); break;
        case 6: f.Mode.Configure(false, "Right"); break;
        case 7: f.Hover(null); f.Hover(DockSide.Left); break;
    }
    f.Flush();
    Check(f.Commands.Count == 0 && f.Active.Count == 0, "deferred launch is canceled by leave/side change/busy/exit/configuration/session change");
    Check(f.Up(NativeMethods.VkSpace), "canceled deferred normal launch still consumes matching key release");
    f.Flush();
}
using (var f = New(false))
{
    f.Hover(DockSide.Left); f.PointerSide = null;
    Check(!f.Down(NativeMethods.VkSpace), "instant pointer loss before edge tick passes new down through");
    f.Flush(); Check(NativeMethods.KeyboardHook is null && !f.Queue.Timer.IsRunning && f.Commands.Count == 0, "instant pointer loss retires temporary listener without stale action");
    f.Up(NativeMethods.VkSpace);
}
foreach (int modifier in new[] { NativeMethods.VkLeftControl, NativeMethods.VkRightControl, NativeMethods.VkLeftShift, NativeMethods.VkRightShift, NativeMethods.VkLeftMenu, NativeMethods.VkRightMenu, NativeMethods.VkLeftWindows, NativeMethods.VkRightWindows })
using (var f = New(false))
{
    f.Hover(DockSide.Left);
    Check(!f.Down(modifier) && !f.Down(NativeMethods.VkSpace) && !f.Up(NativeMethods.VkSpace) && !f.Up(modifier), "normal hover modifier chord remains fully untouched");
    f.Flush(); Check(f.Commands.Count == 0 && !f.Mode.IsActive, "normal modifier chord cannot open shortcut or enter quick mode");
    f.Down(NativeMethods.VkW); f.Down(modifier); f.Up(modifier); f.Flush();
    Check(f.Commands.Count == 0, "modifier begun after a queued normal down invalidates the pending action even after modifier release");
    Check(f.Up(NativeMethods.VkW), "invalidated command release remains consumed");
}
using (var f = New(false))
{
    f.Hover(DockSide.Left); f.Down(NativeMethods.VkW); f.Down(NativeMethods.VkS); f.Flush();
    Check(f.Commands.Count == 0, "opposite normal selection keys cancel pending selection");
    f.Release(); f.Down(0x51); f.Down(0x45); f.Flush();
    Check(f.Commands.Count == 0, "opposite normal list keys cancel pending list change");
    f.Release();
}
foreach (int interaction in Enumerable.Range(0, 6))
using (var f = New(false))
{
    f.Hover(DockSide.Left);
    void BlockInteraction()
    {
        if (interaction == 0) NativeMethods.GuiQuerySuccess = false;
        else if (interaction == 1) NativeMethods.GuiCapture = 123;
        else NativeMethods.GuiFlags = 1u << (interaction - 1);
    }
    BlockInteraction();
    Check(!f.Down(NativeMethods.VkSpace) && !f.Up(NativeMethods.VkSpace), "ordinary native menu/capture/move/resize or failed query keeps key input untouched");
    f.Flush(); Check(f.Commands.Count == 0, "blocked native interaction cannot launch a normal hover shortcut");
    NativeMethods.GuiQuerySuccess = true; NativeMethods.GuiCapture = 0; NativeMethods.GuiFlags = 0;
    f.Down(NativeMethods.VkSpace); BlockInteraction(); f.Flush();
    Check(f.Commands.Count == 0, "native interaction invalidates already queued normal launch");
    Check(f.Up(NativeMethods.VkSpace), "invalidated native interaction preserves swallowed release");
}
using (var f = New())
{
    f.Tap(); f.Down(NativeMethods.VkW); f.Flush();
    f.Mode.Configure(false, "Left");
    Check(NativeMethods.KeyboardHook is not null && NativeMethods.MouseHook is null && !f.Queue.Timer.IsRunning, "disabling quick mode while command held retains only its swallowed key tail");
    Check(f.Down(NativeMethods.VkW) && f.Up(NativeMethods.VkW), "quick-mode disabling does not leak repeat or matching release");
    f.Flush(); Check(NativeMethods.KeyboardHook is null && !f.Queue.Timer.IsRunning, "quick-mode disabled final release retires tail listener");
}
using (var f = New(false))
{
    f.Hover(DockSide.Left); f.Queue.AcceptEnqueue = false;
    Check(f.Down(NativeMethods.VkSpace), "failed dispatcher still consumes originally accepted normal command");
    f.Tick(); Check(f.Commands.Count == 0 && f.Active.Count == 0, "failed normal dispatcher cannot replay launch from timer");
    Check(f.Up(NativeMethods.VkSpace), "failed dispatcher preserves swallowed key release");
    f.Hover(null); f.Queue.AcceptEnqueue = true; f.Flush();
    Check(NativeMethods.KeyboardHook is null && !f.Queue.Timer.IsRunning, "failed dispatcher recovery releases all resources after hover leaves");
}
using (var f = New(false))
{
    NativeMethods.KeyboardHookAvailable = false; f.Hover(DockSide.Left);
    Check(NativeMethods.KeyboardHook is null && NativeMethods.MouseHook is null && !f.Queue.Timer.IsRunning && f.Mode.LastError is not null, "failed ordinary keyboard installation leaves no hooks or timer");
    NativeMethods.KeyboardHookAvailable = true; f.Mode.RefreshPointerTarget();
    Check(NativeMethods.KeyboardHook is not null && NativeMethods.MouseHook is null && f.Queue.Timer.IsRunning, "ordinary keyboard installation can retry without enabling quick mode");
}
using (var f = New())
{
    f.Hover(DockSide.Right); f.Tap(); f.Down(0x45); f.Up(0x45); f.Down(0x46); f.Up(0x46); f.Flush();
    Check(f.ListMoves.SequenceEqual(new[] { 1 }) && f.Expansions == 1 && f.Commands.Count == 0, "active quick mode owns commands instead of normal hovered side");
    f.Mode.Configure(false, "Left");
    Check(NativeMethods.KeyboardHook is not null && NativeMethods.MouseHook is null && f.Queue.Timer.IsRunning, "disabling quick mode preserves ordinary hovered keyboard operation only");
    NativeMethods.ForegroundClass = "Notepad";
    f.Down(NativeMethods.VkSpace); f.Up(NativeMethods.VkSpace); f.Flush();
    Check(f.Commands.SequenceEqual(new[] { new DockKeyboardRequest(DockSide.Right, DockKeyboardAction.Launch) }) && !f.Mode.IsActive, "ordinary hover continues independently with quick mode disabled and application foreground");
}
using (var f = New(false))
{
    f.Hover(DockSide.Left);
    for (int i = 0; i < 1000; i++) { f.Mode.RefreshPointerTarget(); f.Tick(); }
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10000; i++) { f.Mode.RefreshPointerTarget(); f.Tick(); }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated == 0 && !f.Mode.IsActive, "steady ordinary hover reconciliation creates no managed allocations");
    f.Hover(null); before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10000; i++) f.Mode.RefreshPointerTarget();
    allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated == 0 && !f.Queue.Timer.IsRunning && NativeMethods.KeyboardHook is null, "disabled hidden state has no hook/timer or managed allocations");
}
using (var f = New(false))
{
    f.Hover(DockSide.Left); f.Down(NativeMethods.VkW); f.Down(0x46); f.Flush();
    f.Hover(null);
    Check(f.Up(NativeMethods.VkW), "first of two swallowed releases stays consumed after leaving"); f.Flush();
    Check(NativeMethods.KeyboardHook is not null && !f.Queue.Timer.IsRunning, "remaining swallowed held key retains only keyboard listener");
    Check(f.Down(0x46) && f.Up(0x46), "second swallowed repeat and release stay consumed"); f.Flush();
    Check(NativeMethods.KeyboardHook is null && !f.Queue.Timer.IsRunning && f.Commands.Count == 2, "final swallowed release retires temporary listener without replay");
}
foreach (int mutation in Enumerable.Range(0, 3))
using (var f = New(false))
{
    f.Hover(DockSide.Left);
    f.Mode.DockCommandRequested += (_, _) =>
    {
        if (mutation == 0) f.Mode.Configure(false, "Right");
        else if (mutation == 1) f.Hover(DockSide.Right);
        else f.Mode.Dispose();
    };
    f.Down(NativeMethods.VkW); f.Up(NativeMethods.VkW); f.Down(0x46); f.Up(0x46); f.Flush();
    Check(f.Commands.Count == 1 && f.Commands[0].Side == DockSide.Left, "command callback config/side change/dispose cancels remaining captured normal commands");
    if (mutation == 2) Check(NativeMethods.KeyboardHook is null && !f.Queue.Timer.IsRunning, "disposing inside command does not reinstall hook or timer during drain cleanup");
    else { f.Hover(null); Check(NativeMethods.KeyboardHook is null && !f.Queue.Timer.IsRunning, "callback mutation leaves resources releasable without residual listeners"); }
}
foreach (bool quick in new[] { false, true })
foreach (bool list in new[] { false, true })
using (var f = New(quick))
{
    if (!quick) f.Hover(DockSide.Left);
    else { f.Down(NativeMethods.VkLeftControl); f.Up(NativeMethods.VkLeftControl); }
    // Keep quick Enter pending too: cancellation must retain that state change.
    f.Down(list ? 0x51 : NativeMethods.VkW);
    f.Down(list ? 0x45 : NativeMethods.VkS);
    f.Down(0x46); f.Up(0x46); // An unrelated expansion must retain its order.
    f.Up(list ? 0x51 : NativeMethods.VkW);
    f.Up(list ? 0x45 : NativeMethods.VkS);
    f.Flush();
    Check(f.Moves.Count == 0 && f.ListMoves.Count == 0, "opposite keys permanently cancel queued quick move/list request even after both releases before drain");
    if (quick) Check(f.Mode.IsActive && f.Expansions == 1, "quick opposite cancellation preserves pending entry and unrelated expansion");
    else Check(f.Commands.SequenceEqual(new[] { new DockKeyboardRequest(DockSide.Left, DockKeyboardAction.ToggleExpanded) }), "normal opposite cancellation preserves only unrelated queued commands after both releases");
}
foreach (bool quick in new[] { false, true })
foreach (int key in new[] { NativeMethods.VkW, NativeMethods.VkS, 0x41, 0x44 })
using (var f = New(quick))
{
    if (quick) f.Tap(); else { NativeMethods.ForegroundClass = "Notepad"; f.Hover(DockSide.Right); }
    for (int i = 0; i < 8; i++) { Check(f.Down(key), "WASD down and every system repeat stay consumed"); f.Flush(); }
    Check(f.Up(key), "repeating WASD matching release remains consumed"); f.Flush();
    int direction = key is NativeMethods.VkW or 0x41 ? -1 : 1;
    if (quick)
    {
        var moves = key is NativeMethods.VkW or NativeMethods.VkS ? f.VerticalMoves : f.HorizontalMoves;
        Check(moves.Count == 8 && moves.All(value => value == direction), "quick WASD repeats dispatch eight moves on the intended visual axis");
        Check(f.WheelMoves.Count == 0, "quick WASD does not use linear wheel movement event");
        if (key is NativeMethods.VkW or NativeMethods.VkS) Check(f.HorizontalMoves.Count == 0, "W/S never emits horizontal movement");
        else Check(f.VerticalMoves.Count == 0, "A/D never emits vertical movement");
    }
    else
    {
        var action = key switch { NativeMethods.VkW => DockKeyboardAction.Previous, NativeMethods.VkS => DockKeyboardAction.Next, 0x41 => DockKeyboardAction.PreviousColumn, _ => DockKeyboardAction.NextColumn };
        Check(f.Commands.Count == 8 && f.Commands.All(command => command == new DockKeyboardRequest(DockSide.Right, action)), "ordinary WASD repeats retain target and intended visual axis");
        Check(!f.Mode.IsActive && NativeMethods.MouseHook is null, "ordinary repeat does not activate quick mode or install mouse hook");
    }
}
foreach (bool quick in new[] { false, true })
using (var f = New(quick))
{
    if (quick) f.Tap(); else f.Hover(DockSide.Left);
    for (int i = 0; i < 10; i++) { Check(f.Down(0x09), "Tab down and repeats consumed"); f.Flush(); }
    Check(f.Up(0x09), "Tab up consumed");
    if (quick) Check(f.DockSwitches == 1 && f.Mode.IsActive, "quick Tab switches once without ending quick mode");
    else Check(f.Commands.SequenceEqual(new[] { new DockKeyboardRequest(DockSide.Left, DockKeyboardAction.SwitchDock) }), "ordinary Tab dispatches one side-switch request per fresh press");
}
foreach (int key in new[] { 0x41, 0x44, 0x09 })
using (var f = New(false))
{
    f.Down(key); f.Hover(DockSide.Left);
    Check(!f.Down(key) && !f.Up(key), "A/D/Tab held before hover preserves repeat and release stream"); f.Flush();
    Check(f.Commands.Count == 0, "A/D/Tab held before hover cannot start command from repeat");
}
foreach (bool quick in new[] { false, true })
foreach (bool horizontal in new[] { false, true })
using (var f = New(quick))
{
    if (quick) f.Tap(); else f.Hover(DockSide.Left);
    int first = horizontal ? 0x41 : NativeMethods.VkW, second = horizontal ? 0x44 : NativeMethods.VkS;
    int independent = horizontal ? NativeMethods.VkS : 0x44;
    f.Down(first); f.Down(first); f.Down(second); f.Down(second);
    f.Down(independent); f.Up(independent);
    f.Up(second); f.Down(first); // Held surviving key must not resume canceled repeat.
    f.Up(first); f.Flush();
    if (quick)
    {
        Check(horizontal ? f.HorizontalMoves.Count == 0 : f.VerticalMoves.Count == 0, "quick conflict cancels all queued movement and continued held repeat for that axis");
        Check(horizontal ? f.VerticalMoves.SequenceEqual(new[] { 1 }) : f.HorizontalMoves.SequenceEqual(new[] { 1 }), "quick conflicting axis leaves independent visual axis queued");
    }
    else
    {
        var action = horizontal ? DockKeyboardAction.Next : DockKeyboardAction.NextColumn;
        Check(f.Commands.SequenceEqual(new[] { new DockKeyboardRequest(DockSide.Left, action) }), "ordinary conflicting axis cancels its pending repeat but preserves independent axis");
    }
}
foreach (int invalidation in Enumerable.Range(0, 6))
using (var f = New(false))
{
    f.Hover(DockSide.Left); f.Down(0x44); f.Flush();
    switch (invalidation)
    {
        case 0: f.Hover(DockSide.Right); break;
        case 1: f.Hover(null); f.Hover(DockSide.Left); break;
        case 2: f.Busy = true; break;
        case 3: f.Down(NativeMethods.VkLeftShift); f.Up(NativeMethods.VkLeftShift); break;
        case 4: NativeMethods.GuiFlags = 0x4; break;
        case 5: NativeMethods.GuiCapture = 123; break;
    }
    Check(f.Down(0x44), "invalidated held repeat stays swallowed"); f.Flush();
    f.Busy = false; NativeMethods.GuiFlags = 0; NativeMethods.GuiCapture = 0; f.Hover(DockSide.Left);
    Check(f.Down(0x44), "restored eligibility still swallows tail repeat"); f.Flush();
    Check(f.Commands.Count == 1, "invalidated repeat source cannot restart after eligibility returns");
    Check(f.Up(0x44), "invalidated movement matching release swallowed");
    f.Down(0x44); f.Up(0x44); f.Flush();
    Check(f.Commands.Count == 2, "released movement can start new navigation after restored eligibility");
}
using (var f = New(false))
{
    f.Hover(DockSide.Left);
    f.Mode.DockCommandRequested += (_, command) => { if (command.Action == DockKeyboardAction.SwitchDock) f.Hover(DockSide.Right); };
    f.Down(NativeMethods.VkW); f.Flush();
    f.Down(0x09); f.Up(0x09); f.Flush();
    Check(f.Down(NativeMethods.VkW), "Tab switched ordinary session still consumes original held W repeat"); f.Flush();
    Check(f.Commands.Count == 2 && f.Commands[1].Action == DockKeyboardAction.SwitchDock, "Tab target change stops original side's held repeat");
    f.Up(NativeMethods.VkW); f.Down(NativeMethods.VkS); f.Up(NativeMethods.VkS); f.Flush();
    Check(f.Commands[^1] == new DockKeyboardRequest(DockSide.Right, DockKeyboardAction.Next), "caller-provided ordinary Tab session accepts fresh navigation on opposite dock");
    f.Hover(null); Check(NativeMethods.KeyboardHook is null && !f.Queue.Timer.IsRunning, "ordinary Tab session ending releases hook and timer");
}
using (var f = New())
{
    f.Tap(); f.Down(0x44); f.Flush();
    f.Down(0x09); f.Up(0x09); f.Down(0x44); f.Flush();
    Check(f.DockSwitches == 1 && f.HorizontalMoves.Count == 1, "quick Tab cancels queued original target repeats and preserves switch");
    f.Down(0x44); f.Flush(); Check(f.HorizontalMoves.Count == 1, "quick Tab prevents held movement repeat on new side until repressed");
    f.Up(0x44); f.Down(0x44); f.Up(0x44); f.Flush();
    Check(f.HorizontalMoves.Count == 2, "fresh movement after quick Tab resumes navigation");
    NativeMethods.SendWheel(120); NativeMethods.SendWheel(-120); f.Flush();
    Check(f.WheelMoves.SequenceEqual(new[] { -1, 1 }) && f.VerticalMoves.Count == 0, "quick wheel retains independent linear selection event");
}
foreach (bool byTick in new[] { false, true })
using (var f = New(false))
{
    f.Hover(DockSide.Left); f.Down(0x44); f.Flush();
    NativeMethods.GuiFlags = 0x4;
    if (byTick) f.Tick(); else f.Down(0x44);
    NativeMethods.GuiFlags = 0;
    f.Down(0x44); f.Flush();
    Check(f.Commands.Count == 1, "native menu observed by repeat or timer permanently invalidates held normal repeat until release");
    f.Up(0x44);
}
using (var f = New(false))
{
    f.Hover(DockSide.Left); f.Down(0x44);
    NativeMethods.GuiCapture = 123; f.Down(0x44);
    NativeMethods.GuiCapture = 0; f.Flush();
    Check(f.Commands.Count == 0, "invalid repeat cancels earlier queued movement even when native interaction ends before drain");
    f.Up(0x44);
}
foreach (int invalidation in Enumerable.Range(0, 4))
using (var f = New())
{
    f.Tap(); f.Down(0x41); f.Flush();
    switch (invalidation)
    {
        case 0: NativeMethods.ForegroundClass = "Notepad"; break;
        case 1: f.Busy = true; break;
        case 2: f.Mode.Exit(); break;
        case 3: f.Mode.Configure(false, "Left"); break;
    }
    Check(f.Down(0x41), "quick invalidation still swallows original held horizontal repeat"); f.Flush();
    NativeMethods.ForegroundClass = "Progman"; f.Busy = false; f.Hover(DockSide.Left);
    Check(f.Down(0x41), "returning ordinary eligibility cannot inherit quick repeat source"); f.Flush();
    Check(f.HorizontalMoves.Count == 1 && f.Commands.Count == 0 && !f.Mode.IsActive, "quick repeat stops on foreground/busy/Exit/disable and never changes into ordinary repeat");
    Check(f.Up(0x41), "invalidated quick A release stays swallowed");
}
using (var f = New(false))
{
    f.Hover(DockSide.Left); f.Down(0x44); f.Flush();
    for (int i = 0; i < 1000; i++) f.Tick();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10000; i++) f.Tick();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated == 0 && f.Commands.Count == 1, "held repeat source adds no timer-driven movement or steady polling allocations");
    f.Hover(null); f.Up(0x44); f.Flush();
    Check(NativeMethods.KeyboardHook is null && !f.Queue.Timer.IsRunning, "repeat-source release after hidden ordinary session retires all background resources");
}
using (var f = New(false))
{
    NativeMethods.Keys.Add(0x85);
    f.Mode.Configure(true, "Left");
    for (int i = 0; i < 1000; i++) f.Tick();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10000; i++) f.Tick();
    Check(GC.GetAllocatedBytesForCurrentThread() == before, "inherited async key reconciliation creates no steady managed allocations");
    f.Tap(); Check(f.Mode.IsActive, "Ctrl still enters after prolonged inherited async key reconciliation");
}
foreach (bool quick in new[] { false, true })
foreach (DockSide side in Enum.GetValues<DockSide>())
using (var f = New(quick))
{
    if (quick) { f.Mode.Configure(true, side.ToString()); f.Hover(side == DockSide.Left ? DockSide.Right : DockSide.Left); f.Tap(); }
    else f.Hover(side);
    Check(f.Down(0x47), "G accepts a fresh press in either ordinary or quick mode");
    f.Flush();
    for (int repeat = 0; repeat < 12; repeat++)
    {
        Check(f.Down(0x47), "held G repeats stay consumed");
        f.Flush(); f.Tick();
        Check(quick ? f.Pins == 1 : f.Commands.Count == 1, "held G cannot repeatedly toggle the pin even across dispatcher drains and reconciliation ticks");
    }
    Check(f.Up(0x47), "G matching release remains consumed");
    f.Flush();
    Check(f.Down(0x47) && f.Up(0x47), "a second physical G press can toggle the pin again");
    f.Flush();
    if (quick)
        Check(f.Pins == 2 && f.PinSides.All(value => value == side.ToString()) && f.Commands.Count == 0 && f.Mode.IsActive,
            "quick G preserves the configured side and quick mode despite an opposite ordinary target");
    else
        Check(f.Commands.SequenceEqual(Enumerable.Repeat(new DockKeyboardRequest(side, DockKeyboardAction.TogglePinned), 2)) && f.Pins == 0 && !f.Mode.IsActive,
            "ordinary G targets only the requested side without entering quick mode");
}
using (var f = New())
{
    f.Mode.Configure(true, "Both");
    f.Down(NativeMethods.VkLeftControl); f.Up(NativeMethods.VkLeftControl);
    Check(f.Down(0x47) && f.Up(0x47), "G can follow a pending Ctrl entry without waiting for a timer");
    f.Flush();
    Check(f.PinSides.SequenceEqual(new[] { "Both" }) && f.Mode.IsActive,
        "both-side quick configuration dispatches one current-target pin event after entry");
    var order = new List<string>();
    f.Mode.DockSwitchRequested += (_, _) => order.Add("switch");
    f.Mode.PinRequested += (_, _) => order.Add("pin");
    f.Down(0x09); f.Up(0x09); f.Down(0x47); f.Up(0x47); f.Flush();
    Check(order.SequenceEqual(new[] { "switch", "pin" }), "queued Tab reaches the caller before G so the new current side receives the pin request");
}
using (var f = New(false))
{
    f.Hover(DockSide.Left);
    f.Mode.DockCommandRequested += (_, command) => { if (command.Action == DockKeyboardAction.TogglePinned) f.Hover(null); };
    Check(f.Down(0x47), "ordinary G accepts the unpin command before its target disappears");
    f.Flush();
    Check(f.Commands.SequenceEqual(new[] { new DockKeyboardRequest(DockSide.Left, DockKeyboardAction.TogglePinned) }), "unpin callback executes exactly once on its original side");
    Check(NativeMethods.KeyboardHook is not null && !f.Queue.Timer.IsRunning, "unpinning an away-from-pointer target retains only the swallowed key tail");
    Check(f.Down(0x47) && f.Up(0x47), "unpinning does not leak held G repeats or the matching release");
    f.Flush();
    Check(f.Commands.Count == 1 && NativeMethods.KeyboardHook is null && !f.Queue.Timer.IsRunning, "unpinning and releasing G retire the temporary listener without another toggle");
}
foreach (int invalidation in Enumerable.Range(0, 5))
using (var f = New(false))
{
    f.Hover(DockSide.Left); Check(f.Down(0x47), "ordinary G queues against its exact original target");
    switch (invalidation)
    {
        case 0: f.Hover(null); break;
        case 1: f.Hover(DockSide.Right); break;
        case 2: f.PointerSide = null; break;
        case 3: f.Busy = true; break;
        case 4: f.Mode.Exit(); break;
    }
    f.Flush();
    Check(f.Commands.Count == 0 && f.Pins == 0, "ordinary G cannot pin after target loss, side transfer, busy state or session exit");
    Check(f.Up(0x47), "canceled ordinary pin still swallows its matching release");
}
foreach (int invalidation in Enumerable.Range(0, 4))
using (var f = New())
{
    f.Tap(); Check(f.Down(0x47), "quick G initially queues in an eligible context");
    switch (invalidation)
    {
        case 0: NativeMethods.ForegroundClass = "Notepad"; break;
        case 1: f.Busy = true; break;
        case 2: f.Mode.Exit(); break;
        case 3: f.Mode.Configure(false, "Left"); break;
    }
    f.Flush();
    Check(f.Pins == 0 && f.Commands.Count == 0 && !f.Mode.IsActive, "foreground loss, busy state, exit or disable cancels a deferred quick pin");
    Check(f.Down(0x47) && f.Up(0x47), "canceled quick pin keeps the original repeat/release stream consumed");
}
foreach (bool quick in new[] { false, true })
foreach (int modifier in new[] { NativeMethods.VkLeftControl, NativeMethods.VkRightControl, NativeMethods.VkShift,
    NativeMethods.VkLeftShift, NativeMethods.VkRightShift, NativeMethods.VkMenu, NativeMethods.VkLeftMenu,
    NativeMethods.VkRightMenu, NativeMethods.VkLeftWindows, NativeMethods.VkRightWindows })
using (var f = New(quick))
{
    if (quick) f.Tap(); else f.Hover(DockSide.Left);
    Check(!f.Down(modifier) && !f.Down(0x47) && !f.Up(0x47) && !f.Up(modifier), "modified G preserves every original key event");
    f.Flush();
    Check(f.Pins == 0 && f.Commands.Count == 0, "Ctrl/Shift/Alt/Win plus G cannot pin either side");
}
foreach (bool quick in new[] { false, true })
using (var f = New(quick))
{
    if (quick) f.Tap(); else f.Hover(DockSide.Left);
    Check(f.Down(0x47), "unmodified G queues before a later modifier");
    f.Down(NativeMethods.VkLeftShift); f.Up(NativeMethods.VkLeftShift); f.Flush();
    Check(f.Pins == 0 && f.Commands.Count == 0, "a modifier started after G permanently invalidates its queued pin even when released before dispatch");
    Check(f.Up(0x47), "invalidated G preserves its swallowed matching release");
}
foreach (bool beforeDown in new[] { false, true })
using (var f = New(false))
{
    f.DesktopOnlyTarget = true; f.Hover(DockSide.Right);
    if (beforeDown) NativeMethods.ForegroundClass = "Notepad";
    bool consumed = f.Down(0x47);
    if (!beforeDown) NativeMethods.ForegroundClass = "Notepad";
    f.Flush();
    Check(consumed != beforeDown && f.Commands.Count == 0, "desktop-only G passes new application input or cancels a previously queued desktop pin");
    Check(f.Up(0x47) == consumed, "desktop-only context loss preserves the original G stream ownership");
}
foreach (string desktop in new[] { "Progman", "WorkerW" })
{
    NativeMethods.Reset(); NativeMethods.ForegroundClass = desktop;
    Check(DesktopInputContext.IsDesktopForeground(), "desktop foreground allows pinned-dock keyboard commands away from the pointer");
}
foreach (string application in new[] { "Chrome_WidgetWin_1", "Notepad", "Shell_TrayWnd", "WinUIDesktopWin32WindowClass" })
{
    NativeMethods.Reset(); NativeMethods.ForegroundClass = application;
    Check(!DesktopInputContext.IsDesktopForeground(), "application, taskbar or settings foreground cannot inherit pointer-free desktop commands");
}
foreach (int invalidation in Enumerable.Range(0, 11))
{
    NativeMethods.Reset();
    switch (invalidation)
    {
        case 0: NativeMethods.HasForeground = false; break;
        case 1: NativeMethods.Minimized = true; break;
        case 2: NativeMethods.WindowEnabled = false; break;
        case 3: NativeMethods.GuiQuerySuccess = false; break;
        case 4: NativeMethods.GuiActiveOverride = 456; break;
        case 5: NativeMethods.GuiCapture = 123; break;
        case 6: NativeMethods.GuiCaret = 123; break; // Explorer's desktop rename editor.
        case 7: NativeMethods.GuiMenuOwner = 123; break;
        case 8: NativeMethods.GuiMoveSize = 123; break;
        case 9: NativeMethods.GuiFlags = 0x4; break;
        case 10: NativeMethods.ForegroundAfterGuiQuery = 456; break;
    }
    Check(!DesktopInputContext.IsDesktopForeground(), "missing/disabled desktop, rename, menu, capture, move/resize or foreground drift blocks pointer-free commands");
}
foreach (int button in new[] { 1, 2, 4, 5, 6 })
{
    NativeMethods.Reset(); NativeMethods.Keys.Add(button);
    Check(!DesktopInputContext.IsDesktopForeground(), "any held mouse button blocks pointer-free commands during desktop gestures");
    NativeMethods.Keys.Remove(button);
    Check(DesktopInputContext.IsDesktopForeground(), "desktop commands become eligible again after the mouse gesture ends");
}
NativeMethods.Reset(); NativeMethods.Keys.Add(0x85);
Check(DesktopInputContext.IsDesktopForeground(), "inherited F22 state does not invalidate an otherwise idle desktop");
for (int i = 0; i < 1000; i++) _ = DesktopInputContext.IsDesktopForeground();
long desktopAllocationStart = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 10000; i++) _ = DesktopInputContext.IsDesktopForeground();
Check(GC.GetAllocatedBytesForCurrentThread() == desktopAllocationStart, "desktop foreground eligibility adds no steady managed allocations");
// User-defined keys must update the already-running controller, including when quick mode is disabled.
foreach (bool quick in new[] { false, true })
using (var f = New(quick))
{
    var bindings = ShortcutBindingCatalog.CreateDefaults();
    bindings[DockShortcutAction.ToggleExpanded] = new() { Key = "I" };
    f.Mode.Configure(quick, "Left", bindings);
    if (quick) f.Tap(); else f.Hover(DockSide.Left);
    Check(!f.Down(0x46) && !f.Up(0x46), "old F binding stops immediately after live rebind");
    Check(f.Down(0x49) && f.Up(0x49), "custom I binding intercepts its real key pair"); f.Flush();
    Check(quick ? f.Expansions == 1 : f.Commands.SequenceEqual(new[] { new DockKeyboardRequest(DockSide.Left, DockKeyboardAction.ToggleExpanded) }), "custom I executes without restart");
    bindings[DockShortcutAction.ToggleExpanded].Enabled = false;
    f.Mode.Configure(quick, "Left", bindings);
    if (quick) f.Tap();
    Check(!f.Down(0x49) && !f.Up(0x49), "disabling a single shortcut immediately passes I through");
}
foreach ((string key, int vk) in new[] { ("Number1", 0x31), ("Left", 0x25), ("NumPad5", 0x65), ("F24", 0x87), ("OemComma", 0xBC) })
using (var f = New())
{
    var bindings = ShortcutBindingCatalog.CreateDefaults();
    bindings[DockShortcutAction.TogglePinned] = new() { Key = key };
    f.Mode.Configure(true, "Left", bindings); f.Tap();
    Check(f.Down(vk) && f.Up(vk), "named number/navigation/numpad/function/punctuation key is supported"); f.Flush();
    Check(f.Pins == 1, "named key executes configured action");
}
foreach (bool quick in new[] { false, true })
foreach (int modifier in new[] { NativeMethods.VkLeftControl, NativeMethods.VkRightShift, NativeMethods.VkLeftMenu, NativeMethods.VkLeftWindows })
using (var f = New(quick))
{
    string modifierName = modifier switch { NativeMethods.VkLeftControl => "Ctrl", NativeMethods.VkRightShift => "Shift", NativeMethods.VkLeftMenu => "Alt", _ => "Win" };
    var bindings = ShortcutBindingCatalog.CreateDefaults();
    bindings[DockShortcutAction.ToggleExpanded] = new() { Key = "I", Modifiers = modifierName };
    f.Mode.Configure(quick, "Left", bindings);
    if (quick) f.Tap(); else f.Hover(DockSide.Left);
    Check(!f.Down(0x49) && !f.Up(0x49), "plain I cannot trigger a configured modifier chord");
    Check(!f.Down(modifier), "combination modifier retains its native event"); f.Tick();
    Check(f.Down(0x49), "configured modifier plus I is captured after reconciliation tick"); f.Flush();
    Check(quick ? f.Expansions == 1 : f.Commands.Count == 1, "configured modifier chord works in quick and ordinary modes");
    Check(f.Up(0x49) && !f.Up(modifier), "combination preserves matching release ownership"); f.Flush();
    Check(quick ? f.Mode.IsActive : !f.Mode.IsActive, "combination modifier release cannot toggle quick mode accidentally");
}
using (var f = New())
{
    var bindings = ShortcutBindingCatalog.CreateDefaults();
    bindings[DockShortcutAction.PreviousRow] = new() { Key = "I", Modifiers = "Ctrl+Shift" };
    bindings[DockShortcutAction.NextRow] = new() { Key = "K", Modifiers = "Ctrl+Shift" };
    f.Mode.Configure(true, "Left", bindings); f.Tap();
    f.Down(NativeMethods.VkLeftControl); f.Down(NativeMethods.VkLeftShift);
    Check(f.Down(0x49), "multi-modifier movement starts"); f.Flush();
    for (int i = 0; i < 5; i++) Check(f.Down(0x49), "custom movement key repeats are captured");
    f.Flush(); Check(f.VerticalMoves.Count == 6, "holding custom combo follows repeated navigation");
    f.Down(0x4B); f.Down(0x49); f.Flush(); Check(f.VerticalMoves.Count == 6, "custom opposite keys cancel queued repeats");
    f.Up(0x49); f.Up(0x4B); f.Release();
    Check(f.Mode.IsActive, "custom movement combo leaves quick mode active");
}
foreach (string key in new[] { "Ctrl", "Shift", "Alt", "Win", "I" })
using (var f = New())
{
    var activation = new ShortcutBinding { Key = key };
    f.Mode.Configure(true, "Left", activation: activation);
    int vk = key switch { "Ctrl" => NativeMethods.VkLeftControl, "Shift" => NativeMethods.VkRightShift,
        "Alt" => NativeMethods.VkLeftMenu, "Win" => NativeMethods.VkRightWindows, _ => 0x49 };
    f.Tap(vk); Check(f.Mode.IsActive, "standalone custom activation enters on release");
    f.Tap(vk); Check(!f.Mode.IsActive, "standalone custom activation exits on release");
    if (key != "Ctrl") { f.Tap(); Check(!f.Mode.IsActive, "old Ctrl activation stops immediately"); }
    activation.Enabled = false; f.Mode.Configure(true, "Left", activation: activation);
    f.Tap(vk); Check(!f.Mode.IsActive, "disabled activation cannot enter");
}
using (var f = New())
{
    var activation = new ShortcutBinding { Key = "I", Modifiers = "Ctrl+Shift" };
    f.Mode.Configure(true, "Left", activation: activation);
    f.Tap(0x49); Check(!f.Mode.IsActive, "custom activation requires all modifiers");
    f.Down(NativeMethods.VkLeftControl); f.Tick(); f.Down(NativeMethods.VkLeftShift); f.Tick();
    f.Down(0x49); f.Down(0x49); f.Up(0x49); f.Flush();
    Check(f.Mode.IsActive, "custom activation combo toggles once on main key release");
    f.Up(NativeMethods.VkLeftShift); f.Up(NativeMethods.VkLeftControl); f.Tick();
    Check(f.Mode.IsActive, "releasing activation modifiers keeps mode active");
    f.Down(NativeMethods.VkLeftControl); f.Down(NativeMethods.VkLeftShift); f.Down(0x49); f.Up(0x49); f.Release();
    Check(!f.Mode.IsActive, "second custom activation combination exits");
    NativeMethods.ForegroundClass = "Notepad";
    f.Down(NativeMethods.VkLeftControl); f.Down(NativeMethods.VkLeftShift); f.Down(0x49); f.Up(0x49); f.Release();
    Check(!f.Mode.IsActive, "custom activation never triggers over another application");
}
foreach (bool quick in new[] { false, true })
foreach ((string key, int vk) in new[] { ("Ctrl", NativeMethods.VkRightControl), ("Shift", NativeMethods.VkLeftShift),
    ("Alt", NativeMethods.VkRightMenu), ("Win", NativeMethods.VkLeftWindows) })
using (var f = New(quick))
{
    var bindings = ShortcutBindingCatalog.CreateDefaults();
    bindings[DockShortcutAction.ActivateQuickMode] = new() { Key = "I" };
    bindings[DockShortcutAction.ToggleExpanded] = new() { Key = key };
    f.Mode.Configure(quick, "Left", bindings);
    if (quick) f.Tap(0x49); else f.Hover(DockSide.Left);
    Check(f.Down(vk), "standalone modifier action captures either sided variant"); f.Flush();
    Check(quick ? f.Expansions == 1 : f.Commands.Count == 1, "standalone modifier action executes");
    f.Tick(); Check(f.Up(vk), "standalone modifier action retains release after reconciliation tick");
    f.Tick(); Check(quick ? f.Mode.IsActive : !f.Mode.IsActive, "standalone action modifier does not toggle activation");
}
foreach (bool ctrlFirst in new[] { false, true })
using (var f = New())
{
    f.Mode.Configure(true, "Left", activation: new() { Key = "I", Modifiers = "Ctrl+Shift" });
    f.Down(NativeMethods.VkLeftControl); f.Down(NativeMethods.VkLeftShift); f.Down(0x49);
    if (ctrlFirst) { f.Up(NativeMethods.VkLeftControl); f.Tick(); f.Up(NativeMethods.VkLeftShift); }
    else { f.Up(NativeMethods.VkLeftShift); f.Tick(); f.Up(NativeMethods.VkLeftControl); }
    f.Tick(); f.Up(0x49); f.Flush();
    Check(f.Mode.IsActive, "activation chord accepts releasing modifiers before main key in either order");
}
using (var f = New())
{
    var bindings = ShortcutBindingCatalog.CreateDefaults();
    bindings[DockShortcutAction.ToggleExpanded] = new() { Key = "I", Modifiers = "Ctrl" };
    f.Mode.Configure(true, "Left", bindings); f.Tap();
    f.Down(NativeMethods.VkLeftControl); Check(!f.Down(0x43), "unconfigured Ctrl+C still passes with a different configured Ctrl chord");
    f.Release(); Check(!f.Mode.IsActive && f.Expansions == 0, "unconfigured modifier chord exits safely even with custom modifier bindings");
}
using (var f = New())
{
    var bindings = ShortcutBindingCatalog.CreateDefaults();
    bindings[DockShortcutAction.ActivateQuickMode] = new() { Key = "I", Modifiers = "Ctrl" };
    bindings[DockShortcutAction.ToggleExpanded] = new() { Key = "I" };
    f.Mode.Configure(true, "Left", bindings);
    f.Down(NativeMethods.VkLeftControl); f.Down(0x49); f.Up(0x49); f.Up(NativeMethods.VkLeftControl); f.Flush();
    Check(f.Mode.IsActive, "activation combination can share main key with unmodified action");
    f.Tap(0x49); Check(f.Expansions == 1 && f.Mode.IsActive, "unmodified action key is not stolen by different activation chord");
}
using (var f = New())
{
    var bindings = ShortcutBindingCatalog.CreateDefaults();
    bindings[DockShortcutAction.ActivateQuickMode] = new() { Key = "I" };
    f.Mode.Configure(true, "Left", bindings);
    f.Tap(0x49); Check(f.Mode.IsActive, "activation from ordinary binding list applies without legacy argument");
    f.Tap(0x49); f.Tap(); Check(!f.Mode.IsActive, "legacy Ctrl no longer enters after list activation change");
}
NativeMethods.Reset();
Console.WriteLine($"PASS {checks} quick-mode, custom shortcuts, desktop and hover keyboard, wheel, lifecycle, companion-window and idle-allocation checks.");

sealed class Fixture : IDisposable
{
    private readonly Action<bool, string> _check;
    internal DispatcherQueue Queue { get; } = new();
    internal QuickModeController Mode { get; }
    internal bool Busy { get; set; }
    internal nint CompanionWindow { get; set; }
    internal DockSide? PointerSide { get; set; }
    internal bool DesktopOnlyTarget { get; set; }
    internal bool ExitInsideLaunch { get; set; }
    internal List<bool> Active { get; } = new();
    internal List<int> Moves { get; } = new();
    internal List<int> VerticalMoves { get; } = new();
    internal List<int> HorizontalMoves { get; } = new();
    internal List<int> WheelMoves { get; } = new();
    internal int DockSwitches { get; private set; }
    internal List<int> ListMoves { get; } = new();
    internal List<DockKeyboardRequest> Commands { get; } = new();
    internal int Expansions { get; private set; }
    internal int Pins { get; private set; }
    internal List<string> PinSides { get; } = new();
    internal List<string> Order { get; } = new();
    internal int Launches { get; private set; }
    internal Fixture(Action<bool, string> check, bool enable, bool companionCallback = true)
    {
        NativeMethods.Reset(); _check = check;
        Mode = new QuickModeController(Queue, () => Busy, companionCallback ? () => CompanionWindow : null,
            () => !DesktopOnlyTarget || DesktopInputContext.IsDesktopForeground() ? PointerSide : null);
        Mode.ActiveChanged += (_, value) => { _check(!NativeMethods.InHook, "active UI event deferred from native hook"); Active.Add(value); Order.Add("active:" + value); };
        Mode.SelectionRequested += (_, direction) => { _check(!NativeMethods.InHook, "selection UI event deferred from native hook"); WheelMoves.Add(direction); Moves.Add(direction); Order.Add("move:" + direction); };
        Mode.VerticalSelectionRequested += (_, direction) => { _check(!NativeMethods.InHook, "vertical selection UI event deferred from native hook"); VerticalMoves.Add(direction); Moves.Add(direction); Order.Add("move:" + direction); };
        Mode.HorizontalSelectionRequested += (_, direction) => { _check(!NativeMethods.InHook && Mode.IsActive, "horizontal selection UI event deferred from native hook"); HorizontalMoves.Add(direction); };
        Mode.DockSwitchRequested += (_, _) => { _check(!NativeMethods.InHook && Mode.IsActive, "dock switch UI event deferred from native hook"); DockSwitches++; };
        Mode.LaunchRequested += (_, _) => { _check(!NativeMethods.InHook && Mode.IsActive, "launcher called outside hook before selection cleared"); Launches++; Order.Add("launch"); if (ExitInsideLaunch) Mode.Exit(); };
        Mode.ListSwitchRequested += (_, direction) => { _check(!NativeMethods.InHook && Mode.IsActive, "quick list UI event deferred from native hook"); ListMoves.Add(direction); };
        Mode.ExpansionRequested += (_, _) => { _check(!NativeMethods.InHook && Mode.IsActive, "quick expansion UI event deferred from native hook"); Expansions++; };
        Mode.PinRequested += (_, _) => { _check(!NativeMethods.InHook && Mode.IsActive, "quick pin UI event deferred from native hook"); Pins++; PinSides.Add(Mode.Side); Order.Add("pin"); };
        Mode.DockCommandRequested += (_, command) => { _check(!NativeMethods.InHook && !Mode.IsActive, "normal keyboard UI event deferred and never enters quick mode"); Commands.Add(command); };
        if (enable) Mode.Configure(true, "Left");
    }
    internal bool Down(int key) => NativeMethods.SendKey(key, true);
    internal bool Up(int key) => NativeMethods.SendKey(key, false);
    internal void Flush() => Queue.Drain();
    internal void Hover(DockSide? side) { PointerSide = side; Mode.RefreshPointerTarget(); }
    internal void Tick() { Queue.Timer.Fire(); Queue.Drain(); }
    internal void Tap(int key = NativeMethods.VkLeftControl) { Down(key); Up(key); Flush(); }
    internal void Release() { NativeMethods.SetKeys(); Flush(); Tick(); }
    public void Dispose() { Mode.Dispose(); Queue.Drain(); }
}

