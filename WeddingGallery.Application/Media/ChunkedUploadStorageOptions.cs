namespace WeddingGallery.Application.Media;

/// <summary>
/// Directory holding partial uploads. Deliberately separate from the photo directory: that
/// one is served by UseStaticFiles, which would make a half-received file publicly
/// fetchable under a name the uploader chose, before validation has finished.
/// </summary>
public sealed record ChunkedUploadStorageOptions(string RootPath);
