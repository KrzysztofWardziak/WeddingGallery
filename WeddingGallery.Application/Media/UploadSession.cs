namespace WeddingGallery.Application.Media;

/// <summary>
/// A chunked upload in flight. <see cref="Offset"/> is read from the length of the partial
/// file rather than tracked alongside it, so the two can never disagree.
/// </summary>
public sealed record UploadSession(
    Guid Id,
    Guid EventId,
    string UploaderName,
    string FileName,
    long TotalSize,
    string MediaType,
    long Offset);
