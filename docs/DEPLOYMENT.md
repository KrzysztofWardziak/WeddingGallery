# Runbook wdrożeniowy — Wedding Gallery

Dokument opisuje pełne wdrożenie produkcyjne aplikacji Wedding Gallery na serwerze VM
Ubuntu Server 24.04 LTS (Proxmox), pod domeną `kasiaikrzys.pl`. Wszystkie komendy są
gotowe do wklejenia — wykonuj je w podanej kolejności.

## 1. Wymagania wstępne

- VM Ubuntu Server 24.04 LTS na Proxmoksie, adres w sieci lokalnej `192.168.1.41`,
  użytkownik `kris`.
- Dysk danych: druga wirtualna dyskietka VM (`scsi1`) zamontowana jako `/srv/wedding`,
  z podkatalogami:
  - `/srv/wedding/db` — dane PostgreSQL,
  - `/srv/wedding/photos` — przesłane zdjęcia,
  - `/srv/wedding/app` — tu klonowane jest repozytorium.

  ```bash
  sudo mkdir -p /srv/wedding/db /srv/wedding/photos /srv/wedding/app /srv/wedding/backup
  sudo chown -R kris:kris /srv/wedding
  ```

- Docker Engine i Docker Compose v2 (polecenie `docker compose`, **nie** `docker-compose`):

  ```bash
  curl -fsSL https://get.docker.com | sudo sh
  sudo usermod -aG docker kris
  docker compose version
  ```

- Przekierowanie portów 80/443 z routera domowego na `192.168.1.41` (NAT).
  Publiczny adres IP w chwili pisania tego dokumentu: `188.47.103.198` — to łącze
  domowe, więc adres może się zmienić (patrz sekcja 9).
- Domena `kasiaikrzys.pl` zarejestrowana w az.pl. Serwery nazw pozostają bez zmian:
  `ns6.az.pl`, `ns7.az.pl`, `ns8.az.pl`. W panelu DNS ustawione są tylko rekordy A:
  - `@` → aktualny publiczny adres IP,
  - `www` → aktualny publiczny adres IP,

  oba z TTL `600` sekund (niski TTL ułatwia szybką aktualizację po zmianie IP).
- Repozytorium klonowane po SSH przy użyciu read-only deploy key GitHub:

  ```bash
  ssh-keygen -t ed25519 -C "wedding-gallery-deploy" -f ~/.ssh/wedding_gallery_deploy -N ""
  cat ~/.ssh/wedding_gallery_deploy.pub
  # dodaj klucz w GitHub: Settings -> Deploy keys -> Add deploy key (bez zaznaczenia "Allow write access")
  ```

  ```bash
  cat <<'EOF' >> ~/.ssh/config
  Host github.com-wedding-gallery
      HostName github.com
      User git
      IdentityFile ~/.ssh/wedding_gallery_deploy
      IdentitiesOnly yes
  EOF
  git clone git@github.com-wedding-gallery:KrzysztofWardziak/WeddingGallery.git /srv/wedding/app
  ```

## 2. Weryfikacja przed pierwszym uruchomieniem

Zanim uruchomisz kontenery, upewnij się, że DNS i sieć są poprawnie skonfigurowane —
Caddy nie wystawi certyfikatu Let's Encrypt, jeśli domena nie wskazuje na ten serwer.

```bash
nslookup kasiaikrzys.pl 1.1.1.1
```

Oczekiwany wynik: adres zwrócony przez `nslookup` musi być identyczny z publicznym
adresem IP serwera.

```bash
curl -4 ifconfig.me
```

Uruchom na VM — wynik musi zgadzać się z adresem zwróconym przez `nslookup` powyżej.

Na koniec sprawdź z zewnątrz sieci domowej (np. z telefonu w sieci komórkowej, po
wyłączeniu Wi-Fi), że port 80 jest osiągalny:

```bash
curl -I http://kasiaikrzys.pl
```

Jeśli połączenie się nie nawiązuje — sprawdź przekierowanie portów na routerze i
ewentualny firewall dostawcy internetu, zanim przejdziesz dalej.

## 3. Konfiguracja sekretów

```bash
cd /srv/wedding/app
cp .env.example .env
```

Wygeneruj silne wartości dla haseł i tokenu:

```bash
openssl rand -base64 32   # POSTGRES_PASSWORD
openssl rand -base64 32   # ADMIN_PASSWORD
openssl rand -base64 32   # ADMIN_TOKEN
```

Wklej wygenerowane wartości do pliku `.env` (edytuj np. `nano .env`). Plik musi
zawierać co najmniej:

```
DOMAIN=kasiaikrzys.pl
ACME_EMAIL=krzysztof.wardziak@oke.pl
POSTGRES_DB=wedding_gallery
POSTGRES_USER=wedding_user
POSTGRES_PASSWORD=<wygenerowane powyżej>
ADMIN_PASSWORD=<wygenerowane powyżej>
ADMIN_TOKEN=<wygenerowane powyżej>
DATA_ROOT=/srv/wedding
```

`ADMIN_PASSWORD` i `ADMIN_TOKEN` **nie mogą być puste** — API odmawia startu poza
środowiskiem Development, jeśli te wartości są puste (patrz sekcja 8).

Zablokuj dostęp do pliku dla innych użytkowników:

```bash
chmod 600 .env
```

## 4. Pierwsze uruchomienie

```bash
cd /srv/wedding/app
docker compose up -d --build
```

Śledź logi kontenera Caddy, aż pojawi się wpis o pomyślnym wydaniu certyfikatu
(szukaj `certificate obtained successfully` lub `obtain certificate` w logach):

```bash
docker compose logs -f caddy
```

Przerwij podgląd logów kombinacją `Ctrl+C` po potwierdzeniu wpisu — kontenery
pozostają uruchomione w tle.

## 5. Lista kontrolna po wdrożeniu

Przejdź po kolei przez poniższe punkty:

- [ ] Strona `https://kasiaikrzys.pl` wczytuje się poprawnie (bez ostrzeżeń
      certyfikatu).
- [ ] `https://www.kasiaikrzys.pl` przekierowuje na `https://kasiaikrzys.pl`.
- [ ] W panelu administracyjnym udało się założyć nowe wydarzenie.
- [ ] Wydrukowany kod QR wydarzenia otwiera poprawną stronę galerii.
- [ ] Upload kilku zdjęć naraz z telefonu (aparat, zdjęcia o pełnej rozdzielczości)
      kończy się sukcesem.
- [ ] Odświeżenie strony bezpośrednio pod adresem `https://kasiaikrzys.pl/<slug>`
      (bez przechodzenia przez stronę główną) nie zwraca błędu 404.
- [ ] Po `docker compose restart api` przesłane wcześniej zdjęcia nadal są widoczne
      w galerii:

  ```bash
  docker compose restart api
  ```

- [ ] Swagger jest niedostępny publicznie. Wejście na `https://kasiaikrzys.pl/swagger`
      w przeglądarce zwróci `200` i pokaże aplikację Angular, a nie błąd 404 — to
      oczekiwane zachowanie, bo `Caddyfile` przekierowuje na `api:8080` wyłącznie
      ścieżki `/api/*` i `/photos/*`; wszystko inne, w tym `/swagger`, trafia do
      catch-allu `handle { reverse_proxy web:80 }`, a fallback SPA w
      `ClientApp/nginx.conf` (`try_files $uri $uri/ /index.html`) zwraca
      `index.html` dla każdej nieznanej ścieżki. Adres `/swagger` nigdy nie jest
      więc przekazywany do API — sprawdź zamiast tego bezpośrednio, że kontener
      `api` działa w środowisku Production, w którym `Program.cs` w ogóle nie
      rejestruje Swaggera:

  ```bash
  docker compose exec api printenv ASPNETCORE_ENVIRONMENT
  ```

  Oczekiwany wynik: `Production`.

- [ ] Baza danych nie odpowiada z zewnątrz — z innego komputera w sieci lokalnej:

  ```bash
  nc -zv -w 3 192.168.1.41 5432
  ```

  Oczekiwane: połączenie odrzucone / timeout. Tylko kontener `caddy` publikuje
  porty na hosta (80/443) — port 5432 bazy danych nie jest mapowany w
  `docker-compose.yml`, więc jest osiągalny wyłącznie z wewnętrznej sieci Dockera.

## 6. Aktualizacja aplikacji

```bash
cd /srv/wedding/app
git pull
docker compose up -d --build
docker compose logs -f api
```

Migracje EF Core są stosowane automatycznie przy starcie kontenera `api`
(`db.Database.Migrate()` w `Program.cs`), więc nie trzeba uruchamiać ich ręcznie.
W logach `api` powinien pojawić się wpis `Database migrations applied.`.

## 7. Kopie zapasowe

Baza danych (zrzut skompresowany gzip). Zmienne `$POSTGRES_USER`/`$POSTGRES_DB`
rozwijają się w powłoce, w której uruchamiasz komendę — nie wewnątrz kontenera —
więc najpierw wczytaj je z pliku `.env`, inaczej polecenie wykona się jako
`pg_dump -U "" ""`:

```bash
cd /srv/wedding/app
set -a && . ./.env && set +a
mkdir -p /srv/wedding/backup
docker compose exec -T db pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" | gzip > /srv/wedding/backup/db-$(date +%F).sql.gz
```

Zdjęcia (kopia przyrostowa na zewnętrzny cel, np. dysk sieciowy lub inny host —
`<adres-hosta-backupu>` i `<ścieżka-docelowa>` poniżej to placeholdery, zastąp je
prawdziwym celem backupu przed użyciem):

```bash
rsync -a /srv/wedding/photos/ kris@<adres-hosta-backupu>:<ścieżka-docelowa>/
```

Backup zdjęć działa bez hasła (jest uruchamiany automatycznie z crona), więc
wymaga wcześniejszego skonfigurowania logowania kluczem SSH bez hasła na hoście
docelowym:

```bash
ssh-keygen -t ed25519 -C "wedding-gallery-backup" -f ~/.ssh/wedding_gallery_backup -N ""
ssh-copy-id -i ~/.ssh/wedding_gallery_backup.pub kris@<adres-hosta-backupu>
cat <<'EOF' >> ~/.ssh/config
Host backup-host
    HostName <adres-hosta-backupu>
    User kris
    IdentityFile ~/.ssh/wedding_gallery_backup
EOF
```

Po skonfigurowaniu aliasu `backup-host` w `~/.ssh/config` powyższą komendę `rsync`
można wywoływać jako `rsync -a /srv/wedding/photos/ backup-host:<ścieżka-docelowa>/`.

Automatyzacja — codziennie o 3:00 (edytuj `crontab -e` jako użytkownik `kris`;
zmienne `POSTGRES_USER`/`POSTGRES_DB` muszą być dostępne w środowisku crona, więc
najprościej odczytać je bezpośrednio z pliku `.env`):

```
0 3 * * * cd /srv/wedding/app && set -a && . ./.env && set +a && docker compose exec -T db pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" | gzip > /srv/wedding/backup/db-$(date +\%F).sql.gz && rsync -a /srv/wedding/photos/ backup-host:<ścieżka-docelowa>/
```

**Ważne — dysk `scsi1` jest wyłączony z backupu VM w Proxmoksie.** Katalog
`/srv/wedding` (baza, zdjęcia, repozytorium) leży na drugim wirtualnym dysku
celowo wyłączonym z zadania backupu VM na poziomie Proxmoksa. Oznacza to, że
**backup VM nie chroni ani bazy danych, ani zdjęć** — kopie zapasowe opisane w tej
sekcji (dump bazy + rsync zdjęć) są jedynym zabezpieczeniem tych danych i muszą
być wykonywane niezależnie od backupu Proxmoksa.

**Certyfikat TLS Caddy nie jest objęty backupem katalogu danych.** Magazyn
certyfikatów Caddy znajduje się w zarządzanym przez Dockera named volume
`caddy_data`, a nie pod `/srv/wedding`. To celowe: bind-mount katalogu `/data`
Caddy powodowałby problemy z uprawnieniami wewnątrz kontenera, a certyfikat i tak
jest automatycznie ponownie wydawany przez Let's Encrypt przy starcie kontenera.
**Uwaga:** Let's Encrypt zezwala na wydanie tylko pięciu certyfikatów tygodniowo
dla tej samej domeny — powtarzające się niszczenie wolumenu `caddy_data` (np.
przez `docker compose down -v` lub ręczne `docker volume rm`) wyczerpie ten limit
i pozostawi stronę bez HTTPS aż do jego zresetowania (siedem dni od pierwszego
żądania w oknie limitu). Nie usuwaj wolumenu `caddy_data` bez wyraźnej potrzeby.

## 8. Diagnostyka

**Certyfikat się nie wydaje** (w logach `docker compose logs caddy` widać błędy
ACME / `challenge failed`):
- Sprawdź, czy DNS rzeczywiście wskazuje na ten serwer: `nslookup kasiaikrzys.pl 1.1.1.1`
  i porównaj z `curl -4 ifconfig.me` na VM (sekcja 2). Adres publiczny domowego
  łącza mógł się zmienić.
- Sprawdź, czy port 80 jest osiągalny z internetu (przekierowanie na routerze,
  firewall) — Let's Encrypt weryfikuje domenę przez HTTP-01 na porcie 80.
- Sprawdź, czy nie wyczerpano limitu Let's Encrypt (5 certyfikatów/tydzień na
  domenę) — w logach pojawi się komunikat `too many certificates already issued`.
  W takim wypadku trzeba odczekać do zresetowania limitu; nie pomoże ponowne
  uruchamianie kontenera.

**API nie wstaje** (kontener `api` restartuje się w pętli lub kończy działanie
zaraz po starcie):
- `docker compose logs api` — jeśli w logu jest
  `InvalidOperationException: AdminSettings:Password is not configured` (lub
  analogicznie dla `AdminSettings:Token`) — plik `.env` ma puste
  `ADMIN_PASSWORD` lub `ADMIN_TOKEN`. API celowo odmawia startu poza
  środowiskiem Development z pustymi danymi administratora. Uzupełnij `.env` i
  uruchom ponownie: `docker compose up -d --build api`.
- Jeśli logi wskazują na błąd połączenia z bazą — sprawdź, czy kontener `db` jest
  zdrowy (`docker compose ps`, healthcheck `pg_isready`) i czy dane w
  `POSTGRES_USER`/`POSTGRES_PASSWORD`/`POSTGRES_DB` w `.env` zgadzają się z tymi,
  z którymi wolumen bazy został pierwotnie utworzony (zmiana hasła w `.env` po
  pierwszym starcie nie zmienia hasła w istniejącej bazie).

**Zdjęcia znikają po restarcie kontenera:**
- Sprawdź w `docker-compose.yml`, że wolumen `api` jest zamontowany dokładnie
  jako `${DATA_ROOT}/photos:/app/wwwroot/photos`. `PhotoService` zapisuje pliki
  pod `wwwroot/photos` względem katalogu roboczego kontenera, a
  `UseStaticFiles()` w `Program.cs` serwuje pliki z tego samego miejsca —
  niezgodność ścieżki (np. literówka albo montowanie pod inną ścieżkę) sprawia,
  że zdjęcia trafiają do efemerycznej warstwy kontenera i znikają przy jego
  odtworzeniu.
- Sprawdź uprawnienia katalogu `/srv/wedding/photos` na hoście — kontener musi
  mieć prawo zapisu.

**Upload kończy się błędem 413:**
- Limit rozmiaru pojedynczego żądania w API wynosi 200 MB
  (`MaxRequestBodySize` w Kestrelu i `MultipartBodyLengthLimit` w `Program.cs`).
  Błąd 413 oznacza, że przesyłane łącznie w jednym żądaniu zdjęcia przekraczają
  ten limit — poproś gościa o przesłanie mniejszej liczby zdjęć naraz.

**Nocny backup nie produkuje żadnego pliku (brak nowego `db-<data>.sql.gz` albo
zdjęcia nie przybywają na hoście docelowym):**
- Sprawdź, czy zadanie crona w ogóle się wykonało: `grep CRON /var/log/syslog`
  (albo `journalctl -u cron`) na wpisy z godziny 3:00.
- Najczęstsza przyczyna: brak skonfigurowanego logowania kluczem SSH bez hasła
  do hosta docelowego (`backup-host`) — `rsync` uruchomiony z crona nie ma
  terminala, więc każde pytanie o hasło kończy się cichym błędem. Sprawdź
  ręcznie: `ssh backup-host true` powinno zakończyć się bez pytania o hasło; jeśli
  pyta, wróć do konfiguracji klucza w sekcji 7.
- Druga częsta przyczyna: brak miejsca na dysku docelowym (`rsync` kończy się
  błędem `No space left on device`) — sprawdź `df -h` na hoście docelowym i na
  `/srv/wedding/backup` lokalnie.
- Sprawdź też, czy plik `.env` nadal istnieje i ma poprawne uprawnienia
  (`chmod 600 .env` z sekcji 3) — jeśli został przypadkowo usunięty lub
  przeniesiony, `set -a && . ./.env && set +a` w zadaniu crona zakończy się
  błędem i `pg_dump` uruchomi się z pustymi poświadczeniami.

## 9. Znane ograniczenia

- **Zmienny adres IP.** Serwer działa na domowym łączu internetowym, które nie ma
  stałego adresu IP. Rekordy A domeny (`@`, `www`, TTL 600) trzeba aktualizować
  ręcznie po każdej zmianie adresu — w przeciwnym razie strona przestanie być
  osiągalna, a Caddy nie odnowi certyfikatu. Docelowym rozwiązaniem jest
  wykupienie stałego IP u operatora albo skonfigurowanie usługi DDNS
  aktualizującej rekordy A automatycznie.
- **Brak generowania miniatur.** `PhotoService` zapisuje `ThumbPath` jako
  identyczną ścieżkę co `OriginalPath` (miniatura nie jest faktycznie
  generowana) — feed galerii pobiera więc zawsze pełnowymiarowe pliki zdjęć,
  co przy dużej liczbie gości może obciążać transfer i wydłużać ładowanie
  strony na wolniejszym łączu domowym.