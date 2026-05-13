# ADR 0002: FileStorage FileId Metadata

Status: Accepted.

Decision: FileStorage owns `uploaded_files` metadata and upload responses include `fileId` in addition to `objectKey`.

Other services should store `fileId` for durable references. `objectKey` remains an internal storage detail exposed for backward compatibility during Sprint 1.
