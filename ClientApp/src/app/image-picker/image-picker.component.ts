import { Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { from, of } from 'rxjs';
import { catchError, finalize, map, mergeMap, tap, timeout } from 'rxjs/operators';
import { ApiService } from '../services/api.service';

interface SelectedMedia {
  file: File;
  previewUrl: string;
  isVideo: boolean;
  failed: boolean;
}

// Guests are on venue wifi or LTE, where dozens of parallel connections lower
// real throughput and cause timeouts. Three at a time is the compromise.
const MAX_PARALLEL_UPLOADS = 3;

// A stalled request on venue wifi must not trap the guest with a frozen
// progress bar and no way out; a hang becomes a normal failure that the
// existing retry path already handles. Videos are far larger than photos, so the
// window has to be wide enough for a 90 MB clip on a slow uplink.
const UPLOAD_TIMEOUT_MS = 300000;

// Mirrors MediaFileValidator.MaxFileBytes on the server, which in turn is set by
// Cloudflare's 100 MB request limit on the free plan. Rejecting here saves the guest
// from watching a long upload fail; the server check is the one that actually enforces it.
const MAX_FILE_BYTES = 95 * 1024 * 1024;

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
  attemptedCount = 0;
  savedCount = 0;
  totalCount = 0;
  failedCount = 0;

  // Files turned away before any upload started (too large), and the last message the
  // server gave for a rejected file. Both are shown to the guest so a refusal is never silent.
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
        this.rejectedMessages.push(
          `${file.name} (${sizeMb} MB) jest za duży — nagraj krótszy film (do ok. 1 min) ` +
          `lub wyślij go w niższej jakości.`);
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
        failed: false
      });
    }

    event.target.value = '';
  }

  removePhoto(index: number) {
    URL.revokeObjectURL(this.photos[index].previewUrl);
    this.succeeded.delete(this.photos[index]);
    this.photos.splice(index, 1);
  }

  uploadFiles() {
    if (this.photos.length === 0 || !this.guestName || this.isUploading) return;

    localStorage.setItem('guest_name', this.guestName);

    const queue = this.photos.filter(photo => !this.succeeded.has(photo));
    this.isUploading = true;
    this.attemptedCount = 0;
    this.savedCount = 0;
    this.failedCount = 0;
    this.serverErrorMessage = null;
    this.rejectedMessages = [];
    this.totalCount = queue.length;
    queue.forEach(photo => (photo.failed = false));

    from(queue).pipe(
      mergeMap(photo =>
        this.apiService.uploadMedia(this.eventId, this.guestName, photo.file).pipe(
          timeout(UPLOAD_TIMEOUT_MS),
          map(() => ({ photo, ok: true })),
          // One failed file must not abort the rest; collect it for a retry.
          catchError(error => {
            this.rememberServerError(error);
            return of({ photo, ok: false });
          })
        ),
        MAX_PARALLEL_UPLOADS
      ),
      tap(result => {
        // The bar advances on every completed attempt (honest progress), but the
        // number shown to the guest must only count files actually saved.
        this.attemptedCount++;
        if (result.ok) {
          this.savedCount++;
          this.succeeded.add(result.photo);
        } else {
          result.photo.failed = true;
          this.failedCount++;
        }
      }),
      finalize(() => (this.isUploading = false))
    ).subscribe({
      complete: () => {
        if (this.failedCount === 0) {
          this.router.navigate(['/feed']);
        }
      }
    });
  }

  goToFeed() {
    this.router.navigate(['/feed']);
  }

  // A 400 carries a message written for the guest (wrong format, file too large). Anything
  // else is a network or server problem where that text would only confuse them.
  private rememberServerError(error: unknown) {
    if (error instanceof HttpErrorResponse && error.status === 400 && typeof error.error === 'string') {
      this.serverErrorMessage = error.error;
    }
  }
}
