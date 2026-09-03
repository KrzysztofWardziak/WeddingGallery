import { Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { timeout } from 'rxjs/operators';
import { ApiService, UploadSession } from '../services/api.service';

interface SelectedMedia {
  file: File;
  previewUrl: string;
  isVideo: boolean;
  failed: boolean;
  // Bytes the server has acknowledged for this file, so the progress bar can be rebuilt
  // exactly after a partial failure instead of drifting.
  sentBytes: number;
  // Present only while a chunked upload for this file is unfinished; a retry resumes it.
  session?: UploadSession;
}

// Guests are on venue wifi or LTE, where dozens of parallel connections lower
// real throughput and cause timeouts. Three at a time is the compromise.
const MAX_PARALLEL_UPLOADS = 3;

// Below this, a single request is cheaper than three round trips. A 4 MB photo has no
// business paying for an init, a chunk and a completion.
const CHUNK_THRESHOLD_BYTES = 20 * 1024 * 1024;

// Per-file cap. Cloudflare stopped being the constraint once uploads are chunked, so this
// is purely about not letting one guest fill the server's disk. Mirrors
// ChunkedUploadService.MaxFileBytes.
const MAX_FILE_BYTES = 500 * 1024 * 1024;

// A stalled request must not trap the guest with a frozen bar and no way out; a hang
// becomes a normal failure that the retry path already handles.
const SMALL_UPLOAD_TIMEOUT_MS = 120000;
const CHUNK_TIMEOUT_MS = 180000;

@Component({
  selector: 'app-image-picker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './image-picker.component.html',
  styleUrl: './image-picker.component.css'
})
export class ImagePickerComponent implements OnDestroy {
  @ViewChild('fileInput') fileInput!: ElementRef;
  guestName = localStorage.getItem('guest_name') || '';
  photos: SelectedMedia[] = [];
  eventId = localStorage.getItem('guest_event_id') || '';
  isUploading = false;
  savedCount = 0;
  totalCount = 0;
  failedCount = 0;

  // Progress is measured in bytes, not files. A single 500 MB video would leave a
  // file-counting bar frozen for minutes, which reads as a hang and invites a force-quit.
  sentBytes = 0;
  totalBytes = 0;

  rejectedMessages: string[] = [];
  serverErrorMessage: string | null = null;

  // Files already accepted by the server, so a retry only sends what is missing.
  private succeeded = new Set<SelectedMedia>();

  constructor(private apiService: ApiService, private router: Router) {
    if (!this.eventId) {
      this.router.navigate(['/']);
    }
  }

  ngOnDestroy() {
    this.photos.forEach(photo => URL.revokeObjectURL(photo.previewUrl));
  }

  get progressPercent(): number {
    if (!this.totalBytes) return 0;
    return Math.min(100, Math.round((this.sentBytes / this.totalBytes) * 100));
  }

  triggerFileInput() {
    this.fileInput.nativeElement.click();
  }

  onFilesSelected(event: any) {
    const files: FileList = event.target.files;
    this.rejectedMessages = [];

    for (let i = 0; i < files.length; i++) {
      const file = files[i];

      if (file.size > MAX_FILE_BYTES) {
        const sizeMb = Math.round(file.size / (1024 * 1024));
        const limitMb = Math.round(MAX_FILE_BYTES / (1024 * 1024));
        this.rejectedMessages.push(
          `${file.name} (${sizeMb} MB) przekracza limit ${limitMb} MB. Nagraj krótszy film ` +
          `albo przełącz aparat na 1080p.`);
        continue;
      }

      // createObjectURL is synchronous, so the preview stays paired with its file.
      // FileReader resolved out of order and made "remove" delete the wrong photo.
      // The format allowlist deliberately lives only on the server, so the two copies
      // cannot drift apart; a bad format comes back as a 400 with a guest-facing message.
      this.photos.push({
        file,
        previewUrl: URL.createObjectURL(file),
        isVideo: file.type.startsWith('video/'),
        failed: false,
        sentBytes: 0
      });
    }

    event.target.value = '';
  }

  removePhoto(index: number) {
    const media = this.photos[index];
    URL.revokeObjectURL(media.previewUrl);

    // Tell the server to drop the partial file now rather than leaving it for the sweeper.
    if (media.session) {
      this.apiService.abandonUpload(media.session.uploadId).subscribe({ error: () => {} });
    }

    this.succeeded.delete(media);
    this.photos.splice(index, 1);
  }

  async uploadFiles() {
    // The name is optional; only the files are required.
    if (this.photos.length === 0 || this.isUploading) return;

    localStorage.setItem('guest_name', this.guestName);

    const queue = this.photos.filter(photo => !this.succeeded.has(photo));
    this.isUploading = true;
    this.savedCount = 0;
    this.failedCount = 0;
    this.serverErrorMessage = null;
    this.rejectedMessages = [];
    this.totalCount = queue.length;
    this.totalBytes = queue.reduce((sum, photo) => sum + photo.file.size, 0);
    this.sentBytes = 0;
    queue.forEach(photo => {
      photo.failed = false;
      photo.sentBytes = 0;
    });

    await this.runWithConcurrency(queue, MAX_PARALLEL_UPLOADS, async media => {
      try {
        await this.uploadOne(media);
        this.succeeded.add(media);
        this.savedCount++;
      } catch (error) {
        // One failed file must not abort the rest; it stays queued for a retry.
        media.failed = true;
        this.failedCount++;
        this.rememberServerError(error);
      }
    });

    this.isUploading = false;

    if (this.failedCount === 0) {
      this.router.navigate(['/feed']);
    }
  }

  goToFeed() {
    this.router.navigate(['/feed']);
  }

  private async uploadOne(media: SelectedMedia) {
    if (media.file.size <= CHUNK_THRESHOLD_BYTES) {
      await firstValueFrom(
        this.apiService.uploadMedia(this.eventId, this.guestName, media.file)
          .pipe(timeout(SMALL_UPLOAD_TIMEOUT_MS)));
      this.creditBytesTo(media, media.file.size);
      return;
    }

    await this.uploadChunked(media);
  }

  private async uploadChunked(media: SelectedMedia) {
    const session = await this.openOrResumeSession(media);
    let offset = session.offset;
    this.creditBytesTo(media, offset);

    while (offset < media.file.size) {
      const end = Math.min(offset + session.chunkSize, media.file.size);
      const chunk = media.file.slice(offset, end);

      try {
        const result = await firstValueFrom(
          this.apiService.appendChunk(session.uploadId, offset, chunk).pipe(timeout(CHUNK_TIMEOUT_MS)));
        offset = result.offset;
      } catch (error) {
        // 409 means the server disagrees about where we are - usually a chunk that landed
        // while the response was lost. It reports the truth; continue from there.
        if (error instanceof HttpErrorResponse && error.status === 409 && typeof error.error?.offset === 'number') {
          offset = error.error.offset;
          this.creditBytesTo(media, offset);
          continue;
        }
        throw error;
      }

      this.creditBytesTo(media, offset);
    }

    await firstValueFrom(this.apiService.completeUpload(session.uploadId));
    media.session = undefined;
  }

  private async openOrResumeSession(media: SelectedMedia): Promise<UploadSession> {
    if (media.session) {
      try {
        const state = await firstValueFrom(this.apiService.getUploadOffset(media.session.uploadId));
        media.session = { ...media.session, offset: state.offset };
        return media.session;
      } catch (error) {
        // The session expired or was swept; fall through and open a fresh one rather than
        // failing an upload the guest can still complete.
        if (!(error instanceof HttpErrorResponse) || error.status !== 404) {
          throw error;
        }
        media.session = undefined;
        media.sentBytes = 0;
      }
    }

    const session = await firstValueFrom(
      this.apiService.startUpload(this.eventId, this.guestName, media.file));
    media.session = session;
    return session;
  }

  // Credits an absolute per-file offset, so the shared total cannot drift when a chunk is
  // retried or the server corrects our position.
  private creditBytesTo(media: SelectedMedia, absoluteOffset: number) {
    this.sentBytes += absoluteOffset - media.sentBytes;
    media.sentBytes = absoluteOffset;
  }

  private async runWithConcurrency<T>(items: T[], limit: number, worker: (item: T) => Promise<void>) {
    const pending = [...items];
    const runners = Array.from({ length: Math.min(limit, pending.length) }, async () => {
      while (pending.length > 0) {
        await worker(pending.shift()!);
      }
    });
    await Promise.all(runners);
  }

  // A 400 carries a message written for the guest (wrong format, file too large). Anything
  // else is a network or server problem where that text would only confuse them.
  private rememberServerError(error: unknown) {
    if (error instanceof HttpErrorResponse && error.status === 400 && typeof error.error === 'string') {
      this.serverErrorMessage = error.error;
    }
  }
}
