namespace WeddingGallery.Application.Media;

/// <summary>
/// Where uploaded media lands on disk. Injected rather than derived from
/// Directory.GetCurrentDirectory() so the path is explicit at composition time and the
/// service can be exercised against a temp directory in tests.
/// </summary>
public sealed record PhotoStorageOptions(string RootPath);
