# Cloudflare Tunnel — plan implementacji

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wystawić galerię przez tunel wychodzący Cloudflare zamiast przekierowania portów, które na tym łączu jest niemożliwe, i przerobić wysyłkę zdjęć tak, by limit 100 MB na żądanie przestał mieć znaczenie.

**Architecture:** `cloudflared` nawiązuje połączenie wychodzące do Cloudflare i kieruje ruch na `caddy:80`. Caddy traci obsługę TLS — zostaje przy routingu trzech ścieżek. Żaden kontener nie publikuje portów na hoście. Frontend wysyła zdjęcia po jednym pliku na żądanie, trzy równolegle, z rzeczywistym licznikiem postępu.

**Tech Stack:** .NET 8, Angular 17, PostgreSQL 16, Docker Compose, Caddy 2, cloudflared, RxJS

**Spec:** `docs/superpowers/specs/2026-08-26-cloudflare-tunnel-design.md`

## Global Constraints

- Ruch wchodzi wyłącznie przez tunel — **żadna usługa nie ma sekcji `ports`**
- Caddy nasłuchuje wewnętrznie na porcie 80, bez TLS i bez ACME
- Nazwa usługi Caddy'ego w sieci Dockera to `caddy` — tunel kieruje na `caddy:80`
- Jeden plik na żądanie, maksymalnie **trzy** równoległe żądania
- Token tunelu wyłącznie w `.env`; w repozytorium tylko `.env.example` z pustą wartością
- `ACME_EMAIL` przestaje być używany i wypada z konfiguracji
- Backend pozostaje **nietknięty** — żadnych zmian w `WeddingGallery.*`
- Commity w konwencji Conventional Commits
- Gałąź robocza: `feature/cloudflare-tunnel`, bazuje na `master` (775463f)

Uwaga o testach: repozytorium nie ma projektu testowego, a zmieniane pliki to konfiguracja i kod UI. Rolę czerwonej fazy pełnią konkretne komendy weryfikujące, uruchamiane przed zmianą i po niej. Każdy krok podaje oczekiwane wyjście.

---

### Task 1: Warstwa wejściowa — tunel zamiast portów

**Files:**
- Modify: `docker-compose.yml`
- Modify: `Caddyfile`
- Modify: `.env.example`

**Interfaces:**
- Consumes: nic z wcześniejszych zadań
- Produces: usługa `cloudflared` czytająca `CLOUDFLARE_TUNNEL_TOKEN`; `caddy` osiągalny wewnątrz sieci Dockera pod `caddy:80`, bez publikacji portów na hoście

- [ ] **Step 1: Pokaż stan wyjściowy**

```powershell
Select-String -Path docker-compose.yml -Pattern "ports|ACME"
Select-String -Path Caddyfile -Pattern "kasiaikrzys|email"
```

Oczekiwane: `caddy` publikuje `80:80` i `443:443`, w compose i w `Caddyfile` występuje konfiguracja ACME. Po tym zadaniu jedno i drugie ma zniknąć.

- [ ] **Step 2: Przepisz `Caddyfile`**

Cała zawartość. Przedrostek `http://` przy adresach wyłącza automatyczne HTTPS — bez niego Caddy próbowałby pozyskać certyfikat, którego nie potrzebuje i nie ma jak zweryfikować. Blok globalny z `email` znika razem z ACME. Wcięcia tabulatorami.

```caddyfile
# TLS terminates at Cloudflare; cloudflared reaches this container over the
# tunnel, so the http:// prefix disables automatic HTTPS and all ACME attempts.
http://www.{$DOMAIN} {
	redir https://{$DOMAIN}{uri} permanent
}

http://{$DOMAIN} {
	encode zstd gzip

	# API and uploaded photos are both served by the .NET container.
	handle /api/* {
		reverse_proxy api:8080
	}

	handle /photos/* {
		reverse_proxy api:8080
	}

	# Everything else is the Angular app, including guest deep links like /nasze-wesele.
	handle {
		reverse_proxy web:80
	}
}
```

Przekierowanie z `www` prowadzi na `https://`, mimo że sam Caddy mówi po HTTP — to celowe, bo adres w przeglądarce gościa jest obsługiwany po HTTPS przez Cloudflare.

- [ ] **Step 3: Przepisz `docker-compose.yml`**

Cała zawartość:

```yaml
services:
  db:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: ${POSTGRES_DB:?set POSTGRES_DB in .env}
      POSTGRES_USER: ${POSTGRES_USER:?set POSTGRES_USER in .env}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}
    volumes:
      - ${DATA_ROOT:?set DATA_ROOT in .env}/db:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER} -d ${POSTGRES_DB}"]
      interval: 5s
      timeout: 5s
      retries: 10

  api:
    build:
      context: .
      dockerfile: WeddingGallery.Api/Dockerfile
    restart: unless-stopped
    environment:
      - ConnectionStrings__DefaultConnection=Host=db;Database=${POSTGRES_DB:?set POSTGRES_DB in .env};Username=${POSTGRES_USER:?set POSTGRES_USER in .env};Password=${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}
      - ASPNETCORE_ENVIRONMENT=Production
      - AdminSettings__Password=${ADMIN_PASSWORD:?set ADMIN_PASSWORD in .env}
      - AdminSettings__Token=${ADMIN_TOKEN:?set ADMIN_TOKEN in .env}
    # PhotoService writes to wwwroot/photos and UseStaticFiles serves them from there,
    # so the volume has to land on exactly that path.
    volumes:
      - ${DATA_ROOT:?set DATA_ROOT in .env}/photos:/app/wwwroot/photos
    depends_on:
      db:
        condition: service_healthy

  web:
    build:
      context: ./ClientApp
      dockerfile: Dockerfile
    restart: unless-stopped
    depends_on:
      - api

  # No published ports anywhere: the carrier runs DS-Lite, so ports 80 and 443 are
  # not obtainable on this connection. The tunnel is the only way in, and it is
  # established from the inside out.
  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    environment:
      - DOMAIN=${DOMAIN:?set DOMAIN in .env}
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_config:/config
    depends_on:
      - api
      - web

  cloudflared:
    image: cloudflare/cloudflared:latest
    restart: unless-stopped
    command: tunnel --no-autoupdate run --token ${CLOUDFLARE_TUNNEL_TOKEN:?set CLOUDFLARE_TUNNEL_TOKEN in .env}
    depends_on:
      - caddy

volumes:
  caddy_config:
```

Wolumen `caddy_data` znika — przechowywał wyłącznie certyfikaty, których już nie ma. `caddy_config` zostaje, bo Caddy zapisuje tam swoją aktywną konfigurację.

- [ ] **Step 4: Zaktualizuj `.env.example`**

Cała zawartość:

```dotenv
# Copy to .env on the server and fill in. Never commit .env.
# Generate secrets with: openssl rand -hex 24

DOMAIN=kasiaikrzys.pl

# Cloudflare Zero Trust -> Networks -> Tunnels -> your tunnel -> the value
# after --token in the install command. Treat it like a password: anyone
# holding it can run a connector for this tunnel.
CLOUDFLARE_TUNNEL_TOKEN=

POSTGRES_DB=wedding_gallery
POSTGRES_USER=wedding_user
POSTGRES_PASSWORD=

ADMIN_PASSWORD=
ADMIN_TOKEN=

# Host directory holding the database and the uploaded photos.
# Local development: ./data
DATA_ROOT=/srv/wedding
```

- [ ] **Step 5: Zwaliduj konfigurację**

```powershell
@"
DOMAIN=kasiaikrzys.pl
CLOUDFLARE_TUNNEL_TOKEN=dummy-token-for-validation
POSTGRES_DB=wedding_gallery
POSTGRES_USER=wedding_user
POSTGRES_PASSWORD=localdev
ADMIN_PASSWORD=localdev
ADMIN_TOKEN=localdev
DATA_ROOT=./data
"@ | Out-File -Encoding utf8 .env
docker compose config
```

Oczekiwane: konfiguracja wypisana bez ostrzeżeń. Sprawdź wzrokiem, że **żadna** usługa nie ma sekcji `ports` i że `cloudflared` dostał token w `command`.

- [ ] **Step 6: Potwierdź, że brak wystawionych portów**

```powershell
docker compose config | Select-String -Pattern "ports:" -Context 2,2
```

Oczekiwane: brak trafień. Jedno trafienie oznacza, że coś nadal nasłuchuje na hoście.

- [ ] **Step 7: Zbuduj obrazy**

```powershell
docker compose build
```

Oczekiwane: `api` i `web` budują się bez błędu. `caddy` i `cloudflared` są pobierane, nie budowane.

- [ ] **Step 8: Commit**

```bash
git add docker-compose.yml Caddyfile .env.example
git commit -m "feat: expose the gallery through a Cloudflare tunnel instead of port forwarding"
```

---

### Task 2: Wysyłka zdjęć po jednym pliku

To zadanie dotyka ścieżki, którą przechodzi każdy gość. Poza główną zmianą usuwa atrapę ekranu postępu i naprawia błąd w kolejności podglądów — oba siedzą w tym samym kodzie i zostawienie ich oznaczałoby dwa konkurencyjne wskaźniki postępu oraz przycisk „usuń", który kasuje nie to zdjęcie, które gość kliknął.

**Files:**
- Modify: `ClientApp/src/app/services/api.service.ts`
- Modify: `ClientApp/src/app/image-picker/image-picker.component.ts`
- Modify: `ClientApp/src/app/image-picker/image-picker.component.html`
- Modify: `ClientApp/src/app/app.routes.ts`
- Delete: `ClientApp/src/app/upload-progress/upload-progress.component.ts`
- Delete: `ClientApp/src/app/upload-progress/upload-progress.component.html`
- Delete: `ClientApp/src/app/upload-progress/upload-progress.component.css`
- Delete: `ClientApp/src/app/upload-progress/upload-progress.component.spec.ts`

**Interfaces:**
- Consumes: nic z Task 1
- Produces: `ApiService.uploadPhoto(eventId: string, uploaderName: string, file: File): Observable<any>` — jeden plik na wywołanie; trasa `/uploading` przestaje istnieć

- [ ] **Step 1: Pokaż problem — jedno żądanie na wszystkie pliki**

```powershell
Select-String -Path ClientApp\src\app\services\api.service.ts -Pattern "uploadPhotos" -Context 0,10
```

Oczekiwane: `uploadPhotos` pętlą dokłada wszystkie pliki do jednego `FormData`. To jest żądanie, które Cloudflare odrzuci po przekroczeniu 100 MB.

- [ ] **Step 2: Zamień metodę w `ApiService`**

W `ClientApp/src/app/services/api.service.ts` zastąp całą metodę `uploadPhotos` poniższą. Reszta pliku bez zmian.

```typescript
  // One file per request: Cloudflare's free plan rejects requests over 100 MB,
  // and a single photo never comes close. The API endpoint takes a list, so we
  // send a list of one.
  uploadPhoto(eventId: string, uploaderName: string, file: File): Observable<any> {
    const formData = new FormData();
    formData.append('eventId', eventId);
    formData.append('uploaderName', uploaderName);
    formData.append('files', file);
    return this.http.post(`${this.baseUrl}/photos/upload`, formData);
  }
```

- [ ] **Step 3: Przepisz `ImagePickerComponent`**

Cała zawartość `ClientApp/src/app/image-picker/image-picker.component.ts`:

```typescript
import { Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { from, of } from 'rxjs';
import { catchError, finalize, map, mergeMap, tap } from 'rxjs/operators';
import { ApiService } from '../services/api.service';

interface SelectedPhoto {
  file: File;
  previewUrl: string;
  failed: boolean;
}

// Guests are on venue wifi or LTE, where dozens of parallel connections lower
// real throughput and cause timeouts. Three at a time is the compromise.
const MAX_PARALLEL_UPLOADS = 3;

@Component({
  selector: 'app-image-picker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './image-picker.component.html',
  styleUrl: './image-picker.component.css'
})
export class ImagePickerComponent implements OnDestroy {
  @ViewChild('fileInput') fileInput!: ElementRef;
  guestName = localStorage.getItem('guest_name') || '';
  photos: SelectedPhoto[] = [];
  eventId = localStorage.getItem('guest_event_id') || '';
  isUploading = false;
  uploadedCount = 0;
  totalCount = 0;
  failedCount = 0;

  // Photos already accepted by the server, so a retry only sends what is missing.
  private succeeded = new Set<SelectedPhoto>();

  constructor(private apiService: ApiService, private router: Router) {
    if (!this.eventId) {
      this.router.navigate(['/']);
    }
  }

  ngOnDestroy() {
    this.photos.forEach(photo => URL.revokeObjectURL(photo.previewUrl));
  }

  triggerFileInput() {
    this.fileInput.nativeElement.click();
  }

  onFilesSelected(event: any) {
    const files: FileList = event.target.files;
    for (let i = 0; i < files.length; i++) {
      // createObjectURL is synchronous, so the preview stays paired with its file.
      // FileReader resolved out of order and made "remove" delete the wrong photo.
      this.photos.push({
        file: files[i],
        previewUrl: URL.createObjectURL(files[i]),
        failed: false
      });
    }
    event.target.value = '';
  }

  removePhoto(index: number) {
    URL.revokeObjectURL(this.photos[index].previewUrl);
    this.succeeded.delete(this.photos[index]);
    this.photos.splice(index, 1);
  }

  uploadFiles() {
    if (this.photos.length === 0 || !this.guestName || this.isUploading) return;

    localStorage.setItem('guest_name', this.guestName);

    const queue = this.photos.filter(photo => !this.succeeded.has(photo));
    this.isUploading = true;
    this.uploadedCount = 0;
    this.failedCount = 0;
    this.totalCount = queue.length;
    queue.forEach(photo => (photo.failed = false));

    from(queue).pipe(
      mergeMap(photo =>
        this.apiService.uploadPhoto(this.eventId, this.guestName, photo.file).pipe(
          map(() => ({ photo, ok: true })),
          // One failed photo must not abort the rest; collect it for a retry.
          catchError(() => of({ photo, ok: false }))
        ),
        MAX_PARALLEL_UPLOADS
      ),
      tap(result => {
        this.uploadedCount++;
        if (result.ok) {
          this.succeeded.add(result.photo);
        } else {
          result.photo.failed = true;
          this.failedCount++;
        }
      }),
      finalize(() => (this.isUploading = false))
    ).subscribe({
      complete: () => {
        if (this.failedCount === 0) {
          this.router.navigate(['/feed']);
        }
      }
    });
  }

  goToFeed() {
    this.router.navigate(['/feed']);
  }
}
```

- [ ] **Step 4: Przepisz szablon**

Cała zawartość `ClientApp/src/app/image-picker/image-picker.component.html`:

```html
<div class="flex flex-col min-h-screen p-6 pb-24">

  <!-- Header -->
  <div class="mb-6 text-center">
    <h2 class="text-2xl font-light text-navy-blue tracking-wide">Wybierz Zdjęcia</h2>
  </div>

  <!-- Name Input -->
  <div class="mb-8">
    <input type="text" [(ngModel)]="guestName" placeholder="Twoje Imię/Pseudonim" [disabled]="isUploading"
           class="w-full px-4 py-3 text-lg bg-white/60 border border-white rounded-lg shadow-sm focus:outline-none focus:ring-1 focus:ring-teal-primary text-navy-blue placeholder-navy-blue/40 disabled:opacity-60">
  </div>

  <input type="file" multiple accept="image/*" #fileInput (change)="onFilesSelected($event)" class="hidden">

  <!-- Image Grid -->
  <div class="grid grid-cols-3 gap-2 flex-1 content-start mb-8">

    <div *ngFor="let photo of photos; let i = index"
         class="aspect-square bg-white/40 border-[1.5px] relative"
         [class.border-teal-primary]="!photo.failed"
         [class.border-red-500]="photo.failed">
      <img [src]="photo.previewUrl" class="w-full h-full object-cover">
      <button *ngIf="!isUploading" (click)="removePhoto(i)"
              class="absolute top-1 right-1 w-5 h-5 bg-red-500 rounded-full flex items-center justify-center text-white text-xs shadow">
        &times;
      </button>
      <div *ngIf="photo.failed" class="absolute bottom-0 left-0 right-0 bg-red-500/90 text-white text-[10px] text-center py-0.5">
        Nie wysłano
      </div>
    </div>

    <!-- Add More Placeholder -->
    <div *ngIf="!isUploading" (click)="triggerFileInput()"
         class="aspect-square bg-white/40 border-2 border-dashed border-teal-primary/40 flex flex-col items-center justify-center cursor-pointer hover:bg-white/60">
      <span class="text-teal-primary text-2xl font-light">+</span>
    </div>
  </div>

  <!-- Bottom Actions Fixed -->
  <div class="fixed bottom-0 left-0 right-0 p-6 bg-gradient-to-t from-dusty-teal via-dusty-teal to-transparent space-y-3">

    <div *ngIf="isUploading" class="space-y-2">
      <div class="h-1 bg-navy-blue/10 rounded-full overflow-hidden">
        <div class="h-full bg-eucalyptus transition-all duration-300"
             [style.width.%]="totalCount ? (uploadedCount / totalCount) * 100 : 0"></div>
      </div>
      <p class="text-center text-sm text-navy-blue/70 font-light">
        Zapisujemy Twoje chwile… {{ uploadedCount }} z {{ totalCount }}
      </p>
    </div>

    <p *ngIf="!isUploading && failedCount > 0" class="text-center text-sm text-red-600 font-light">
      Nie udało się wysłać {{ failedCount }} zdjęć. Spróbuj ponownie — wyślemy tylko te brakujące.
    </p>

    <button (click)="uploadFiles()" [disabled]="photos.length === 0 || !guestName || isUploading"
            class="btn-primary w-full disabled:opacity-50">
      {{ isUploading ? 'Wysyłanie…' : (failedCount > 0 ? 'Ponów wysyłkę' : 'Wyślij ' + photos.length + ' Zdjęć') }}
    </button>

    <button *ngIf="!isUploading && failedCount > 0" (click)="goToFeed()"
            class="btn-secondary w-full">
      Przejdź do galerii
    </button>
  </div>

</div>
```

- [ ] **Step 5: Usuń atrapę ekranu postępu**

```powershell
Remove-Item ClientApp\src\app\upload-progress -Recurse -Force
```

W `ClientApp/src/app/app.routes.ts` usuń import `UploadProgressComponent` oraz wiersz trasy:

```typescript
  { path: 'uploading', component: UploadProgressComponent },
```

Pozostałe trasy zostają bez zmian.

- [ ] **Step 6: Zbuduj frontend**

```powershell
cd ClientApp; npm run build -- --configuration=production; cd ..
```

Oczekiwane: build kończy się sukcesem. Błąd o nieznalezionym `UploadProgressComponent` oznacza, że w `app.routes.ts` został import albo trasa.

- [ ] **Step 7: Potwierdź, że stara metoda zniknęła**

```powershell
Select-String -Path ClientApp\src -Pattern "uploadPhotos|uploading" -Recurse
```

Oczekiwane: brak trafień w plikach `.ts` i `.html`.

- [ ] **Step 8: Commit**

```bash
git add ClientApp/src/app
git commit -m "feat: upload photos one per request with real progress and per-file retry"
```

---

### Task 3: Runbook — rozdział o tunelu

**Files:**
- Modify: `docs/DEPLOYMENT.md`

**Interfaces:**
- Consumes: nazwy zmiennych z Task 1, zachowanie wysyłki z Task 2
- Produces: dokument opisujący wdrożenie przez tunel zamiast przekierowania portów

- [ ] **Step 1: Zaktualizuj dokument**

Zmiany, sekcja po sekcji. Reszta dokumentu zostaje.

1. **Wymagania wstępne** — zamiast przekierowania portów 80/443 i rekordów A w az.pl: konto Cloudflare, domena dodana w planie darmowym, serwery nazw w az.pl podmienione na wskazane przez Cloudflare, tunel utworzony w Zero Trust. Dopisz, że rejestracja domeny zostaje w az.pl.
2. **Weryfikacja przed uruchomieniem** — zamiast `curl -4 ifconfig.me` i testu portu 80: `nslookup -type=NS kasiaikrzys.pl 1.1.1.1` musi zwracać `*.ns.cloudflare.com`, a domena musi mieć w panelu status **Active**.
3. **Konfiguracja sekretów** — dopisz `CLOUDFLARE_TUNNEL_TOKEN` z ostrzeżeniem, że ma siłę hasła i przy wycieku należy usunąć tunel i utworzyć go na nowo. Usuń `ACME_EMAIL`.
4. **Pierwsze uruchomienie** — `docker compose up -d --build`, potem `docker compose logs -f cloudflared` i oczekiwanie na wpis o zarejestrowanym połączeniu. **Nowy krok:** dopiero gdy tunel ma status Healthy, w panelu Zero Trust dodaje się dwie nazwy hosta — `kasiaikrzys.pl` i `www.kasiaikrzys.pl`, obie typu HTTP na `caddy:80`. Zaznacz wyraźnie, że zakładka Public Hostname jest niedostępna, dopóki konektor się nie zamelduje.
5. **Lista kontrolna** — zamiast sprawdzania certyfikatu Let's Encrypt: wysyłka dwudziestu zdjęć naraz z licznikiem postępu; zachowanie po rozłączeniu WiFi w trakcie wysyłki (część zdjęć oznaczona jako nieudana, przycisk ponowienia wysyła tylko je); `sudo ss -tlnp | grep -E ':(80|443)\s'` **nie zwraca nic**, bo żaden kontener nie publikuje portów.
6. **Kopie zapasowe** — usuń akapit o wolumenie `caddy_data` i limicie pięciu certyfikatów tygodniowo; nie dotyczą już niczego. Dopisz, że `.env` z tokenem tunelu należy przechowywać poza serwerem.
7. **Diagnostyka** — usuń wpisy o niewydanym certyfikacie. Dodaj: strona zwraca błąd 502 z Cloudflare (konektor działa, ale `caddy` nie odpowiada — `docker compose ps`); tunel w stanie Down (`docker compose logs cloudflared`, najczęściej błędny token); strona nieosiągalna mimo działającego tunelu (brak nazwy hosta w Public Hostname albo wskazanie na zły adres); część zdjęć nie dochodzi (sprawdź konsolę przeglądarki — kod 413 oznaczałby, że któreś pojedyncze zdjęcie przekracza limit).
8. **Znane ograniczenia** — zamiast zmiennego adresu IP: tunel jest pojedynczym punktem awarii, ratunkiem jest `docker compose restart cloudflared`; ruch przechodzi przez Cloudflare, który go odszyfrowuje i widzi przesyłane zdjęcia; darmowy plan tnie żądania na 100 MB, stąd wysyłka po jednym pliku. Zostaw wpis o braku miniatur.
9. **Dopisz krótki rozdział „Powrót do przekierowania portów"** — gdyby operator udostępnił publiczny adres IPv4: usunąć `cloudflared` z compose, przywrócić `ports` i wersję `Caddyfile` z `https://`, wrócić z serwerami nazw do az.pl albo zostawić Cloudflare w trybie DNS-only. Odwołaj się do `docs/superpowers/specs/2026-08-25-production-deployment-design.md`.

Usuń też podrozdział o `DOMAIN=localhost` do testów lokalnych — dotyczył unikania limitu Let's Encrypt, który przestał istnieć. Do testów lokalnych wystarczy uruchomić wszystko bez `cloudflared`.

- [ ] **Step 2: Sprawdź, że nie zostały odwołania do nieistniejącej konfiguracji**

```powershell
Select-String -Path docs\DEPLOYMENT.md -Pattern "ACME_EMAIL|caddy_data|Let's Encrypt|przekierowanie port"
```

Oczekiwane: trafienia wyłącznie w nowym rozdziale o powrocie do przekierowania portów. Każde inne oznacza pozostałość po starym wariancie.

- [ ] **Step 3: Commit**

```bash
git add docs/DEPLOYMENT.md
git commit -m "docs: rewrite the runbook for tunnel-based deployment"
```

---

### Task 4: Weryfikacja całości

**Files:**
- Żadnych zmian — zadanie wyłącznie weryfikujące

**Interfaces:**
- Consumes: komplet zmian z Task 1–3
- Produces: potwierdzenie, że gałąź nadaje się do wdrożenia

- [ ] **Step 1: Backend nietknięty**

```powershell
git diff --name-only master...HEAD | Select-String -Pattern "WeddingGallery\."
```

Oczekiwane: brak trafień. Jakakolwiek zmiana w projektach backendu oznacza wyjście poza zakres.

- [ ] **Step 2: Backend nadal się kompiluje**

```powershell
dotnet build
```

Oczekiwane: `Build succeeded`, zero błędów.

- [ ] **Step 3: Frontend buduje się produkcyjnie**

```powershell
cd ClientApp; npm run build -- --configuration=production; cd ..
```

Oczekiwane: sukces, `dist/client-app/browser/index.html` istnieje.

- [ ] **Step 4: Brak adresów bezwzględnych w bundlu**

```powershell
Select-String -Path ClientApp\dist\client-app\browser\*.js -Pattern "localhost:5205"
```

Oczekiwane: brak trafień.

- [ ] **Step 5: Obrazy budują się, brak wystawionych portów**

```powershell
docker compose build
docker compose config | Select-String -Pattern "ports:"
```

Oczekiwane: build bez błędu, brak trafień na `ports:`.

- [ ] **Step 6: Sprzątanie**

```powershell
Remove-Item .env -ErrorAction SilentlyContinue
git status --porcelain
```

Oczekiwane: pusta odpowiedź.

---

## Kolejność i zależności

Task 1 i Task 2 są niezależne — pierwszy dotyka wyłącznie infrastruktury, drugi wyłącznie frontendu. Task 3 wymaga obu. Task 4 zamyka.

## Poza zakresem planu

Po stronie Cloudflare i az.pl, opisane w runbooku, a nie w kodzie: założenie konta, dodanie domeny, usunięcie zaimportowanych rekordów A, podmiana serwerów nazw, utworzenie tunelu, skopiowanie tokenu do `.env`, dodanie dwóch nazw hosta po zameldowaniu się konektora, usunięcie reguł przekierowania w routerze.

Niezmiennie otwarte: nieuwierzytelniony `GET /api/photos/event/{id}/download`.
