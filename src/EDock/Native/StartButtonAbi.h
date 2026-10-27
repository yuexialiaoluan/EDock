// Public Windows Runtime ABI used by EDock's independent Start button client.
// GUIDs and vtable positions follow Microsoft's Windows SDK interface declarations.
#pragma once
#include "TaskbarDiagnosticsAbi.h"
#include <roapi.h>
#include <winstring.h>

namespace StartButtonAbi {
inline constexpr GUID InspectableId = {0xaf86e2e0,0xb12d,0x4c6a,{0x9c,0x5a,0xd7,0xaa,0x65,0x10,0x1e,0x90}};
inline constexpr GUID ImageId = {0x495b7402,0x9af3,0x4e50,{0xaa,0x90,0x03,0x38,0x8f,0x30,0x86,0xd2}};
inline constexpr GUID PanelId = {0xa50a4bbd,0x8361,0x469c,{0x90,0xda,0xe9,0xa4,0x0c,0x74,0x74,0xdf}};
inline constexpr GUID ReaderId = {0x9891c6bd,0x534f,0x4955,{0xb8,0x5a,0x8a,0x8d,0xc0,0xdc,0xa6,0x02}};
inline constexpr GUID AutomationId = {0xb618fd7b,0x32d0,0x4970,{0x9c,0x42,0x7c,0x03,0x9a,0xc7,0xbe,0x78}};
inline constexpr GUID DependencyId = {0x5c526665,0xf60e,0x4912,{0xaf,0x59,0x5f,0xe0,0x68,0x0f,0x08,0x9d}};
inline constexpr GUID ElementStaticsId = {0x58d3573b,0xf52c,0x45be,{0x98,0x8b,0xa5,0x86,0x95,0x64,0x87,0x3c}};
inline constexpr GUID RoutedHandlerId = {0xa856e674,0xb0b6,0x4bc3,{0xbb,0xa8,0x1b,0xa0,0x6e,0x40,0xd4,0xb5}};
inline constexpr GUID FailedHandlerId = {0x68e0e810,0xf6ea,0x42bc,{0x85,0x5b,0x5d,0x9b,0x67,0xe6,0xa2,0x62}};
inline constexpr GUID PointerHandlerId = {0xe4385929,0xc004,0x4bcf,{0x89,0x70,0x35,0x94,0x86,0xe3,0x9f,0x88}};
// WinRT parameterized IID of IReference<Windows.UI.Xaml.Input.PointerEventHandler>.
inline constexpr GUID PointerReferenceId = {0xe33644c8,0xe0e0,0x5645,{0x87,0x22,0x60,0x06,0x6c,0x85,0x87,0x3c}};
inline constexpr GUID PropertyValueId = {0x4bd682dd,0x7554,0x40e9,{0x9a,0x9b,0x82,0x65,0x4e,0xde,0x7e,0x62}};
inline constexpr GUID ToggleButtonId = {0x589877fb,0x0fc7,0x4036,{0x9d,0x8b,0x12,0x7d,0xfa,0x75,0xc1,0x6d}};
inline constexpr GUID BitmapSourceId = {0x23d86411,0x202f,0x41b2,{0x8c,0x5b,0xa8,0xa3,0xb3,0x33,0x80,0x0b}};
inline constexpr GUID RandomStreamId = {0x905a0fe1,0xbc53,0x11df,{0x8c,0x49,0x00,0x1e,0x4f,0xc6,0x86,0xda}};
inline constexpr GUID ExceptionArgsId = {0xdd9ff16a,0x4b62,0x4a6c,{0xa4,0x9d,0x06,0x71,0xef,0x61,0x36,0xbe}};
struct Token { INT64 Value{}; };
struct Thickness { DOUBLE Left, Top, Right, Bottom; };
struct Handler : IUnknown { virtual HRESULT STDMETHODCALLTYPE Invoke(IInspectable*, IInspectable*) = 0; };
struct PointerReference : IInspectable { virtual HRESULT STDMETHODCALLTYPE get_Value(Handler**) = 0; };
struct BooleanReference : IInspectable { virtual HRESULT STDMETHODCALLTYPE get_Value(BYTE*) = 0; };
struct ToggleButton : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_IsChecked(BooleanReference**) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_IsChecked(BooleanReference*) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_IsThreeState(BYTE*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_IsThreeState(BYTE) = 0;
    virtual HRESULT STDMETHODCALLTYPE add_Checked(Handler*,Token*) = 0;
    virtual HRESULT STDMETHODCALLTYPE remove_Checked(Token) = 0;
    virtual HRESULT STDMETHODCALLTYPE add_Unchecked(Handler*,Token*) = 0;
    virtual HRESULT STDMETHODCALLTYPE remove_Unchecked(Token) = 0;
};
struct BitmapSource : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_PixelWidth(INT*) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_PixelHeight(INT*) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetSource(IInspectable*) = 0;
};
struct ExceptionArgs : IInspectable { virtual HRESULT STDMETHODCALLTYPE get_ErrorMessage(HSTRING*) = 0; };
struct Point { FLOAT X,Y; };
struct Size { FLOAT Width,Height; };
struct Rect { FLOAT X,Y,Width,Height; };
// IPropertyValue accompanies IReference in standard WinRT boxes. A delegate has
// OtherType; conversions to primitive values are unsupported.
struct PropertyValue : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Type(INT*) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_IsNumericScalar(BYTE*) = 0;
#define EDOCK_PROPERTY_SCALAR(Name, Type) virtual HRESULT STDMETHODCALLTYPE Get##Name(Type*) = 0;
    EDOCK_PROPERTY_SCALAR(UInt8,BYTE)
    EDOCK_PROPERTY_SCALAR(Int16,INT16)
    EDOCK_PROPERTY_SCALAR(UInt16,UINT16)
    EDOCK_PROPERTY_SCALAR(Int32,INT32)
    EDOCK_PROPERTY_SCALAR(UInt32,UINT32)
    EDOCK_PROPERTY_SCALAR(Int64,INT64)
    EDOCK_PROPERTY_SCALAR(UInt64,UINT64)
    EDOCK_PROPERTY_SCALAR(Single,FLOAT)
    EDOCK_PROPERTY_SCALAR(Double,DOUBLE)
    EDOCK_PROPERTY_SCALAR(Char16,WCHAR)
    EDOCK_PROPERTY_SCALAR(Boolean,BYTE)
    EDOCK_PROPERTY_SCALAR(String,HSTRING)
    EDOCK_PROPERTY_SCALAR(Guid,GUID)
    EDOCK_PROPERTY_SCALAR(DateTime,INT64)
    EDOCK_PROPERTY_SCALAR(TimeSpan,INT64)
    EDOCK_PROPERTY_SCALAR(Point,Point)
    EDOCK_PROPERTY_SCALAR(Size,Size)
    EDOCK_PROPERTY_SCALAR(Rect,Rect)
#undef EDOCK_PROPERTY_SCALAR
#define EDOCK_PROPERTY_ARRAY(Name, Type) virtual HRESULT STDMETHODCALLTYPE Get##Name##Array(UINT32*,Type**) = 0;
    EDOCK_PROPERTY_ARRAY(UInt8,BYTE)
    EDOCK_PROPERTY_ARRAY(Int16,INT16)
    EDOCK_PROPERTY_ARRAY(UInt16,UINT16)
    EDOCK_PROPERTY_ARRAY(Int32,INT32)
    EDOCK_PROPERTY_ARRAY(UInt32,UINT32)
    EDOCK_PROPERTY_ARRAY(Int64,INT64)
    EDOCK_PROPERTY_ARRAY(UInt64,UINT64)
    EDOCK_PROPERTY_ARRAY(Single,FLOAT)
    EDOCK_PROPERTY_ARRAY(Double,DOUBLE)
    EDOCK_PROPERTY_ARRAY(Char16,WCHAR)
    EDOCK_PROPERTY_ARRAY(Boolean,BYTE)
    EDOCK_PROPERTY_ARRAY(String,HSTRING)
    EDOCK_PROPERTY_ARRAY(Inspectable,IInspectable*)
    EDOCK_PROPERTY_ARRAY(Guid,GUID)
    EDOCK_PROPERTY_ARRAY(DateTime,INT64)
    EDOCK_PROPERTY_ARRAY(TimeSpan,INT64)
    EDOCK_PROPERTY_ARRAY(Point,Point)
    EDOCK_PROPERTY_ARRAY(Size,Size)
    EDOCK_PROPERTY_ARRAY(Rect,Rect)
#undef EDOCK_PROPERTY_ARRAY
};
struct Image : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Source(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Source(IInspectable*) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Stretch(INT*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Stretch(INT) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_NineGrid(Thickness*) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_NineGrid(Thickness) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_PlayToSource(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE add_ImageFailed(Handler*, Token*) = 0;
    virtual HRESULT STDMETHODCALLTYPE remove_ImageFailed(Token) = 0;
    virtual HRESULT STDMETHODCALLTYPE add_ImageOpened(Handler*, Token*) = 0;
    virtual HRESULT STDMETHODCALLTYPE remove_ImageOpened(Token) = 0;
};
struct Children : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE GetAt(UINT, IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Size(UINT*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetView(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE IndexOf(IInspectable*, UINT*, BYTE*) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetAt(UINT, IInspectable*) = 0;
    virtual HRESULT STDMETHODCALLTYPE InsertAt(UINT, IInspectable*) = 0;
    virtual HRESULT STDMETHODCALLTYPE RemoveAt(UINT) = 0;
    virtual HRESULT STDMETHODCALLTYPE Append(IInspectable*) = 0;
    virtual HRESULT STDMETHODCALLTYPE RemoveAtEnd() = 0;
    virtual HRESULT STDMETHODCALLTYPE Clear() = 0;
};
struct Panel : IInspectable { virtual HRESULT STDMETHODCALLTYPE get_Children(Children**) = 0; };
struct Reader : IInspectable { virtual HRESULT STDMETHODCALLTYPE Load(HSTRING,IInspectable**) = 0; };

// A few late methods are called by their public SDK vtable position instead of
// repeating every unrelated method in IUIElement/IAutomationPropertiesStatics.
template<typename Function> Function Method(IUnknown* object, size_t index) {
    return reinterpret_cast<Function>((*reinterpret_cast<void***>(object))[index]);
}
inline HRESULT GetAutomationId(IInspectable* statics, IInspectable* dependency, HSTRING* value) {
    using Function = HRESULT(STDMETHODCALLTYPE*)(IInspectable*,IInspectable*,HSTRING*);
    return Method<Function>(statics,13)(statics,dependency,value);
}
inline HRESULT RoutedEvent(IInspectable* statics, UINT slot, IInspectable** value) {
    using Function = HRESULT(STDMETHODCALLTYPE*)(IInspectable*,IInspectable**);
    return Method<Function>(statics,slot)(statics,value);
}
inline HRESULT AddHandler(UiElement* element,IInspectable* event,IInspectable* handler) {
    using Function = HRESULT(STDMETHODCALLTYPE*)(UiElement*,IInspectable*,IInspectable*,BYTE);
    return Method<Function>(element,96)(element,event,handler,TRUE);
}
inline HRESULT RemoveHandler(UiElement* element,IInspectable* event,IInspectable* handler) {
    using Function = HRESULT(STDMETHODCALLTYPE*)(UiElement*,IInspectable*,IInspectable*);
    return Method<Function>(element,97)(element,event,handler);
}
}
