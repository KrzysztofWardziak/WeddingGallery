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

// Incremental polls never learn that something was deleted, because they only ever ask for
// what is newer. Every so often the feed refetches everything so an admin's deletion reaches
// guests who have had the page open all evening. Thirty polls is five minutes.
const FULL_REFRESH_EVERY = 30;

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
  private pollCount = 0;

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
    const isFullRefresh = this.pollCount % FULL_REFRESH_EVERY === 0;
    this.pollCount++;

    // The list is newest first, so the first entry is the high-water mark.
    const since = isFullRefresh ? undefined : this.photos[0]?.uploadedAt;

    this.apiService.getPhotos(this.eventId, since).subscribe({
      next: (data) => {
        const incoming = data as GalleryItem[];
        this.photos = isFullRefresh ? incoming : this.mergeNewest(incoming);
      },
      error: (err) => console.error(err)
    });
  }

  // Incoming items are all newer than everything held, so they belong in front. Deduplicated
  // by id because two photos can share a timestamp and `since` is exclusive on time alone.
  private mergeNewest(incoming: GalleryItem[]): GalleryItem[] {
    if (incoming.length === 0) return this.photos;

    const known = new Set(this.photos.map(p => p.id));
    const fresh = incoming.filter(p => !known.has(p.id));

    return fresh.length === 0 ? this.photos : [...fresh, ...this.photos];
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
