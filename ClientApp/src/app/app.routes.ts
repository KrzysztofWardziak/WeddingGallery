import { Routes } from '@angular/router';
import { WelcomeComponent } from './welcome/welcome.component';
import { ImagePickerComponent } from './image-picker/image-picker.component';
import { GuestFeedComponent } from './guest-feed/guest-feed.component';
import { AdminSetupComponent } from './admin-setup/admin-setup.component';
import { AdminEventsComponent } from './admin-events/admin-events.component';
import { AdminDashboardComponent } from './admin-dashboard/admin-dashboard.component';
import { AdminLoginComponent } from './admin-login/admin-login.component';
import { AdminGuard } from './admin.guard';
import { AdminPrintQrComponent } from './admin-print-qr/admin-print-qr.component';

export const routes: Routes = [
  { path: '', redirectTo: 'admin/events', pathMatch: 'full' },
  { path: 'admin/login', component: AdminLoginComponent },
  { path: 'admin/setup', component: AdminSetupComponent, canActivate: [AdminGuard] },

  // The event id lives in the URL, not in localStorage: it is what makes a gallery
  // linkable, and what lets the admin hold more than one event at a time.
  { path: 'admin/events', component: AdminEventsComponent, canActivate: [AdminGuard] },
  { path: 'admin/events/:id', component: AdminDashboardComponent, canActivate: [AdminGuard] },
  { path: 'admin/events/:id/print-qr', component: AdminPrintQrComponent, canActivate: [AdminGuard] },

  // Keeps bookmarks to the old single-event dashboard working.
  { path: 'admin', redirectTo: 'admin/events', pathMatch: 'full' },

  { path: 'pick', component: ImagePickerComponent },
  { path: 'feed', component: GuestFeedComponent },
  { path: ':slug', component: WelcomeComponent },
  { path: '**', redirectTo: 'admin/events' }
];
