import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../services/api.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-guest-feed',
  standalone: true,
  imports: [RouterLink, CommonModule, FormsModule],
  templateUrl: './guest-feed.component.html',
  styleUrl: './guest-feed.component.css'
})
export class GuestFeedComponent implements OnInit {
  photos: any[] = [];
  eventId = localStorage.getItem('guest_event_id') || '';
  selectedIndex: number | null = null;
  selectedPhotoUrl: string | null = null;
  imagesUrl = environment.imagesUrl;
  searchTerm = '';

  get filteredPhotos(): any[] {
    if (!this.searchTerm.trim()) return this.photos;
    const term = this.searchTerm.toLowerCase();
    return this.photos.filter(p => p.uploaderName?.toLowerCase().includes(term));
  }

  constructor(private apiService: ApiService) {}

  ngOnInit() {
    if (this.eventId) {
      this.loadPhotos();
      setInterval(() => this.loadPhotos(), 10000); // Poll every 10s
    }
  }

  loadPhotos() {
    this.apiService.getPhotos(this.eventId).subscribe({
      next: (data) => this.photos = data,
      error: (err) => console.error(err)
    });
  }

  openPhoto(index: number) {
    this.selectedIndex = index;
    this.selectedPhotoUrl = this.imagesUrl + this.filteredPhotos[index].url;
  }

  closePhoto() {
    this.selectedIndex = null;
    this.selectedPhotoUrl = null;
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
