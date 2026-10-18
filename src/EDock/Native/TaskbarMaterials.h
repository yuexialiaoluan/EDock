#pragma once
#include "TaskbarDiagnosticsAbi.h"
#include <roapi.h>
#include <winstring.h>
#include <algorithm>

namespace TaskbarMaterials {
template<typename T> class Com final {
    T* _value{};
public:
    ~Com() { if (_value) _value->Release(); }
    T* operator->() const { return _value; }
    T* Get() const { return _value; }
    T** Put() { return &_value; }
    void** VoidPut() { return reinterpret_cast<void**>(&_value); }
    T* Detach() { T* value = _value; _value = nullptr; return value; }
    Com() = default;
    Com(const Com&) = delete;
    Com& operator=(const Com&) = delete;
};
class String final {
    HSTRING _value{};
public:
    explicit String(const wchar_t* value) { WindowsCreateString(value, static_cast<UINT32>(wcslen(value)), &_value); }
    ~String() { if (_value) WindowsDeleteString(_value); }
    operator HSTRING() const { return _value; }
};

inline HRESULT BoxDouble(double value, DoubleReference** result) {
    *result = nullptr;
    String name(L"Windows.Foundation.PropertyValue");
    Com<PropertyValueStatics> factory;
    HRESULT hr = RoGetActivationFactory(name, IidPropertyValueStatics, factory.VoidPut());
    if (FAILED(hr)) return hr;
    Com<IInspectable> boxed;
    hr = factory->CreateDouble(value, boxed.Put());
    return FAILED(hr) ? hr : boxed->QueryInterface(IidDoubleReference, reinterpret_cast<void**>(result));
}

inline ColorArgb SystemTint() {
    DWORD light = 1, size = sizeof(light);
    RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
        L"SystemUsesLightTheme", RRF_RT_REG_DWORD, nullptr, &light, &size);
    return light ? ColorArgb{255, 243, 243, 243} : ColorArgb{255, 32, 32, 32};
}

// Both materials use Windows' real taskbar XAML backdrop blur. Acrylic adds a strong
// luminosity layer; Blur keeps that layer clear for a frosted-glass appearance.
// Opacity here is tint concentration, not element opacity: zero preserves blur.
inline HRESULT Create(LONG material, LONG millionths, Brush** result) {
    *result = nullptr;
    String name(L"Windows.UI.Xaml.Media.AcrylicBrush");
    Com<AcrylicBrushFactory> factory;
    HRESULT hr = RoGetActivationFactory(name, IidAcrylicBrushFactory, factory.VoidPut());
    if (FAILED(hr)) return hr;
    Com<IInspectable> inner;
    Com<AcrylicBrush> acrylic;
    hr = factory->CreateInstance(nullptr, inner.Put(), acrylic.Put());
    if (FAILED(hr)) return hr;
    hr = acrylic->put_BackgroundSource(1); // Match the taskbar's original AcrylicBrush XAML backdrop source.
    if (FAILED(hr)) return hr;
    hr = acrylic->put_AlwaysUseFallback(FALSE);
    if (FAILED(hr)) return hr;
    hr = acrylic->put_TintTransitionDuration(0);
    if (FAILED(hr)) return hr;
    ColorArgb tint = SystemTint();
    hr = acrylic->put_TintColor(tint);
    if (FAILED(hr)) return hr;
    hr = acrylic->put_TintOpacity(static_cast<double>(std::clamp(millionths, 0L, 1000000L)) / 1000000.0);
    if (FAILED(hr)) return hr;
    Com<AcrylicBrush2> acrylic2;
    hr = acrylic->QueryInterface(IidAcrylicBrush2, acrylic2.VoidPut());
    if (FAILED(hr)) return hr;
    Com<DoubleReference> luminosity;
    hr = BoxDouble(material == 1 ? 0.85 : 0.0, luminosity.Put());
    if (FAILED(hr)) return hr;
    hr = acrylic2->put_TintLuminosityOpacity(luminosity.Get());
    if (FAILED(hr)) return hr;
    Com<CompositionBrushBase> composition;
    hr = acrylic->QueryInterface(IidCompositionBrushBase, composition.VoidPut());
    if (FAILED(hr)) return hr;
    hr = composition->put_FallbackColor(tint);
    if (FAILED(hr)) return hr;
    return acrylic->QueryInterface(IidBrush, reinterpret_cast<void**>(result));
}
}
