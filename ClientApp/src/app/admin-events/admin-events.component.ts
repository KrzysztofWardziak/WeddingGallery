import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AdminEvent, ApiService } from '../services/api.service';

@Component({
  selector: 'app-admin-events',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule],
  templateUrl: './admin-events.component.html',
  styleUrl: './admin-events.component.css'
})
export class AdminEventsComponent implements OnInit {
  events: AdminEvent[] = [];
  isLoading = true;
  error = '';

  // Deletion is irreversible, so it happens behind a dialog that requires retyping the
  // event's name rather than a single tap that could be triggered by accident.
  eventToDelete: AdminEvent | null = null;
  confirmName = '';
  isDeleting = false;
  deleteError = '';

  constructor(private apiService: ApiService, private router: Router) {}

  ngOnInit() {
    this.loadEvents();
  }

  loadEvents() {
    this.isLoading = true;
    this.error = '';
    this.apiService.getAdminEvents().subscribe({
      next: (events) => {
        this.events = events;
        this.isLoading = false;
      },
      error: (err) => {
        console.error(err);
        this.error = 'Nie udało się wczytać listy wydarzeń.';
        this.isLoading = false;
      }
    });
  }

  totalCount(event: AdminEvent): number {
    return event.photoCount + event.videoCount;
  }

  askToDelete(event: AdminEvent) {
    this.eventToDelete = event;
    this.confirmName = '';
    this.deleteError = '';
  }

  cancelDelete() {
    if (this.isDeleting) return;
    this.eventToDelete = null;
    this.confirmName = '';
    this.deleteError = '';
  }

  // Case and padding are forgiven here and on the server: the admin is retyping a name off
  // the screen, usually on a keyboard that capitalises for them.
  get canConfirmDelete(): boolean {
    if (!this.eventToDelete || this.isDeleting) return false;
    return this.confirmName.trim().toLowerCase() === this.eventToDelete.name.trim().toLowerCase();
  }

  confirmDelete() {
    if (!this.eventToDelete || !this.canConfirmDelete) return;

    const target = this.eventToDelete;
    this.isDeleting = true;
    this.deleteError = '';

    this.apiService.deleteAdminEvent(target.id, this.confirmName).subscribe({
      next: () => {
        this.isDeleting = false;
        this.eventToDelete = null;
        this.confirmName = '';
        this.loadEvents();
      },
      error: (err: unknown) => {
        console.error(err);
        this.isDeleting = false;
        this.deleteError = err instanceof HttpErrorResponse && err.status === 400 && typeof err.error === 'string'
          ? err.error
          : 'Nie udało się usunąć wydarzenia.';
      }
    });
  }

  logout() {
    localStorage.removeItem('admin_token');
    this.router.navigate(['/admin/login']);
  }
}
