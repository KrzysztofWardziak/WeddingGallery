namespace WeddingGallery.Application.Events;

/// <summary>
/// Outcome of an attempt to delete an event. Deleting a gallery is irreversible, so the
/// caller is told exactly which guard stopped it rather than getting a bare boolean.
/// </summary>
public enum EventDeletionResult
{
    Deleted,
    NotFound,

    /// <summary>
    /// The typed confirmation did not match the event name. Checked here rather than only in
    /// the browser so the guard is real: a mistyped curl must not be able to wipe a gallery.
    /// </summary>
    NameMismatch
}
