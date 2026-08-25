import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { QRCodeModule } from 'angularx-qrcode';

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

  constructor(private router: Router) {}

  ngOnInit() {
    const eventId = localStorage.getItem('admin_event_id');
    const slug = localStorage.getItem('admin_event_slug');
    const name = localStorage.getItem('admin_event_name');

    if (!eventId || !slug) {
      this.router.navigate(['/admin']);
      return;
    }

    this.eventName = name || 'Nasze Wesele';
    this.eventUrl = `${window.location.origin}/${slug}`;
  }

  print() {
    window.print();
  }

  goBack() {
    this.router.navigate(['/admin']);
  }
}
