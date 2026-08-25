import { Routes } from '@angular/router';
import { WelcomeComponent } from './welcome/welcome.component';
import { ImagePickerComponent } from './image-picker/image-picker.component';
import { UploadProgressComponent } from './upload-progress/upload-progress.component';
import { GuestFeedComponent } from './guest-feed/guest-feed.component';
import { AdminSetupComponent } from './admin-setup/admin-setup.component';
import { AdminDashboardComponent } from './admin-dashboard/admin-dashboard.component';
import { AdminLoginComponent } from './admin-login/admin-login.component';
import { AdminGuard } from './admin.guard';
import { AdminPrintQrComponent } from './admin-print-qr/admin-print-qr.component';

export const routes: Routes = [
  { path: '', redirectTo: 'admin/setup', pathMatch: 'full' },
  { path: 'admin/login', component: AdminLoginComponent },
  { path: 'admin/setup', component: AdminSetupComponent, canActivate: [AdminGuard] },
  { path: 'admin', component: AdminDashboardComponent, canActivate: [AdminGuard] },
  { path: 'admin/print-qr', component: AdminPrintQrComponent, canActivate: [AdminGuard] },
  { path: 'pick', component: ImagePickerComponent },
  { path: 'uploading', component: UploadProgressComponent },
  { path: 'feed', component: GuestFeedComponent },
  { path: ':slug', component: WelcomeComponent },
  { path: '**', redirectTo: 'admin/setup' }
];
