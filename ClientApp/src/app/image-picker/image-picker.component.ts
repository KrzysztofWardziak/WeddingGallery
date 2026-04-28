import { Component, ElementRef, ViewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-image-picker',
  standalone: true,
  imports: [RouterLink, CommonModule, FormsModule],
  templateUrl: './image-picker.component.html',
  styleUrl: './image-picker.component.css'
})
export class ImagePickerComponent {
  @ViewChild('fileInput') fileInput!: ElementRef;
  guestName = localStorage.getItem('guest_name') || '';
  selectedFiles: File[] = [];
  previewUrls: string[] = [];
  eventId = localStorage.getItem('guest_event_id') || '';
  isUploading = false;

  constructor(private apiService: ApiService, private router: Router) {
    if (!this.eventId) {
      this.router.navigate(['/']);
    }
  }

  triggerFileInput() {
    this.fileInput.nativeElement.click();
  }

  onFilesSelected(event: any) {
    const files: FileList = event.target.files;
    for (let i = 0; i < files.length; i++) {
      this.selectedFiles.push(files[i]);
      const reader = new FileReader();
      reader.onload = (e: any) => this.previewUrls.push(e.target.result);
      reader.readAsDataURL(files[i]);
    }
  }

  removeFile(index: number) {
    this.selectedFiles.splice(index, 1);
    this.previewUrls.splice(index, 1);
  }

  uploadFiles() {
    if (this.selectedFiles.length === 0 || !this.guestName) return;
    localStorage.setItem('guest_name', this.guestName);
    this.isUploading = true;

    this.apiService.uploadPhotos(this.eventId, this.guestName, this.selectedFiles).subscribe({
      next: () => {
        this.isUploading = false;
        this.router.navigate(['/uploading']);
      },
      error: (err) => {
        console.error(err);
        this.isUploading = false;
        alert('Wystąpił błąd podczas przesyłania.');
      }
    });
  }
}
