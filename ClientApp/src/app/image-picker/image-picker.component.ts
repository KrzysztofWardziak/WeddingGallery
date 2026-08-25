import { Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { from, of } from 'rxjs';
import { catchError, finalize, map, mergeMap, tap } from 'rxjs/operators';
import { ApiService } from '../services/api.service';

interface SelectedPhoto {
  file: File;
  previewUrl: string;
  failed: boolean;
}

// Guests are on venue wifi or LTE, where dozens of parallel connections lower
// real throughput and cause timeouts. Three at a time is the compromise.
const MAX_PARALLEL_UPLOADS = 3;

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
  photos: SelectedPhoto[] = [];
  eventId = localStorage.getItem('guest_event_id') || '';
  isUploading = false;
  uploadedCount = 0;
  totalCount = 0;
  failedCount = 0;

  // Photos already accepted by the server, so a retry only sends what is missing.
  private succeeded = new Set<SelectedPhoto>();

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
    for (let i = 0; i < files.length; i++) {
      // createObjectURL is synchronous, so the preview stays paired with its file.
      // FileReader resolved out of order and made "remove" delete the wrong photo.
      this.photos.push({
        file: files[i],
        previewUrl: URL.createObjectURL(files[i]),
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
    this.uploadedCount = 0;
    this.failedCount = 0;
    this.totalCount = queue.length;
    queue.forEach(photo => (photo.failed = false));

    from(queue).pipe(
      mergeMap(photo =>
        this.apiService.uploadPhoto(this.eventId, this.guestName, photo.file).pipe(
          map(() => ({ photo, ok: true })),
          // One failed photo must not abort the rest; collect it for a retry.
          catchError(() => of({ photo, ok: false }))
        ),
        MAX_PARALLEL_UPLOADS
      ),
      tap(result => {
        this.uploadedCount++;
        if (result.ok) {
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
}