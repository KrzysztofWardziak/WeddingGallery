# Runbook wdrożeniowy — Wedding Gallery

Dokument opisuje pełne wdrożenie produkcyjne aplikacji Wedding Gallery na serwerze VM
Ubuntu Server 24.04 LTS (Proxmox), pod domeną `kasiaikrzys.pl`. Wszystkie komendy są
gotowe do wklejenia — wykonuj je w podanej kolejności.

Ruch do serwera nie wchodzi przez przekierowanie portów na routerze, tylko przez
**Cloudflare Tunnel** — patrz sekcja 1, dlaczego.

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

- **Dlaczego tunel, a nie przekierowanie portów.** Łącze domowe, na którym stoi
  serwer, działa w technologii DS-Lite (carrier-grade NAT operatora). Dwa niezależne
  dowody, które to potwierdziły:
  - `traceroute` z serwera pokazuje jako drugi przeskok adres `192.0.0.1` — pulę
    zarezerwowaną przez RFC 6333 wyłącznie na łącze między urządzeniem abonenta a
    bramą AFTR operatora.
  - Router był w stanie wynegocjować na zewnątrz tylko porty `9909` i `9920` zamiast
    żądanych `80` i `443`.

  Let's Encrypt weryfikuje domenę wyłącznie na porcie 80 (HTTP-01) albo 443
  (TLS-ALPN-01) — te numery są częścią protokołu ACME i nie da się ich zmienić —
  więc żaden certyfikat nie mógł zostać wydany, a goście i tak nie dotarliby do
  strony pod portem `9909`. Cloudflare Tunnel omija ten problem: połączenie
  inicjuje kontener `cloudflared` z wnętrza sieci domowej w stronę Cloudflare, więc
  żaden port nie musi być otwierany na routerze.

- Konto Cloudflare (wystarczy plan darmowy) i domena `kasiaikrzys.pl` dodana do
  tego konta.
- Serwery nazw domeny podmienione w panelu az.pl na te wskazane przez Cloudflare
  po dodaniu domeny (dwa unikalne adresy `*.ns.cloudflare.com`). **Rejestracja
  domeny zostaje w az.pl** — zmienia się tylko to, kto obsługuje DNS.
- Tunel utworzony w Cloudflare Zero Trust (**Networks → Tunnels → Create a
  tunnel**, typ Cloudflared, dowolna nazwa np. `wedding-gallery`) i skopiowany
  token instalacyjny — wartość po `--token` w poleceniu instalacyjnym pokazanym
  przez kreator. Zapisz go, będzie potrzebny w sekcji 3.

  **Zakładka Public Hostname nie jest jeszcze dostępna na tym etapie** — Cloudflare
  odblokowuje ją dopiero, gdy jakiś konektor faktycznie zamelduje się z tym
  tokenem. Publiczne nazwy hosta dodajemy dopiero w sekcji 4, po pierwszym
  uruchomieniu.
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

Zanim uruchomisz kontenery, upewnij się, że domena rzeczywiście korzysta z DNS
Cloudflare — bez tego tunel nie będzie miał gdzie opublikować nazw hosta.

```bash
nslookup -type=NS kasiaikrzys.pl 1.1.1.1
```

Oczekiwany wynik: zwrócone serwery nazw kończą się na `.ns.cloudflare.com`.

Dodatkowo w panelu Cloudflare (**Websites**) domena `kasiaikrzys.pl` musi mieć
status **Active** — dopóki widnieje jako **Pending Nameservers**, propagacja
zmiany serwerów nazw z az.pl jeszcze się nie zakończyła i trzeba poczekać (zwykle
do kilku godzin, czasem do 24h).

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

Wklej wygenerowane wartości do pliku `.env` (edytuj np. `nano .env`), a
`CLOUDFLARE_TUNNEL_TOKEN` uzupełnij tokenem skopiowanym w sekcji 1. Plik musi
zawierać co najmniej:

```
DOMAIN=kasiaikrzys.pl
CLOUDFLARE_TUNNEL_TOKEN=<token skopiowany w sekcji 1>
POSTGRES_DB=wedding_gallery
POSTGRES_USER=wedding_user
POSTGRES_PASSWORD=<wygenerowane powyżej>
ADMIN_PASSWORD=<wygenerowane powyżej>
ADMIN_TOKEN=<wygenerowane powyżej>
DATA_ROOT=/srv/wedding
```

`ADMIN_PASSWORD` i `ADMIN_TOKEN` **nie mogą być puste** — API odmawia startu poza
środowiskiem Development, jeśli te wartości są puste (patrz sekcja 8).

**`CLOUDFLARE_TUNNEL_TOKEN` ma siłę hasła.** Kto go posiada, może uruchomić
własny konektor podszywający się pod ten tunel i przejąć ruch do serwera.
Traktuj go jak każdy inny sekret w tym pliku. Jeśli token wycieknie (np.
trafi do repozytorium albo na zrzut ekranu), Cloudflare nie oferuje jego
rotacji w miejscu — jedynym wyjściem jest usunięcie tunelu w Zero Trust i
utworzenie nowego (co oznacza też ponowne dodanie obu nazw hosta z sekcji 4).

Zablokuj dostęp do pliku dla innych użytkowników:

```bash
chmod 600 .env
```

Do testów lokalnych na własnej maszynie deweloperskiej trzeba dołożyć nakładkę
`docker-compose.local.yml` i pominąć kontener `cloudflared`:

```bash
docker compose -f docker-compose.yml -f docker-compose.local.yml up -d --build db api web caddy
```

Aplikacja jest wtedy pod `http://localhost:8080`. W `.env` musi być `DOMAIN=localhost`:
`Caddyfile` dopasowuje żądania po nazwie hosta, a Caddy na żądanie z niedopasowanym
nagłówkiem `Host` odpowiada `200` z **pustym ciałem** — w przeglądarce widać białą
stronę, nie błąd, więc łatwo wziąć to za awarię aplikacji.

**Nakładka jest obowiązkowa i nie ładuje się sama.** Bazowy `docker-compose.yml`
nie publikuje żadnego portu — na serwerze ruch wchodzi wyłącznie tunelem. Samo
`docker compose up -d db api web caddy` wystartuje więc komplet zdrowych
kontenerów, do których nie da się dostać z przeglądarki. Publikację portu hosta
dodaje dopiero `-f docker-compose.local.yml`.

**Uwaga przy diagnozowaniu konfiguracji.** `docker compose config` wypisuje
rozwiniętą konfigurację ze wszystkimi zmiennymi z `.env`, w tym hasłami,
`ADMIN_TOKEN` i `CLOUDFLARE_TUNNEL_TOKEN` w jawnej postaci. Nie wklejaj jego
wyjścia nigdzie (issue, czat, log) — to równoważne wklejeniu całego pliku `.env`.

## 4. Pierwsze uruchomienie

```bash
cd /srv/wedding/app
docker compose up -d --build
```

Śledź logi kontenera `cloudflared`, aż pojawi się wpis o zarejestrowanym
połączeniu (szukaj `Registered tunnel connection`):

```bash
docker compose logs -f cloudflared
```

Przerwij podgląd logów kombinacją `Ctrl+C` po potwierdzeniu wpisu — kontenery
pozostają uruchomione w tle. Potwierdź też w panelu Cloudflare Zero Trust
(**Networks → Tunnels**), że tunel ma status **Healthy**.

**Dopiero teraz** zakładka **Public Hostname** tunelu staje się dostępna. Dodaj w
niej dwie nazwy hosta:

| Publiczna nazwa hosta       | Typ usługi | Adres URL usługi |
|------------------------------|------------|-------------------|
| `kasiaikrzys.pl`             | HTTP       | `caddy:80`        |
| `www.kasiaikrzys.pl`         | HTTP       | `caddy:80`        |

Obie wskazują na kontener `caddy` po nazwie usługi z `docker-compose.yml` — działa
to, bo `cloudflared` i `caddy` są w tej samej sieci Dockera. TLS między
przeglądarką gościa a Cloudflare obsługuje sama Cloudflare; odcinek między
`cloudflared` a `caddy` biegnie już przez tunel, więc `caddy` nasłuchuje zwykłym
`http://` (patrz `Caddyfile`).

Włącz też **Always Use HTTPS** (panel Cloudflare, **SSL/TLS → Edge
Certificates**). Przekierowanie na HTTPS zdefiniowane w `Caddyfile` obejmuje
wyłącznie hosta `www` — bez tej opcji gość, który wpisze
`http://kasiaikrzys.pl` bez `https://`, zostałby obsłużony przez zwykły
protokół HTTP zamiast zostać przekierowanym.

Gdy tunel już działa i strona jest dostępna pod `https://kasiaikrzys.pl`, usuń
z routera domowego reguły przekierowania portów `wedding-http` i
`wedding-https` (zewnętrzne porty `9909` i `9920` z sekcji 1) — są to jedyne
pozostałości starej konfiguracji sprzed przejścia na tunel i nic już z nich nie
korzysta.

## 5. Lista kontrolna po wdrożeniu

Przejdź po kolei przez poniższe punkty:

- [ ] Strona `https://kasiaikrzys.pl` wczytuje się poprawnie (bez ostrzeżeń
      certyfikatu — certyfikat wystawia i utrzymuje Cloudflare, nie Caddy).
- [ ] `https://www.kasiaikrzys.pl` przekierowuje na `https://kasiaikrzys.pl`.
- [ ] W panelu administracyjnym udało się założyć nowe wydarzenie.
- [ ] Wydrukowany kod QR wydarzenia otwiera poprawną stronę galerii.
- [ ] Wysyłka dwudziestu zdjęć naraz z telefonu (aparat, zdjęcia o pełnej
      rozdzielczości) kończy się sukcesem, z licznikiem postępu widocznym w
      trakcie wysyłki (zdjęcia idą pojedynczo, po trzy równolegle — patrz
      `image-picker.component.ts`).
- [ ] Zachowanie po rozłączeniu Wi-Fi w trakcie wysyłki: część zdjęć zostaje
      oznaczona jako nieudana, a przycisk ponowienia wysyła tylko te
      nieudane, nie całą partię od nowa. Uwaga: ten test może wyprodukować
      duplikaty zdjęć — jeśli żądanie dotarło do API, ale odpowiedź zginęła
      po drodze (typowe przy zrywanym Wi-Fi), klient uznaje wysyłkę za nieudaną
      i wyśle to samo zdjęcie ponownie. Endpoint nie ma klucza idempotencji, więc
      ewentualne duplikaty administrator usuwa ręcznie z panelu.
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

  Oczekiwane: połączenie odrzucone / timeout. Port bazy danych nie jest mapowany
  na hosta w `docker-compose.yml`, więc jest osiągalny wyłącznie z wewnętrznej
  sieci Dockera.

- [ ] Żaden kontener nie publikuje portów na hoście — cały ruch wchodzi przez
      tunel wychodzący, nie przez port nasłuchujący na serwerze:

  ```bash
  sudo ss -tlnp | grep -E ':(80|443)\s'
  ```

  Oczekiwane: polecenie **nie zwraca żadnej linii**.

## 6. Aktualizacja aplikacji

Od wprowadzenia CI/CD wdrożenia robi się przyciskiem w GitHubie — patrz sekcja 11.
Ręczna ścieżka opisana niżej pozostaje ważna i jest awaryjnym wyjściem, gdy agent nie
działa.

```bash
cd /srv/wedding/app
git fetch --tags --force origin
git checkout --detach --force production
docker compose build && docker compose up -d
```

Kolejność ma znaczenie: `build` nie rusza działających kontenerów, więc nieudana
kompilacja nie kładzie galerii. Dopiero `up -d` restartuje.

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

**Plik `.env` z tokenem tunelu trzymaj poza serwerem.** Poza hasłami do bazy i
panelu administracyjnego, `.env` zawiera teraz `CLOUDFLARE_TUNNEL_TOKEN` — sekret
wystarczający do uruchomienia obcego konektora podszywającego się pod ten tunel.
Włącz `.env` do backupu poza serwer (np. w bezpiecznym menedżerze haseł albo w
zaszyfrowanym archiwum obok kopii bazy), żeby odtworzenie środowiska po awarii VM
nie wymagało tworzenia tunelu i wszystkich sekretów od nowa — ale nigdy nie
umieszczaj go w repozytorium ani w kopii zapasowej bez szyfrowania.

## 8. Diagnostyka

**Strona zwraca błąd 502 od Cloudflare:**
- Konektor działa (tunel jest osiągalny), ale kontener obsługujący ruch po
  drugiej stronie nie odpowiada. Sprawdź stan kontenerów:

  ```bash
  docker compose ps
  ```

  Najczęściej to `caddy` jest zatrzymany albo restartuje się w pętli — sprawdź
  `docker compose logs caddy`.

**Tunel w panelu Cloudflare ma status Down:**
- `docker compose logs cloudflared` — najczęstsza przyczyna to błędny albo
  nieaktualny `CLOUDFLARE_TUNNEL_TOKEN` w `.env` (np. wklejony z literówką, albo
  pochodzący z tunelu, który już nie istnieje). Popraw wartość w `.env` i
  uruchom ponownie: `docker compose up -d cloudflared`.

**Lokalnie strona się nie otwiera, choć `docker compose ps` pokazuje `80/tcp`:**
- Kolumna PORTS z samym `80/tcp` oznacza port, na którym kontener nasłuchuje
  *wewnątrz sieci Dockera* — to `EXPOSE` z obrazu, nie mapowanie na hosta.
  Opublikowany port wygląda tak: `0.0.0.0:8080->80/tcp`. Rozstrzygające sprawdzenie:

  ```bash
  docker port weddinggallery-caddy-1
  ```

  Pusty wynik = nic nie jest wystawione na hosta, żaden adres w przeglądarce nie
  zadziała. Przyczyna niemal zawsze ta sama: stos wystartował bez nakładki
  `docker-compose.local.yml` (patrz sekcja 3).

**Port jest opublikowany, ale przeglądarka pokazuje białą stronę:**
- Niedopasowany `Host` — `DOMAIN` w `.env` nie zgadza się z adresem, pod który
  wchodzisz. Caddy nie ma wtedy żadnej trasy dla tego żądania i zwraca `200`
  z pustym ciałem, co wygląda jak zepsuta aplikacja, a nie jak błąd routingu.
  Porównaj rozmiar odpowiedzi dla obu nazw:

  ```bash
  curl -s -o /dev/null -w '%{size_download}\n' -H 'Host: localhost' http://localhost:8080/
  ```

  Zero bajtów = zły `Host`. Lokalnie ustaw `DOMAIN=localhost`; na serwerze `DOMAIN`
  musi być dokładnie tą nazwą, którą tunel podaje w nagłówku `Host`.

**Strona nieosiągalna mimo tunelu w stanie Healthy:**
- Sprawdź w Cloudflare Zero Trust (**Networks → Tunnels → Public Hostname**), czy
  obie nazwy hosta z sekcji 4 rzeczywiście istnieją i wskazują na
  `caddy:80` — brak wpisu albo wskazanie na zły adres/port da błąd po stronie
  Cloudflare, mimo że sam konektor jest zdrowy.

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

**Usunięte zdjęcie nadal jest dostępne pod bezpośrednim adresem URL:**
- Usunięcie zdjęcia w panelu administracyjnym kasuje wiersz w bazie i plik na
  dysku, ale kopia mogła zostać zbuforowana na brzegu sieci Cloudflare. Adres
  `https://kasiaikrzys.pl/photos/<guid>.jpg` może więc pozostać osiągalny przez
  do pięciu minut po usunięciu (`Cache-Control: public, max-age=300` ustawione
  w `Caddyfile` dla ścieżki `/photos/*`). Jeśli potrzebne jest natychmiastowe
  usunięcie z brzegu sieci, wyczyść cache ręcznie w panelu Cloudflare:
  **Caching → Configuration → Purge Everything**, albo wyczyść tylko ten jeden
  adres (**Custom Purge → Purge by URL**).

**Część zdjęć nie dochodzi:**
- Każde zdjęcie wysyłane jest osobnym żądaniem (patrz `image-picker.component.ts`
  i `api.service.ts`), więc problem dotyczy zwykle pojedynczego pliku, nie całej
  partii. Sprawdź konsolę przeglądarki gościa: kod **413** oznaczałby, że
  konkretne pojedyncze zdjęcie przekracza limit rozmiaru żądania — mało
  prawdopodobne dla pojedynczego zdjęcia z telefonu, ale możliwe przy eksporcie
  z profesjonalnego aparatu. Limit darmowego planu Cloudflare wynosi 100 MB na
  żądanie (patrz sekcja 9) i to on jest w praktyce pierwszym ograniczeniem.

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

- **Tunel jako pojedynczy punkt awarii.** Cały ruch do serwera przechodzi przez
  jeden kontener `cloudflared`. Jeśli przestanie się łączyć (np. po chwilowej
  awarii sieci Cloudflare albo zawieszeniu procesu), strona staje się
  nieosiągalna, mimo że `caddy`, `api` i `web` działają poprawnie. Pierwszy krok
  w takiej sytuacji: `docker compose restart cloudflared`.
- **Cloudflare widzi cały ruch w postaci jawnej.** TLS między przeglądarką gościa
  a Cloudflare kończy się na Cloudflare — to tam ruch jest odszyfrowywany, zanim
  trafi tunelem do `cloudflared` i dalej do `caddy`. Cloudflare technicznie ma
  wgląd w przesyłane zdjęcia i inne dane aplikacji, tak jak każdy dostawca CDN
  terminujący TLS.
- **Limit rozmiaru żądania na darmowym planie Cloudflare.** Plan darmowy tnie
  pojedyncze żądania HTTP na 100 MB. Dlatego wysyłka zdjęć w aplikacji odbywa się
  po jednym pliku na żądanie (`image-picker.component.ts`, `api.service.ts`), a
  nie jedną wspólną partią — pojedyncze zdjęcie z telefonu nigdy nie zbliża się
  do tego limitu, ale cała partia dwudziestu zdjęć w jednym żądaniu mogłaby.
- **Brak ograniczenia liczby prób logowania do panelu administracyjnego.**
  `POST /api/Admin/login` jest publicznie dostępny i nie ma żadnego rate
  limitingu w kodzie API — nic nie blokuje próby odgadnięcia `ADMIN_PASSWORD`
  brute-force'em. Zalecana mitygacja to reguła rate-limitingu lub WAF w panelu
  Cloudflare ograniczająca żądania do ścieżki `/api/Admin/*`.
- **Brak generowania miniatur.** `PhotoService` zapisuje `ThumbPath` jako
  identyczną ścieżkę co `OriginalPath` (miniatura nie jest faktycznie
  generowana) — feed galerii pobiera więc zawsze pełnowymiarowe pliki zdjęć,
  co przy dużej liczbie gości może obciążać transfer i wydłużać ładowanie
  strony na wolniejszym łączu domowym.

## 10. Powrót do przekierowania portów

Gdyby operator kiedyś udostępnił publiczny, statyczny adres IPv4 (koniec
DS-Lite na tym łączu), da się wrócić do prostszego modelu z bezpośrednim
przekierowaniem portów i certyfikatem Let's Encrypt wystawianym przez Caddy.
Skrócony plan zmian — pełne uzasadnienie architektoniczne obu wariantów jest w
`docs/superpowers/specs/2026-08-25-production-deployment-design.md`:

1. Usuń usługę `cloudflared` z `docker-compose.yml` i przywróć na `caddy` sekcję
   `ports` publikującą `80:80` i `443:443` na hoście.
2. Przywróć w `Caddyfile` wariant z automatycznym HTTPS (adresy bloków bez
   prefiksu `http://`, tak jak wygląda to dla `{$DOMAIN}` gdy Caddy sam zarządza
   certyfikatem Let's Encrypt) i `ACME_EMAIL` z powrotem w `.env`.
3. Skonfiguruj na routerze domowym przekierowanie portów 80/443 na
   `192.168.1.41`.
4. W panelu DNS wróć z serwerami nazw do az.pl i ustaw rekordy A (`@`, `www`) na
   aktualny publiczny adres IP — albo zostaw serwery nazw wskazane na Cloudflare
   i przełącz tam rekordy `@`/`www` w tryb **DNS only** (szara chmurka) zamiast
   proxy, żeby ruch szedł bezpośrednio do serwera z pominięciem tunelu.
5. Usuń z Zero Trust tunel, który przestał być używany.

## 11. Automatyczne wdrożenia (CI/CD)

Serwer jest za DS-Lite, więc GitHub nie ma jak się do niego połączyć. Wdrożenie jest
odwrócone: przycisk w GitHubie przestawia tag `production`, a timer na serwerze co dwie
minuty sam się do niego zbiega.

### Instalacja agenta (jednorazowa)

```bash
cd /srv/wedding/app
sudo install -m 755 deploy/wedding-deploy.sh /usr/local/bin/wedding-deploy
sudo install -m 644 deploy/wedding-deploy.service /etc/systemd/system/
sudo install -m 644 deploy/wedding-deploy.timer /etc/systemd/system/
sudo systemd-analyze verify /etc/systemd/system/wedding-deploy.service
sudo systemctl daemon-reload
sudo systemctl enable --now wedding-deploy.timer
```

Skrypt i pliki jednostek są **kopiowane** z repozytorium. Zmiana któregokolwiek z nich
wymaga powtórzenia tych komend — agent nie aktualizuje się sam, bo nie może wykonywać
pliku, który jego własny `git checkout` podmienia mu pod nogami.

### Wdrożenie

Actions → Deploy → Run workflow → podaj commit, gałąź lub tag (domyślnie `master`).
W ciągu dwóch minut serwer się przełączy. Żeby nie czekać na timer, można wymusić
natychmiastowe sprawdzenie tagu:

```bash
sudo systemctl start wedding-deploy.service
```

### Cofnięcie

Ten sam przycisk ze starszym SHA. To jedyny mechanizm — nie ma osobnej procedury awaryjnej.

### Co się dzieje i czy się udało

```bash
systemctl status wedding-deploy.timer
journalctl -u wedding-deploy -n 50 --no-pager
cat /srv/wedding/deployed-commit
```

**Przycisk w GitHubie zapala się na zielono, gdy tag się przestawi — nie gdy wdrożenie
się uda.** Prawda jest tylko w journalu.

### Nieudane wdrożenie

Agent próbuje **raz na przestawienie tagu**. Po porażce checkout albo `docker compose
build` zapisuje commit i przestaje, żeby nie przebudowywać co dwie minuty w kółko:

```bash
cat /srv/wedding/failed-commit
```

Ponowna próba tego samego commita:

```bash
sudo rm /srv/wedding/failed-commit
```

Samo usunięcie pliku nic jeszcze nie zmienia — dopiero kolejne uruchomienie agenta
podejmie próbę ponownie. Żeby nie czekać do dwóch minut, wymuś je od razu:

```bash
sudo systemctl start wedding-deploy.service
```

Nieudany `build` **nie rusza działających kontenerów** — galeria dalej chodzi na starym
kodzie, a commit trafia do `failed-commit` i nie jest ponawiany automatycznie. Nieudane
`up -d` jest traktowane inaczej: ten commit **nie** trafia do `failed-commit`, bo stos
może zostać w stanie połowicznie uruchomionym, a przyczyny (zajęty port, wolny wolumen,
chwilowy OOM przy odtwarzaniu kontenera) bywają przejściowe — kolejne uruchomienie
spróbuje tego samego commita ponownie bez interwencji człowieka. Warto wtedy zajrzeć od
razu do journala.

### Ręczne wdrożenie (awaryjnie)

Gdy agent nie działa:

```bash
cd /srv/wedding/app
git fetch --tags --force origin
git checkout --detach --force production
docker compose build && docker compose up -d
```
