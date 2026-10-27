using EDock.Interop;
using EDock.Models;
using EDock.Services;
using Microsoft.UI.Dispatching;

int checks = 0;
void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception(description);
}
void Run(string name, Action<DispatcherQueue, List<ShortcutBinding>, List<bool>, ShortcutKeyRecorder> scenario)
{
    NativeMethods.Reset();
    var queue = new DispatcherQueue();
    var candidates = new List<ShortcutBinding>();
    var finishes = new List<bool>();
    ShortcutKeyRecorder? recorder = null;
    using var recorderOwner = recorder = new ShortcutKeyRecorder(queue, () => 123, candidates.Add,
        accept => { finishes.Add(accept); recorder!.Stop(); });
    Check(recorder.Start(), "Recorder starts.");
    scenario(queue, candidates, finishes, recorder);
    recorder.Stop();
    foreach (int key in NativeMethods.Keys.ToArray()) NativeMethods.Send(key, false);
    queue.Drain();
    Check(NativeMethods.Hook == null, "Temporary hook is released: " + name);
    Console.WriteLine("PASS " + name);
}

Run("Ctrl records once and never duplicates itself", (queue, candidates, finishes, recorder) =>
{
    Check(NativeMethods.Send(0xA2, true), "Ctrl down swallowed.");
    queue.Drain();
    Check(candidates.Count == 1 && candidates[0].Key == "Ctrl" && candidates[0].Modifiers == "", "Standalone Ctrl canonical.");
    Check(NativeMethods.Send(0xA2, false), "Ctrl release swallowed.");
    NativeMethods.Send(0x0D, true); queue.Drain();
    Check(finishes.SequenceEqual(new[] { true }), "Enter saves once.");
    Check(!recorder.IsRecording, "Confirmation exits editing.");
    Check(NativeMethods.Send(0x0D, true), "Held Enter repeat cannot reenter editing.");
    queue.Drain();
    Check(finishes.Count == 1, "Held Enter does not confirm twice.");
    Check(NativeMethods.Send(0x0D, false), "Paired Enter release swallowed.");
    Check(NativeMethods.Hook == null, "Hook removed immediately on final release.");
});

Run("Full combination survives key and modifier release", (queue, candidates, finishes, recorder) =>
{
    NativeMethods.Send(0xA2, true); NativeMethods.Send(0x49, true);
    NativeMethods.Send(0x49, false); NativeMethods.Send(0xA2, false);
    queue.Drain();
    Check(candidates.Count == 2, "Two meaningful candidates while forming chord.");
    Check(candidates[^1].Key == "I" && candidates[^1].Modifiers == "Ctrl", "Ctrl+I stays final candidate.");
    NativeMethods.Send(0x0D, true); NativeMethods.Send(0x0D, false); queue.Drain();
    Check(finishes.SequenceEqual(new[] { true }), "Full chord accepted.");
});

Run("Esc cancels and no queued key can begin editing again", (queue, candidates, finishes, recorder) =>
{
    NativeMethods.Send(0x4A, true); NativeMethods.Send(0x4A, false);
    NativeMethods.Send(0x1B, true); NativeMethods.Send(0x1B, false);
    queue.Drain();
    Check(candidates.Single().Key == "J", "Real key captured, not text.");
    Check(finishes.SequenceEqual(new[] { false }), "Esc cancels once.");
    Check(!NativeMethods.Send(0x49, true), "After cancellation normal keys pass through.");
    NativeMethods.Send(0x49, false); queue.Drain();
    Check(candidates.Count == 1, "No edit until next explicit Start.");
});

Run("Tab and Windows combination are captured without navigation", (queue, candidates, finishes, recorder) =>
{
    Check(NativeMethods.Send(0x09, true), "Tab down captured."); NativeMethods.Send(0x09, false);
    NativeMethods.Send(0x5B, true); NativeMethods.Send(0x4A, true);
    NativeMethods.Send(0x4A, false); NativeMethods.Send(0x5B, false); queue.Drain();
    Check(candidates[0].Key == "Tab", "Tab candidate.");
    Check(candidates[^1].Key == "J" && candidates[^1].Modifiers == "Win", "Windows chord candidate.");
});

Run("Focus leaving settings cancels and does not swallow other application keys", (queue, candidates, finishes, recorder) =>
{
    NativeMethods.Foreground = 999;
    Check(!NativeMethods.Send(0x49, true), "Other window receives its keydown.");
    Check(!NativeMethods.Send(0x49, false), "Other window receives its keyup.");
    queue.Drain();
    Check(candidates.Count == 0 && finishes.SequenceEqual(new[] { false }), "Leaving editor cancels.");
});

Run("Stopping cancels pending candidate delivery and drains paired releases", (queue, candidates, finishes, recorder) =>
{
    NativeMethods.Send(0x49, true);
    recorder.Stop(); queue.Drain();
    Check(candidates.Count == 0 && finishes.Count == 0, "Stopped session cannot leak a candidate.");
    Check(NativeMethods.Hook != null, "Release guard survives until swallowed key is released.");
    Check(NativeMethods.Send(0x49, false), "Stopped capture consumes its paired release.");
    Check(NativeMethods.Hook == null, "Release guard then stops.");
    Check(recorder.Start(), "A new explicit start rearms.");
    NativeMethods.Send(0x4B, true); NativeMethods.Send(0x4B, false); queue.Drain();
    Check(candidates.Single().Key == "K", "New session captures normally.");
});

Run("Unknown and repeated keys remain bounded", (queue, candidates, finishes, recorder) =>
{
    NativeMethods.Send(0x49, true); NativeMethods.Send(0x49, true); NativeMethods.Send(0x49, true);
    NativeMethods.Send(0x49, false); NativeMethods.Send(0xFF, true); NativeMethods.Send(0xFF, false);
    queue.Drain();
    Check(candidates.Count == 1 && candidates[0].Key == "I", "Auto repeat does not flood UI callbacks.");
});

Run("Digits, numpad, punctuation and navigation keep native identities", (queue, candidates, finishes, recorder) =>
{
    foreach (int key in new[] { 0x31, 0x61, 0xBB, 0x26, 0xE2 })
    {
        NativeMethods.Send(key, true); NativeMethods.Send(key, false);
    }
    queue.Drain();
    Check(candidates.Select(binding => binding.Key).SequenceEqual(new[] { "1", "NumPad1", "=", "Up", "Oem102" }),
        "Supported keys preserve their distinct native identities.");
    Check(candidates.All(binding => binding.Enabled), "All captured supported keys stay enabled.");
});

NativeMethods.Reset();
{
    var queue = new DispatcherQueue();
    var candidates = new List<ShortcutBinding>();
    bool reject = true;
    int confirmations = 0;
    ShortcutKeyRecorder? recorder = null;
    using var owner = recorder = new ShortcutKeyRecorder(queue, () => 123, candidates.Add, accept =>
    {
        confirmations++;
        if (!reject || !accept) recorder!.Stop();
    });
    Check(recorder.Start(), "Conflict retry recorder starts.");
    NativeMethods.Send(0x57, true); NativeMethods.Send(0x57, false);
    NativeMethods.Send(0x0D, true); NativeMethods.Send(0x0D, false);
    queue.Drain();
    Check(confirmations == 1 && recorder.IsRecording, "Rejected confirmation resumes recording.");
    NativeMethods.Send(0x49, true); NativeMethods.Send(0x49, false);
    queue.Drain();
    Check(candidates[^1].Key == "I", "New candidate replaces rejected conflict.");
    reject = false;
    NativeMethods.Send(0x0D, true); NativeMethods.Send(0x0D, false);
    queue.Drain();
    Check(confirmations == 2 && !recorder.IsRecording && NativeMethods.Hook == null,
        "Successful retry ends editing and removes hook.");
    Console.WriteLine("PASS Rejected conflicting confirmation allows another key without a new click");
}

Console.WriteLine($"{checks} shortcut recorder checks passed.");
