namespace WeddingGallery.Application.Media;

/// <summary>
/// One file arriving from a guest. The size is carried alongside the stream because
/// validation must happen before anything is read or written.
/// </summary>
public sealed record UploadedFile(string FileName, long SizeInBytes, Stream Content);
