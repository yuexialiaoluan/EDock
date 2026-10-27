// EDock Start menu background client. Only named, childless background visuals
// change opacity; text, application tiles and input handling remain untouched.
#include "TaskbarDiagnosticsAbi.h"
#include <atomic>
#include <map>
#include <set>
#include <string>
#include <vector>
#include <mutex>
#include <new>
#include <algorithm>
#include <cstddef>

namespace {
constexpr wchar_t MappingName[] = L"Local\\EDock.StartMenuAppearance.v1";
constexpr wchar_t WindowClass[] = L"EDock.StartMenuAppearance.v1";
constexpr GUID ClassIdStartMenu = {0x970b06c1,0x0b22,0x4f18,{0x9f,0x97,0x86,0xf8,0xc2,0xe1,0x08,0x48}};
constexpr unsigned Magic = 0x4544534D;
constexpr unsigned ApplyMessage = WM_APP + 82;
struct SharedState {
    unsigned Magic, Version;
    volatile LONG OwnerPid, Enabled, OpacityMillionths, Generation;
    volatile LONG Matched, Applied, LastResult, Acknowledged;
    volatile LONG TargetPid;
    LONG Reserved[5];
};
static_assert(sizeof(SharedState) == 64);
static_assert(offsetof(SharedState, TargetPid) == 40);
HMODULE Module;
std::mutex WindowsLock;
std::vector<HWND> Windows;
std::atomic<unsigned long> Objects{0};

void FreeChain(unsigned sources, PropertyChainSource* source, unsigned count, PropertyChainValue* values) {
    for (unsigned i = 0; source && i < sources; ++i) {
        SysFreeString(source[i].TargetType); SysFreeString(source[i].Name);
        SysFreeString(source[i].SrcInfo.FileName); SysFreeString(source[i].SrcInfo.Hash);
    }
    for (unsigned i = 0; values && i < count; ++i) {
        SysFreeString(values[i].Type); SysFreeString(values[i].DeclaringType);
        SysFreeString(values[i].ValueType); SysFreeString(values[i].ItemType);
        SysFreeString(values[i].Value); SysFreeString(values[i].PropertyName);
    }
    CoTaskMemFree(source); CoTaskMemFree(values);
}

struct Rectangle {
    unsigned Index{};
    double OriginalOpacity{1.0};
    UiElement* Element{};
    bool Changed{};
    bool OpacityChanged{};
    bool WasLocal{true};
    DWORD Thread{};
    void ReleaseCaptured() {
        if (Element) { Element->Release(); Element = nullptr; }
    }
    ~Rectangle() { ReleaseCaptured(); }
};
struct TreeStrings {
    VisualElement& Element;
    bool Owned;
    ~TreeStrings() {
        if (!Owned) return;
        SysFreeString(Element.SrcInfo.FileName); SysFreeString(Element.SrcInfo.Hash);
        SysFreeString(Element.Type); SysFreeString(Element.Name);
    }
};
struct ThreadResult { LONG Applied{}, Generation{}; HRESULT Result{S_OK}; };
struct PropertyOrigin { bool Local{true}, Found{}; int Source{-1}; unsigned Index{}, Chain{}; long long Metadata{}; };

class Site final : public IObjectWithSite, public VisualCallback2 {
    std::atomic<ULONG> _refs{1};
    IUnknown* _site{};
    VisualService2* _service{};
    XamlDiagnostics* _diagnostics{};
    IUnknown* _marshaler{};
    HANDLE _mapping{}, _owner{}, _ownerWait{};
    SharedState* _state{};
    HANDLE _traceMapping{};
    BYTE* _trace{};
    DWORD _ownerPid{};
    HRESULT _watchError{S_OK};
    std::recursive_mutex _lock;
    std::map<InstanceHandle, Rectangle> _rectangles;
    std::map<DWORD, HWND> _windows;
    std::map<DWORD, ThreadResult> _results;
    std::atomic<int> _subscription{0}; // not started / pending / active / failed

    static void CALLBACK OwnerExited(void* context, BOOLEAN) {
        // Do not mutate the shared configuration: a replacement EDock instance
        // may already own it. UI threads independently verify owner liveness.
        // The callback holds no Site pointer, so asynchronous wait cancellation
        // cannot leave a dangling object reference.
        // Windows excludes immersive Start-menu windows from EnumWindows and
        // EnumThreadWindows. Class-specific FindWindowEx still reaches each
        // helper, including invisible UI threads, so owner exit always restores.
        HWND window = nullptr;
        while ((window = FindWindowExW(nullptr, window, WindowClass, nullptr))) {
            DWORD pid = 0; GetWindowThreadProcessId(window, &pid);
            if (pid == GetCurrentProcessId()) PostMessageW(window, ApplyMessage, 0, 0);
        }
    }

    bool Enabled(LONG requested, DWORD pid) {
        if (!_state || _state->Magic != Magic || _state->Version != 1 || !requested
            || (_state->TargetPid && static_cast<DWORD>(_state->TargetPid) != GetCurrentProcessId())) { _watchError = S_OK; return false; }
        if (_ownerPid != pid || !_owner) {
            StopOwnerWait();
            _watchError = S_OK;
            _owner = OpenProcess(SYNCHRONIZE, FALSE, pid);
            _ownerPid = pid;
            if (!_owner) _watchError = HRESULT_FROM_WIN32(GetLastError());
            else if (!RegisterWaitForSingleObject(&_ownerWait, _owner, OwnerExited, nullptr, INFINITE, WT_EXECUTEONLYONCE)) {
                _watchError = HRESULT_FROM_WIN32(GetLastError());
                CloseHandle(_owner); _owner = nullptr;
            }
        }
        return _owner && WaitForSingleObject(_owner, 0) == WAIT_TIMEOUT;
    }

    void StopOwnerWait() {
        if (_ownerWait) { UnregisterWaitEx(_ownerWait, nullptr); _ownerWait = nullptr; }
        if (_owner) { CloseHandle(_owner); _owner = nullptr; }
        _ownerPid = 0;
    }

    void CloseTrace() {
        if (_trace) { UnmapViewOfFile(_trace); _trace = nullptr; }
        if (_traceMapping) { CloseHandle(_traceMapping); _traceMapping = nullptr; }
    }

    void OpenTrace() {
        if (_trace) return;
        _traceMapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, L"Local\\EDock.StartMenuAppearance.Trace.v1");
        if (_traceMapping) {
            _trace = static_cast<BYTE*>(MapViewOfFile(_traceMapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, 2 * 1024 * 1024));
            if (!_trace) { CloseHandle(_traceMapping); _traceMapping = nullptr; }
        }
    }

    void Trace(VisualElement& element, ParentChildRelation relation, int mutation) {
        // A separately-created test mapping opts into a bounded tree dump. Normal
        // EDock sessions never create this mapping or keep a diagnostic log.
        if (!_trace) return;
        if (!*reinterpret_cast<volatile LONG*>(_trace)) { CloseTrace(); return; }
        auto length = reinterpret_cast<volatile LONG*>(_trace + 4);
        LONG count = *length;
        constexpr LONG capacity = (2 * 1024 * 1024 - 8) / sizeof(wchar_t);
        wchar_t line[1536]{};
        int written = swprintf(line, 1536, L"%d %llu parent=%llu children=%u thread=%lu %ls #%ls\n",
            mutation, element.Handle, relation.Parent, element.NumChildren, GetCurrentThreadId(),
            element.Type ? element.Type : L"", element.Name ? element.Name : L"");
        if (written <= 0 || count < 0 || count + written >= capacity) return;
        memcpy(_trace + 8 + count * sizeof(wchar_t), line, written * sizeof(wchar_t));
        MemoryBarrier(); InterlockedExchange(length, count + written);
    }

    PropertyOrigin Origin(InstanceHandle handle, const wchar_t* name) {
        PropertyOrigin origin;
        unsigned sources = 0, count = 0;
        PropertyChainSource* source = nullptr;
        PropertyChainValue* values = nullptr;
        if (SUCCEEDED(_service->GetPropertyValuesChain(handle, &sources, &source, &count, &values))) {
            for (unsigned i = 0; i < count; ++i) {
                if (values[i].PropertyName && wcscmp(values[i].PropertyName, name) == 0 && !values[i].Overridden) {
                    origin.Found = true;
                    origin.Index = values[i].Index;
                    origin.Chain = values[i].PropertyChainIndex;
                    origin.Metadata = values[i].MetadataBits;
                    if (origin.Chain < sources) origin.Source = source[origin.Chain].Source;
                    origin.Local = origin.Source == -1 || origin.Source == 4;
                    break;
                }
            }
        }
        FreeChain(sources, source, count, values);
        return origin;
    }

    HRESULT Capture(InstanceHandle handle, Rectangle& rect) {
        IInspectable* inspectable = nullptr;
        HRESULT hr = _diagnostics->GetIInspectableFromHandle(handle, &inspectable);
        if (FAILED(hr)) return hr;
        UiElement* element = nullptr;
        hr = inspectable->QueryInterface(IidUiElement, reinterpret_cast<void**>(&element));
        inspectable->Release();
        if (FAILED(hr)) return hr;
        hr = element->get_Opacity(&rect.OriginalOpacity);
        if (FAILED(hr)) { element->Release(); return hr; }
        rect.WasLocal = true;
        PropertyOrigin origin = Origin(handle, L"Opacity");
        if (origin.Found) {
            rect.Index = origin.Index;
            rect.WasLocal = origin.Local;
        }
        rect.Element = element;
        return S_OK;
    }

    HRESULT SetOpacity(InstanceHandle handle, Rectangle& rect, LONG millionths) {
        if (!rect.Element) {
            HRESULT hr = Capture(handle, rect);
            if (FAILED(hr)) return hr;
        }
        millionths = std::clamp(millionths, 0L, 1000000L);
        HRESULT hr = rect.Element->put_Opacity(static_cast<double>(millionths) / 1000000.0);
        if (SUCCEEDED(hr)) rect.Changed = rect.OpacityChanged = true;
        return hr;
    }

    HRESULT Restore(InstanceHandle handle, Rectangle& rect) {
        HRESULT result = S_OK;
        if (rect.OpacityChanged && rect.Element) {
            HRESULT hr = rect.WasLocal ? rect.Element->put_Opacity(rect.OriginalOpacity)
                : _service->ClearProperty(handle, rect.Index);
            if (FAILED(hr)) result = hr; else rect.OpacityChanged = false;
        }
        rect.Changed = rect.OpacityChanged;
        if (!rect.Changed) rect.ReleaseCaptured();
        return result;
    }

    void Apply() {
        std::lock_guard guard(_lock);
        if (_trace && !*reinterpret_cast<volatile LONG*>(_trace)) CloseTrace();
        LONG generation = _state ? _state->Generation : 0;
        LONG requested = _state ? _state->Enabled : 0;
        DWORD owner = _state ? static_cast<DWORD>(_state->OwnerPid) : 0;
        LONG opacity = _state ? _state->OpacityMillionths : 0;
        bool enabled = Enabled(requested, owner);
        LONG applied = 0;
        HRESULT result = _watchError;
        std::set<DWORD> activeThreads;
        for (auto& entry : _rectangles) {
            activeThreads.insert(entry.second.Thread);
            if (entry.second.Thread != GetCurrentThreadId()) continue;
            if (enabled) {
                HRESULT hr = SetOpacity(entry.first, entry.second, opacity);
                if (FAILED(hr)) result = hr; else ++applied;
            } else {
                HRESULT hr = Restore(entry.first, entry.second);
                if (FAILED(hr)) result = hr;
            }
        }
        // Another monitor can use a separate XAML thread. Keep the owner-death
        // recovery wait until every thread has restored all its rectangles.
        if (!enabled && std::none_of(_rectangles.begin(), _rectangles.end(), [](const auto& entry) { return entry.second.Changed; }))
            StopOwnerWait();
        if (_state && (!_state->TargetPid || static_cast<DWORD>(_state->TargetPid) == GetCurrentProcessId())) {
            if (activeThreads.count(GetCurrentThreadId()))
                _results[GetCurrentThreadId()] = {applied, generation, result};
            else
                _results.erase(GetCurrentThreadId());
            LONG total = 0;
            bool acknowledged = true;
            // A removed monitor's UI thread cannot acknowledge future changes.
            // Only threads that still own a menu background participate.
            for (auto thread : activeThreads) {
                auto found = _results.find(thread);
                if (found == _results.end() || found->second.Generation != generation) acknowledged = false;
                else { total += found->second.Applied; if (FAILED(found->second.Result)) result = found->second.Result; }
            }
            InterlockedExchange(&_state->Matched, static_cast<LONG>(_rectangles.size()));
            InterlockedExchange(&_state->Applied, total);
            InterlockedExchange(&_state->LastResult, result);
            if (acknowledged) InterlockedExchange(&_state->Acknowledged, generation);
        }
    }

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wp, LPARAM lp) {
        auto self = reinterpret_cast<Site*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_NCCREATE) {
            self = static_cast<Site*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        }
        if (message == ApplyMessage && self) {
            try { self->Apply(); }
            catch (...) { if (self->_state) InterlockedExchange(&self->_state->LastResult, E_FAIL); }
            return 0;
        }
        return DefWindowProcW(window, message, wp, lp);
    }

    HRESULT EnsureWindow() {
        DWORD thread = GetCurrentThreadId();
        auto existing = _windows.find(thread);
        if (existing != _windows.end()) {
            if (IsWindow(existing->second) && GetWindowThreadProcessId(existing->second, nullptr) == thread) return S_OK;
            _windows.erase(existing);
        }
        WNDCLASSEXW wc{}; wc.cbSize = sizeof(wc); wc.hInstance = Module;
        wc.lpfnWndProc = WindowProc; wc.lpszClassName = WindowClass;
        if (!RegisterClassExW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
            return HRESULT_FROM_WIN32(GetLastError());
        HWND window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WindowClass, L"EDock Start menu appearance", 0,
            0, 0, 0, 0, nullptr, nullptr, Module, this);
        if (window) {
            _windows.emplace(thread, window);
            std::lock_guard guard(WindowsLock); Windows.push_back(window);
            return S_OK;
        }
        return HRESULT_FROM_WIN32(GetLastError());
    }

    static DWORD WINAPI Subscribe(void* argument) {
        auto self = static_cast<Site*>(argument);
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (self->_state) InterlockedExchange(&self->_state->Reserved[0], 20);
        HRESULT hr = self->_service->AdviseVisualTreeChange(static_cast<VisualCallback2*>(self));
        self->_subscription.store(SUCCEEDED(hr) ? 2 : 3);
        if (self->_state) InterlockedExchange(&self->_state->Reserved[0], SUCCEEDED(hr) ? 30 : -20);
        if (FAILED(hr) && self->_state) InterlockedExchange(&self->_state->LastResult, hr);
        CoUninitialize();
        self->Release();
        return 0;
    }

    HRESULT StartSubscription() {
        _subscription.store(1);
        AddRef();
        HANDLE thread = CreateThread(nullptr, 0, Subscribe, this, 0, nullptr);
        if (!thread) {
            HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
            _subscription.store(3); Release(); return hr;
        }
        CloseHandle(thread);
        return S_OK;
    }

public:
    Site() { ++Objects; CoCreateFreeThreadedMarshaler(static_cast<IObjectWithSite*>(this), &_marshaler); }
    ~Site() {
        StopOwnerWait();
        CloseTrace();
        if (_state) UnmapViewOfFile(_state);
        if (_mapping) CloseHandle(_mapping);
        if (_service) _service->Release();
        if (_diagnostics) _diagnostics->Release();
        if (_site) _site->Release();
        if (_marshaler) _marshaler->Release();
        --Objects;
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (IsEqualIID(iid, IID_IUnknown) || IsEqualIID(iid, IID_IObjectWithSite)) *result = static_cast<IObjectWithSite*>(this);
        else if (IsEqualIID(iid, IidVisualCallback) || IsEqualIID(iid, IidVisualCallback2)) *result = static_cast<VisualCallback2*>(this);
        else if (IsEqualIID(iid, IID_IMarshal) && _marshaler) return _marshaler->QueryInterface(iid, result);
        else return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++_refs; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG count = --_refs; if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override {
        if (!site) return S_OK; // Diagnostics retains the site while Explorer lives.
        std::lock_guard guard(_lock);
        OpenTrace();
        if (_site) return _subscription.load() == 3 ? StartSubscription() : S_OK;
        VisualService2* service = nullptr;
        XamlDiagnostics* diagnostics = nullptr;
        HRESULT hr = site->QueryInterface(IidVisualService2, reinterpret_cast<void**>(&service));
        if (FAILED(hr)) return hr;
        hr = site->QueryInterface(IidXamlDiagnostics, reinterpret_cast<void**>(&diagnostics));
        if (FAILED(hr)) { service->Release(); return hr; }
        HANDLE mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, MappingName);
        if (!mapping) {
            hr = HRESULT_FROM_WIN32(GetLastError());
            diagnostics->Release(); service->Release(); return hr;
        }
        auto state = static_cast<SharedState*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(SharedState)));
        if (!state) {
            hr = HRESULT_FROM_WIN32(GetLastError());
            CloseHandle(mapping); diagnostics->Release(); service->Release(); return hr;
        }
        _service = service; _diagnostics = diagnostics; _mapping = mapping; _state = state;
        InterlockedExchange(&_state->Reserved[0], 10);
        _site = site; site->AddRef();
        hr = StartSubscription();
        if (FAILED(hr)) {
            _site->Release(); _site = nullptr;
            UnmapViewOfFile(_state); _state = nullptr;
            CloseHandle(_mapping); _mapping = nullptr;
            _diagnostics->Release(); _diagnostics = nullptr;
            _service->Release(); _service = nullptr;
            return hr;
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid, void** result) override {
        std::lock_guard guard(_lock);
        return _site ? _site->QueryInterface(iid, result) : E_FAIL;
    }
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, int mutation) override {
        TreeStrings strings{element, mutation == 0};
        try {
            std::lock_guard guard(_lock);
            if (_state) InterlockedExchange(&_state->Reserved[0], 40);
            if (mutation == 1) {
                auto removed = _rectangles.find(element.Handle);
                if (removed != _rectangles.end()) {
                    DWORD thread = removed->second.Thread;
                    // A removed visual can remain alive and be attached again.
                    // Restore its local values while its owning UI thread is
                    // available, so a later Add never captures our own brush.
                    if (thread == GetCurrentThreadId() && removed->second.Changed)
                        Restore(element.Handle, removed->second);
                    _rectangles.erase(removed);
                    if (std::none_of(_rectangles.begin(), _rectangles.end(), [thread](const auto& entry) { return entry.second.Thread == thread; }))
                        _results.erase(thread);
                    Apply();
                }
                return S_OK;
            }
            Trace(element, relation, mutation);
            // A visual initially added empty can later gain content. Restore it
            // immediately rather than ever applying opacity to that content.
            auto parent = _rectangles.find(relation.Parent);
            if (parent != _rectangles.end()) {
                if (parent->second.Thread == GetCurrentThreadId() && parent->second.Changed)
                    Restore(parent->first, parent->second);
                _rectangles.erase(parent);
            }
            bool border = element.Type && wcscmp(element.Type, L"Windows.UI.Xaml.Controls.Border") == 0;
            bool rectangle = element.Type && wcscmp(element.Type, L"Windows.UI.Xaml.Shapes.Rectangle") == 0;
            bool background = element.Name && (wcscmp(element.Name, L"AcrylicBorder") == 0
                || wcscmp(element.Name, L"AcrylicOverlay") == 0 || wcscmp(element.Name, L"AppBorder") == 0
                || wcscmp(element.Name, L"AccentAppBorder") == 0 || wcscmp(element.Name, L"LayerBorder") == 0
                || wcscmp(element.Name, L"BackgroundFill") == 0 || wcscmp(element.Name, L"BackgroundStroke") == 0);
            // Never apply element opacity to a container: it would fade text and icons.
            if (background && (border || rectangle) && element.NumChildren == 0) {
                auto inserted = _rectangles.try_emplace(element.Handle);
                inserted.first->second.Thread = GetCurrentThreadId();
                HRESULT windowResult = EnsureWindow();
                if (FAILED(windowResult)) {
                    if (_state) InterlockedExchange(&_state->LastResult, windowResult);
                    return S_OK;
                }
                Apply();
            }
        } catch (...) { if (_state) InterlockedExchange(&_state->LastResult, E_FAIL); }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, int, LPCWSTR) override { return S_OK; }
};

std::mutex SingletonLock;
Site* SingletonSite{};

class Factory final : public IClassFactory {
    std::atomic<ULONG> _refs{1};
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        if (!IsEqualIID(iid, IID_IUnknown) && !IsEqualIID(iid, IID_IClassFactory)) return E_NOINTERFACE;
        *result = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++_refs; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG count = --_refs; if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** result) override {
        if (outer) return CLASS_E_NOAGGREGATION;
        std::lock_guard guard(SingletonLock);
        // The diagnostics DLL remains loaded for Explorer's lifetime. Retain
        // one client so repeated EDock attachments share the original values.
        if (!SingletonSite) SingletonSite = new (std::nothrow) Site();
        if (!SingletonSite) return E_OUTOFMEMORY;
        return SingletonSite->QueryInterface(iid, result);
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return S_OK; }
};
}

extern "C" __declspec(dllexport) HRESULT WINAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** object) {
    if (!IsEqualCLSID(clsid, ClassIdStartMenu)) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new (std::nothrow) Factory(); if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(iid, object); factory->Release(); return hr;
}
extern "C" __declspec(dllexport) HRESULT WINAPI DllCanUnloadNow() { return S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, void*) {
    if (reason == DLL_PROCESS_ATTACH) { Module = module; DisableThreadLibraryCalls(module); }
    return TRUE;
}
