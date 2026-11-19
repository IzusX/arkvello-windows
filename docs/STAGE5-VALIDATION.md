# Stage 5 — archive workflows

2026-11-19. Portable x64 development checkpoint. WinUI 3 uses native bridge ABI 4.

- **Add** uses the pinned 7-Zip `UpdateArchive` service and loads writable formats
  from the adjacent `7z.dll`. The WinUI dialog exposes compression method and
  level, dictionary and order, threads, memory limit, solid blocks, encryption,
  volumes, SFX, metadata selection, timestamps, path modes and update modes.
  Codec properties can also be entered directly. The portable build includes
  the upstream `7z.sfx` module.
- **Update** writes a complete staged archive, holds a read lease on the input
  during encoding, then replaces the destination. It checks the prior archive
  retained by the atomic replacement. Conflicts or filesystem failures retain
  recovery files. Publication of a new archive or its volumes never overwrites
  an existing file. When creating a new archive from regular files, the option
  to remove inputs delegates cleanup to 7-Zip only after successful publication.
- **Extract and Test** work on an open archive or files selected in a filesystem
  panel. Extraction offers path and overwrite modes, NTFS metadata, a password,
  and separate folders per archive. Opening an archive protected by an encrypted
  header offers a password retry.
- **Delete from archive** maps selected members by native index, stages and
  verifies the result, then reopens the archive. A successful replacement
  keeps a backup beside the archive. The current guard excludes split archives,
  read-only handlers and archives with a nonzero stream offset.

The bridge checks created several formats and verified bytes and integrity
using the pinned original 7-Zip console. They exercised Unicode names, stored
and compressed archives, encrypted headers, password retry, ZIP AES, volumes,
SFX, cancellation while updating, extraction modes and member deletion. The
desktop-hosted WinUI checks invoked the toolbar buttons, changed the Add
format, created ZIP, extracted a selected archive and deleted an archive
member.

Stage acceptance still requires compression-memory estimates and full
format-dependent parameter coverage, source deletion for folder selections,
wider password retry coverage when decoding member data, and
validation of unusual metadata, nested mutations and archive drag/drop. The
changes have local Windows 11 evidence. Windows 10 and clean-system
installation remain unverified.
