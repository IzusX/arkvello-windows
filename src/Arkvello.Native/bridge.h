#pragma once
#include <stdint.h>
#include <wchar.h>
// ABI 1. All strings are UTF-16, borrowed only until the callback returns.
// Functions run synchronously on the caller's worker. A session is single-owner,
// non-reentrant; cancel/pause are the only concurrent calls. Never close in use.
struct AvEvent {
  uint32_t kind, code, index, flags;
  uint64_t completed, total, size1, size2, time1, time2;
  const wchar_t *path1, *path2;
};
// 1=item, 2=progress, 3=password, 4=overwrite, 5=issue, 6=current path,
// 7=pause acknowledged. Password: return 0 and fill reply; negative=cancel.
// Overwrite response: upstream NOverwriteAnswer (0=yes,1=all,2=no,3=no all,
// 4=auto-rename incoming,5=cancel). No exceptions may escape callback.
typedef int (__cdecl *AvCallback)(void *, const AvEvent *, wchar_t *reply, uint32_t capacity);
#define AV_API extern "C" __declspec(dllexport)
AV_API uint32_t av_abi();
AV_API void *av_job_create(AvCallback callback, void *context);
AV_API void av_job_cancel(void *job);
AV_API void av_job_pause(void *job, int pause);
AV_API void av_job_close(void *job);
AV_API int32_t av_open(const wchar_t *path, void *job, void **session);
AV_API int32_t av_list(void *session, void *job);
AV_API int32_t av_extract(void *session, void *job, const uint32_t *indices,
  uint32_t count, const wchar_t *destination, int test);
// Materialize exactly one native index into a new caller-owned regular file.
// No archive path is used as an output path. Neither call overwrites a file.
AV_API int32_t av_export_item(void *session, void *job, uint32_t index, const wchar_t *output);
// Reuse Agent's index-based UpdateOneFile; write a new archive, leaving the
// source/session intact. The caller owns commit/recovery of this staged output.
AV_API int32_t av_replace_item(void *session, void *job, uint32_t index,
  const wchar_t *replacement, const wchar_t *output);
AV_API void av_close(void *session);
