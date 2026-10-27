// EDock's independent Start button image client. Uses public XAML diagnostics
// and Windows Runtime interfaces; no shell symbols, input hooks or Windhawk.
#include "StartButtonAbi.h"
#include <atomic>
#include <map>
#include <set>
#include <array>
#include <string>
#include <vector>
#include <memory>
#include <mutex>
#include <algorithm>
#include <new>
#include <cstddef>
#include <cstring>
#include <shcore.h>
#include <wincodec.h>
#ifdef EDOCK_STARTBUTTON_PROBE
#include <sddl.h>
#endif

namespace {
namespace Abi = StartButtonAbi;
#ifdef EDOCK_STARTBUTTON_PROBE
#define EDOCK_STRINGIFY_INNER(value) #value
#define EDOCK_STRINGIFY(value) EDOCK_STRINGIFY_INNER(value)
#define EDOCK_WIDEN_INNER(value) L##value
#define EDOCK_WIDEN(value) EDOCK_WIDEN_INNER(value)
constexpr wchar_t MappingName[] = L"Local\\EDock.StartButtonAppearance.probe" EDOCK_WIDEN(EDOCK_STRINGIFY(EDOCK_STARTBUTTON_PROBE));
constexpr wchar_t WindowClass[] = L"EDock.StartButtonAppearance.probe" EDOCK_WIDEN(EDOCK_STRINGIFY(EDOCK_STARTBUTTON_PROBE));
constexpr GUID ClassId = {0x2b309e31,0xd20e,0x45f2,{0xa3,0x26,0x02,0xee,0x74,0xc6,0x3b,static_cast<BYTE>(0x91-EDOCK_STARTBUTTON_PROBE)}};
#else
constexpr wchar_t MappingName[] = L"Local\\EDock.StartButtonAppearance.v3";
constexpr wchar_t WindowClass[] = L"EDock.StartButtonAppearance.v3";
constexpr GUID ClassId = {0x2b309e31,0xd20e,0x45f2,{0xa3,0x26,0x02,0xee,0x74,0xc6,0x3b,0x93}};
#endif
constexpr UINT ApplyMessage = WM_APP + 83;
constexpr UINT MenuStateMessage = WM_APP + 84;
constexpr UINT_PTR RestoreTimer = 83;
constexpr UINT Magic = 0x45445342;
struct SharedState {
    UINT Magic, Version;
    volatile LONG OwnerPid, Enabled, IconSize, Generation;
    volatile LONG Matched, Applied, LastResult, Acknowledged, TargetPid, Publishing;
    LONG Reserved[4];
    WCHAR Normal[4096], Hover[4096], Pressed[4096], Activated[4096];
    WCHAR ErrorMessage[2048];
    WCHAR ImageMappings[4][256];
    UINT32 ImageLengths[4];
    BYTE Padding[26544];
};
static_assert(sizeof(SharedState) == 65536);
static_assert(offsetof(SharedState, Normal) == 64);
static_assert(offsetof(SharedState, Hover) == 8256);
static_assert(offsetof(SharedState, Pressed) == 16448);
static_assert(offsetof(SharedState, Activated) == 24640);
static_assert(offsetof(SharedState, ErrorMessage) == 32832);
static_assert(offsetof(SharedState, ImageMappings) == 36928);
static_assert(offsetof(SharedState, ImageLengths) == 38976);
struct Configuration {
    LONG Owner{}, Enabled{}, Size{}, Generation{}, Target{};
    std::array<std::wstring,4> Paths;
    std::array<std::wstring,4> ImageMappings;
    std::array<UINT32,4> ImageLengths{};
};
HMODULE Module;
template<class T> class Com {
    T* _value{};
public:
    Com() = default;
    Com(const Com&) = delete;
    Com& operator=(const Com&) = delete;
    Com(Com&& other) noexcept : _value(other.Detach()) {}
    Com& operator=(Com&& other) noexcept { Reset(); _value = other.Detach(); return *this; }
    ~Com() { Reset(); }
    void Reset() { if (_value) { _value->Release(); _value = nullptr; } }
    T* Get() const { return _value; }
    T** Put() { Reset(); return &_value; }
    void** VoidPut() { return reinterpret_cast<void**>(Put()); }
    T* Detach() { T* result = _value; _value = nullptr; return result; }
    void Attach(T* value) { Reset(); _value=value; }
    T* operator->() const { return _value; }
    explicit operator bool() const { return _value != nullptr; }
};
class String {
    HSTRING _value{};
public:
    explicit String(const std::wstring& text) { WindowsCreateString(text.c_str(),static_cast<UINT>(text.size()),&_value); }
    ~String() { WindowsDeleteString(_value); }
    HSTRING Get() const { return _value; }
};
HRESULT Factory(const wchar_t* name, REFIID id, void** output) {
    String type(name);
    return RoGetActivationFactory(type.Get(), id, output);
}
HRESULT ReadXaml(const std::wstring& text,IInspectable** output) {
    Com<Abi::Reader> reader;
    HRESULT hr = Factory(L"Windows.UI.Xaml.Markup.XamlReader",Abi::ReaderId,reader.VoidPut());
    if (FAILED(hr)) return hr;
    String xaml(text);
    return reader->Load(xaml.Get(),output);
}
constexpr wchar_t XamlNamespace[] = L" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
void FreeChain(UINT sources,PropertyChainSource* source,UINT count,PropertyChainValue* values) {
    for(UINT i=0;source && i<sources;++i) {
        SysFreeString(source[i].TargetType); SysFreeString(source[i].Name);
        SysFreeString(source[i].SrcInfo.FileName); SysFreeString(source[i].SrcInfo.Hash);
    }
    for(UINT i=0;values && i<count;++i) {
        SysFreeString(values[i].Type); SysFreeString(values[i].DeclaringType);
        SysFreeString(values[i].ValueType); SysFreeString(values[i].ItemType);
        SysFreeString(values[i].Value); SysFreeString(values[i].PropertyName);
    }
    CoTaskMemFree(source); CoTaskMemFree(values);
}
struct TreeStrings {
    VisualElement Element;
    bool Owned;
    ~TreeStrings() {
        if (!Owned) return;
        SysFreeString(Element.SrcInfo.FileName); SysFreeString(Element.SrcInfo.Hash);
        SysFreeString(Element.Type); SysFreeString(Element.Name);
    }
};
struct Node {
    InstanceHandle Parent{};
    DWORD Thread{};
    bool Start{};
};
class Site;
struct Instance;
class Event final : public Abi::Handler, public IInspectable {
    std::atomic<ULONG> _refs{1};
    std::weak_ptr<Instance> _instance;
    Site* _site;
    INT _kind,_index;
    LONG _generation;
public:
    Event(Site* site,const std::shared_ptr<Instance>& instance,LONG generation,INT kind,INT index=0)
        :_instance(instance),_site(site),_kind(kind),_index(index),_generation(generation) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** output) override {
        if(!output) return E_POINTER;
        *output=nullptr;
        if(IsEqualIID(id,IID_IUnknown) || IsEqualIID(id,Abi::RoutedHandlerId)
            || IsEqualIID(id,Abi::FailedHandlerId) || IsEqualIID(id,Abi::PointerHandlerId))
            *output=static_cast<Abi::Handler*>(this);
        else if(IsEqualIID(id,Abi::InspectableId)) *output=static_cast<IInspectable*>(this);
        else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++_refs; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG refs=--_refs;if(!refs) delete this;return refs; }
    HRESULT STDMETHODCALLTYPE GetIids(ULONG* count,IID** ids) override {
        if(!count || !ids) return E_POINTER; *count=0;*ids=nullptr;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetRuntimeClassName(HSTRING* name) override {
        return WindowsCreateString(L"EDock.StartButtonEvent",22,name);
    }
    HRESULT STDMETHODCALLTYPE GetTrustLevel(TrustLevel* level) override { if(!level) return E_POINTER;*level=BaseTrust;return S_OK; }
    HRESULT STDMETHODCALLTYPE Invoke(IInspectable*,IInspectable*) override;
};
class PointerBox final : public Abi::PointerReference, public Abi::PropertyValue {
    std::atomic<ULONG> _refs{1};
    Com<Abi::Handler> _handler;
public:
    explicit PointerBox(Abi::Handler* handler) { _handler.Attach(handler); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** output) override {
        if(!output)return E_POINTER;*output=nullptr;
        if(IsEqualIID(id,IID_IUnknown) || IsEqualIID(id,Abi::InspectableId) || IsEqualIID(id,Abi::PointerReferenceId))
            *output=static_cast<Abi::PointerReference*>(this);
        else if(IsEqualIID(id,Abi::PropertyValueId))*output=static_cast<Abi::PropertyValue*>(this);
        else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++_refs;}
    ULONG STDMETHODCALLTYPE Release() override{ULONG count=--_refs;if(!count)delete this;return count;}
    HRESULT STDMETHODCALLTYPE GetIids(ULONG* count,IID** ids) override {
        if(!count || !ids)return E_POINTER;*count=0;*ids=nullptr;
        auto values=static_cast<IID*>(CoTaskMemAlloc(2*sizeof(IID)));if(!values)return E_OUTOFMEMORY;
        values[0]=Abi::PointerReferenceId;values[1]=Abi::PropertyValueId;*count=2;*ids=values;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetRuntimeClassName(HSTRING* name) override {
        constexpr wchar_t type[]=L"Windows.Foundation.IReference`1<Windows.UI.Xaml.Input.PointerEventHandler>";
        return WindowsCreateString(type,static_cast<UINT>(std::size(type)-1),name);
    }
    HRESULT STDMETHODCALLTYPE GetTrustLevel(TrustLevel* level) override{if(!level)return E_POINTER;*level=BaseTrust;return S_OK;}
    HRESULT STDMETHODCALLTYPE get_Value(Abi::Handler** value) override {
        if(!value)return E_POINTER;*value=_handler.Get();if(*value)(*value)->AddRef();return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Type(INT* value) override{if(!value)return E_POINTER;*value=20;return S_OK;}
    HRESULT STDMETHODCALLTYPE get_IsNumericScalar(BYTE* value) override{if(!value)return E_POINTER;*value=FALSE;return S_OK;}
#define EDOCK_UNSUPPORTED_SCALAR(Name, Type) HRESULT STDMETHODCALLTYPE Get##Name(Type*) override{return E_NOTIMPL;}
    EDOCK_UNSUPPORTED_SCALAR(UInt8,BYTE)
    EDOCK_UNSUPPORTED_SCALAR(Int16,INT16)
    EDOCK_UNSUPPORTED_SCALAR(UInt16,UINT16)
    EDOCK_UNSUPPORTED_SCALAR(Int32,INT32)
    EDOCK_UNSUPPORTED_SCALAR(UInt32,UINT32)
    EDOCK_UNSUPPORTED_SCALAR(Int64,INT64)
    EDOCK_UNSUPPORTED_SCALAR(UInt64,UINT64)
    EDOCK_UNSUPPORTED_SCALAR(Single,FLOAT)
    EDOCK_UNSUPPORTED_SCALAR(Double,DOUBLE)
    EDOCK_UNSUPPORTED_SCALAR(Char16,WCHAR)
    EDOCK_UNSUPPORTED_SCALAR(Boolean,BYTE)
    EDOCK_UNSUPPORTED_SCALAR(String,HSTRING)
    EDOCK_UNSUPPORTED_SCALAR(Guid,GUID)
    EDOCK_UNSUPPORTED_SCALAR(DateTime,INT64)
    EDOCK_UNSUPPORTED_SCALAR(TimeSpan,INT64)
    EDOCK_UNSUPPORTED_SCALAR(Point,Abi::Point)
    EDOCK_UNSUPPORTED_SCALAR(Size,Abi::Size)
    EDOCK_UNSUPPORTED_SCALAR(Rect,Abi::Rect)
#undef EDOCK_UNSUPPORTED_SCALAR
#define EDOCK_UNSUPPORTED_ARRAY(Name, Type) HRESULT STDMETHODCALLTYPE Get##Name##Array(UINT32*,Type**) override{return E_NOTIMPL;}
    EDOCK_UNSUPPORTED_ARRAY(UInt8,BYTE)
    EDOCK_UNSUPPORTED_ARRAY(Int16,INT16)
    EDOCK_UNSUPPORTED_ARRAY(UInt16,UINT16)
    EDOCK_UNSUPPORTED_ARRAY(Int32,INT32)
    EDOCK_UNSUPPORTED_ARRAY(UInt32,UINT32)
    EDOCK_UNSUPPORTED_ARRAY(Int64,INT64)
    EDOCK_UNSUPPORTED_ARRAY(UInt64,UINT64)
    EDOCK_UNSUPPORTED_ARRAY(Single,FLOAT)
    EDOCK_UNSUPPORTED_ARRAY(Double,DOUBLE)
    EDOCK_UNSUPPORTED_ARRAY(Char16,WCHAR)
    EDOCK_UNSUPPORTED_ARRAY(Boolean,BYTE)
    EDOCK_UNSUPPORTED_ARRAY(String,HSTRING)
    EDOCK_UNSUPPORTED_ARRAY(Inspectable,IInspectable*)
    EDOCK_UNSUPPORTED_ARRAY(Guid,GUID)
    EDOCK_UNSUPPORTED_ARRAY(DateTime,INT64)
    EDOCK_UNSUPPORTED_ARRAY(TimeSpan,INT64)
    EDOCK_UNSUPPORTED_ARRAY(Point,Abi::Point)
    EDOCK_UNSUPPORTED_ARRAY(Size,Abi::Size)
    EDOCK_UNSUPPORTED_ARRAY(Rect,Abi::Rect)
#undef EDOCK_UNSUPPORTED_ARRAY
};
struct ImageLayer {
    Com<IInspectable> Element;
    Com<Abi::Image> Image;
    Com<UiElement> Ui;
    Com<IInspectable> Stream;
    Abi::Token Opened{},Failed{};
    bool HasOpened{},HasFailed{},Ready{},Error{};
};
struct ButtonEvent {
    Com<IInspectable> RoutedEvent;
    Com<IInspectable> Handler;
};
struct Instance {
    InstanceHandle Icon{},Button{},Parent{};
    DWORD Thread{};
    LONG Generation{-1};
    Com<UiElement> Stock,ButtonUi;
    Com<IInspectable> Host;
    Com<Abi::Children> ParentChildren;
    std::vector<std::unique_ptr<ImageLayer>> Images;
    std::array<UINT,4> ImageByState{};
    std::vector<ButtonEvent> Events;
    Com<Abi::ToggleButton> Toggle;
    Abi::Token Checked{},Unchecked{};
    bool HasChecked{},HasUnchecked{};
    DOUBLE OriginalOpacity{1};
    UINT OpacityIndex{};
    bool OpacityLocal{true},HasOpacityIndex{},Hidden{},Inserted{},Building{},Detached{true};
    bool Hover{},Pressed{},Active{},Retired{},MenuFallback{};
    HRESULT Error{S_OK};
};
struct Result { LONG Applied{},Generation{};HRESULT Error{S_OK}; };
struct RestoreRetry { LONG Generation{-1};UINT Attempts{};bool Pending{}; };
class Site final : public IObjectWithSite,public VisualCallback2 {
    std::atomic<ULONG> _refs{1};
    Com<IUnknown> _site,_marshaler;
    Com<VisualService2> _service;
    Com<XamlDiagnostics> _diagnostics;
    HANDLE _mapping{},_owner{},_ownerWait{};
    SharedState* _shared{};
    DWORD _ownerPid{};
    std::recursive_mutex _lock;
    std::map<InstanceHandle,Node> _nodes;
    std::map<InstanceHandle,std::shared_ptr<Instance>> _instances;
    std::map<DWORD,HWND> _windows;
    std::set<DWORD> _pendingApply;
    std::map<DWORD,Result> _results;
    std::map<DWORD,RestoreRetry> _restoreRetries;
    std::map<DWORD,HWINEVENTHOOK> _menuHooks;
    std::atomic<INT> _subscription{0};
    UINT _changing{};
    HRESULT _ownerError{S_OK};

#ifdef EDOCK_STARTBUTTON_PROBE
    void ProbeIncrement(UINT index) {
        if(_shared)InterlockedIncrement(reinterpret_cast<volatile LONG*>(_shared->Padding)+index);
    }
    void ProbeValue(UINT index,LONG value) {
        if(_shared)InterlockedExchange(reinterpret_cast<volatile LONG*>(_shared->Padding)+index,value);
    }
#endif

    static bool IsStartHostWindow(HWND window) {
        wchar_t type[128]{};
        if(!GetClassNameW(window,type,128) || (wcscmp(type,L"Windows.UI.Core.CoreWindow")!=0
            && wcscmp(type,L"XamlExplorerHostIslandWindow")!=0))return false;
        DWORD pid=0;GetWindowThreadProcessId(window,&pid);
        HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);if(!process)return false;
        wchar_t path[1024]{};DWORD size=1024;
        bool found=QueryFullProcessImageNameW(process,0,path,&size)!=FALSE;CloseHandle(process);
        const wchar_t* file=wcsrchr(path,L'\\');
        return found && file && _wcsicmp(file+1,L"StartMenuExperienceHost.exe")==0;
    }
    static bool MenuVisible() {
        constexpr const wchar_t* classes[]={L"Windows.UI.Core.CoreWindow",L"XamlExplorerHostIslandWindow"};
        for(auto type:classes) {
            HWND window=nullptr;
            while((window=FindWindowExW(nullptr,window,type,nullptr)))
                if(IsWindowVisible(window) && IsStartHostWindow(window))return true;
        }
        return false;
    }
    static void CALLBACK MenuVisibilityEvent(HWINEVENTHOOK,DWORD,HWND changed,LONG object,LONG child,DWORD,DWORD) {
        if(object!=OBJID_WINDOW || child!=CHILDID_SELF || !changed || !IsStartHostWindow(changed))return;
        bool visible=MenuVisible();HWND window=nullptr;
        while((window=FindWindowExW(nullptr,window,WindowClass,nullptr))) {
            DWORD pid=0;DWORD thread=GetWindowThreadProcessId(window,&pid);
            if(pid==GetCurrentProcessId() && thread==GetCurrentThreadId())PostMessageW(window,MenuStateMessage,visible,0);
        }
    }
    HRESULT EnsureMenuHook() {
        DWORD thread=GetCurrentThreadId();if(_menuHooks.count(thread))return S_OK;
        auto hook=SetWinEventHook(EVENT_OBJECT_SHOW,EVENT_OBJECT_HIDE,nullptr,MenuVisibilityEvent,0,0,WINEVENT_OUTOFCONTEXT|WINEVENT_SKIPOWNPROCESS);
        if(!hook)return HRESULT_FROM_WIN32(GetLastError());
        _menuHooks[thread]=hook;return S_OK;
    }
    HRESULT OpenMappedImageStream(const std::wstring& name,UINT32 length,Com<IStream>& stream) {
        auto failed=[&](HRESULT error,const wchar_t* detail) {
            if(_shared) {
                std::wstring message=L"无法读取开始按钮图片："+std::wstring(detail)+L"。";
                wcsncpy(_shared->ErrorMessage,message.c_str(),2047);_shared->ErrorMessage[2047]=0;
            }
            return error;
        };
        constexpr wchar_t prefix[]=L"Local\\EDock.StartButtonImage.v1.";
        constexpr size_t prefixLength=std::size(prefix)-1;
        if(name.rfind(prefix,0)!=0 || name.size()<=prefixLength || name.size()>=256
            || !std::all_of(name.begin()+prefixLength,name.end(),[](wchar_t c) {
                return (c>=L'0' && c<=L'9') || (c>=L'a' && c<=L'z') || (c>=L'A' && c<=L'Z') || c==L'.' || c==L'-';
            }) || length==0 || length>16UL*1024*1024)
            return failed(E_INVALIDARG,L"图片共享数据无效或超过 16 MB 限制");
        HANDLE mapping=OpenFileMappingW(FILE_MAP_READ,FALSE,name.c_str());
        if(!mapping)return failed(HRESULT_FROM_WIN32(GetLastError()),L"图片共享数据已失效，请重新应用图片");
        struct MappingDone {HANDLE Value;~MappingDone(){CloseHandle(Value);}} mappingDone{mapping};
        const void* view=MapViewOfFile(mapping,FILE_MAP_READ,0,0,length);
        if(!view)return failed(HRESULT_FROM_WIN32(GetLastError()),L"无法打开图片共享数据");
        struct ViewDone {const void* Value;~ViewDone(){UnmapViewOfFile(Value);}} viewDone{view};
        HGLOBAL memory=GlobalAlloc(GMEM_MOVEABLE,length);
        if(!memory)return failed(E_OUTOFMEMORY,L"可用内存不足");
        void* bytes=GlobalLock(memory);
        if(!bytes){GlobalFree(memory);return failed(E_OUTOFMEMORY,L"可用内存不足");}
        memcpy(bytes,view,length);GlobalUnlock(memory);
        HRESULT result=CreateStreamOnHGlobal(memory,TRUE,stream.Put());
        if(FAILED(result)){GlobalFree(memory);return failed(result,L"无法创建图片数据流");}
        return S_OK;
    }
    HRESULT ValidateImageStream(IStream* stream) {
        Com<IWICImagingFactory> factory;Com<IWICBitmapDecoder> decoder;Com<IWICBitmapFrameDecode> frame;
        HRESULT result=CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,
            IID_IWICImagingFactory,factory.VoidPut());
        if(SUCCEEDED(result))result=factory->CreateDecoderFromStream(stream,nullptr,WICDecodeMetadataCacheOnDemand,decoder.Put());
        UINT frames=0,width=0,height=0;
        if(SUCCEEDED(result))result=decoder->GetFrameCount(&frames);
        if(SUCCEEDED(result) && !frames)result=HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        if(SUCCEEDED(result))result=decoder->GetFrame(0,frame.Put());
        if(SUCCEEDED(result))result=frame->GetSize(&width,&height);
        if(SUCCEEDED(result) && (!width || !height))result=HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        if(FAILED(result) && _shared) {
            constexpr wchar_t message[]=L"开始按钮图片已损坏或无法解码，请重新选择 PNG、JPG 或 GIF 图片。";
            wcsncpy(_shared->ErrorMessage,message,2047);_shared->ErrorMessage[2047]=0;
        }
        LARGE_INTEGER zero{};
        HRESULT seek=stream->Seek(zero,STREAM_SEEK_SET,nullptr);
        return FAILED(result)?result:seek;
    }
    HRESULT OpenImageStream(const std::wstring& path,const std::wstring& mapping,UINT32 byteLength,Com<IStream>& stream) {
        // EDock opens cached images in its own context. Explorer may have a
        // different AppData view, so a mapped payload is authoritative whenever
        // supplied. Never silently reopen the pathname after a mapping failure.
        if(!mapping.empty())return OpenMappedImageStream(mapping,byteLength,stream);
        auto stage=[&](LONG value){if(_shared)InterlockedExchange(&_shared->Reserved[0],value);};
        auto failed=[&](DWORD error) {
            if(_shared) {
                std::wstring message=L"无法读取开始按钮图片："+path+L"（Windows 错误 "+std::to_wstring(error)+L"）。";
#ifdef EDOCK_STARTBUTTON_PROBE
                message+=DescribeFileAccess(path);
#endif
                wcsncpy(_shared->ErrorMessage,message.c_str(),2047);_shared->ErrorMessage[2047]=0;
            }
            return HRESULT_FROM_WIN32(error);
        };
        std::wstring extended=path;
        if(path.rfind(L"\\\\?\\",0)!=0)
            extended=path.rfind(L"\\\\",0)==0?L"\\\\?\\UNC\\"+path.substr(2):L"\\\\?\\"+path;
        stage(20);
        HANDLE file=CreateFileW(extended.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
            nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_SEQUENTIAL_SCAN,nullptr);
        if(file==INVALID_HANDLE_VALUE)return failed(GetLastError());
        struct FileDone {HANDLE Value;~FileDone(){CloseHandle(Value);}} fileDone{file};
        stage(21);LARGE_INTEGER length{};
        if(!GetFileSizeEx(file,&length))return failed(GetLastError());
        if(length.QuadPart<=0)return failed(ERROR_INVALID_DATA);
        // Keep source buffering bounded even for very large animated files.
        if(length.QuadPart>16*1024*1024)return failed(ERROR_FILE_TOO_LARGE);
        stage(22);
        HGLOBAL memory=GlobalAlloc(GMEM_MOVEABLE,static_cast<SIZE_T>(length.QuadPart));
        if(!memory)return failed(ERROR_NOT_ENOUGH_MEMORY);
        auto bytes=static_cast<BYTE*>(GlobalLock(memory));
        if(!bytes){GlobalFree(memory);return failed(ERROR_NOT_ENOUGH_MEMORY);}
        DWORD read=0,total=0,size=static_cast<DWORD>(length.QuadPart),error=0;
        while(total<size) {
            if(!ReadFile(file,bytes+total,std::min(size-total,4UL*1024*1024),&read,nullptr)){error=GetLastError();break;}
            if(!read){error=ERROR_HANDLE_EOF;break;}total+=read;
        }
        GlobalUnlock(memory);
        if(error){GlobalFree(memory);return failed(error);}
        stage(23);
        HRESULT hr=CreateStreamOnHGlobal(memory,TRUE,stream.Put());
        if(FAILED(hr))GlobalFree(memory);
        return hr;
    }
#ifdef EDOCK_STARTBUTTON_PROBE
    static std::wstring DescribeToken(HANDLE token) {
        if(!token)return L"none";
        std::wstring text;
        DWORD bytes=0;GetTokenInformation(token,TokenUser,nullptr,0,&bytes);
        std::vector<BYTE> data(bytes);
        if(bytes && GetTokenInformation(token,TokenUser,data.data(),bytes,&bytes)) {
            LPWSTR sid=nullptr;
            if(ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(data.data())->User.Sid,&sid)) {
                text+=L"sid="+std::wstring(sid);LocalFree(sid);
            }
        }
        for(auto item:{TokenIsAppContainer,TokenVirtualizationEnabled,TokenType,TokenImpersonationLevel}) {
            DWORD value=0;
            if(GetTokenInformation(token,item,&value,sizeof(value),&bytes))
                text+=L","+std::to_wstring(item)+L"="+std::to_wstring(value);
        }
        bytes=0;GetTokenInformation(token,TokenIntegrityLevel,nullptr,0,&bytes);data.resize(bytes);
        if(bytes && GetTokenInformation(token,TokenIntegrityLevel,data.data(),bytes,&bytes)) {
            auto sid=reinterpret_cast<TOKEN_MANDATORY_LABEL*>(data.data())->Label.Sid;
            text+=L",IL="+std::to_wstring(*GetSidSubAuthority(sid,*GetSidSubAuthorityCount(sid)-1));
        }
        return text;
    }
    static std::wstring DescribeFileAccess(const std::wstring& path) {
        auto attribute=[](const std::wstring& item) {
            SetLastError(0);DWORD value=GetFileAttributesW(item.c_str()),error=GetLastError();
            return std::to_wstring(value)+L"/err"+std::to_wstring(error);
        };
        std::wstring text=L" len="+std::to_wstring(path.size())+L";attr="+attribute(path);
        std::wstring parent=path;
        for(int i=0;i<5;++i) {
            auto slash=parent.find_last_of(L"\\/");if(slash==std::wstring::npos)break;parent.resize(slash);
            text+=L";parent"+std::to_wstring(i)+L"="+attribute(parent);
        }
        HANDLE direct=CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
            nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_SEQUENTIAL_SCAN,nullptr);
        text+=L";plain="+std::to_wstring(direct==INVALID_HANDLE_VALUE?GetLastError():0);
        if(direct!=INVALID_HANDLE_VALUE)CloseHandle(direct);
        HANDLE process=nullptr,thread=nullptr;
        if(OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&process)) {
            text+=L";process["+DescribeToken(process)+L"]";CloseHandle(process);
        } else text+=L";processErr="+std::to_wstring(GetLastError());
        if(OpenThreadToken(GetCurrentThread(),TOKEN_QUERY,TRUE,&thread)) {
            text+=L";thread["+DescribeToken(thread)+L"]";CloseHandle(thread);
        } else text+=L";threadErr="+std::to_wstring(GetLastError());
        wchar_t value[1024]{};
        if(GetEnvironmentVariableW(L"LOCALAPPDATA",value,1024))text+=L";local="+std::wstring(value);
        wchar_t drive[]={path.size()>0?path[0]:L'C',L':',0};
        if(QueryDosDeviceW(drive,value,1024))text+=L";drive="+std::wstring(value);
        if(GetCurrentDirectoryW(1024,value))text+=L";cwd="+std::wstring(value);
        return text;
    }
#endif

    bool Snapshot(Configuration& value) {
        if(!_shared || _shared->Magic!=Magic || _shared->Version!=2 || _shared->Publishing) return false;
        value.Generation=_shared->Generation;
        MemoryBarrier();
        value.Owner=_shared->OwnerPid;value.Enabled=_shared->Enabled;
        value.Size=std::clamp(static_cast<LONG>(_shared->IconSize),8L,64L);
        value.Target=_shared->TargetPid;
        value.Paths={std::wstring(_shared->Normal,wcsnlen(_shared->Normal,4096)),
            std::wstring(_shared->Hover,wcsnlen(_shared->Hover,4096)),
            std::wstring(_shared->Pressed,wcsnlen(_shared->Pressed,4096)),
            std::wstring(_shared->Activated,wcsnlen(_shared->Activated,4096))};
        for(UINT index=0;index<4;++index) {
            value.ImageMappings[index]=std::wstring(_shared->ImageMappings[index],wcsnlen(_shared->ImageMappings[index],256));
            value.ImageLengths[index]=_shared->ImageLengths[index];
        }
        MemoryBarrier();
        return !_shared->Publishing && value.Generation==_shared->Generation;
    }
    static void CALLBACK OwnerExited(void*,BOOLEAN) {
        HWND window=nullptr;
        while((window=FindWindowExW(nullptr,window,WindowClass,nullptr))) {
            DWORD process=0;GetWindowThreadProcessId(window,&process);
            if(process==GetCurrentProcessId()) PostMessageW(window,ApplyMessage,0,0);
        }
    }
    void StopOwnerWait() {
        if(_ownerWait){UnregisterWaitEx(_ownerWait,nullptr);_ownerWait=nullptr;}
        if(_owner){CloseHandle(_owner);_owner=nullptr;}
        _ownerPid=0;
    }
    bool Enabled(const Configuration& config) {
        if(!config.Enabled || config.Paths[0].empty()
            || (config.Target && static_cast<DWORD>(config.Target)!=GetCurrentProcessId())) {_ownerError=S_OK;return false;}
        if(!_owner || _ownerPid!=static_cast<DWORD>(config.Owner)) {
            StopOwnerWait();_ownerError=S_OK;_ownerPid=config.Owner;
            _owner=OpenProcess(SYNCHRONIZE,FALSE,_ownerPid);
            if(!_owner) {
                DWORD error=GetLastError();
                // A terminated owner is the normal automatic-restore case.
                if(error!=ERROR_INVALID_PARAMETER)_ownerError=HRESULT_FROM_WIN32(error);
            }
            else if(!RegisterWaitForSingleObject(&_ownerWait,_owner,OwnerExited,nullptr,INFINITE,WT_EXECUTEONLYONCE)) {
                _ownerError=HRESULT_FROM_WIN32(GetLastError());CloseHandle(_owner);_owner=nullptr;
            }
        }
        return _owner && WaitForSingleObject(_owner,0)==WAIT_TIMEOUT;
    }
    bool IsStartButton(InstanceHandle handle,const wchar_t* name) {
        Com<IInspectable> object,dependency,statics;
        if(FAILED(_diagnostics->GetIInspectableFromHandle(handle,object.Put()))) return false;
        if(SUCCEEDED(object->QueryInterface(Abi::DependencyId,dependency.VoidPut()))
            && SUCCEEDED(Factory(L"Windows.UI.Xaml.Automation.AutomationProperties",Abi::AutomationId,statics.VoidPut()))) {
            HSTRING id=nullptr;
            HRESULT result=Abi::GetAutomationId(statics.Get(),dependency.Get(),&id);
            bool start=SUCCEEDED(result) && wcscmp(WindowsGetStringRawBuffer(id,nullptr),L"StartButton")==0;
            WindowsDeleteString(id);
            if(start) return true;
        }
        return name && wcscmp(name,L"StartButton")==0;
    }
    InstanceHandle StartAncestor(InstanceHandle handle) {
        for(UINT depth=0;handle && depth<64;++depth) {
            auto node=_nodes.find(handle);
            if(node==_nodes.end()) return 0;
            if(node->second.Start) return handle;
            handle=node->second.Parent;
        }
        return 0;
    }
    void CaptureOpacity(Instance& instance) {
        instance.HasOpacityIndex=false;instance.OpacityLocal=true;
        UINT sources=0,count=0;PropertyChainSource* source=nullptr;PropertyChainValue* values=nullptr;
        if(SUCCEEDED(_service->GetPropertyValuesChain(instance.Icon,&sources,&source,&count,&values))) {
            for(UINT i=0;i<count;++i) {
                auto& value=values[i];
                if(value.PropertyName && wcscmp(value.PropertyName,L"Opacity")==0 && !value.Overridden) {
                    instance.HasOpacityIndex=true;instance.OpacityIndex=value.Index;
                    INT origin=value.PropertyChainIndex<sources?source[value.PropertyChainIndex].Source:-1;
                    instance.OpacityLocal=origin==-1 || origin==4;break;
                }
            }
        }
        FreeChain(sources,source,count,values);
    }
    HRESULT Detach(Instance& instance) {
        ++_changing;
        struct Done {UINT& Depth;~Done(){--Depth;}} done{_changing};
        instance.Detached=true;
        HRESULT result=S_OK;
        // A failed removal must remain retryable. Hide our layers immediately,
        // restore the system icon and retain every outstanding XAML reference.
        for(auto& image:instance.Images) {
            HRESULT hr=image->Ui->put_Opacity(0);if(FAILED(hr))result=hr;
        }
        for(auto event=instance.Events.begin();event!=instance.Events.end();) {
            HRESULT hr=Abi::RemoveHandler(instance.ButtonUi.Get(),event->RoutedEvent.Get(),event->Handler.Get());
            if(FAILED(hr)){result=hr;++event;}else event=instance.Events.erase(event);
        }
        if(instance.HasChecked) {
            HRESULT hr=instance.Toggle->remove_Checked(instance.Checked);
            if(FAILED(hr))result=hr;else instance.HasChecked=false;
        }
        if(instance.HasUnchecked) {
            HRESULT hr=instance.Toggle->remove_Unchecked(instance.Unchecked);
            if(FAILED(hr))result=hr;else instance.HasUnchecked=false;
        }
        if(!instance.HasChecked && !instance.HasUnchecked)instance.Toggle.Reset();
        for(auto& image:instance.Images) {
            if(image->HasOpened) {
                HRESULT hr=image->Image->remove_ImageOpened(image->Opened);
                if(FAILED(hr))result=hr;else image->HasOpened=false;
            }
            if(image->HasFailed) {
                HRESULT hr=image->Image->remove_ImageFailed(image->Failed);
                if(FAILED(hr))result=hr;else image->HasFailed=false;
            }
        }
        if(instance.Hidden && instance.Stock) {
            HRESULT hr=instance.OpacityLocal || !instance.HasOpacityIndex
                ?instance.Stock->put_Opacity(instance.OriginalOpacity)
                :_service->ClearProperty(instance.Icon,instance.OpacityIndex);
            if(FAILED(hr))result=hr;else instance.Hidden=false;
        }
        if(instance.Inserted && instance.ParentChildren && instance.Host) {
            UINT index=0;BYTE found=FALSE;
            Com<UiElement> hostUi;
            HRESULT hr=instance.Host->QueryInterface(IidUiElement,hostUi.VoidPut());
            if(SUCCEEDED(hr))hr=instance.ParentChildren->IndexOf(hostUi.Get(),&index,&found);
            if(SUCCEEDED(hr) && found)hr=instance.ParentChildren->RemoveAt(index);
            if(FAILED(hr))result=hr;else instance.Inserted=false;
        }
        bool imageEvents=std::any_of(instance.Images.begin(),instance.Images.end(),[](const auto& image){return image->HasOpened||image->HasFailed;});
        if(!instance.Inserted && !imageEvents){instance.Images.clear();instance.Host.Reset();instance.ParentChildren.Reset();}
        if(instance.Events.empty())instance.ButtonUi.Reset();
        if(!instance.Hidden)instance.Stock.Reset();
        instance.Generation=-1;instance.Hover=instance.Pressed=instance.Active=instance.MenuFallback=false;
        return result;
    }
    HRESULT Attach(const std::shared_ptr<Instance>& instance,const Configuration& config) {
        auto stage=[&](LONG value){if(_shared)InterlockedExchange(&_shared->Reserved[0],value);};
        stage(1);
        HRESULT result=Detach(*instance);
        if(FAILED(result))return result;
        instance->Detached=false;instance->Building=true;
        ++_changing;
        struct Done {Site* Self;Instance* Item;~Done(){--Self->_changing;Item->Building=false;}} done{this,instance.get()};
        instance->Generation=config.Generation;
        instance->Error=S_OK;
        if(_shared) {
            _shared->ErrorMessage[0]=0;InterlockedExchange(&_shared->Reserved[2],0);InterlockedExchange(&_shared->Reserved[3],0);
        }
        Com<IInspectable> icon,parent,button;
        stage(2);
        if(FAILED(result=_diagnostics->GetIInspectableFromHandle(instance->Icon,icon.Put())))return result;
        if(FAILED(result=icon->QueryInterface(IidUiElement,instance->Stock.VoidPut())))return result;
        if(FAILED(result=instance->Stock->get_Opacity(&instance->OriginalOpacity)))return result;
        CaptureOpacity(*instance);
        stage(3);
        if(FAILED(result=_diagnostics->GetIInspectableFromHandle(instance->Parent,parent.Put())))return result;
        Com<Abi::Panel> panel;
        if(FAILED(result=parent->QueryInterface(Abi::PanelId,panel.VoidPut())))return result;
        if(FAILED(result=panel->get_Children(instance->ParentChildren.Put())))return result;
        stage(4);
        if(FAILED(result=_diagnostics->GetIInspectableFromHandle(instance->Button,button.Put())))return result;
        if(FAILED(result=button->QueryInterface(IidUiElement,instance->ButtonUi.VoidPut())))return result;
        auto size=std::to_wstring(config.Size);
        std::wstring markup=L"<Grid";markup+=XamlNamespace;
        markup+=L" Name=\"EDockStartButtonImage\" IsHitTestVisible=\"False\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Canvas.ZIndex=\"32767\" Width=\""+size+L"\" Height=\""+size+L"\"/>";
        stage(5);
        if(FAILED(result=ReadXaml(markup,instance->Host.Put())))return result;
        stage(6);
        Com<Abi::Panel> hostPanel;Com<Abi::Children> children;
        if(FAILED(result=instance->Host->QueryInterface(Abi::PanelId,hostPanel.VoidPut())))return result;
        if(FAILED(result=hostPanel->get_Children(children.Put())))return result;
        std::vector<std::wstring> loaded;
        for(UINT state=0;state<4;++state) {
            std::wstring path=config.Paths[state].empty()?config.Paths[0]:config.Paths[state];
            auto duplicate=std::find(loaded.begin(),loaded.end(),path);
            if(duplicate!=loaded.end()){instance->ImageByState[state]=static_cast<UINT>(duplicate-loaded.begin());continue;}
            UINT index=static_cast<UINT>(instance->Images.size());
            if(_shared)InterlockedExchange(&_shared->Reserved[1],index);
            instance->ImageByState[state]=index;loaded.push_back(path);
            auto layer=std::make_unique<ImageLayer>();
            // 4x covers high DPI without decoding the source at its full dimensions.
            auto decode=std::to_wstring(std::clamp(config.Size*4,32L,256L));
            markup=L"<Image";markup+=XamlNamespace;
            markup+=L" IsHitTestVisible=\"False\" Opacity=\"0\" Stretch=\"Uniform\" HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Stretch\"><Image.Source><BitmapImage DecodePixelWidth=\""+decode+L"\"/></Image.Source></Image>";
            stage(7);
            if(FAILED(result=ReadXaml(markup,layer->Element.Put())))return result;
            stage(8);
            if(FAILED(result=layer->Element->QueryInterface(Abi::ImageId,layer->Image.VoidPut())))return result;
            if(FAILED(result=layer->Element->QueryInterface(IidUiElement,layer->Ui.VoidPut())))return result;
            instance->Images.push_back(std::move(layer));
            auto& image=*instance->Images.back();
            Com<Abi::Handler> opened,failed;
            opened.Attach(new(std::nothrow) Event(this,instance,config.Generation,1,index));
            failed.Attach(new(std::nothrow) Event(this,instance,config.Generation,2,index));
            if(!opened || !failed)return E_OUTOFMEMORY;
            stage(9);
            if(FAILED(result=image.Image->add_ImageOpened(opened.Get(),&image.Opened)))return result;
            image.HasOpened=true;
            stage(10);
            if(FAILED(result=image.Image->add_ImageFailed(failed.Get(),&image.Failed)))return result;
            image.HasFailed=true;
            // Read once with Win32 and supply a memory stream. Neither the
            // decoder nor a Shell stream must reopen the source pathname.
            stage(19);
            Com<IStream> file;
            UINT payload=config.Paths[state].empty()?0:state;
            if(FAILED(result=OpenImageStream(path,config.ImageMappings[payload],config.ImageLengths[payload],file))) {
                if(state==0)return result;image.Error=true;continue;
            }
            // A corrupt stream can return S_OK from XAML SetSource without
            // raising Image.ImageFailed. Validate its format and dimensions
            // synchronously so failure is acknowledged rather than left pending.
            if(FAILED(result=ValidateImageStream(file.Get()))) {
                if(state==0)return result;image.Error=true;continue;
            }
            stage(24);
            if(FAILED(result=CreateRandomAccessStreamOverStream(file.Get(),BSOS_DEFAULT,Abi::RandomStreamId,image.Stream.VoidPut()))) {
                if(state==0)return result;image.Error=true;continue;
            }
            Com<IInspectable> source;Com<Abi::BitmapSource> bitmap;
            stage(25);
            if(FAILED(result=image.Image->get_Source(source.Put())))return result;
            stage(26);
            if(FAILED(result=source->QueryInterface(Abi::BitmapSourceId,bitmap.VoidPut())))return result;
            stage(27);
            if(FAILED(result=bitmap->SetSource(image.Stream.Get()))) {
                if(state==0)return result;image.Error=true;continue;
            }
            INT width=0,height=0;
            if(SUCCEEDED(bitmap->get_PixelWidth(&width)) && SUCCEEDED(bitmap->get_PixelHeight(&height)) && width>0 && height>0)
                image.Ready=true;
            stage(11);
            if(FAILED(result=children->Append(image.Ui.Get())))return result;
        }
        Com<IInspectable> eventStatics;
        stage(12);
        if(FAILED(result=Factory(L"Windows.UI.Xaml.UIElement",Abi::ElementStaticsId,eventStatics.VoidPut())))return result;
        // The public AddHandler API observes handled events without altering input.
        constexpr UINT eventSlots[]={8,12,9,11,13,14};
        for(UINT i=0;i<6;++i) {
            ButtonEvent event;
            stage(13);
            if(FAILED(result=Abi::RoutedEvent(eventStatics.Get(),eventSlots[i],event.RoutedEvent.Put())))return result;
            Event* handler=new(std::nothrow) Event(this,instance,config.Generation,10+i);
            if(!handler)return E_OUTOFMEMORY;
            auto box=new(std::nothrow) PointerBox(handler);
            if(!box){handler->Release();return E_OUTOFMEMORY;}
            event.Handler.Attach(static_cast<Abi::PointerReference*>(box));
            stage(14);
            if(FAILED(result=Abi::AddHandler(instance->ButtonUi.Get(),event.RoutedEvent.Get(),event.Handler.Get())))return result;
            instance->Events.push_back(std::move(event));
        }
        stage(18);
        if(SUCCEEDED(button->QueryInterface(Abi::ToggleButtonId,instance->Toggle.VoidPut()))) {
            Com<Abi::Handler> checked,unchecked;
            checked.Attach(new(std::nothrow) Event(this,instance,config.Generation,16));
            unchecked.Attach(new(std::nothrow) Event(this,instance,config.Generation,17));
            if(!checked || !unchecked)return E_OUTOFMEMORY;
            if(FAILED(result=instance->Toggle->add_Checked(checked.Get(),&instance->Checked)))return result;
            instance->HasChecked=true;
            if(FAILED(result=instance->Toggle->add_Unchecked(unchecked.Get(),&instance->Unchecked)))return result;
            instance->HasUnchecked=true;
            Com<Abi::BooleanReference> isChecked;
            if(SUCCEEDED(instance->Toggle->get_IsChecked(isChecked.Put())) && isChecked) {
                BYTE active=FALSE;if(SUCCEEDED(isChecked->get_Value(&active)))instance->Active=active;
            }
        } else {
            if(FAILED(result=EnsureMenuHook()))return result;
            instance->MenuFallback=true;instance->Active=MenuVisible();
        }
        Com<UiElement> hostUi;
        stage(15);
        if(FAILED(result=instance->Host->QueryInterface(IidUiElement,hostUi.VoidPut())))return result;
        instance->Inserted=true; // Reentrant diagnostics now sees a fully owned host.
        stage(16);
        result=instance->ParentChildren->Append(hostUi.Get());
        if(FAILED(result))instance->Inserted=false;
        else stage(17);
        return result;
    }
    void UpdateImage(Instance& instance) {
        if(instance.Detached || instance.Images.empty() || !instance.Stock) return;
        // Press feedback wins; an open Start menu retains the activated image
        // as the pointer moves away. Empty optional images resolve to normal.
        UINT wanted=instance.ImageByState[instance.Pressed?2:instance.Active?3:instance.Hover?1:0];
        if(wanted>=instance.Images.size() || !instance.Images[wanted]->Ready)wanted=0;
        if(!instance.Images[0]->Ready) {
            if(instance.Images[0]->Error) {
                instance.Error=HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                for(auto& image:instance.Images)image->Ui->put_Opacity(0);
                if(instance.Hidden) {
                    HRESULT restored=instance.OpacityLocal || !instance.HasOpacityIndex
                        ?instance.Stock->put_Opacity(instance.OriginalOpacity)
                        :_service->ClearProperty(instance.Icon,instance.OpacityIndex);
                    if(SUCCEEDED(restored))instance.Hidden=false;
                }
            }
            return;
        }
        for(UINT i=0;i<instance.Images.size();++i) {
            HRESULT hr=instance.Images[i]->Ui->put_Opacity(i==wanted?1.0:0.0);
            if(FAILED(hr)) {
                instance.Error=hr;
                if(instance.Hidden) {
                    HRESULT restored=instance.OpacityLocal || !instance.HasOpacityIndex
                        ?instance.Stock->put_Opacity(instance.OriginalOpacity)
                        :_service->ClearProperty(instance.Icon,instance.OpacityIndex);
                    if(SUCCEEDED(restored))instance.Hidden=false;
                }
                return;
            }
        }
        if(!instance.Hidden) {
            HRESULT hr=instance.Stock->put_Opacity(0);
            if(SUCCEEDED(hr))instance.Hidden=true;else instance.Error=hr;
        }
    }
    void Apply() {
        std::lock_guard guard(_lock);
        // AdviseVisualTreeChange synchronously enumerates existing XAML. Do
        // not modify that same tree while diagnostics is still enumerating it.
        if(_changing || _subscription.load()==1)return;
        Configuration config;
        if(!Snapshot(config))return;
        bool enabled=Enabled(config);
        LONG applied=0;HRESULT error=_ownerError;bool cleanupFailed=false;
        std::set<DWORD> threads;
        std::vector<InstanceHandle> retiredDone;
        for(auto& entry:_instances) {
            auto instance=entry.second;threads.insert(instance->Thread);
            if(instance->Thread!=GetCurrentThreadId())continue;
            if(instance->Retired) {
                HRESULT hr=Detach(*instance);
                if(FAILED(hr)){error=hr;cleanupFailed=true;}else retiredDone.push_back(entry.first);
                continue;
            }
            if(enabled) {
                if(instance->Generation!=config.Generation) {
                    HRESULT hr=Attach(instance,config);
                    if(FAILED(hr)){Detach(*instance);instance->Generation=config.Generation;instance->Error=hr;}
                }
                UpdateImage(*instance);
                if(instance->Hidden && instance->Inserted && instance->Host && !instance->Images.empty()
                    && instance->Images[0]->Ready && SUCCEEDED(instance->Error))++applied;
                if(FAILED(instance->Error))error=instance->Error;
            } else {
                HRESULT hr=Detach(*instance);if(FAILED(hr)){error=hr;cleanupFailed=true;}
            }
        }
        for(auto handle:retiredDone)_instances.erase(handle);
        DWORD current=GetCurrentThreadId();
        bool needsMenuHook=std::any_of(_instances.begin(),_instances.end(),[current](const auto& item){
            return item.second->Thread==current && item.second->MenuFallback && !item.second->Detached && !item.second->Retired;
        });
        if(!needsMenuHook) {
            auto hook=_menuHooks.find(current);
            if(hook!=_menuHooks.end()){UnhookWinEvent(hook->second);_menuHooks.erase(hook);}
        }
        // Retry transient restoration failures only for a bounded period. No
        // timer is active during normal use or after a successful restoration.
        auto window=_windows.find(current);
        if(cleanupFailed && window!=_windows.end()) {
            auto& retry=_restoreRetries[current];
            if(retry.Generation!=config.Generation) {
                KillTimer(window->second,RestoreTimer);retry={config.Generation,0,false};
            }
            constexpr UINT delays[]={100,200,400,800,1200,1600};
            if(!retry.Pending && retry.Attempts<std::size(delays)) {
                retry.Pending=SetTimer(window->second,RestoreTimer,delays[retry.Attempts++],nullptr)!=0;
            }
        } else {
            if(window!=_windows.end())KillTimer(window->second,RestoreTimer);
            _restoreRetries.erase(current);
        }
        if(!enabled && std::none_of(_instances.begin(),_instances.end(),[](const auto& item){return item.second->Hidden;}))StopOwnerWait();
        if(threads.count(GetCurrentThreadId()))_results[GetCurrentThreadId()]={applied,config.Generation,error};
        LONG total=0;bool acknowledged=true;
        for(auto thread:threads) {
            auto found=_results.find(thread);
            if(found==_results.end() || found->second.Generation!=config.Generation)acknowledged=false;
            else {total+=found->second.Applied;if(FAILED(found->second.Error))error=found->second.Error;}
        }
        LONG matched=static_cast<LONG>(std::count_if(_instances.begin(),_instances.end(),[](const auto& item){return !item.second->Retired;}));
        InterlockedExchange(&_shared->Matched,matched);
        InterlockedExchange(&_shared->Applied,total);
        InterlockedExchange(&_shared->LastResult,error);
        if(acknowledged)InterlockedExchange(&_shared->Acknowledged,config.Generation);
    }
    static LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM wp,LPARAM lp) {
        auto self=reinterpret_cast<Site*>(GetWindowLongPtrW(window,GWLP_USERDATA));
        if(message==WM_NCCREATE){self=static_cast<Site*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);SetWindowLongPtrW(window,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(self));}
        if(message==ApplyMessage && self) {
            try {
                std::lock_guard guard(self->_lock);
                self->_pendingApply.erase(GetCurrentThreadId());self->Apply();
            }catch(...){if(self->_shared)InterlockedExchange(&self->_shared->LastResult,E_FAIL);}
            return 0;
        }
        if(message==MenuStateMessage && self) {
            try {
                std::lock_guard guard(self->_lock);
                for(auto& item:self->_instances) {
                    auto& instance=*item.second;
                    if(instance.Thread==GetCurrentThreadId() && instance.MenuFallback && !instance.Detached) {
                        instance.Active=wp!=0;self->UpdateImage(instance);
                    }
                }
                self->Apply();
            }catch(...){if(self->_shared)InterlockedExchange(&self->_shared->LastResult,E_FAIL);}
            return 0;
        }
        if(message==WM_TIMER && wp==RestoreTimer && self) {
            KillTimer(window,RestoreTimer);
            try {
                std::lock_guard guard(self->_lock);
                auto retry=self->_restoreRetries.find(GetCurrentThreadId());
                if(retry!=self->_restoreRetries.end())retry->second.Pending=false;
                self->Apply();
            } catch(...){if(self->_shared)InterlockedExchange(&self->_shared->LastResult,E_FAIL);}
            return 0;
        }
        return DefWindowProcW(window,message,wp,lp);
    }
    HRESULT EnsureWindow() {
        DWORD thread=GetCurrentThreadId();auto existing=_windows.find(thread);
        if(existing!=_windows.end() && IsWindow(existing->second))return S_OK;
        WNDCLASSEXW wc{};wc.cbSize=sizeof(wc);wc.hInstance=Module;wc.lpfnWndProc=WindowProc;wc.lpszClassName=WindowClass;
        if(!RegisterClassExW(&wc) && GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)return HRESULT_FROM_WIN32(GetLastError());
        HWND window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,WindowClass,L"EDock Start button appearance",0,0,0,0,0,nullptr,nullptr,Module,this);
        if(!window)return HRESULT_FROM_WIN32(GetLastError());
        _windows[thread]=window;return S_OK;
    }
    void ScheduleApply() {
        if(_subscription.load()==1)return;
        for(const auto& item:_windows) {
            if(_pendingApply.insert(item.first).second && !PostMessageW(item.second,ApplyMessage,0,0))
                _pendingApply.erase(item.first);
        }
    }
    static DWORD WINAPI Subscribe(void* context) {
        auto self=static_cast<Site*>(context);CoInitializeEx(nullptr,COINIT_MULTITHREADED);
        HRESULT hr=self->_service->AdviseVisualTreeChange(self);
        {
            std::lock_guard guard(self->_lock);
            // Advise retains this callback before enumerating dispatcher
            // roots. A later enumeration failure can return E_UNEXPECTED even
            // though the actual taskbar subscription is already active. Only
            // accept that partial result with a discovered Start target and
            // UI dispatch window; Attach still validates every interface and
            // image before changing the stock icon. Re-Advising would fail
            // because the existing callback remains registered.
            bool partial=hr==E_UNEXPECTED && !self->_instances.empty() && !self->_windows.empty();
            self->_subscription.store(SUCCEEDED(hr)?2:partial?4:3);
#ifdef EDOCK_STARTBUTTON_PROBE
            self->ProbeValue(4,hr);
            self->ProbeValue(5,static_cast<LONG>(self->_instances.size()));
            self->ProbeValue(6,static_cast<LONG>(self->_windows.size()));
            self->ProbeValue(7,self->_subscription.load());
#endif
            if(SUCCEEDED(hr) || partial)self->ScheduleApply();
            else if(self->_shared) {
                constexpr wchar_t message[]=L"开始按钮组件未能订阅 Windows 界面变化（AdviseVisualTreeChange）；当前保留系统图标。";
                wcsncpy(self->_shared->ErrorMessage,message,2047);self->_shared->ErrorMessage[2047]=0;
                InterlockedExchange(&self->_shared->LastResult,hr);
            }
        }
        CoUninitialize();self->Release();return 0;
    }
    HRESULT StartSubscription() {
        _subscription.store(1);AddRef();HANDLE thread=CreateThread(nullptr,0,Subscribe,this,0,nullptr);
        if(!thread){_subscription.store(3);Release();return HRESULT_FROM_WIN32(GetLastError());}
        CloseHandle(thread);return S_OK;
    }
public:
    Site(){CoCreateFreeThreadedMarshaler(static_cast<IObjectWithSite*>(this),_marshaler.Put());}
    ~Site(){StopOwnerWait();if(_shared)UnmapViewOfFile(_shared);if(_mapping)CloseHandle(_mapping);}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** output) override {
        if(!output)return E_POINTER;*output=nullptr;
        if(IsEqualIID(id,IID_IUnknown)||IsEqualIID(id,IID_IObjectWithSite))*output=static_cast<IObjectWithSite*>(this);
        else if(IsEqualIID(id,IidVisualCallback)||IsEqualIID(id,IidVisualCallback2))*output=static_cast<VisualCallback2*>(this);
        else if(IsEqualIID(id,IID_IMarshal)&&_marshaler)return _marshaler->QueryInterface(id,output);
        else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++_refs;}
    ULONG STDMETHODCALLTYPE Release() override{ULONG count=--_refs;if(!count)delete this;return count;}
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override {
        if(!site)return S_OK;
        std::lock_guard guard(_lock);
        if(_site)return _subscription.load()==3?StartSubscription():S_OK;
        HRESULT hr=site->QueryInterface(IidVisualService2,_service.VoidPut());if(FAILED(hr))return hr;
        hr=site->QueryInterface(IidXamlDiagnostics,_diagnostics.VoidPut());if(FAILED(hr))return hr;
        _mapping=OpenFileMappingW(FILE_MAP_READ|FILE_MAP_WRITE,FALSE,MappingName);
        if(!_mapping)return HRESULT_FROM_WIN32(GetLastError());
        _shared=static_cast<SharedState*>(MapViewOfFile(_mapping,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(SharedState)));
        if(!_shared)return HRESULT_FROM_WIN32(GetLastError());
        site->AddRef();_site.Attach(site);return StartSubscription();
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID id,void** output) override {std::lock_guard guard(_lock);return _site?_site->QueryInterface(id,output):E_FAIL;}
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation,VisualElement element,INT mutation) override {
        TreeStrings strings{element,mutation==0};
        try {
            std::lock_guard guard(_lock);
#ifdef EDOCK_STARTBUTTON_PROBE
            ProbeIncrement(0);
            ProbeIncrement(_subscription.load()==1?1:2);
            if(_changing)ProbeIncrement(3);
#endif
            if(_changing)return S_OK;
            if(mutation==1) {
                auto removed=_instances.find(element.Handle);
                if(removed!=_instances.end()) {
                    auto instance=removed->second;
                    instance->Retired=true;
                    if(instance->Thread!=GetCurrentThreadId()) {
                        auto window=_windows.find(instance->Thread);
                        if(window!=_windows.end())PostMessageW(window->second,ApplyMessage,0,0);
                    }
                }
                _nodes.erase(element.Handle);ScheduleApply();return S_OK;
            }
            bool taskbar=element.Type && wcscmp(element.Type,L"Taskbar.TaskbarFrame")==0;
            if(!taskbar && !_nodes.count(relation.Parent))return S_OK;
            Node node{relation.Parent,GetCurrentThreadId(),false};
            if(element.Type && wcscmp(element.Type,L"Taskbar.ExperienceToggleButton")==0)
                node.Start=IsStartButton(element.Handle,element.Name);
            _nodes[element.Handle]=node;
            if(element.Name && element.Type && wcscmp(element.Name,L"Icon")==0
                && (wcscmp(element.Type,L"Microsoft.UI.Xaml.Controls.AnimatedVisualPlayer")==0
                    || wcscmp(element.Type,L"Windows.UI.Xaml.Controls.AnimatedVisualPlayer")==0)) {
                InstanceHandle button=StartAncestor(relation.Parent);
                if(button) {
                    if(_instances.count(element.Handle))return S_OK;
                    auto instance=std::make_shared<Instance>();
                    instance->Icon=element.Handle;instance->Button=button;instance->Parent=relation.Parent;instance->Thread=GetCurrentThreadId();
                    _instances[element.Handle]=instance;
                    HRESULT hr=EnsureWindow();if(FAILED(hr)){if(_shared)InterlockedExchange(&_shared->LastResult,hr);return S_OK;}
                    ScheduleApply();
                }
            }
        } catch(...){if(_shared)InterlockedExchange(&_shared->LastResult,E_FAIL);}
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle,INT,LPCWSTR) override{return S_OK;}
    void OnEvent(const std::shared_ptr<Instance>& instance,LONG generation,INT kind,INT index,IInspectable* args) {
        std::lock_guard guard(_lock);
        if(_shared)InterlockedExchange(&_shared->Reserved[2],kind);
        if(instance->Detached || instance->Generation!=generation || instance->Thread!=GetCurrentThreadId())return;
        if(kind==1 && index>=0 && static_cast<size_t>(index)<instance->Images.size())instance->Images[index]->Ready=true;
        else if(kind==2 && index>=0 && static_cast<size_t>(index)<instance->Images.size()) {
            instance->Images[index]->Error=true;instance->Images[index]->Ready=false;
            Com<Abi::ExceptionArgs> error;
            if(args && _shared && SUCCEEDED(args->QueryInterface(Abi::ExceptionArgsId,error.VoidPut()))) {
                HSTRING message=nullptr;
                if(SUCCEEDED(error->get_ErrorMessage(&message))) {
                    const auto text=WindowsGetStringRawBuffer(message,nullptr);
                    if(text){wcsncpy(_shared->ErrorMessage,text,2047);_shared->ErrorMessage[2047]=0;}
                }
                WindowsDeleteString(message);
            }
        }
        else if(kind==10)instance->Hover=true;
        else if(kind==11){instance->Hover=false;instance->Pressed=false;}
        else if(kind==12)instance->Pressed=true;
        else if(kind==13 || kind==14 || kind==15)instance->Pressed=false;
        else if(kind==16)instance->Active=true;
        else if(kind==17)instance->Active=false;
        LONG ready=0;
        for(UINT i=0;i<instance->Images.size();++i)if(instance->Images[i]->Ready)ready|=1<<i;
        if(_shared)InterlockedExchange(&_shared->Reserved[3],ready);
        if(!instance->Building){UpdateImage(*instance);Apply();}
    }
};
HRESULT Event::Invoke(IInspectable*,IInspectable* args) {
    try{if(auto instance=_instance.lock())_site->OnEvent(instance,_generation,_kind,_index,args);}catch(...){}
    return S_OK;
}
std::mutex SingletonLock;
Site* SingletonSite{};
class ClassFactory final : public IClassFactory {
    std::atomic<ULONG> _refs{1};
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** output) override {
        if(!output)return E_POINTER;*output=nullptr;
        if(!IsEqualIID(id,IID_IUnknown)&&!IsEqualIID(id,IID_IClassFactory))return E_NOINTERFACE;
        *output=static_cast<IClassFactory*>(this);AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++_refs;}
    ULONG STDMETHODCALLTYPE Release() override{ULONG count=--_refs;if(!count)delete this;return count;}
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID id,void** output) override {
        if(outer)return CLASS_E_NOAGGREGATION;std::lock_guard guard(SingletonLock);
        if(!SingletonSite)SingletonSite=new(std::nothrow) Site();
        return SingletonSite?SingletonSite->QueryInterface(id,output):E_OUTOFMEMORY;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override{return S_OK;}
};
}
extern "C" __declspec(dllexport) HRESULT WINAPI DllGetClassObject(REFCLSID clsid,REFIID iid,void** output) {
    if(!IsEqualCLSID(clsid,ClassId))return CLASS_E_CLASSNOTAVAILABLE;
    auto factory=new(std::nothrow) ClassFactory();if(!factory)return E_OUTOFMEMORY;
    HRESULT hr=factory->QueryInterface(iid,output);factory->Release();return hr;
}
extern "C" __declspec(dllexport) HRESULT WINAPI DllCanUnloadNow(){return S_FALSE;}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,void*) {
    if(reason==DLL_PROCESS_ATTACH){Module=module;DisableThreadLibraryCalls(module);}return TRUE;
}
