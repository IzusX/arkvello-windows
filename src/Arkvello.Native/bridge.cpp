#include <windows.h>
#include <atomic>
#include <condition_variable>
#include <memory>
#include <mutex>
#include <vector>
#include "bridge.h"
#include "../../CPP/Common/MyInitGuid.h"
#include "../../CPP/Windows/FileDir.h"
#include "../../CPP/Windows/FileName.h"
#include "../../CPP/Windows/PropVariant.h"
#include "../../CPP/7zip/UI/Common/ArchiveExtractCallback.h"

HINSTANCE g_hInstance = nullptr;
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
  if (reason == DLL_PROCESS_ATTACH) { g_hInstance = instance; DisableThreadLibraryCalls(instance); }
  return TRUE;
}

namespace {
struct Job {
  AvCallback callback;
  void *context;
  std::atomic<bool> cancelled{false}, paused{false};
  std::mutex mutex, callbackMutex;
  std::condition_variable wake;
  std::atomic<UInt64> total{0};
  HRESULT emit(AvEvent e, wchar_t *reply = nullptr, uint32_t capacity = 0, int *answer = nullptr) {
    if (cancelled) return E_ABORT;
    std::lock_guard<std::mutex> lock(callbackMutex);
    int value = callback ? callback(context, &e, reply, capacity) : -1;
    if (answer) *answer = value;
    if (value < 0) { cancelled = true; return E_ABORT; }
    return cancelled ? E_ABORT : S_OK;
  }
  HRESULT check() {
    if (cancelled) return E_ABORT;
    if (paused) {
      AvEvent e{}; e.kind = 7;
      RINOK(emit(e))
      std::unique_lock<std::mutex> lock(mutex);
      wake.wait(lock, [&] { return !paused || cancelled; });
    }
    return cancelled ? E_ABORT : S_OK;
  }
  HRESULT progress(const UInt64 *value) {
    RINOK(check())
    AvEvent e{}; e.kind = 2; e.total = total; e.completed = value ? *value : 0;
    return emit(e);
  }
  HRESULT password(BSTR *password) {
    wchar_t reply[4096]{};
    AvEvent e{}; e.kind = 3;
    HRESULT h = emit(e, reply, 4096);
    if (h == S_OK) {
      reply[4095] = 0;
      *password = SysAllocString(reply);
      if (!*password) h = E_OUTOFMEMORY;
    }
    SecureZeroMemory(reply, sizeof(reply));
    return h;
  }
};

struct Session {
  CCodecs *codecs = new CCodecs;
  CMyComPtr<IUnknown> codecsOwner;
  CArchiveLink link;
  Session() : codecsOwner(static_cast<ICompressCodecsInfo *>(codecs)) {}
  ~Session() { link.Release(); codecs->CloseLibs(); }
};

class OpenCallback final : public IOpenCallbackUI {
  Job &job;
public:
  explicit OpenCallback(Job &value) : job(value) {}
  HRESULT Open_CheckBreak() override { return job.check(); }
  HRESULT Open_SetTotal(const UInt64 *, const UInt64 *bytes) override { job.total = bytes ? *bytes : 0; return job.check(); }
  HRESULT Open_SetCompleted(const UInt64 *, const UInt64 *bytes) override { return job.progress(bytes); }
  HRESULT Open_Finished() override { return job.check(); }
  HRESULT Open_CryptoGetTextPassword(BSTR *password) override { return job.password(password); }
};

class ExtractCallback final : public IFolderArchiveExtractCallback,
  public ICryptoGetTextPassword, public CMyUnknownImp {
  Z7_COM_QI_BEGIN2(IFolderArchiveExtractCallback)
  Z7_COM_QI_ENTRY(ICryptoGetTextPassword)
  Z7_COM_QI_END
  Z7_COM_ADDREF_RELEASE
  Job &job;
  UString currentPath;
public:
  explicit ExtractCallback(Job &value) : job(value) {}
  Z7_COM7F_IMF(SetTotal(UInt64 total)) { job.total = total; return job.check(); }
  Z7_COM7F_IMF(SetCompleted(const UInt64 *value)) { return job.progress(value); }
  Z7_COM7F_IMF(CryptoGetTextPassword(BSTR *password)) { return job.password(password); }
  Z7_COM7F_IMF(AskOverwrite(const wchar_t *existName, const FILETIME *existTime, const UInt64 *existSize,
    const wchar_t *newName, const FILETIME *newTime, const UInt64 *newSize, Int32 *answer)) {
    AvEvent e{}; e.kind = 4; e.path1 = existName; e.path2 = newName;
    if (existSize) { e.size1 = *existSize; e.flags |= 1; }
    if (newSize) { e.size2 = *newSize; e.flags |= 2; }
    if (existTime) { e.time1 = (UInt64(existTime->dwHighDateTime) << 32) | existTime->dwLowDateTime; e.flags |= 4; }
    if (newTime) { e.time2 = (UInt64(newTime->dwHighDateTime) << 32) | newTime->dwLowDateTime; e.flags |= 8; }
    int response = 5;
    RINOK(job.emit(e, nullptr, 0, &response))
    if (response < 0 || response > 5) return E_INVALIDARG;
    *answer = response;
    return S_OK;
  }
  Z7_COM7F_IMF(PrepareOperation(const wchar_t *name, Int32, Int32, const UInt64 *)) {
    RINOK(job.check())
    currentPath = name;
    AvEvent e{}; e.kind = 6; e.path1 = name; return job.emit(e);
  }
  Z7_COM7F_IMF(MessageError(const wchar_t *message)) {
    AvEvent e{}; e.kind = 5; e.path1 = message; return job.emit(e);
  }
  Z7_COM7F_IMF(SetOperationResult(Int32 result, Int32 encrypted)) {
    if (result == NArchive::NExtract::NOperationResult::kOK) return job.check();
    AvEvent e{}; e.kind = 5; e.code = (uint32_t)result; e.flags = (uint32_t)encrypted | 0x10000u; e.path1 = currentPath;
    return job.emit(e);
  }
};

uint64_t Number(IInArchive *arc, UInt32 index, PROPID id, bool *defined = nullptr) {
  NWindows::NCOM::CPropVariant p;
  if (arc->GetProperty(index, id, &p) != S_OK) return 0;
  if (defined) *defined = p.vt == VT_UI8 || p.vt == VT_UI4;
  return p.vt == VT_UI8 ? p.uhVal.QuadPart : p.vt == VT_UI4 ? p.ulVal : 0;
}
}

uint32_t av_abi() { return 1; }
void *av_job_create(AvCallback callback, void *context) {
  try { return new Job{callback, context}; } catch (...) { return nullptr; }
}
void av_job_cancel(void *value) {
  auto &j = *static_cast<Job *>(value);
  { std::lock_guard<std::mutex> lock(j.mutex); j.cancelled = true; }
  j.wake.notify_all();
}
void av_job_pause(void *value, int pause) {
  auto &j = *static_cast<Job *>(value);
  { std::lock_guard<std::mutex> lock(j.mutex); j.paused = pause != 0; }
  j.wake.notify_all();
}
void av_job_close(void *value) { delete static_cast<Job *>(value); }
void av_close(void *value) { delete static_cast<Session *>(value); }

int32_t av_open(const wchar_t *path, void *value, void **result) {
  if (!path || !value || !result) return E_INVALIDARG;
  *result = nullptr;
  try {
    auto session = std::make_unique<Session>();
    // Load only our adjacent codec DLL, never registry-installed 7-Zip or plugins.
    wchar_t module[32768];
    DWORD len = GetModuleFileNameW(g_hInstance, module, 32768);
    if (!len || len >= 32768) return E_FAIL;
    std::wstring dll(module, len);
    dll.resize(dll.find_last_of(L"\\/") + 1); dll += L"7z.dll";
    bool loaded = false;
    RINOK(session->codecs->LoadDll(dll.c_str(), false, &loaded))
    if (!loaded) return HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
    session->codecs->NeedSetLibCodecs = false;
    COpenOptions options;
    CObjectVector<COpenType> types;
    CIntVector excluded;
    options.types = &types;
    options.excludedFormats = &excluded;
    options.codecs = session->codecs;
    options.filePath = path;
    options.props = nullptr;
    OpenCallback callback(*static_cast<Job *>(value));
    RINOK(session->link.Open_Strict(options, &callback))
    for (unsigned i = 0; i < session->link.Arcs.Size(); ++i) {
      const auto &info = session->link.Arcs[i].ErrorInfo;
      if (info.ErrorFlags || info.WarningFlags) {
        AvEvent e{}; e.kind = 5; e.code = 0x100;
        e.size1 = info.ErrorFlags; e.size2 = info.WarningFlags;
        RINOK(static_cast<Job *>(value)->emit(e))
      }
    }
    *result = session.release();
    return S_OK;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; }
    catch (...) { return E_FAIL; }
}

int32_t av_list(void *sessionValue, void *jobValue) {
  if (!sessionValue || !jobValue) return E_INVALIDARG;
  try {
    auto &session = *static_cast<Session *>(sessionValue);
    auto &job = *static_cast<Job *>(jobValue);
    IInArchive *arc = session.link.GetArchive();
    UInt32 count = 0; RINOK(arc->GetNumberOfItems(&count))
    for (UInt32 i = 0; i < count; ++i) {
      RINOK(job.check())
      UString path; RINOK(session.link.GetArc()->GetItem_Path(i, path))
      bool dir = false; RINOK(Archive_IsItem_Dir(arc, i, dir))
      AvEvent e{}; e.kind = 1; e.index = i; e.path1 = path;
      e.flags = dir ? 1u : 0u;
      bool hasSize = false;
      e.size1 = Number(arc, i, kpidSize, &hasSize);
      if (hasSize) e.flags |= 2;
      e.size2 = Number(arc, i, kpidPackSize);
      NWindows::NCOM::CPropVariant time;
      RINOK(arc->GetProperty(i, kpidMTime, &time))
      if (time.vt == VT_FILETIME) {
        e.time1 = (UInt64(time.filetime.dwHighDateTime) << 32) | time.filetime.dwLowDateTime;
        e.flags |= 4;
      }
      RINOK(job.emit(e))
    }
    return S_OK;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}

int32_t av_extract(void *sessionValue, void *jobValue, const uint32_t *indices,
  uint32_t count, const wchar_t *destination, int test) {
  if (!sessionValue || !jobValue || !indices || !count || (!test && (!destination || !*destination))) return E_INVALIDARG;
  try {
    auto &session = *static_cast<Session *>(sessionValue);
    auto &job = *static_cast<Job *>(jobValue);
    RINOK(job.check())
    UInt32 itemCount = 0; RINOK(session.link.GetArchive()->GetNumberOfItems(&itemCount))
    CUIntVector selected;
    for (uint32_t i = 0; i < count; ++i) {
      if (indices[i] >= itemCount || (i && indices[i] <= indices[i - 1])) return E_INVALIDARG;
      selected.Add(indices[i]);
    }
    FString dest;
    if (!test) {
      dest = destination;
      NWindows::NFile::NName::NormalizeDirPathPrefix(dest);
      if (!NWindows::NFile::NDir::CreateComplexDir(dest)) return HRESULT_FROM_WIN32(GetLastError());
    }
    auto *uiSpec = new ExtractCallback(job);
    CMyComPtr<IFolderArchiveExtractCallback> ui = uiSpec;
    auto *ecs = new CArchiveExtractCallback;
    CMyComPtr<IArchiveExtractCallback> callback = ecs;
    ecs->InitForMulti(false, NExtract::NPathMode::kFullPaths, NExtract::NOverwriteMode::kAsk,
      NExtract::NZoneIdMode::kNone, true);
    ecs->InitBeforeNewArchive();
    CExtractNtOptions options;
    UStringVector removeParts;
    ecs->Init(options, nullptr, session.link.GetArc(), ui, false, test != 0, dest,
      removeParts, false, (UInt64)(Int64)-1);
    CArchiveExtractCallback_Closer closer(ecs);
    if (!test) { RINOK(ecs->PrepareHardLinks(&selected)) }
    HRESULT h = session.link.GetArchive()->Extract(selected.ConstData(), selected.Size(), test, callback);
    HRESULT closeResult = closer.Close();
    return h == S_OK ? closeResult : h;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}
