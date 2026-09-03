namespace WeddingGallery.Application.Media;

/// <summary>The upload id is unknown, already completed, or was swept.</summary>
public sealed class UploadSessionNotFoundException : Exception
{
    public UploadSessionNotFoundException(Guid uploadId)
        : base($"Upload session {uploadId} does not exist.")
    {
        UploadId = uploadId;
    }

    public Guid UploadId { get; }
}

/// <summary>
/// The client's idea of the offset disagrees with the partial file's real length. Carries
/// the true offset so the client can re-point instead of restarting or corrupting the file.
/// </summary>
public sealed class UploadOffsetMismatchException : Exception
{
    public UploadOffsetMismatchException(long expectedOffset, long actualOffset)
        : base($"Chunk arrived for offset {expectedOffset} but the upload is at {actualOffset}.")
    {
        ActualOffset = actualOffset;
    }

    public long ActualOffset { get; }
}

/// <summary>Completion was requested before every byte arrived.</summary>
public sealed class IncompleteUploadException : Exception
{
    public IncompleteUploadException(long received, long expected)
        : base($"Upload holds {received} of {expected} bytes.")
    {
        Received = received;
        Expected = expected;
    }

    public long Received { get; }

    public long Expected { get; }
}
