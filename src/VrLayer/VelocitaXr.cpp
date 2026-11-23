#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <cmath>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <mutex>
#include <vector>

typedef int32_t XrResult;
typedef uint64_t XrVersion;
typedef uint64_t XrSystemId;
typedef int64_t XrTime;
typedef int64_t XrDuration;
typedef uint32_t XrBool32;
typedef uint64_t XrFlags64;
typedef struct XrInstance_T* XrInstance;
typedef struct XrSession_T* XrSession;
typedef struct XrSpace_T* XrSpace;
typedef struct XrSwapchain_T* XrSwapchain;
typedef void (*PFN_xrVoidFunction)(void);
typedef XrResult (*PFN_xrGetInstanceProcAddr)(XrInstance, const char*, PFN_xrVoidFunction*);

constexpr XrResult XR_SUCCESS = 0;
constexpr XrResult XR_ERROR_INITIALIZATION_FAILED = -6;
constexpr XrResult XR_ERROR_FUNCTION_UNSUPPORTED = -7;
constexpr XrResult XR_ERROR_EXTENSION_NOT_PRESENT = -9;

constexpr int XR_TYPE_SYSTEM_PROPERTIES = 5;
constexpr int XR_TYPE_SWAPCHAIN_CREATE_INFO = 9;
constexpr int XR_TYPE_INSTANCE_PROPERTIES = 32;
constexpr int XR_TYPE_COMPOSITION_LAYER_QUAD = 36;
constexpr int XR_TYPE_REFERENCE_SPACE_CREATE_INFO = 37;
constexpr int XR_TYPE_SPACE_LOCATION = 42;
constexpr int XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO = 55;
constexpr int XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO = 56;
constexpr int XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO = 57;
constexpr int XR_TYPE_GRAPHICS_BINDING_D3D11_KHR = 1000027000;
constexpr int XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR = 1000027001;
constexpr int XR_TYPE_COMPOSITION_LAYER_CYLINDER_KHR = 1000017000;
constexpr const char* CylinderExtension = "XR_KHR_composition_layer_cylinder";

constexpr uint32_t XR_LOADER_INTERFACE_STRUCT_LOADER_INFO = 1;
constexpr uint32_t XR_LOADER_INTERFACE_STRUCT_API_LAYER_REQUEST = 2;
constexpr uint32_t XR_LOADER_INTERFACE_STRUCT_API_LAYER_CREATE_INFO = 4;
constexpr uint32_t XR_CURRENT_LOADER_API_LAYER_VERSION = 1;

struct XrBaseHeader { int type; const void* next; };
struct XrVector3f { float x, y, z; };
struct XrQuaternionf { float x, y, z, w; };
struct XrPosef { XrQuaternionf orientation; XrVector3f position; };
struct XrExtent2Df { float width, height; };
struct XrOffset2Di { int32_t x, y; };
struct XrExtent2Di { int32_t width, height; };
struct XrRect2Di { XrOffset2Di offset; XrExtent2Di extent; };

struct XrApplicationInfo { char applicationName[128]; uint32_t applicationVersion; char engineName[128]; uint32_t engineVersion; XrVersion apiVersion; };
struct XrInstanceCreateInfo
{
    int type; const void* next; XrFlags64 createFlags; XrApplicationInfo applicationInfo;
    uint32_t enabledApiLayerCount; const char* const* enabledApiLayerNames;
    uint32_t enabledExtensionCount; const char* const* enabledExtensionNames;
};
struct XrApiLayerNextInfo;
typedef XrResult (*PFN_xrCreateApiLayerInstance)(const XrInstanceCreateInfo*, const struct XrApiLayerCreateInfo*, XrInstance*);

struct XrApiLayerNextInfo
{
    uint32_t structType;
    uint32_t structVersion;
    size_t structSize;
    char layerName[256];
    PFN_xrGetInstanceProcAddr nextGetInstanceProcAddr;
    PFN_xrCreateApiLayerInstance nextCreateApiLayerInstance;
    XrApiLayerNextInfo* next;
};

struct XrApiLayerCreateInfo
{
    uint32_t structType;
    uint32_t structVersion;
    size_t structSize;
    void* loaderInstance;
    char settings_file_location[512];
    XrApiLayerNextInfo* nextInfo;
};

struct XrNegotiateLoaderInfo
{
    uint32_t structType;
    uint32_t structVersion;
    size_t structSize;
    uint32_t minInterfaceVersion;
    uint32_t maxInterfaceVersion;
    XrVersion minApiVersion;
    XrVersion maxApiVersion;
};

struct XrNegotiateApiLayerRequest
{
    uint32_t structType;
    uint32_t structVersion;
    size_t structSize;
    uint32_t layerInterfaceVersion;
    XrVersion layerApiVersion;
    PFN_xrGetInstanceProcAddr getInstanceProcAddr;
    PFN_xrCreateApiLayerInstance createApiLayerInstance;
};

struct XrInstanceProperties { int type; void* next; XrVersion runtimeVersion; char runtimeName[128]; };
struct XrSystemProperties
{
    int type; void* next; XrSystemId systemId; uint32_t vendorId; char systemName[256];
    uint32_t maxSwapchainImageHeight, maxSwapchainImageWidth, maxLayerCount;
    XrBool32 orientationTracking, positionTracking;
};
struct XrSessionCreateInfo { int type; const void* next; XrFlags64 createFlags; XrSystemId systemId; };
struct XrGraphicsBindingD3D11KHR { int type; const void* next; ID3D11Device* device; };
struct XrSwapchainCreateInfo
{
    int type; const void* next; XrFlags64 createFlags; XrFlags64 usageFlags; int64_t format;
    uint32_t sampleCount, width, height, faceCount, arraySize, mipCount;
};
struct XrSwapchainImageD3D11KHR { int type; void* next; ID3D11Texture2D* texture; };
struct XrSwapchainImageAcquireInfo { int type; const void* next; };
struct XrSwapchainImageWaitInfo { int type; const void* next; XrDuration timeout; };
struct XrSwapchainImageReleaseInfo { int type; const void* next; };
struct XrReferenceSpaceCreateInfo { int type; const void* next; int referenceSpaceType; XrPosef poseInReferenceSpace; };
struct XrSpaceLocation { int type; void* next; XrFlags64 locationFlags; XrPosef pose; };
struct XrSwapchainSubImage { XrSwapchain swapchain; XrRect2Di imageRect; uint32_t imageArrayIndex; };
struct XrCompositionLayerQuad
{
    int type; const void* next; XrFlags64 layerFlags; XrSpace space; int eyeVisibility;
    XrSwapchainSubImage subImage; XrPosef pose; XrExtent2Df size;
};
struct XrCompositionLayerCylinderKHR
{
    int type; const void* next; XrFlags64 layerFlags; XrSpace space; int eyeVisibility;
    XrSwapchainSubImage subImage; XrPosef pose; float radius, centralAngle, aspectRatio;
};
struct XrFrameEndInfo
{
    int type; const void* next; XrTime displayTime; int environmentBlendMode;
    uint32_t layerCount; const XrBaseHeader* const* layers;
};

typedef XrResult (*PFN_xrDestroyInstance)(XrInstance);
typedef XrResult (*PFN_xrGetInstanceProperties)(XrInstance, XrInstanceProperties*);
typedef XrResult (*PFN_xrGetSystemProperties)(XrInstance, XrSystemId, XrSystemProperties*);
typedef XrResult (*PFN_xrCreateSession)(XrInstance, const XrSessionCreateInfo*, XrSession*);
typedef XrResult (*PFN_xrDestroySession)(XrSession);
typedef XrResult (*PFN_xrEndFrame)(XrSession, const XrFrameEndInfo*);
typedef XrResult (*PFN_xrEnumerateSwapchainFormats)(XrSession, uint32_t, uint32_t*, int64_t*);
typedef XrResult (*PFN_xrCreateSwapchain)(XrSession, const XrSwapchainCreateInfo*, XrSwapchain*);
typedef XrResult (*PFN_xrDestroySwapchain)(XrSwapchain);
typedef XrResult (*PFN_xrEnumerateSwapchainImages)(XrSwapchain, uint32_t, uint32_t*, XrBaseHeader*);
typedef XrResult (*PFN_xrAcquireSwapchainImage)(XrSwapchain, const XrSwapchainImageAcquireInfo*, uint32_t*);
typedef XrResult (*PFN_xrWaitSwapchainImage)(XrSwapchain, const XrSwapchainImageWaitInfo*);
typedef XrResult (*PFN_xrReleaseSwapchainImage)(XrSwapchain, const XrSwapchainImageReleaseInfo*);
typedef XrResult (*PFN_xrCreateReferenceSpace)(XrSession, const XrReferenceSpaceCreateInfo*, XrSpace*);
typedef XrResult (*PFN_xrDestroySpace)(XrSpace);
typedef XrResult (*PFN_xrLocateSpace)(XrSpace, XrSpace, XrTime, XrSpaceLocation*);

namespace
{
    constexpr uint32_t Magic = 0x31525856;
    constexpr uint32_t Version = 1;
    constexpr uint32_t MaxSlots = 16;
    constexpr uint32_t HeaderBytes = 4096;
    constexpr uint32_t SlotPixelBytes = 2 * 1024 * 1024;
    constexpr uint64_t StaleMs = 2000;
    constexpr int64_t FormatBgraSrgb = 91, FormatBgra = 87, FormatRgbaSrgb = 29, FormatRgba = 28;

    struct SharedHeader
    {
        uint32_t magic;
        uint32_t version;
        uint64_t writerTick;
        uint64_t readerTick;
        uint32_t count;
        uint32_t recenter;
    };

    struct SharedSlot
    {
        volatile uint64_t seq;
        uint32_t id;
        uint32_t mode;
        float x, y, distance, width;
        uint32_t w, h;
        XrQuaternionf rotation;
        float curveRadius;
        uint32_t pad;
    };

    constexpr uint32_t ReportAt = 2048;

    struct LayerReport
    {
        uint64_t tick;
        int32_t endFrameResult;
        uint32_t widgetsSent;
        uint32_t framesWithWidgets;
        uint32_t framesRejected;
        uint32_t maxLayers;
        uint32_t flags;
        int64_t format;
        int32_t swapchainResult;
        uint32_t pad;
        char runtime[128];
    };

    static_assert(sizeof(SharedHeader) == 32, "header");
    static_assert(sizeof(SharedSlot) == 64, "slot");
    static_assert(sizeof(LayerReport) == 176, "report");
    static_assert(ReportAt >= sizeof(SharedHeader) + MaxSlots * sizeof(SharedSlot) && ReportAt + sizeof(LayerReport) <= HeaderBytes, "report place");

    struct Quad
    {
        uint32_t id = 0;
        XrSwapchain swapchain = nullptr;
        std::vector<ID3D11Texture2D*> images;
        uint32_t w = 0, h = 0;
        uint64_t shownSeq = 0;
        bool ready = false;
    };

    struct Next
    {
        PFN_xrGetInstanceProcAddr getInstanceProcAddr = nullptr;
        PFN_xrDestroyInstance destroyInstance = nullptr;
        PFN_xrGetInstanceProperties getInstanceProperties = nullptr;
        PFN_xrGetSystemProperties getSystemProperties = nullptr;
        PFN_xrCreateSession createSession = nullptr;
        PFN_xrDestroySession destroySession = nullptr;
        PFN_xrEndFrame endFrame = nullptr;
        PFN_xrEnumerateSwapchainFormats enumerateSwapchainFormats = nullptr;
        PFN_xrCreateSwapchain createSwapchain = nullptr;
        PFN_xrDestroySwapchain destroySwapchain = nullptr;
        PFN_xrEnumerateSwapchainImages enumerateSwapchainImages = nullptr;
        PFN_xrAcquireSwapchainImage acquireSwapchainImage = nullptr;
        PFN_xrWaitSwapchainImage waitSwapchainImage = nullptr;
        PFN_xrReleaseSwapchainImage releaseSwapchainImage = nullptr;
        PFN_xrCreateReferenceSpace createReferenceSpace = nullptr;
        PFN_xrDestroySpace destroySpace = nullptr;
        PFN_xrLocateSpace locateSpace = nullptr;
    };

    std::mutex g_lock;
    Next g_next;
    XrInstance g_instance = nullptr;
    bool g_active = false;
    bool g_faulted = false;
    XrSession g_session = nullptr;
    ID3D11Device* g_device = nullptr;
    ID3D11DeviceContext* g_context = nullptr;
    XrSpace g_local = nullptr, g_view = nullptr, g_anchored = nullptr;
    uint32_t g_recenterSeen = 0;
    bool g_recenterKnown = false;
    int64_t g_format = 0;
    bool g_swizzle = false;
    uint32_t g_maxLayers = 16;
    Quad g_quads[MaxSlots];
    HANDLE g_mapping = nullptr;
    uint8_t* g_shared = nullptr;
    ULONGLONG g_lastOpenTry = 0;
    std::vector<const XrBaseHeader*> g_layers;
    XrCompositionLayerQuad g_quadLayers[MaxSlots];
    XrCompositionLayerCylinderKHR g_cylinderLayers[MaxSlots];
    const XrBaseHeader* g_built[MaxSlots];
    bool g_cylinder = false;
    std::vector<uint8_t> g_scratch;
    char g_runtimeName[128] = {};
    bool g_rejected = false;
    int32_t g_lastEndFrame = 0;
    int32_t g_lastSwapchain = 0;
    uint32_t g_lastSent = 0;
    uint32_t g_framesWithWidgets = 0;
    uint32_t g_framesRejected = 0;

    void WriteReport()
    {
        if (!g_shared) return;
        auto report = reinterpret_cast<LayerReport*>(g_shared + ReportAt);
        report->endFrameResult = g_lastEndFrame;
        report->widgetsSent = g_lastSent;
        report->framesWithWidgets = g_framesWithWidgets;
        report->framesRejected = g_framesRejected;
        report->maxLayers = g_maxLayers;
        report->flags = (g_cylinder ? 1u : 0u) | (g_rejected ? 2u : 0u) | (g_device ? 4u : 0u) | (g_format ? 8u : 0u);
        report->format = g_format;
        report->swapchainResult = g_lastSwapchain;
        memcpy(report->runtime, g_runtimeName, sizeof(report->runtime));
        MemoryBarrier();
        report->tick = GetTickCount64();
    }

    constexpr LONGLONG MaxLogBytes = 64 * 1024;
    bool g_logStarted = false;
    int g_sharedState = -1;
    bool g_firstFrameLogged = false;
    bool g_firstWidgetsLogged = false;
    bool g_firstRejectLogged = false;

    void Log(const char* format, ...)
    {
        wchar_t folder[MAX_PATH] = {};
        DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", folder, MAX_PATH);
        if (length == 0 || length >= MAX_PATH - 40) return;
        wchar_t path[MAX_PATH] = {};
        swprintf_s(path, L"%s\\Velocita", folder);
        CreateDirectoryW(path, nullptr);
        wcscat_s(path, L"\\OpenXR");
        CreateDirectoryW(path, nullptr);
        wcscat_s(path, L"\\layer.log");
        DWORD disposition = OPEN_ALWAYS;
        if (!g_logStarted)
        {
            WIN32_FILE_ATTRIBUTE_DATA info{};
            if (GetFileAttributesExW(path, GetFileExInfoStandard, &info)
                && ((LONGLONG(info.nFileSizeHigh) << 32) | info.nFileSizeLow) > MaxLogBytes)
                disposition = CREATE_ALWAYS;
            g_logStarted = true;
        }
        HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, disposition, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        char line[768] = {};
        SYSTEMTIME now{};
        GetLocalTime(&now);
        int at = sprintf_s(line, "%04u-%02u-%02u %02u:%02u:%02u [%lu] ", now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, GetCurrentProcessId());
        if (at < 0) at = 0;
        va_list args;
        va_start(args, format);
        vsnprintf(line + at, sizeof(line) - size_t(at) - 3, format, args);
        va_end(args);
        size_t used = strnlen(line, sizeof(line) - 3);
        line[used++] = '\r';
        line[used++] = '\n';
        DWORD written = 0;
        WriteFile(file, line, DWORD(used), &written, nullptr);
        CloseHandle(file);
    }

    HANDLE g_processClaim = nullptr;

    bool ClaimProcess()
    {
        if (g_processClaim) return true;
        wchar_t name[64] = {};
        swprintf_s(name, L"Local\\VelocitaXrActive.%lu", GetCurrentProcessId());
        HANDLE claim = CreateMutexW(nullptr, FALSE, name);
        if (!claim) return false;
        if (GetLastError() == ERROR_ALREADY_EXISTS)
        {
            CloseHandle(claim);
            return false;
        }
        g_processClaim = claim;
        return true;
    }

    bool IsTargetProcess()
    {
        wchar_t path[MAX_PATH] = {};
        GetModuleFileNameW(nullptr, path, MAX_PATH);
        const wchar_t* name = wcsrchr(path, L'\\');
        name = name ? name + 1 : path;
        return _wcsnicmp(name, L"iRacingSim", 10) == 0;
    }

    template <typename T>
    void Load(const char* name, T& fn)
    {
        PFN_xrVoidFunction f = nullptr;
        if (g_next.getInstanceProcAddr(g_instance, name, &f) == XR_SUCCESS) fn = reinterpret_cast<T>(f);
    }

    bool OpenShared()
    {
        if (g_shared) return true;
        ULONGLONG now = GetTickCount64();
        if (now - g_lastOpenTry < 1000) return false;
        g_lastOpenTry = now;
        g_mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, L"Local\\VelocitaVr");
        if (!g_mapping)
        {
            if (g_sharedState != 0) Log("Velocita's widget memory not found (error %lu): Velocita isn't running with VR widgets on, or runs as another user", GetLastError());
            g_sharedState = 0;
            return false;
        }
        g_shared = static_cast<uint8_t*>(MapViewOfFile(g_mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, 0));
        if (!g_shared)
        {
            CloseHandle(g_mapping);
            g_mapping = nullptr;
            return false;
        }
        MEMORY_BASIC_INFORMATION region{};
        bool whole = VirtualQuery(g_shared, &region, sizeof(region)) == sizeof(region)
            && region.RegionSize >= size_t(HeaderBytes) + size_t(MaxSlots) * SlotPixelBytes;
        auto header = reinterpret_cast<SharedHeader*>(g_shared);
        if (!whole || header->magic != Magic || header->version != Version)
        {
            if (g_sharedState != 2) Log("Velocita's widget memory doesn't match this layer (magic %08x, version %u, whole %d)", header->magic, header->version, whole ? 1 : 0);
            g_sharedState = 2;
            UnmapViewOfFile(g_shared);
            CloseHandle(g_mapping);
            g_shared = nullptr;
            g_mapping = nullptr;
            return false;
        }
        if (g_sharedState != 1) Log("Connected to Velocita's widget memory");
        g_sharedState = 1;
        return true;
    }

    void CloseShared()
    {
        if (g_shared) UnmapViewOfFile(g_shared);
        if (g_mapping) CloseHandle(g_mapping);
        g_shared = nullptr;
        g_mapping = nullptr;
        g_recenterKnown = false;
    }

    void DropQuad(Quad& q)
    {
        if (q.swapchain && g_next.destroySwapchain) g_next.destroySwapchain(q.swapchain);
        q = Quad{};
    }

    void DropSession()
    {
        for (auto& q : g_quads) DropQuad(q);
        if (g_local && g_next.destroySpace) g_next.destroySpace(g_local);
        if (g_view && g_next.destroySpace) g_next.destroySpace(g_view);
        if (g_anchored && g_next.destroySpace) g_next.destroySpace(g_anchored);
        g_local = g_view = g_anchored = nullptr;
        if (g_context) g_context->Release();
        if (g_device) g_device->Release();
        g_context = nullptr;
        g_device = nullptr;
        g_session = nullptr;
        g_format = 0;
    }

    bool MakeQuad(Quad& q, uint32_t id, uint32_t w, uint32_t h)
    {
        DropQuad(q);
        XrSwapchainCreateInfo info{};
        info.type = XR_TYPE_SWAPCHAIN_CREATE_INFO;
        info.usageFlags = 0x1 | 0x10 | 0x20;
        info.format = g_format;
        info.sampleCount = 1;
        info.width = w;
        info.height = h;
        info.faceCount = 1;
        info.arraySize = 1;
        info.mipCount = 1;
        XrResult created = g_next.createSwapchain(g_session, &info, &q.swapchain);
        g_lastSwapchain = created;
        if (created != XR_SUCCESS) { q.swapchain = nullptr; return false; }
        uint32_t count = 0;
        if (g_next.enumerateSwapchainImages(q.swapchain, 0, &count, nullptr) != XR_SUCCESS || count == 0) { DropQuad(q); return false; }
        std::vector<XrSwapchainImageD3D11KHR> images(count);
        for (auto& i : images) { i.type = XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR; i.next = nullptr; i.texture = nullptr; }
        if (g_next.enumerateSwapchainImages(q.swapchain, count, &count, reinterpret_cast<XrBaseHeader*>(images.data())) != XR_SUCCESS) { DropQuad(q); return false; }
        for (auto& i : images) q.images.push_back(i.texture);
        q.id = id;
        q.w = w;
        q.h = h;
        return true;
    }

    bool Upload(Quad& q, const SharedSlot& slot, const uint8_t* pixels)
    {
        uint32_t index = 0;
        XrSwapchainImageAcquireInfo acquire{ XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO, nullptr };
        if (g_next.acquireSwapchainImage(q.swapchain, &acquire, &index) != XR_SUCCESS) return false;
        XrSwapchainImageWaitInfo wait{ XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO, nullptr, 100000000 };
        bool ok = g_next.waitSwapchainImage(q.swapchain, &wait) == XR_SUCCESS;
        if (ok && index < q.images.size())
        {
            const uint8_t* source = pixels;
            size_t bytes = size_t(slot.w) * slot.h * 4;
            if (g_swizzle)
            {
                g_scratch.resize(bytes);
                for (size_t i = 0; i < bytes; i += 4)
                {
                    g_scratch[i] = pixels[i + 2];
                    g_scratch[i + 1] = pixels[i + 1];
                    g_scratch[i + 2] = pixels[i];
                    g_scratch[i + 3] = pixels[i + 3];
                }
                source = g_scratch.data();
            }
            D3D11_BOX box{ 0, 0, 0, slot.w, slot.h, 1 };
            g_context->UpdateSubresource(q.images[index], 0, &box, source, slot.w * 4, 0);
        }
        XrSwapchainImageReleaseInfo release{ XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO, nullptr };
        g_next.releaseSwapchainImage(q.swapchain, &release);
        return ok;
    }

    void Recenter(XrTime time)
    {
        if (!g_next.locateSpace || !g_local || !g_view) return;
        XrSpaceLocation head{};
        head.type = XR_TYPE_SPACE_LOCATION;
        if (g_next.locateSpace(g_view, g_local, time, &head) != XR_SUCCESS || (head.locationFlags & 0x3) != 0x3) return;
        const XrQuaternionf& q = head.pose.orientation;
        float forwardX = -2 * (q.x * q.z + q.w * q.y);
        float forwardZ = -(1 - 2 * (q.x * q.x + q.y * q.y));
        float yaw = std::fabs(forwardX) + std::fabs(forwardZ) < 1e-6f ? 0.0f : std::atan2(-forwardX, -forwardZ);
        XrReferenceSpaceCreateInfo space{};
        space.type = XR_TYPE_REFERENCE_SPACE_CREATE_INFO;
        space.referenceSpaceType = 2;
        space.poseInReferenceSpace.orientation = { 0, std::sin(yaw / 2), 0, std::cos(yaw / 2) };
        space.poseInReferenceSpace.position = head.pose.position;
        XrSpace anchored = nullptr;
        if (g_next.createReferenceSpace(g_session, &space, &anchored) != XR_SUCCESS) return;
        if (g_anchored) g_next.destroySpace(g_anchored);
        g_anchored = anchored;
    }

    uint32_t BuildQuads(XrTime time)
    {
        if (!g_session || !g_device || !g_format || !OpenShared()) return 0;
        auto header = reinterpret_cast<SharedHeader*>(g_shared);
        uint32_t recenter = header->recenter;
        if (!g_recenterKnown)
        {
            g_recenterSeen = recenter;
            g_recenterKnown = true;
        }
        else if (recenter != g_recenterSeen)
        {
            g_recenterSeen = recenter;
            Recenter(time);
        }
        ULONGLONG now = GetTickCount64();
        header->readerTick = now;
        bool stale = now - header->writerTick > StaleMs;
        uint32_t count = stale ? 0 : (header->count < MaxSlots ? header->count : MaxSlots);
        uint32_t built = 0;
        for (uint32_t i = 0; i < MaxSlots; i++)
        {
            Quad& q = g_quads[i];
            if (i >= count) continue;
            auto slot = reinterpret_cast<SharedSlot*>(g_shared + sizeof(SharedHeader) + i * sizeof(SharedSlot));
            uint64_t seq = slot->seq;
            if (seq & 1) { if (!q.ready || q.id != slot->id) continue; }
            else
            {
                SharedSlot copy = *slot;
                if (copy.mode == 0 || copy.w == 0 || copy.h == 0 || size_t(copy.w) * copy.h * 4 > SlotPixelBytes) continue;
                if (!q.swapchain || q.w != copy.w || q.h != copy.h || q.id != copy.id)
                    if (!MakeQuad(q, copy.id, copy.w, copy.h)) continue;
                if (q.shownSeq != seq)
                {
                    const uint8_t* pixels = g_shared + HeaderBytes + size_t(i) * SlotPixelBytes;
                    if (Upload(q, copy, pixels) && slot->seq == seq)
                    {
                        q.shownSeq = seq;
                        q.ready = true;
                    }
                    else q.shownSeq = 0;
                }
            }
            if (!q.ready) continue;
            SharedSlot s = *slot;
            if (s.mode == 0 || s.w != q.w || s.h != q.h) continue;
            XrSpace space = s.mode == 2 ? g_view : g_anchored ? g_anchored : g_local;
            if (!space) continue;
            XrSwapchainSubImage sub{};
            sub.swapchain = q.swapchain;
            sub.imageRect = { { 0, 0 }, { int32_t(q.w), int32_t(q.h) } };
            float turned = s.rotation.x * s.rotation.x + s.rotation.y * s.rotation.y + s.rotation.z * s.rotation.z + s.rotation.w * s.rotation.w;
            XrQuaternionf r = turned > 0.5f && turned < 1.5f ? s.rotation : XrQuaternionf{ 0, 0, 0, 1 };
            XrVector3f at = { s.x, s.y, -s.distance };
            if (g_cylinder && s.curveRadius > 0.01f && s.width > 0)
            {
                XrCompositionLayerCylinderKHR& layer = g_cylinderLayers[built];
                layer = {};
                layer.type = XR_TYPE_COMPOSITION_LAYER_CYLINDER_KHR;
                layer.layerFlags = 0x2;
                layer.space = space;
                layer.subImage = sub;
                layer.pose.orientation = r;
                float k = s.curveRadius;
                layer.pose.position = {
                    at.x + 2 * (r.x * r.z + r.y * r.w) * k,
                    at.y + 2 * (r.y * r.z - r.x * r.w) * k,
                    at.z + (1 - 2 * (r.x * r.x + r.y * r.y)) * k };
                layer.radius = k;
                layer.centralAngle = s.width / k;
                layer.aspectRatio = float(q.w) / float(q.h);
                g_built[built] = reinterpret_cast<const XrBaseHeader*>(&layer);
            }
            else
            {
                XrCompositionLayerQuad& layer = g_quadLayers[built];
                layer = {};
                layer.type = XR_TYPE_COMPOSITION_LAYER_QUAD;
                layer.layerFlags = 0x2;
                layer.space = space;
                layer.subImage = sub;
                layer.pose.orientation = r;
                layer.pose.position = at;
                layer.size = { s.width, s.width * float(q.h) / float(q.w) };
                g_built[built] = reinterpret_cast<const XrBaseHeader*>(&layer);
            }
            built++;
        }
        return built;
    }

    XrResult Layer_xrEndFrame(XrSession session, const XrFrameEndInfo* info)
    {
        std::lock_guard<std::mutex> guard(g_lock);
        if (g_faulted || session != g_session || !info) return g_next.endFrame(session, info);
        if (!g_firstFrameLogged)
        {
            g_firstFrameLogged = true;
            Log("First frame from iRacing: %u game layers, room for %u", info->layerCount, g_maxLayers);
        }
        OpenShared();
        uint32_t quads = 0;
        try { quads = BuildQuads(info->displayTime); }
        catch (...)
        {
            g_faulted = true;
            quads = 0;
        }
        uint32_t room = g_maxLayers > info->layerCount ? g_maxLayers - info->layerCount : 0;
        if (quads > room) quads = room;
        if (g_rejected) quads = 0;
        g_lastSent = quads;
        if (quads == 0)
        {
            WriteReport();
            return g_next.endFrame(session, info);
        }
        g_layers.assign(info->layers, info->layers + info->layerCount);
        for (uint32_t i = 0; i < quads; i++) g_layers.push_back(g_built[i]);
        XrFrameEndInfo copy = *info;
        copy.layerCount = uint32_t(g_layers.size());
        copy.layers = g_layers.data();
        XrResult result = g_next.endFrame(session, &copy);
        g_lastEndFrame = result;
        g_framesWithWidgets++;
        if (!g_firstWidgetsLogged)
        {
            g_firstWidgetsLogged = true;
            Log("First frame with %u widgets: result %d", quads, result);
        }
        if (result < 0)
        {
            g_framesRejected++;
            if (!g_firstRejectLogged)
            {
                g_firstRejectLogged = true;
                Log("The headset rejected a frame with widgets (result %d); widgets stop if a frame without them works", result);
            }
            XrResult plain = g_next.endFrame(session, info);
            if (plain >= 0) g_rejected = true;
            result = plain;
        }
        WriteReport();
        return result;
    }

    void PickFormat()
    {
        uint32_t count = 0;
        if (g_next.enumerateSwapchainFormats(g_session, 0, &count, nullptr) != XR_SUCCESS || count == 0) return;
        std::vector<int64_t> formats(count);
        if (g_next.enumerateSwapchainFormats(g_session, count, &count, formats.data()) != XR_SUCCESS) return;
        char offered[256] = {};
        size_t at = 0;
        for (uint32_t i = 0; i < count && at < sizeof(offered) - 24; i++)
        {
            int n = sprintf_s(offered + at, sizeof(offered) - at, i == 0 ? "%lld" : " %lld", static_cast<long long>(formats[i]));
            if (n > 0) at += size_t(n);
        }
        Log("Picture formats offered by the headset: %s", offered);
        const int64_t wanted[] = { FormatBgraSrgb, FormatRgbaSrgb, FormatBgra, FormatRgba };
        for (int64_t w : wanted)
            for (int64_t f : formats)
                if (f == w)
                {
                    g_format = f;
                    g_swizzle = f == FormatRgbaSrgb || f == FormatRgba;
                    return;
                }
    }

    void SetUpSession(XrInstance instance, const XrSessionCreateInfo* info, XrSession session, ID3D11Device* device);

    XrResult Layer_xrCreateSession(XrInstance instance, const XrSessionCreateInfo* info, XrSession* session)
    {
        XrResult result = g_next.createSession(instance, info, session);
        if (result != XR_SUCCESS || !info) return result;
        std::lock_guard<std::mutex> guard(g_lock);
        ID3D11Device* device = nullptr;
        for (auto p = static_cast<const XrBaseHeader*>(info->next); p; p = static_cast<const XrBaseHeader*>(p->next))
            if (p->type == XR_TYPE_GRAPHICS_BINDING_D3D11_KHR) { device = reinterpret_cast<const XrGraphicsBindingD3D11KHR*>(p)->device; break; }
        if (!device)
        {
            Log("VR session started without Direct3D 11, so no widgets can be drawn");
            return result;
        }
        if (g_faulted)
        {
            Log("VR session started, but the layer stopped after an earlier fault");
            return result;
        }
        try { SetUpSession(instance, info, *session, device); }
        catch (...) { g_faulted = true; }
        g_firstFrameLogged = g_firstWidgetsLogged = g_firstRejectLogged = false;
        Log("VR session started: Direct3D 11, picture format %lld%s, layer limit %u", static_cast<long long>(g_format), g_format ? "" : " (none usable, so no widgets)", g_maxLayers);
        return result;
    }

    void SetUpSession(XrInstance instance, const XrSessionCreateInfo* info, XrSession session, ID3D11Device* device)
    {
        DropSession();
        g_rejected = false;
        g_lastEndFrame = 0;
        g_lastSwapchain = 0;
        g_lastSent = 0;
        g_framesWithWidgets = 0;
        g_framesRejected = 0;
        g_session = session;
        g_device = device;
        g_device->AddRef();
        g_device->GetImmediateContext(&g_context);
        XrSystemProperties props{};
        props.type = XR_TYPE_SYSTEM_PROPERTIES;
        if (g_next.getSystemProperties && g_next.getSystemProperties(instance, info->systemId, &props) == XR_SUCCESS && props.maxLayerCount > 0)
            g_maxLayers = props.maxLayerCount;
        XrReferenceSpaceCreateInfo space{};
        space.type = XR_TYPE_REFERENCE_SPACE_CREATE_INFO;
        space.poseInReferenceSpace.orientation = { 0, 0, 0, 1 };
        space.referenceSpaceType = 2;
        if (g_next.createReferenceSpace(g_session, &space, &g_local) != XR_SUCCESS) g_local = nullptr;
        space.referenceSpaceType = 1;
        if (g_next.createReferenceSpace(g_session, &space, &g_view) != XR_SUCCESS) g_view = nullptr;
        PickFormat();
    }

    XrResult Layer_xrDestroySession(XrSession session)
    {
        {
            std::lock_guard<std::mutex> guard(g_lock);
            if (session == g_session)
            {
                try { DropSession(); }
                catch (...) { g_faulted = true; g_session = nullptr; }
            }
        }
        return g_next.destroySession(session);
    }

    XrResult Layer_xrDestroyInstance(XrInstance instance)
    {
        PFN_xrDestroyInstance destroy = g_next.destroyInstance;
        {
            std::lock_guard<std::mutex> guard(g_lock);
            if (instance == g_instance)
            {
                DropSession();
                CloseShared();
                g_active = false;
                g_instance = nullptr;
            }
        }
        return destroy ? destroy(instance) : XR_ERROR_FUNCTION_UNSUPPORTED;
    }

    XrResult Layer_xrGetInstanceProcAddr(XrInstance instance, const char* name, PFN_xrVoidFunction* function)
    {
        if (!g_next.getInstanceProcAddr) return XR_ERROR_FUNCTION_UNSUPPORTED;
        XrResult result = g_next.getInstanceProcAddr(instance, name, function);
        if (result != XR_SUCCESS || !g_active || !name) return result;
        if (strcmp(name, "xrGetInstanceProcAddr") == 0) *function = reinterpret_cast<PFN_xrVoidFunction>(&Layer_xrGetInstanceProcAddr);
        else if (strcmp(name, "xrEndFrame") == 0) *function = reinterpret_cast<PFN_xrVoidFunction>(&Layer_xrEndFrame);
        else if (strcmp(name, "xrCreateSession") == 0) *function = reinterpret_cast<PFN_xrVoidFunction>(&Layer_xrCreateSession);
        else if (strcmp(name, "xrDestroySession") == 0) *function = reinterpret_cast<PFN_xrVoidFunction>(&Layer_xrDestroySession);
        else if (strcmp(name, "xrDestroyInstance") == 0) *function = reinterpret_cast<PFN_xrVoidFunction>(&Layer_xrDestroyInstance);
        return result;
    }

    XrResult Layer_xrCreateApiLayerInstance(const XrInstanceCreateInfo* info, const XrApiLayerCreateInfo* layerInfo, XrInstance* instance)
    {
        if (!layerInfo || !layerInfo->nextInfo || layerInfo->structType != XR_LOADER_INTERFACE_STRUCT_API_LAYER_CREATE_INFO)
            return XR_ERROR_INITIALIZATION_FAILED;
        XrApiLayerCreateInfo nextInfo = *layerInfo;
        nextInfo.nextInfo = layerInfo->nextInfo->next;
        bool cylinder = false;
        XrResult result = XR_ERROR_EXTENSION_NOT_PRESENT;
        if (info && IsTargetProcess())
        {
            bool asked = false;
            for (uint32_t i = 0; i < info->enabledExtensionCount; i++)
                if (info->enabledExtensionNames[i] && strcmp(info->enabledExtensionNames[i], CylinderExtension) == 0) asked = true;
            if (asked) cylinder = true;
            else
            {
                std::vector<const char*> names(info->enabledExtensionNames, info->enabledExtensionNames + info->enabledExtensionCount);
                names.push_back(CylinderExtension);
                XrInstanceCreateInfo withCylinder = *info;
                withCylinder.enabledExtensionCount = uint32_t(names.size());
                withCylinder.enabledExtensionNames = names.data();
                result = layerInfo->nextInfo->nextCreateApiLayerInstance(&withCylinder, &nextInfo, instance);
                cylinder = result == XR_SUCCESS;
            }
        }
        if (result != XR_SUCCESS)
        {
            result = layerInfo->nextInfo->nextCreateApiLayerInstance(info, &nextInfo, instance);
            if (result != XR_SUCCESS)
            {
                if (IsTargetProcess()) Log("iRacing's OpenXR start failed further down the chain (result %d)", result);
                return result;
            }
        }
        std::lock_guard<std::mutex> guard(g_lock);
        g_next = Next{};
        g_next.getInstanceProcAddr = layerInfo->nextInfo->nextGetInstanceProcAddr;
        g_instance = *instance;
        g_active = false;
        g_cylinder = cylinder;
        Load("xrDestroyInstance", g_next.destroyInstance);
        Load("xrGetInstanceProperties", g_next.getInstanceProperties);
        Load("xrGetSystemProperties", g_next.getSystemProperties);
        Load("xrCreateSession", g_next.createSession);
        Load("xrDestroySession", g_next.destroySession);
        Load("xrEndFrame", g_next.endFrame);
        Load("xrEnumerateSwapchainFormats", g_next.enumerateSwapchainFormats);
        Load("xrCreateSwapchain", g_next.createSwapchain);
        Load("xrDestroySwapchain", g_next.destroySwapchain);
        Load("xrEnumerateSwapchainImages", g_next.enumerateSwapchainImages);
        Load("xrAcquireSwapchainImage", g_next.acquireSwapchainImage);
        Load("xrWaitSwapchainImage", g_next.waitSwapchainImage);
        Load("xrReleaseSwapchainImage", g_next.releaseSwapchainImage);
        Load("xrCreateReferenceSpace", g_next.createReferenceSpace);
        Load("xrDestroySpace", g_next.destroySpace);
        Load("xrLocateSpace", g_next.locateSpace);
        bool complete = g_next.destroyInstance && g_next.getInstanceProperties && g_next.createSession && g_next.destroySession && g_next.endFrame
            && g_next.enumerateSwapchainFormats && g_next.createSwapchain && g_next.destroySwapchain && g_next.enumerateSwapchainImages
            && g_next.acquireSwapchainImage && g_next.waitSwapchainImage && g_next.releaseSwapchainImage && g_next.createReferenceSpace && g_next.destroySpace;
        if (!IsTargetProcess()) return result;
        if (!complete)
        {
            Log("Loaded in iRacing, but the runtime is missing functions the layer needs; staying out");
            return result;
        }
        XrInstanceProperties props{};
        props.type = XR_TYPE_INSTANCE_PROPERTIES;
        if (g_next.getInstanceProperties(g_instance, &props) != XR_SUCCESS)
        {
            Log("Loaded in iRacing, but the runtime didn't give its name; staying out");
            return result;
        }
        props.runtimeName[sizeof(props.runtimeName) - 1] = 0;
        memcpy(g_runtimeName, props.runtimeName, sizeof(g_runtimeName));
        if (strstr(props.runtimeName, "SteamVR") != nullptr)
        {
            Log("Loaded in iRacing on runtime \"%s\": SteamVR shows Velocita's widgets itself, so the layer stays out", props.runtimeName);
            return result;
        }
        if (!ClaimProcess())
        {
            Log("Loaded in iRacing on runtime \"%s\": another copy of the layer is already active", props.runtimeName);
            return result;
        }
        g_active = true;
        OpenShared();
        Log("Loaded in iRacing on runtime \"%s\": active, curved widgets %s", props.runtimeName, cylinder ? "on" : "off");
        return result;
    }
}

extern "C" __declspec(dllexport) XrResult xrNegotiateLoaderApiLayerInterface(const XrNegotiateLoaderInfo* loaderInfo, const char*, XrNegotiateApiLayerRequest* request)
{
    if (!loaderInfo || !request || loaderInfo->structType != XR_LOADER_INTERFACE_STRUCT_LOADER_INFO
        || request->structType != XR_LOADER_INTERFACE_STRUCT_API_LAYER_REQUEST
        || loaderInfo->minInterfaceVersion > XR_CURRENT_LOADER_API_LAYER_VERSION || loaderInfo->maxInterfaceVersion < XR_CURRENT_LOADER_API_LAYER_VERSION)
        return XR_ERROR_INITIALIZATION_FAILED;
    if (IsTargetProcess()) Log("Layer loaded by the OpenXR loader in iRacing");
    request->layerInterfaceVersion = XR_CURRENT_LOADER_API_LAYER_VERSION;
    request->layerApiVersion = loaderInfo->maxApiVersion < ((uint64_t(1) << 48) | (uint64_t(1) << 32)) ? loaderInfo->maxApiVersion : ((uint64_t(1) << 48) | (uint64_t(1) << 32));
    request->getInstanceProcAddr = &Layer_xrGetInstanceProcAddr;
    request->createApiLayerInstance = &Layer_xrCreateApiLayerInstance;
    return XR_SUCCESS;
}
