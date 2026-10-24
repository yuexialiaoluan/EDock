// EDock's own XAML Diagnostics client. No code is injected into taskbar input
// handling: only the taskbar background rectangles' Fill and Opacity change.
#include "TaskbarDiagnosticsAbi.h"
#include "TaskbarMaterials.h"
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
constexpr wchar_t MappingName[] = L"Local\\EDock.TaskbarAppearance.v8";
constexpr wchar_t WindowClass[] = L"EDock.TaskbarAppearance.v8";
constexpr unsigned Magic = 0x45445442;
constexpr unsigned ApplyMessage = WM_APP + 81;
struct SharedState {
    unsigned Magic, Version;
    volatile LONG OwnerPid, Enabled, OpacityMillionths, Generation;
    volatile LONG Matched, Applied, LastResult, Acknowledged;
    volatile LONG Material;
    LONG Reserved[5];
};
static_assert(sizeof(SharedState) == 64);
static_assert(offsetof(SharedState, Material) == 40);
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
    unsigned Index{}, FillIndex{};
    double OriginalOpacity{1.0};
    UiElement* Element{};
    Shape* ShapeElement{};
    Brush* OriginalFill{};
    bool Changed{};
    bool OpacityChanged{}, FillChanged{}, IsBackgroundFill{};
    bool WasLocal{true};
    bool FillWasLocal{true}, FillHasIndex{};
    DWORD Thread{};
    void ReleaseCaptured() {
        if (Element) { Element->Release(); Element = nullptr; }
        if (ShapeElement) { ShapeElement->Release(); ShapeElement = nullptr; }
        if (OriginalFill) { OriginalFill->Release(); OriginalFill = nullptr; }
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
    DWORD _ownerPid{};
    HRESULT _watchError{S_OK};
    std::recursive_mutex _lock;
    std::set<InstanceHandle> _taskbarNodes;
    std::map<InstanceHandle, Rectangle> _rectangles;
    std::map<DWORD, HWND> _windows;
    std::map<DWORD, ThreadResult> _results;
    std::atomic<int> _subscription{0}; // not started / pending / active / failed

    static void CALLBACK OwnerExited(void* context, BOOLEAN) {
        // Do not mutate the shared configuration: a replacement EDock instance
        // may already own it. UI threads independently verify owner liveness.
        // The callback holds no Site pointer, so asynchronous wait cancellation
        // cannot leave a dangling object reference.
        EnumWindows([](HWND window, LPARAM) -> BOOL {
            DWORD pid = 0; GetWindowThreadProcessId(window, &pid);
            wchar_t name[128]{}; GetClassNameW(window, name, 128);
            if (pid == GetCurrentProcessId() && wcscmp(name, WindowClass) == 0)
                PostMessageW(window, ApplyMessage, 0, 0);
            return TRUE;
        }, 0);
    }

    bool Enabled(LONG requested, DWORD pid) {
        if (!_state || _state->Magic != Magic || _state->Version != 2 || !requested) { _watchError = S_OK; return false; }
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

    void InspectFill(InstanceHandle handle) {
        PropertyOrigin origin = Origin(handle, L"Fill");
        InterlockedExchange(&_state->Reserved[1], origin.Source);
        InterlockedExchange(&_state->Reserved[2], static_cast<LONG>(origin.Metadata));
        InterlockedExchange(&_state->Reserved[3], static_cast<LONG>(origin.Chain));
        InterlockedExchange(&_state->Reserved[4], origin.Found ? 1 : 0);
    }

    HRESULT Capture(InstanceHandle handle, Rectangle& rect) {
        InterlockedExchange(&_state->Reserved[0], 10);
        TaskbarMaterials::Com<IInspectable> inspectable;
        HRESULT hr = _diagnostics->GetIInspectableFromHandle(handle, inspectable.Put());
        if (FAILED(hr)) return hr;
        TaskbarMaterials::Com<UiElement> element;
        hr = inspectable->QueryInterface(IidUiElement, element.VoidPut());
        if (FAILED(hr)) return hr;
        hr = element->get_Opacity(&rect.OriginalOpacity);
        if (FAILED(hr)) return hr;
        rect.WasLocal = true;
        PropertyOrigin origin = Origin(handle, L"Opacity");
        if (origin.Found) {
            rect.Index = origin.Index;
            rect.WasLocal = origin.Local;
        }
        rect.Element = element.Detach();
        return S_OK;
    }

    HRESULT CaptureFill(InstanceHandle handle, Rectangle& rect) {
        if (rect.ShapeElement) return S_OK;
        TaskbarMaterials::Com<Shape> shape;
        HRESULT hr = rect.Element->QueryInterface(IidShape, shape.VoidPut());
        if (FAILED(hr)) return hr;
        TaskbarMaterials::Com<Brush> fill;
        hr = shape->get_Fill(fill.Put());
        if (FAILED(hr)) return hr;
        // Diagnostics may reject GetPropertyIndex("Fill") even though its
        // complete property chain exposes the actual Shape.Fill index.
        PropertyOrigin origin = Origin(handle, L"Fill");
        rect.FillHasIndex = origin.Found;
        rect.FillIndex = origin.Index;
        // Replacing a local binding would destroy its expression. Unsupported
        // or unknown sources must remain untouched rather than pretending a
        // saved brush is sufficient to restore that expression later.
        if (!origin.Found || origin.Source <= 0 || (origin.Local && (origin.Metadata & 0x10))) return E_NOTIMPL;
        rect.FillWasLocal = origin.Local;
        rect.ShapeElement = shape.Detach();
        rect.OriginalFill = fill.Detach();
        return S_OK;
    }

    HRESULT SetOpacity(InstanceHandle handle, Rectangle& rect, LONG millionths) {
        if (!rect.Element) {
            HRESULT hr = Capture(handle, rect);
            if (FAILED(hr)) return hr;
        }
        millionths = std::clamp(millionths, 0L, 1000000L);
        InterlockedExchange(&_state->Reserved[0], 60);
        HRESULT hr = rect.Element->put_Opacity(static_cast<double>(millionths) / 1000000.0);
        if (SUCCEEDED(hr)) rect.Changed = rect.OpacityChanged = true;
        return hr;
    }

    HRESULT RestoreFill(InstanceHandle handle, Rectangle& rect) {
        if (!rect.FillChanged) return S_OK;
        HRESULT hr = rect.FillWasLocal ? rect.ShapeElement->put_Fill(rect.OriginalFill)
            : _service->ClearProperty(handle, rect.FillIndex);
        if (SUCCEEDED(hr)) {
            rect.FillChanged = false;
            rect.Changed = rect.OpacityChanged;
        }
        return hr;
    }

    HRESULT SetAppearance(InstanceHandle handle, Rectangle& rect, LONG material, LONG opacity) {
        if (material == 0) {
            HRESULT hr = RestoreFill(handle, rect);
            return FAILED(hr) ? hr : SetOpacity(handle, rect, opacity);
        }
        // A second blurred brush on the thin outline would sample twice. Hide
        // that outline while the main background is rendered by our material.
        if (!rect.IsBackgroundFill) return SetOpacity(handle, rect, 0);
        if (!rect.Element) {
            HRESULT hr = Capture(handle, rect);
            if (FAILED(hr)) return hr;
        }
        HRESULT hr = CaptureFill(handle, rect);
        if (FAILED(hr)) return hr;
        TaskbarMaterials::Com<Brush> brush;
        hr = TaskbarMaterials::Create(material, opacity, brush.Put());
        if (FAILED(hr)) return hr;
        hr = rect.ShapeElement->put_Fill(brush.Get());
        if (FAILED(hr)) return hr;
        rect.Changed = rect.FillChanged = true;
        return SetOpacity(handle, rect, 1000000);
    }

    HRESULT Restore(InstanceHandle handle, Rectangle& rect) {
        HRESULT result = RestoreFill(handle, rect);
        if (rect.OpacityChanged && rect.Element) {
            HRESULT hr = rect.WasLocal ? rect.Element->put_Opacity(rect.OriginalOpacity)
                : _service->ClearProperty(handle, rect.Index);
            if (FAILED(hr)) result = hr; else rect.OpacityChanged = false;
        }
        rect.Changed = rect.FillChanged || rect.OpacityChanged;
        if (!rect.Changed) rect.ReleaseCaptured();
        return result;
    }

    void Apply() {
        std::lock_guard guard(_lock);
        LONG generation = _state ? _state->Generation : 0;
        LONG requested = _state ? _state->Enabled : 0;
        DWORD owner = _state ? static_cast<DWORD>(_state->OwnerPid) : 0;
        LONG opacity = _state ? _state->OpacityMillionths : 0;
        LONG material = _state ? std::clamp(static_cast<LONG>(_state->Material), 0L, 2L) : 0;
        bool enabled = Enabled(requested, owner);
        LONG applied = 0;
        HRESULT result = _watchError;
        std::set<DWORD> activeThreads;
        for (auto& entry : _rectangles) {
            activeThreads.insert(entry.second.Thread);
            if (entry.second.Thread != GetCurrentThreadId()) continue;
            if (enabled) {
                HRESULT hr = SetAppearance(entry.first, entry.second, material, opacity);
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
        if (_state) {
            if (activeThreads.count(GetCurrentThreadId()))
                _results[GetCurrentThreadId()] = {applied, generation, result};
            else
                _results.erase(GetCurrentThreadId());
            LONG total = 0;
            bool acknowledged = true;
            // A removed monitor's UI thread cannot acknowledge future changes.
            // Only threads that still own a taskbar background participate.
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
        HWND window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WindowClass, L"EDock taskbar appearance", 0,
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
        HRESULT hr = self->_service->AdviseVisualTreeChange(static_cast<VisualCallback2*>(self));
        self->_subscription.store(SUCCEEDED(hr) ? 2 : 3);
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
            if (mutation == 1) {
                _taskbarNodes.erase(element.Handle);
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
            bool taskbar = element.Type && wcscmp(element.Type, L"Taskbar.TaskbarFrame") == 0;
            if (taskbar || _taskbarNodes.count(relation.Parent)) _taskbarNodes.insert(element.Handle);
            if (_taskbarNodes.count(element.Handle) && element.Name && element.Type
                && wcscmp(element.Type, L"Windows.UI.Xaml.Shapes.Rectangle") == 0
                && (wcscmp(element.Name, L"BackgroundFill") == 0 || wcscmp(element.Name, L"BackgroundStroke") == 0)) {
                auto inserted = _rectangles.try_emplace(element.Handle);
                inserted.first->second.Thread = GetCurrentThreadId();
                inserted.first->second.IsBackgroundFill = wcscmp(element.Name, L"BackgroundFill") == 0;
                if (inserted.first->second.IsBackgroundFill && _state) InspectFill(element.Handle);
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
    if (!IsEqualCLSID(clsid, ClassIdTaskbar)) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new (std::nothrow) Factory(); if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(iid, object); factory->Release(); return hr;
}
extern "C" __declspec(dllexport) HRESULT WINAPI DllCanUnloadNow() { return S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, void*) {
    if (reason == DLL_PROCESS_ATTACH) { Module = module; DisableThreadLibraryCalls(module); }
    return TRUE;
}
