# Wdrożenie produkcyjne kasiaikrzys.pl — plan implementacji

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Doprowadzić repozytorium do stanu, w którym `docker compose up -d` na serwerze uruchamia komplet aplikacji pod `https://kasiaikrzys.pl` z automatycznym certyfikatem TLS, trwałym zapisem zdjęć i sekretami poza repozytorium.

**Architecture:** Wszystkie usługi żyją w sieci wewnętrznej Dockera. Na zewnątrz wystawiony jest wyłącznie Caddy, który terminuje TLS i kieruje `/api/*` oraz `/photos/*` do API, a resztę do kontenera z Angularem. Frontend nie zna żadnego adresu bezwzględnego, dzięki czemu całość działa pod jednym originem i CORS w produkcji nie występuje.

**Tech Stack:** .NET 8, Angular 17, PostgreSQL 16, Docker Compose, Caddy 2, nginx (alpine)

**Spec:** `docs/superpowers/specs/2026-08-25-production-deployment-design.md`

## Global Constraints

- Domena produkcyjna: `kasiaikrzys.pl`, z przekierowaniem 301 z `www.kasiaikrzys.pl`
- Katalog danych na serwerze: `/srv/wedding` (`db/`, `photos/`, `app/`)
- Zdjęcia montowane pod ścieżkę `/app/wwwroot/photos` — tam zapisuje `PhotoService` i tam szuka `UseStaticFiles`
- Limit żądania: 200 MB (`200L * 1024 * 1024`) w Kestrelu i `FormOptions`
- Żadnych sekretów w repozytorium — wyłącznie w `.env` na serwerze, w repo `.env.example`
- `ASPNETCORE_ENVIRONMENT=Production` w compose — Swagger tylko w środowisku deweloperskim
- Wszystkie usługi z `restart: unless-stopped`
- Commity w konwencji Conventional Commits
- Gałąź robocza: `feature/production-deployment`, bazuje na `fix/docker-api-port-and-admin-config`

Uwaga o testach: to zadanie infrastrukturalne — zmieniane pliki to konfiguracja i bootstrap aplikacji, których nie da się sensownie pokryć testem jednostkowym. Rolę czerwonej fazy pełnią tu konkretne komendy weryfikujące, uruchamiane **przed** zmianą (mają zawieść lub pokazać zły wynik) i **po** niej. Każdy krok podaje oczekiwane wyjście.

---

### Task 1: Frontend — adresy względne i obsługa deep-linków

Bez tego zadania build produkcyjny woła `http://localhost:5205`, a wejście z kodu QR na `kasiaikrzys.pl/<slug>` zwraca 404 przy odświeżeniu strony.

**Files:**
- Modify: `ClientApp/src/environments/environment.ts`
- Create: `ClientApp/nginx.conf`
- Modify: `ClientApp/Dockerfile`

**Interfaces:**
- Consumes: nic — pierwsze zadanie
- Produces: obraz kontenera `web` nasłuchujący na porcie 80, serwujący SPA z fallbackiem na `index.html`; `environment.apiUrl === '/api'`, `environment.imagesUrl === ''`

- [ ] **Step 1: Pokaż problem — sprawdź, co siedzi w buildzie produkcyjnym**

```powershell
Select-String -Path ClientApp\src\environments\environment.ts -Pattern "localhost"
```

Oczekiwane: dwa trafienia z `http://localhost:5205`. To adres, który trafiłby do przeglądarki gościa.

- [ ] **Step 2: Przestaw frontend na adresy względne**

Cała zawartość `ClientApp/src/environments/environment.ts`:

```typescript
export const environment = {
  production: true,
  apiUrl: '/api',
  imagesUrl: ''
};
```

`environment.development.ts` zostaje bez zmian — `ng serve` nadal woła `http://localhost:5205`.

- [ ] **Step 3: Zweryfikuj, że localhost zniknął z buildu produkcyjnego**

```powershell
Select-String -Path ClientApp\src\environments\environment.ts -Pattern "localhost"
```

Oczekiwane: brak trafień.

- [ ] **Step 4: Dodaj konfigurację nginx**

Utwórz `ClientApp/nginx.conf`:

```nginx
server {
    listen 80;
    server_name _;
    root /usr/share/nginx/html;
    index index.html;

    gzip on;
    gzip_min_length 1024;
    gzip_types text/css application/javascript application/json image/svg+xml;

    # Pliki z hashem w nazwie nigdy nie zmieniają treści — można je cache'ować agresywnie.
    location ~* \.(?:js|css|woff2?|png|jpe?g|gif|svg|ico)$ {
        expires 1y;
        add_header Cache-Control "public, immutable";
        try_files $uri =404;
    }

    # index.html musi być zawsze świeży, inaczej przeglądarka utknie na starym bundlu.
    location = /index.html {
        add_header Cache-Control "no-cache";
    }

    # Angular obsługuje routing po stronie klienta: /<slug> musi trafić do index.html,
    # inaczej odświeżenie strony z kodu QR kończy się błędem 404.
    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

- [ ] **Step 5: Wgraj konfigurację do obrazu**

W `ClientApp/Dockerfile` zmień etap budowania i etap nginx. Linia `RUN npm run build --configuration=production` jest myląca — npm zjada flagę i nie przekazuje jej do `ng`; działa wyłącznie dlatego, że `production` jest domyślne w `angular.json`. Zapis z `--` jest jednoznaczny.

```dockerfile
# Use the official Node.js image to build the Angular app
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build -- --configuration=production

# Use Nginx to serve the compiled Angular app
FROM nginx:alpine
COPY --from=build /app/dist/client-app/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

- [ ] **Step 6: Zbuduj obraz i sprawdź fallback**

```powershell
docker build -t wedding-web-test ./ClientApp
docker run -d --name wedding-web-test -p 8081:80 wedding-web-test
curl.exe -s -o NUL -w "%{http_code}" http://localhost:8081/dowolny-slug
```

Oczekiwane: `200`. Przed dodaniem `nginx.conf` ta sama komenda zwróciłaby `404`.

Sprzątanie:

```powershell
docker rm -f wedding-web-test; docker rmi wedding-web-test
```

- [ ] **Step 7: Commit**

```bash
git add ClientApp/src/environments/environment.ts ClientApp/nginx.conf ClientApp/Dockerfile
git commit -m "feat: serve SPA from nginx with deep-link fallback and relative API urls"
```

---

### Task 2: Backend — limity uploadu, CORS z konfiguracji, sekrety poza repo

**Files:**
- Modify: `WeddingGallery.Api/Program.cs`
- Modify: `WeddingGallery.Api/appsettings.json`
- Modify: `WeddingGallery.Api/appsettings.Development.json`

**Interfaces:**
- Consumes: nic z Task 1
- Produces: API czytające `AdminSettings:Password`, `AdminSettings:Token` oraz `Cors:AllowedOrigins` wyłącznie z konfiguracji środowiskowej; przyjmujące żądania do 200 MB; przerywające start poza środowiskiem deweloperskim, gdy sekrety są puste

- [ ] **Step 1: Pokaż problem — sekrety w repozytorium**

```powershell
Select-String -Path WeddingGallery.Api\appsettings.json -Pattern "admin|secret-admin-token"
```

Oczekiwane: trafienia na `"Password": "admin"` i `"Token": "secret-admin-token-12345"`.

- [ ] **Step 2: Wyczyść `appsettings.json`**

Cała zawartość `WeddingGallery.Api/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "AdminSettings": {
    "Password": "",
    "Token": ""
  },
  "Cors": {
    "AllowedOrigins": []
  }
}
```

- [ ] **Step 3: Uzupełnij konfigurację deweloverską**

Cała zawartość `WeddingGallery.Api/appsettings.Development.json`. To jedyne miejsce, gdzie słabe hasło jest w porządku — plik służy wyłącznie pracy lokalnej i nigdy nie trafia na serwer.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=weddinggallery;Username=postgres;Password=admin123"
  },
  "AdminSettings": {
    "Password": "admin",
    "Token": "dev-token"
  },
  "Cors": {
    "AllowedOrigins": [ "http://localhost:4200" ]
  }
}
```

- [ ] **Step 4: Przepisz `Program.cs`**

Cała zawartość `WeddingGallery.Api/Program.cs`. Zmiany względem obecnej wersji: nowy `using`, limity Kestrela i `FormOptions`, CORS z konfiguracji zamiast `AllowAnyOrigin`, oraz kontrola sekretów przy starcie.

```csharp
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Application.Services;
using WeddingGallery.Domain.Interfaces;
using WeddingGallery.Infrastructure.Data;
using WeddingGallery.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Guests upload several full-size phone photos in one request; Kestrel's 30 MB default is too low.
const long MaxUploadBytes = 200L * 1024 * 1024;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxUploadBytes;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes;
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<WeddingGalleryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<IPhotoRepository, PhotoRepository>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IPhotoService, PhotoService>();

// In production the SPA is served from the same origin, so the list is empty and no CORS
// headers are emitted at all. Development keeps ng serve on localhost:4200.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                  ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var app = builder.Build();

// Admin credentials come from the environment. Empty values would let anyone authenticate
// with an empty password, so refuse to start instead of running wide open.
if (!app.Environment.IsDevelopment())
{
    foreach (var key in new[] { "AdminSettings:Password", "AdminSettings:Token" })
    {
        if (string.IsNullOrWhiteSpace(app.Configuration[key]))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it in .env before starting the container.");
        }
    }
}

// Apply pending EF Core migrations on startup so the schema exists in a fresh container.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WeddingGalleryDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    const int maxAttempts = 10;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            db.Database.Migrate();
            logger.LogInformation("Database migrations applied.");
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{MaxAttempts}), retrying in 3s...", attempt, maxAttempts);
            Thread.Sleep(TimeSpan.FromSeconds(3));
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// TLS is terminated by Caddy in front of this container, so no redirect here.
app.UseStaticFiles(); // Serve photos
app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

- [ ] **Step 5: Zbuduj backend**

```powershell
dotnet build
```

Oczekiwane: `Build succeeded`, zero błędów.

- [ ] **Step 6: Sprawdź, że sekrety zniknęły z repozytorium**

```powershell
git grep -n "secret-admin-token-12345"
```

Oczekiwane: brak trafień poza katalogiem `docs/` i plikami `bin/` (te ostatnie są ignorowane przez git).

- [ ] **Step 7: Commit**

```bash
git add WeddingGallery.Api/Program.cs WeddingGallery.Api/appsettings.json WeddingGallery.Api/appsettings.Development.json
git commit -m "feat: raise upload limits, scope CORS to configured origins, move admin secrets to environment"
```

---

### Task 3: Caddy i szablon sekretów

**Files:**
- Create: `Caddyfile`
- Create: `.env.example`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: nazwy usług `api` (port 8080) i `web` (port 80) — muszą zgadzać się z Task 4
- Produces: zmienne `DOMAIN`, `ACME_EMAIL`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `ADMIN_PASSWORD`, `ADMIN_TOKEN`, `DATA_ROOT` — używane przez `docker-compose.yml` w Task 4

- [ ] **Step 1: Utwórz `Caddyfile`**

```caddyfile
{
	email {$ACME_EMAIL}
}

# One canonical address: everything on www redirects to the bare domain.
www.{$DOMAIN} {
	redir https://{$DOMAIN}{uri} permanent
}

{$DOMAIN} {
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

Wcięcia w `Caddyfile` muszą być tabulatorami — Caddy odrzuca mieszanie tabulatorów ze spacjami.

- [ ] **Step 2: Utwórz `.env.example`**

```dotenv
# Copy to .env on the server and fill in. Never commit .env.
# Generate secrets with: openssl rand -base64 32

DOMAIN=kasiaikrzys.pl
ACME_EMAIL=krzysztof.wardziak@oke.pl

POSTGRES_DB=wedding_gallery
POSTGRES_USER=wedding_user
POSTGRES_PASSWORD=

ADMIN_PASSWORD=
ADMIN_TOKEN=

# Host directory holding the database and the uploaded photos.
# Local development: ./data
DATA_ROOT=/srv/wedding
```

- [ ] **Step 3: Dopisz `.env` do `.gitignore`**

W sekcji `# Secrets / Environment overrides` w `.gitignore`, pod `appsettings.Development.json`, dodaj:

```gitignore
.env
data/
```

- [ ] **Step 4: Sprawdź, że git ignoruje `.env`**

```powershell
"POSTGRES_PASSWORD=test" | Out-File -Encoding utf8 .env
git status --porcelain .env
```

Oczekiwane: pusta odpowiedź. Gdyby `.env` się pojawił, reguła nie działa.

- [ ] **Step 5: Commit**

```bash
git add Caddyfile .env.example .gitignore
git commit -m "feat: add Caddy reverse proxy config and environment template"
```

---

### Task 4: Przepisanie `docker-compose.yml`

**Files:**
- Modify: `docker-compose.yml`

**Interfaces:**
- Consumes: zmienne z `.env.example` (Task 3), `Caddyfile` (Task 3), obraz `web` z `ClientApp/Dockerfile` (Task 1), zmienne `AdminSettings__*` czytane przez `Program.cs` (Task 2)
- Produces: cztery usługi — `db`, `api`, `web`, `caddy` — gdzie tylko `caddy` publikuje porty

- [ ] **Step 1: Pokaż problem — wolumen zdjęć wskazuje na złą ścieżkę**

```powershell
Select-String -Path docker-compose.yml -Pattern "photo_storage"
Select-String -Path WeddingGallery.Application\Services\PhotoService.cs -Pattern "_uploadPath"
```

Oczekiwane: compose montuje wolumen na `/app/photos`, a `PhotoService` zapisuje do `wwwroot/photos`. Rozjazd oznacza, że dziś wszystkie uploady giną przy odtworzeniu kontenera.

- [ ] **Step 2: Przepisz `docker-compose.yml`**

Cała zawartość. Klucz `version` zniknął — jest przestarzały i nowy Compose wypisuje o nim ostrzeżenie.

```yaml
services:
  db:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: ${POSTGRES_DB}
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - ${DATA_ROOT}/db:/var/lib/postgresql/data
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
      - ConnectionStrings__DefaultConnection=Host=db;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}
      - ASPNETCORE_ENVIRONMENT=Production
      - AdminSettings__Password=${ADMIN_PASSWORD}
      - AdminSettings__Token=${ADMIN_TOKEN}
    # PhotoService writes to wwwroot/photos and UseStaticFiles serves them from there,
    # so the volume has to land on exactly that path.
    volumes:
      - ${DATA_ROOT}/photos:/app/wwwroot/photos
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

  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    environment:
      - DOMAIN=${DOMAIN}
      - ACME_EMAIL=${ACME_EMAIL}
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
      - caddy_config:/config
    depends_on:
      - api
      - web

volumes:
  caddy_data:
  caddy_config:
```

`caddy_data` musi być trwały — trzyma wystawione certyfikaty. Bez niego każdy restart pyta Let's Encrypt o nowy certyfikat i szybko trafisz na limit pięciu wydań tygodniowo dla tej samej domeny.

- [ ] **Step 3: Załóż lokalny `.env` do weryfikacji**

```powershell
@"
DOMAIN=kasiaikrzys.pl
ACME_EMAIL=krzysztof.wardziak@oke.pl
POSTGRES_DB=wedding_gallery
POSTGRES_USER=wedding_user
POSTGRES_PASSWORD=localdev
ADMIN_PASSWORD=localdev
ADMIN_TOKEN=localdev
DATA_ROOT=./data
"@ | Out-File -Encoding utf8 .env
```

- [ ] **Step 4: Zwaliduj podstawienia**

```powershell
docker compose config
```

Oczekiwane: wypisana konfiguracja bez ostrzeżeń `variable is not set`. Sprawdź wzrokiem, że `api` montuje `./data/photos:/app/wwwroot/photos` i że ani `db`, ani `api`, ani `web` nie mają sekcji `ports`.

- [ ] **Step 5: Zbuduj oba obrazy**

```powershell
docker compose build
```

Oczekiwane: `web` i `api` budują się bez błędu.

- [ ] **Step 6: Commit**

```bash
git add docker-compose.yml
git commit -m "feat: put services behind Caddy, drop exposed ports, fix photo volume path"
```

---

### Task 5: Runbook wdrożeniowy

**Files:**
- Create: `docs/DEPLOYMENT.md`

**Interfaces:**
- Consumes: wszystko z Task 1–4
- Produces: dokument, z którego da się odtworzyć wdrożenie bez tej rozmowy

- [ ] **Step 1: Napisz `docs/DEPLOYMENT.md`**

Dokument ma zawierać dokładnie te sekcje, z komendami gotowymi do wklejenia:

1. **Wymagania wstępne** — VM Ubuntu 24.04 pod `192.168.1.41`, Docker, `/srv/wedding/{db,photos,app}`, przekierowanie portów 80/443, rekordy A dla `@` i `www` wskazujące na adres publiczny, serwery nazw `ns6/ns7/ns8.az.pl` bez zmian
2. **Weryfikacja przed pierwszym uruchomieniem** — `nslookup kasiaikrzys.pl 1.1.1.1` musi zwrócić adres publiczny serwera; `curl -4 ifconfig.me` na VM musi zwrócić ten sam adres; test dostępności portu 80 z sieci komórkowej
3. **Konfiguracja sekretów** — `cp .env.example .env`, generowanie wartości przez `openssl rand -base64 32`, `chmod 600 .env`
4. **Pierwsze uruchomienie** — `docker compose up -d --build`, `docker compose logs -f caddy` i oczekiwanie na wpis o wydanym certyfikacie
5. **Lista kontrolna po wdrożeniu** — wejście na `https://kasiaikrzys.pl`, przekierowanie z `www`, założenie wydarzenia w panelu, wydruk QR, upload kilku zdjęć naraz z telefonu, odświeżenie strony pod adresem `/<slug>`, `docker compose restart api` i sprawdzenie, że zdjęcia nadal są widoczne, `curl https://kasiaikrzys.pl/swagger` zwracające 404, brak odpowiedzi na porcie 5432 z innego komputera w sieci
6. **Aktualizacja aplikacji** — `git pull`, `docker compose up -d --build`, `docker compose logs -f api`
7. **Kopie zapasowe** — zrzut bazy komendą
   `docker compose exec -T db pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" | gzip > /srv/wedding/backup/db-$(date +%F).sql.gz`,
   kopia zdjęć przez `rsync -a /srv/wedding/photos/ <cel>/`, wpis w `crontab -e` na codzienną 3:00, przypomnienie, że dysk `scsi1` jest wyłączony z backupu VM w Proxmoksie
8. **Diagnostyka** — co robić, gdy: certyfikat się nie wydaje (DNS, blokada portu 80, limit Let's Encrypt), API nie wstaje (puste sekrety w `.env`, baza nieosiągalna), zdjęcia znikają po restarcie (błędna ścieżka wolumenu), upload kończy się błędem 413 (limit żądania)
9. **Znane ograniczenia** — zmienny adres IP u operatora wymaga DDNS lub stałego IP; miniatury nie są generowane, więc feed pobiera pełne pliki

- [ ] **Step 2: Sprawdź spis treści**

```powershell
Select-String -Path docs\DEPLOYMENT.md -Pattern "^#{1,3} "
```

Oczekiwane: dziewięć nagłówków sekcji odpowiadających liście powyżej.

- [ ] **Step 3: Commit**

```bash
git add docs/DEPLOYMENT.md
git commit -m "docs: add production deployment runbook"
```

---

### Task 6: Weryfikacja całości i wypchnięcie gałęzi

**Files:**
- Żadnych zmian — zadanie wyłącznie weryfikujące

**Interfaces:**
- Consumes: komplet zmian z Task 1–5
- Produces: gałąź `feature/production-deployment` na GitHubie, gotowa do sklonowania na serwerze

- [ ] **Step 1: Zbuduj backend**

```powershell
dotnet build
```

Oczekiwane: `Build succeeded`, zero błędów.

- [ ] **Step 2: Zbuduj frontend produkcyjnie**

```powershell
cd ClientApp; npm ci; npm run build -- --configuration=production; cd ..
```

Oczekiwane: build kończy się sukcesem, katalog `dist/client-app/browser` zawiera `index.html`.

- [ ] **Step 3: Potwierdź brak adresów bezwzględnych w bundlu**

```powershell
Select-String -Path ClientApp\dist\client-app\browser\*.js -Pattern "localhost:5205"
```

Oczekiwane: brak trafień. Trafienie oznacza, że build wziął konfigurację deweloverską.

- [ ] **Step 4: Zbuduj obrazy**

```powershell
docker compose build
```

Oczekiwane: oba obrazy budują się bez błędu.

- [ ] **Step 5: Sprzątnij lokalne artefakty**

```powershell
Remove-Item .env -ErrorAction SilentlyContinue
git status --porcelain
```

Oczekiwane: pusta odpowiedź — wszystko zacommitowane, `.env` usunięty.

- [ ] **Step 6: Wypchnij gałęzie**

Gałąź bazowa nie była dotąd na GitHubie, więc idą obie.

```bash
git push -u origin fix/docker-api-port-and-admin-config
git push -u origin feature/production-deployment
```

- [ ] **Step 7: Załóż pull request**

```bash
gh pr create --base master --head feature/production-deployment \
  --title "feat: production deployment for kasiaikrzys.pl" \
  --body "Implements docs/superpowers/specs/2026-08-25-production-deployment-design.md"
```

---

## Kolejność i zależności

Task 1 i Task 2 są niezależne i mogą iść równolegle. Task 3 zależy od nazw usług ustalonych w Task 4, ale te są z góry określone w tym planie, więc praktycznie też jest niezależny. Task 4 wymaga Task 1, 2 i 3. Task 5 wymaga wszystkich poprzednich. Task 6 zamyka.

## Poza zakresem planu

Rzeczy, które muszą wydarzyć się po stronie serwera i sieci, opisane w `docs/DEPLOYMENT.md`, a nie w kodzie: rekordy A w az.pl, przekierowanie portów na routerze, rezerwacja DHCP, wygenerowanie sekretów do `.env`, test blokady portu 80 przez operatora, weryfikacja stałości adresu publicznego.
