import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../services/api.service';
import { environment } from '../../environments/environment';

export interface GalleryItem {
  id: string;
  url: string;
  thumbUrl: string;
  // Matches WeddingGallery.Domain.MediaTypes.
  mediaType: 'image' | 'video';
  uploaderName: string;
  uploadedAt: string;
}

const POLL_INTERVAL_MS = 10000;

@Component({
  selector: 'app-guest-feed',
  standalone: true,
  imports: [RouterLink, CommonModule, FormsModule],
  templateUrl: './guest-feed.component.html',
  styleUrl: './guest-feed.component.css'
})
export class GuestFeedComponent implements OnInit, OnDestroy {
  photos: GalleryItem[] = [];
  eventId = localStorage.getItem('guest_event_id') || '';
  selectedIndex: number | null = null;
  imagesUrl = environment.imagesUrl;
  searchTerm = '';

  private pollHandle: ReturnType<typeof setInterval> | null = null;

  get filteredPhotos(): GalleryItem[] {
    if (!this.searchTerm.trim()) return this.photos;
    const term = this.searchTerm.toLowerCase();
    return this.photos.filter(p => p.uploaderName?.toLowerCase().includes(term));
  }

  // Single source of truth for what the lightbox shows, so the index and the URL can
  // never disagree after a poll reorders or shortens the list.
  get selectedItem(): GalleryItem | null {
    if (this.selectedIndex === null) return null;
    return this.filteredPhotos[this.selectedIndex] ?? null;
  }

  constructor(private apiService: ApiService) {}

  ngOnInit() {
    if (this.eventId) {
      this.loadPhotos();
      this.pollHandle = setInterval(() => this.loadPhotos(), POLL_INTERVAL_MS);
    }
  }

  ngOnDestroy() {
    if (this.pollHandle !== null) {
      clearInterval(this.pollHandle);
    }
  }

  loadPhotos() {
    this.apiService.getPhotos(this.eventId).subscribe({
      next: (data) => this.photos = data as GalleryItem[],
      error: (err) => console.error(err)
    });
  }

  // Videos whose poster frame could not be produced come back with an empty thumbUrl;
  // the grid then renders a placeholder tile instead of a broken image.
  hasThumbnail(item: GalleryItem): boolean {
    return !!item.thumbUrl;
  }

  openPhoto(index: number) {
    this.selectedIndex = index;
  }

  closePhoto() {
    this.selectedIndex = null;
  }

  nextPhoto(event: Event) {
    event.stopPropagation();
    if (this.selectedIndex !== null && this.selectedIndex < this.filteredPhotos.length - 1) {
      this.openPhoto(this.selectedIndex + 1);
    }
  }

  prevPhoto(event: Event) {
    event.stopPropagation();
    if (this.selectedIndex !== null && this.selectedIndex > 0) {
      this.openPhoto(this.selectedIndex - 1);
    }
  }
}
