# Chunked upload design

## Problem

Cloudflare rejects any request body over 100 MB on the Free plan, and the tunnel is the
only route into this deployment: the carrier runs DS-Lite, so the DNS-only workaround
Cloudflare documents is not available here. Guests therefore cannot contribute a video
longer than roughly 33 seconds at 4K/30 or 14 seconds at 4K/60, which is what the current
95 MB per-file cap enforces.

Cloudflare's limit applies per request, not per file. Splitting one file across several
requests removes the ceiling entirely, which paying does not: Pro keeps the same 100 MB,
and Business at $250/month only reaches 200 MB.

## Approach

Offset-based append, the core idea of the tus protocol without the dependency.

The server keeps one temporary file per upload session. The client sends a chunk together
with the offset it believes the file has reached; the server compares that against the
file's real length and appends only on a match. Resumption falls out of this for free: the
client asks for the current offset and continues from there.

Rejected alternatives:

- **Numbered chunks reassembled at the end.** Concatenating parts means reading and
  writing the whole file a second time. For a 500 MB video that is a gigabyte of avoidable
  disk traffic and several seconds of silence at the worst possible moment.
- **The tus protocol via `tusdotnet` + `tus-js-client`.** Battle-tested, with session
  expiry and checksums included, but it costs two dependencies and forces our validation
  and thumbnail work into someone else's lifecycle hooks. Right for a file host, oversized
  for one wedding.

## Decisions

These were settled rather than derived, and each is a single constant to revisit:

| Decision | Value | Why |
| --- | --- | --- |
| Chunk size | 25 MB | Comfortably under Cloudflare's 100 MB, and a small enough retry unit that a dropped chunk on venue wifi costs little. |
| Per-file cap | 500 MB | About 3 minutes at 4K/30. Protects the volume from one guest recording half an hour; Cloudflare no longer constrains it. |
| Chunking threshold | 20 MB | Below it, the existing single-request path stays. A 4 MB photo has no business paying for three extra round trips. |
| Resumption scope | Within the page session | The browser holds the upload id in memory. A reload loses it, so persisting sessions in Postgres would buy nothing without also identifying guests across visits. |

## API contract

All endpoints are unauthenticated, matching the existing guest upload path.

| Endpoint | Purpose |
| --- | --- |
| `POST /api/photos/uploads` | Opens a session. Body carries `eventId`, `uploaderName`, `fileName`, `totalSize`. Validates extension and declared size **before** any bytes travel, so a rejected file costs one small request rather than 500 MB. Returns `uploadId`, `offset`, `chunkSize`. |
| `GET /api/photos/uploads/{uploadId}` | Returns the current `offset`. This is the resume primitive. |
| `POST /api/photos/uploads/{uploadId}/chunk?offset=N` | Raw body is appended when `N` matches the file's real length. A mismatch returns `409` carrying the true offset, so a confused or retrying client can correct itself instead of corrupting the file. |
| `POST /api/photos/uploads/{uploadId}/complete` | Verifies the received length equals the declared size, moves the file into place, generates the video thumbnail, inserts the `Photo` row. Returns the same shape as the existing upload endpoint. |
| `DELETE /api/photos/uploads/{uploadId}` | Guest abandoned the upload; drop the temporary file now rather than waiting for the sweeper. |

## State on disk

Each session is two files in a dedicated directory:

- `<uploadId>.part` — the bytes received so far. Its length **is** the offset; no counter is
  tracked separately, so the two can never disagree.
- `<uploadId>.json` — the session's own description: event, uploader, original file name,
  declared size, resolved media type, creation time.

The sidecar exists so a session survives a container restart. Holding this in process
memory would be simpler, but a restart would then silently discard a guest's 400 MB
mid-flight, and the sweeper could not say what it was deleting.

**The directory must live outside `wwwroot`.** `UseStaticFiles` serves everything under
`wwwroot/photos`, so a partial file placed there would be publicly fetchable, under a name
and extension the uploader chose, before validation has finished. The temporary directory
gets its own Docker volume mount: a 500 MB partial file must not land on the container's
writable layer.

## Concurrency

Appends to one session are serialised by a per-`uploadId` lock held in a
`ConcurrentDictionary`. Two simultaneous appends to the same file would interleave and
corrupt it. The offset check alone is not enough — it narrows the race without closing it.
A single container makes an in-process lock sufficient; this assumption breaks if the API
is ever scaled out, and the lock is where that would first hurt.

## Cleanup

Abandoned sessions leak disk. A guest who closes the browser mid-upload leaves a `.part`
file nothing will ever complete.

A sweeper deletes sessions whose files are older than 24 hours, running once at startup
and every hour after. Age is read from the filesystem, so orphans left by a restart are
collected even when no process remembers them.

## Error handling

- Rejected at `init`: unsupported format, or declared size over the cap. The guest sees the
  existing Polish message before any bytes leave the phone.
- Offset mismatch: `409` with the true offset. The client re-points and continues.
- `complete` with a length short of the declared size: `400`, session preserved so the
  client can resume rather than restart.
- Unknown or swept `uploadId`: `404`. The client falls back to opening a fresh session.
- A failed thumbnail keeps its existing behaviour — the video is saved with an empty
  `ThumbPath` and the gallery renders a placeholder.

## Progress reporting

The picker currently counts completed files. A 500 MB video would leave that bar frozen for
minutes, which reads as a hang and invites a force-quit. Progress moves to bytes: the sum of
chunks acknowledged across the queue against the total bytes selected. This replaces the
file-count model rather than sitting beside it.

## Testing

Unit tests, against a temporary directory:

- append at the correct offset advances it; at a wrong offset it is refused and the file is
  left untouched
- a full sequence of chunks completes into exactly the original bytes
- `complete` short of the declared size is refused and the session survives
- an oversized or wrong-format file is refused at `init`, before a session exists
- the sweeper removes an aged session and spares a fresh one

End-to-end, against the local stack: a real file over 100 MB uploaded in chunks, its
reassembled bytes compared to the source, and its thumbnail generated.

**What cannot be verified locally:** that Cloudflare passes the chunks. Nothing on this
machine exercises the tunnel. Only the deployed environment can confirm it, and that check
remains outstanding until then.

## Known risks

- **Disk exposure grows.** Unauthenticated guests can already upload, but the ceiling per
  sequence rises from 95 MB to 500 MB, and nothing rate-limits how many sessions one client
  opens. A per-event total cap or a rate limit would address it; neither is in this scope.
- **Two upload paths** now exist, small and chunked. The threshold is the seam, and a bug
  that only appears on one side of it is the likely failure mode.
- **The in-process lock ties correctness to running exactly one API container.**
