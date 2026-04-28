import { Routes } from '@angular/router';
import { WelcomeComponent } from './welcome/welcome.component';
import { ImagePickerComponent } from './image-picker/image-picker.component';
import { UploadProgressComponent } from './upload-progress/upload-progress.component';
import { GuestFeedComponent } from './guest-feed/guest-feed.component';

export const routes: Routes = [
  { path: '', component: WelcomeComponent },
  { path: 'pick', component: ImagePickerComponent },
  { path: 'uploading', component: UploadProgressComponent },
  { path: 'feed', component: GuestFeedComponent },
  { path: '**', redirectTo: '' }
];
