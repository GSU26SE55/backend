# ADR 0004: Google Avatar vs Uploaded Avatar

Status: Accepted.

Decision: Uploaded avatars are stored as `AccountProfile.AvatarFileId` with `AvatarSource=Uploaded`. Google avatars are stored as `AccountProfile.ExternalAvatarUrl`.

Google login may refresh `ExternalAvatarUrl`, but it must not clear or replace `AvatarFileId`. Profile responses resolve `displayAvatarUrl` as uploaded avatar first, then Google avatar, then `null`.
