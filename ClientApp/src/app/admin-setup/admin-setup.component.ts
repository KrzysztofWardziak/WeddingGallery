import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-admin-setup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-setup.component.html',
  styleUrl: './admin-setup.component.css'
})
export class AdminSetupComponent {
  eventName = '';
  // Bound to <input type="date">, which yields "YYYY-MM-DD" - exactly what DateOnly parses.
  eventDate = '';
  isLoading = false;

  // No redirect when an event already exists: bouncing the admin away from this form was
  // what made a second event impossible to create.
  constructor(private apiService: ApiService, private router: Router) {}

  createEvent() {
    if (!this.eventName) return;
    this.isLoading = true;
    // The date is optional, so an empty field goes over as null rather than "".
    this.apiService.createEvent(this.eventName, this.eventDate || null).subscribe({
      next: (res) => this.router.navigate(['/admin/events', res.id]),
      error: (err) => {
        console.error(err);
        this.isLoading = false;
      }
    });
  }
}
