// Public Windows XAML Diagnostics ABI, declared here to keep the helper small.
// Interface identifiers and method order are from Microsoft's xamlOM.idl.
#pragma once
#include <windows.h>
#include <ocidl.h>
#include <inspectable.h>

using InstanceHandle = unsigned long long;
struct SourceInfo { BSTR FileName; unsigned LineNumber, ColumnNumber, CharPosition; BSTR Hash; };
struct ParentChildRelation { InstanceHandle Parent, Child; unsigned ChildIndex; };
struct VisualElement { InstanceHandle Handle; SourceInfo SrcInfo; BSTR Type, Name; unsigned NumChildren; };
struct PropertyChainSource { InstanceHandle Handle; BSTR TargetType, Name; int Source; SourceInfo SrcInfo; };
struct PropertyChainValue {
    unsigned Index; BSTR Type, DeclaringType, ValueType, ItemType, Value;
    BOOL Overridden; long long MetadataBits; BSTR PropertyName; unsigned PropertyChainIndex;
};

inline constexpr GUID IidVisualCallback = {0xaa7a8931,0x80e4,0x4fec,{0x8f,0x3b,0x55,0x3f,0x87,0xb4,0x96,0x6e}};
inline constexpr GUID IidVisualCallback2 = {0xbad9eb88,0xae77,0x4397,{0xb9,0x48,0x5f,0xa2,0xdb,0x0a,0x19,0xea}};
inline constexpr GUID IidVisualService2 = {0x130f5136,0xec43,0x4f61,{0x89,0xc7,0x98,0x01,0xa3,0x6d,0x2e,0x95}};
inline constexpr GUID IidXamlDiagnostics = {0x18c9e2b6,0x3f43,0x4116,{0x9f,0x2b,0xff,0x93,0x5d,0x77,0x70,0xd2}};
inline constexpr GUID ClassIdTaskbar = {0x5ede36d4,0x6324,0x476b,{0xb1,0x83,0x29,0x76,0x36,0x2e,0x52,0x97}};
inline constexpr GUID IidUiElement = {0x676d0be9,0xb65c,0x41c6,{0xba,0x40,0x58,0xcf,0x87,0xf2,0x01,0xc1}};
struct SizeF { float Width, Height; };
struct UiElement : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_DesiredSize(SizeF*) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_AllowDrop(BYTE*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_AllowDrop(BYTE) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Opacity(DOUBLE*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Opacity(DOUBLE) = 0;
};

// Public Windows Runtime ABI from Windows.UI.Xaml.Shapes/Media and
// Windows.Foundation in the Microsoft Windows SDK. Only the used prefix of
// each interface is declared; no private XAML or composition APIs are used.
inline constexpr GUID IidShape = {0x786f2b75,0x9aa0,0x454d,{0xae,0x06,0xa2,0x46,0x6e,0x37,0xc8,0x32}};
inline constexpr GUID IidBrush = {0x8806a321,0x1e06,0x422c,{0xa1,0xcc,0x01,0x69,0x65,0x59,0xe0,0x21}};
inline constexpr GUID IidAcrylicBrush = {0x79bbcf4e,0xcd66,0x4f1b,{0xa8,0xb6,0xcd,0x6d,0x29,0x77,0xc1,0x8d}};
inline constexpr GUID IidAcrylicBrush2 = {0xc9645383,0xb19e,0x5ac0,{0x86,0xff,0x3d,0x90,0x50,0x6d,0xbc,0xda}};
inline constexpr GUID IidAcrylicBrushFactory = {0x81a32568,0xf6cc,0x4013,{0x83,0x63,0x92,0x8a,0xe2,0x3b,0x7a,0x61}};
inline constexpr GUID IidCompositionBrushBase = {0x03e432d9,0xb35c,0x4a79,{0x81,0x1c,0xc5,0x65,0x20,0x04,0xda,0x0e}};
inline constexpr GUID IidPropertyValueStatics = {0x629bdbc8,0xd932,0x4ff4,{0x96,0xb9,0x8d,0x96,0xc5,0xc1,0xe8,0x58}};
inline constexpr GUID IidDoubleReference = {0x2f2d6c29,0x5473,0x5f3e,{0x92,0xe7,0x96,0x57,0x2b,0xb9,0x90,0xe2}};
struct ColorArgb { BYTE A, R, G, B; };
struct Brush : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Opacity(DOUBLE*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Opacity(DOUBLE) = 0;
};
struct Shape : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Fill(Brush**) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Fill(Brush*) = 0;
};
struct AcrylicBrush : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_BackgroundSource(INT*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_BackgroundSource(INT) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_TintColor(ColorArgb*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_TintColor(ColorArgb) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_TintOpacity(DOUBLE*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_TintOpacity(DOUBLE) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_TintTransitionDuration(INT64*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_TintTransitionDuration(INT64) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_AlwaysUseFallback(BYTE*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_AlwaysUseFallback(BYTE) = 0;
};
struct DoubleReference : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Value(DOUBLE*) = 0;
};
struct AcrylicBrush2 : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_TintLuminosityOpacity(DoubleReference**) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_TintLuminosityOpacity(DoubleReference*) = 0;
};
struct AcrylicBrushFactory : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE CreateInstance(IInspectable*, IInspectable**, AcrylicBrush**) = 0;
};
struct CompositionBrushBase : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_FallbackColor(ColorArgb*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_FallbackColor(ColorArgb) = 0;
};
struct PropertyValueStatics : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE CreateEmpty(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateUInt8(BYTE, IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateInt16(INT16, IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateUInt16(UINT16, IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateInt32(INT32, IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateUInt32(UINT32, IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateInt64(INT64, IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateUInt64(UINT64, IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateSingle(FLOAT, IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateDouble(DOUBLE, IInspectable**) = 0;
};

struct VisualCallback2 : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation, VisualElement, int) = 0;
    virtual HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, int, LPCWSTR) = 0;
};
struct VisualService2 : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE AdviseVisualTreeChange(VisualCallback2*) = 0;
    virtual HRESULT STDMETHODCALLTYPE UnadviseVisualTreeChange(VisualCallback2*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetEnums(unsigned*, void**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateInstance(BSTR, BSTR, InstanceHandle*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetPropertyValuesChain(InstanceHandle,unsigned*,PropertyChainSource**,unsigned*,PropertyChainValue**) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetProperty(InstanceHandle,InstanceHandle,unsigned) = 0;
    virtual HRESULT STDMETHODCALLTYPE ClearProperty(InstanceHandle,unsigned) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetCollectionCount(InstanceHandle,unsigned*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetCollectionElements(InstanceHandle,unsigned,unsigned*,void**) = 0;
    virtual HRESULT STDMETHODCALLTYPE AddChild(InstanceHandle,InstanceHandle,unsigned) = 0;
    virtual HRESULT STDMETHODCALLTYPE RemoveChild(InstanceHandle,unsigned) = 0;
    virtual HRESULT STDMETHODCALLTYPE ClearChildren(InstanceHandle) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetPropertyIndex(InstanceHandle,LPCWSTR,unsigned*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetProperty(InstanceHandle,unsigned,InstanceHandle*) = 0;
};
struct XamlDiagnostics : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE GetDispatcher(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetUiLayer(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetApplication(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetIInspectableFromHandle(InstanceHandle,IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetHandleFromIInspectable(IInspectable*,InstanceHandle*) = 0;
    virtual HRESULT STDMETHODCALLTYPE HitTest(RECT,unsigned*,InstanceHandle**) = 0;
    virtual HRESULT STDMETHODCALLTYPE RegisterInstance(IInspectable*,InstanceHandle*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetInitializationData(BSTR*) = 0;
};
