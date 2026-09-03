import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { QRCodeModule } from 'angularx-qrcode';
import { ApiService } from '../services/api.service';
import { formatEventDate } from '../shared/event-date';

@Component({
  selector: 'app-admin-print-qr',
  standalone: true,
  imports: [CommonModule, QRCodeModule],
  templateUrl: './admin-print-qr.component.html',
  styleUrl: './admin-print-qr.component.css'
})
export class AdminPrintQrComponent implements OnInit {
  eventName = '';
  eventUrl = '';
  formattedDate = '';

  private eventId = '';

  constructor(
    private apiService: ApiService,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit() {
    this.eventId = this.route.snapshot.paramMap.get('id') || '';

    if (!this.eventId) {
      this.router.navigate(['/admin/events']);
      return;
    }

    this.apiService.getAdminEvent(this.eventId).subscribe({
      next: (event) => {
        this.eventName = event.name;
        this.formattedDate = formatEventDate(event.eventDate);
        this.eventUrl = `${window.location.origin}/${event.slug}`;
      },
      error: (err) => {
        console.error(err);
        this.router.navigate(['/admin/events']);
      }
    });
  }

  print() {
    window.print();
  }

  goBack() {
    this.router.navigate(['/admin/events', this.eventId]);
  }
}
