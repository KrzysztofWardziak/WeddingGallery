import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { QRCodeModule } from 'angularx-qrcode';
import { ApiService } from '../services/api.service';
import { GalleryItem } from '../guest-feed/guest-feed.component';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [CommonModule, QRCodeModule, RouterLink],
  templateUrl: './admin-dashboard.component.html',
  styleUrl: './admin-dashboard.component.css'
})
export class AdminDashboardComponent implements OnInit {
  eventId = '';
  eventSlug = '';
  eventUrl = '';
  photos: GalleryItem[] = [];
  imagesUrl = environment.imagesUrl;
  apiUrl = environment.apiUrl;

  constructor(private apiService: ApiService, private router: Router) {}

  ngOnInit() {
    this.eventId = localStorage.getItem('admin_event_id') || '';
    this.eventSlug = localStorage.getItem('admin_event_slug') || '';
    
    if (!this.eventId) {
      this.router.navigate(['/admin/setup']);
      return;
    }

    this.eventUrl = `${window.location.origin}/${this.eventSlug}`;
    this.loadPhotos();
  }

  loadPhotos() {
    this.apiService.getPhotos(this.eventId).subscribe({
      next: (data) => this.photos = data as GalleryItem[],
      error: (err) => console.error(err)
    });
  }

  deletePhoto(id: string) {
    if (confirm('Czy na pewno chcesz usunąć ten plik?')) {
      this.apiService.deletePhoto(id).subscribe({
        next: () => this.loadPhotos(),
        error: (err) => console.error(err)
      });
    }
  }

  downloadAll() {
    this.apiService.downloadEventZip(this.eventId).subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `${this.eventSlug || 'gallery'}.zip`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => console.error(err)
    });
  }

  logout() {
    localStorage.removeItem('admin_event_id');
    localStorage.removeItem('admin_event_slug');
    localStorage.removeItem('admin_token');
    this.router.navigate(['/admin/login']);
  }
}
