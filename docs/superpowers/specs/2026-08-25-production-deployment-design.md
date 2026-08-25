# Wdrożenie produkcyjne pod domeną kasiaikrzys.pl

Data: 2026-08-25
Gałąź: `feature/production-deployment` (bazuje na `fix/docker-api-port-and-admin-config`)

## Cel

Uruchomić Wedding Gallery na domowym serwerze Proxmox pod `https://kasiaikrzys.pl`, tak aby
wszystkie adresy — aplikacja, API, zdjęcia i link z kodu QR — żyły pod jedną domeną i jednym
certyfikatem TLS.

## Dlaczego serwer własny, nie hosting serverless

Rozważano Vercel z darmową bazą. Odrzucone: aplikacja zapisuje pliki na dysk lokalny, a system
plików w środowisku serverless jest efemeryczny — zdjęcia znikałyby przy każdym wdrożeniu.
Dodatkowo darmowe bazy mają limity rzędu 0,5 GB, podczas gdy galeria weselna to realnie
kilkanaście–kilkadziesiąt GB. Backend w .NET z zapisem na dysk wymaga trwałego wolumenu.

## Stan wyjściowy — co blokowało wdrożenie

| Problem | Miejsce |
| --- | --- |
| `apiUrl` i `imagesUrl` na sztywno `http://localhost:5205` w buildzie produkcyjnym | `ClientApp/src/environments/environment.ts` |
| Brak konfiguracji nginx w obrazie frontendu — deep-link `/<slug>` zwraca 404 po odświeżeniu | `ClientApp/Dockerfile` |
| Zdjęcia zapisywane do `/app/wwwroot/photos`, wolumen montowany na `/app/photos` — uploady giną przy restarcie | `WeddingGallery.Application/Services/PhotoService.cs` vs `docker-compose.yml` |
| `ASPNETCORE_ENVIRONMENT=Development` — publicznie dostępny Swagger | `docker-compose.yml` |
| Hasło administratora `admin` i token `secret-admin-token-12345` w repozytorium | `appsettings.json`, `docker-compose.yml` |
| Postgres nasłuchujący na `0.0.0.0:5432` | `docker-compose.yml` |
| CORS `AllowAnyOrigin` | `WeddingGallery.Api/Program.cs` |
| Kestrel tnie żądania powyżej 30 MB — kilka zdjęć z telefonu przekracza limit | `Program.cs` |
| Brak reverse proxy i TLS | — |

## Architektura docelowa

```
Internet :80 :443
   |
Router — przekierowanie portów 80/443 -> 192.168.1.41
   |
VM Ubuntu Server 24.04 LTS (Proxmox), Docker
   |
   +-- caddy   :80 :443   <- jedyny kontener wystawiony na zewnątrz
   |      /api/*    -> api:8080
   |      /photos/* -> api:8080
   |      /*        -> web:80
   +-- web     nginx + statyczny Angular, tylko sieć wewnętrzna
   +-- api     .NET 8, tylko sieć wewnętrzna
   +-- db      PostgreSQL 16, tylko sieć wewnętrzna, bez wystawionych portów

dane: /srv/wedding/db      -> wolumen Postgresa
      /srv/wedding/photos  -> /app/wwwroot/photos w kontenerze api
      /srv/wedding/app     -> katalog roboczy z repozytorium
```

Infrastruktura VM: 2 vCPU (typ `host`), 4 GB RAM bez balonowania, dysk systemowy 32 GB oraz
osobny dysk 150 GB zamontowany po `LABEL=wedding-data` jako `/srv/wedding`. Osobny dysk pozwala
rozszerzać przestrzeń na zdjęcia bez ruszania systemu i wyłączyć go z cotygodniowego backupu VM.

Wybrano maszynę wirtualną zamiast kontenera LXC: Docker w LXC wymaga `nesting=1` i obejść na
keyctl oraz overlayfs, a korzyść z lżejszej wirtualizacji jest przy tej skali pomijalna.

## Mapowanie adresów

| Adres | Cel | Uwaga |
| --- | --- | --- |
| `https://kasiaikrzys.pl/` | Angular | dziś przekierowuje na `/admin/setup` — patrz Dług techniczny |
| `https://kasiaikrzys.pl/<slug>` | Angular, `WelcomeComponent` | adres z kodu QR; wymaga fallbacku w nginx |
| `https://kasiaikrzys.pl/api/...` | .NET API | front używa ścieżki względnej |
| `https://kasiaikrzys.pl/photos/...` | .NET, pliki statyczne | |
| `https://www.kasiaikrzys.pl` | przekierowanie 301 na wersję bez `www` | jeden adres kanoniczny |

Konsekwencja: frontend nie zna żadnego adresu bezwzględnego. `environment.ts` przyjmuje
`apiUrl: '/api'` i `imagesUrl: ''`, przez co w produkcji nie występuje CORS, a `AdminPrintQrComponent`
— budujący link z `window.location.origin` — generuje poprawny `https://kasiaikrzys.pl/<slug>`
bez dodatkowej konfiguracji.

## Decyzje projektowe

**Caddy zamiast nginx z certbotem.** Caddy sam pozyskuje i odnawia certyfikat Let's Encrypt.
Znika certbot, cron do odnawiania i ręczne pliki `.pem`. Kosztem jest jeden dodatkowy kontener.

**Wolumen zdjęć montowany pod faktyczną ścieżkę zapisu.** `PhotoService` zapisuje do
`wwwroot/photos` i pod tą samą ścieżką szuka ich `UseStaticFiles`. Zamiast przerabiać logikę
aplikacji, wolumen montujemy na `/app/wwwroot/photos`. Zero zmian w kodzie, ścieżki `/photos/...`
zapisane w bazie pozostają poprawne.

**Sekrety w pliku `.env` obok `docker-compose.yml`.** Plik trafia do `.gitignore`, w repozytorium
zostaje `.env.example`. Wartości generowane na serwerze przez `openssl rand -base64 32`.

**Limit żądania podniesiony do 200 MB** w Kestrelu i `FormOptions`. Caddy nie narzuca własnego
limitu, nginx nie stoi na ścieżce uploadu.

**CORS zawężony do adresów z konfiguracji.** W produkcji lista jest pusta, bo front i API dzielą
origin. Środowisko deweloperskie zachowuje `http://localhost:4200`.

## Zakres zmian

Nowe pliki:

- `Caddyfile` — terminacja TLS, routing, przekierowanie `www`, kompresja
- `ClientApp/nginx.conf` — `try_files $uri $uri/ /index.html`, cache dla zasobów z hashem
- `.env.example` — szablon sekretów
- `docs/DEPLOYMENT.md` — procedura wdrożenia i utrzymania

Zmieniane pliki:

- `docker-compose.yml` — usługa `caddy`, brak wystawionych portów w `db`/`api`/`web`, zmienne
  z `.env`, `ASPNETCORE_ENVIRONMENT=Production`, `restart: unless-stopped`, bind-mounty na `/srv/wedding`
- `ClientApp/Dockerfile` — kopiowanie `nginx.conf` do obrazu
- `ClientApp/src/environments/environment.ts` — adresy względne
- `WeddingGallery.Api/Program.cs` — limity uploadu, CORS z konfiguracji
- `WeddingGallery.Api/appsettings.json` — usunięcie sekretów
- `.gitignore` — `.env`

## Warunki brzegowe po stronie sieci

Adres publiczny `188.47.103.198` nie mieści się w zakresie CGNAT, więc klasyczne przekierowanie
portów jest możliwe. Domena stoi na serwerach nazw az.pl (`ns6`/`ns7`/`ns8.az.pl`), które
pozostają bez zmian; ustawiane są wyłącznie rekordy A dla `@` i `www` z TTL 600.

Dwa ryzyka pozostają do zweryfikowania przed uruchomieniem:

1. **Blokada portu 80 przez operatora** — część polskich ISP blokuje go na łączach domowych.
   Weryfikacja przez połączenie z sieci komórkowej. W razie blokady rozwiązaniem jest Cloudflare
   Tunnel, co wymusza przeniesienie serwerów nazw na Cloudflare.
2. **Zmienny adres IP** — typowy dla łączy domowych. W razie zmiany konieczny klient DDNS
   aktualizujący rekord A albo wykupienie stałego adresu u operatora.

## Testowanie

- `dotnet build` i `ng build --configuration production` przed commitem
- `docker compose config` — walidacja podstawień z `.env`
- `docker compose build` — weryfikacja obu obrazów
- Po wdrożeniu, ręcznie: deep-link `/<slug>` po odświeżeniu strony, upload wielu zdjęć naraz,
  widoczność zdjęć po `docker compose restart api`, ocena certyfikatu, przekierowanie `www`,
  brak dostępu do Swaggera, brak dostępu do portu 5432 z sieci

## Dług techniczny i sprawy poza zakresem

- Goły `kasiaikrzys.pl` przekierowuje na `/admin/setup`, więc goście trafiają na panel
  administracyjny. Zmiana produktowa, celowo poza zakresem tego wdrożenia.
- Uwierzytelnianie administratora opiera się na statycznym tokenie porównywanym z konfiguracją,
  bez wygasania i bez sesji. Dla jednorazowego wesela akceptowalne, ale to nie jest mechanizm
  do ponownego użycia.
- Miniatury nie są generowane — `ThumbPath` wskazuje na oryginał. Przy dużej galerii feed będzie
  ciągnął pełnowymiarowe pliki.
- Brak automatycznego backupu bazy i zdjęć poza snapshotem VM. Do opisania w `DEPLOYMENT.md`
  jako procedura ręczna, docelowo `pg_dump` z crona i `rsync` zdjęć poza serwer.
