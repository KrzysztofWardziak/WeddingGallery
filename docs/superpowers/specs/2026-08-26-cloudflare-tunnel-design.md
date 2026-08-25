# Wystawienie galerii przez Cloudflare Tunnel

Data: 2026-08-26
Gałąź: `feature/cloudflare-tunnel`
Poprzedni spec: `docs/superpowers/specs/2026-08-25-production-deployment-design.md`

## Dlaczego ta zmiana

Pierwotne wdrożenie zakładało klasyczne przekierowanie portów 80 i 443 z routera na serwer.
Założenie okazało się nieprawdziwe dla tego łącza.

Diagnostyka wykazała, że operator udostępnia internet w trybie **DS-Lite** — odmianie CGNAT
opisanej w RFC 6333. Świadczą o tym dwie niezależne obserwacje:

1. `traceroute` pokazuje `192.0.0.1` jako drugi przeskok. Zakres `192.0.0.0/29` jest
   zarezerwowany wyłącznie dla komunikacji między urządzeniem abonenta a bramą AFTR operatora.
2. Panel routera przyjął reguły przekierowania dopiero po zaznaczeniu opcji akceptacji
   propozycji portu, po czym przydzielił porty zewnętrzne **9909** i **9920** zamiast żądanych
   80 i 443. Router nie dysponuje portami 80 i 443 na współdzielonym adresie `188.47.103.198`.

Konsekwencja jest podwójna. Let's Encrypt nie wyda certyfikatu, bo wyzwanie HTTP-01 łączy się
wyłącznie z portem 80, a TLS-ALPN-01 wyłącznie z 443 — numery są częścią protokołu ACME i nie
podlegają konfiguracji. Nawet gdyby certyfikat pozyskać wyzwaniem DNS-01, goście i tak nie
weszliby na `https://kasiaikrzys.pl`, bo port 443 tego adresu nie prowadzi do tego serwera.

## Rozważane warianty

**Publiczny adres IPv4 od operatora** — zachowałby całe dotychczasowe wdrożenie bez jednej zmiany
w kodzie. Odrzucony na teraz, bo wymaga zamówienia usługi, wiąże się z opłatą i nieznanym czasem
realizacji. Pozostaje wariantem odwrotu — po jego uzyskaniu wystarczy wrócić do poprzedniego specu.

**Praca na przydzielonym porcie 9920** — certyfikat przez DNS-01, goście wchodzą pod adresem
z numerem portu, którego i tak nie wpisują ręcznie, bo skanują kod QR. Odrzucony: sale weselne
i sieci hotelowe często blokują ruch wychodzący na niestandardowe porty, a awaria ujawniłaby się
dopiero w trakcie przyjęcia, u części gości, bez możliwości diagnozy.

**Cloudflare Tunnel** — wybrany. Serwer nawiązuje połączenie wychodzące, więc brak portów
przychodzących przestaje mieć znaczenie. Dostępny natychmiast i niezależny od operatora.

## Architektura docelowa

```
Internet
   |  HTTPS, certyfikat po stronie Cloudflare
Cloudflare
   |  tunel zestawiony od środka, połączenie wychodzące
VM Ubuntu 24.04 (Proxmox), Docker
   |
   +-- cloudflared  — bez portów, łączy się na zewnątrz
   +-- caddy   :80 wewnętrznie, bez TLS i bez ACME
   |      /api/*    -> api:8080
   |      /photos/* -> api:8080
   |      /*        -> web:80
   +-- web     nginx + Angular
   +-- api     .NET 8
   +-- db      PostgreSQL 16
```

**Żaden kontener nie publikuje portów na hoście.** Serwer przestaje nasłuchiwać na 80 i 443,
więc reguły przekierowania w routerze i wpisy w `ufw` dla tych portów stają się zbędne.
Jedyną drogą do aplikacji jest tunel, który serwer sam zainicjował.

Caddy zostaje, bo nadal odpowiada za routing trzech ścieżek i za przekierowanie `www`.
Traci wyłącznie obsługę TLS: adresy w `Caddyfile` dostają przedrostek `http://`, co wyłącza
automatyczne HTTPS i wszelkie próby ACME.

## Limit 100 MB i wysyłka zdjęć

Darmowy plan Cloudflare odrzuca żądania powyżej 100 MB. `ImagePickerComponent` wysyła dziś
**wszystkie** zaznaczone zdjęcia w jednym `FormData`, więc gość zaznaczający kilkadziesiąt zdjęć
z nowego telefonu przekroczyłby limit i zobaczył błąd w trakcie przyjęcia.

Rozwiązanie: **jeden plik na żądanie, trzy żądania równolegle**.

Pojedyncze zdjęcie nigdy nie zbliży się do 100 MB, więc limit przestaje istnieć zamiast być
omijany arytmetyką. Dodatkowo pasek postępu przestaje być atrapą — pokazuje faktyczną liczbę
wysłanych plików. Niepowodzenie jednego pliku nie przewraca całej paczki; nieudane wysyłki są
zbierane i na końcu proponowane do ponowienia.

Ograniczenie do trzech równoległych żądań wynika z warunków, w jakich aplikacja będzie używana:
goście łączą się przez salowe WiFi albo LTE, gdzie kilkadziesiąt równoległych połączeń obniża
rzeczywistą przepustowość i zwiększa liczbę przekroczeń czasu.

Backend pozostaje nietknięty. Endpoint `POST /api/photos/upload` przyjmuje listę plików, więc
frontend wysyła listę jednoelementową. Limit 200 MB w Kestrelu zostaje jako zabezpieczenie przed
pojedynczym nadmiarowym plikiem.

## Zakres zmian

Nowe:

- `.env.example` — dochodzi `CLOUDFLARE_TUNNEL_TOKEN`, wypada `ACME_EMAIL`

Zmieniane:

- `docker-compose.yml` — usługa `cloudflared`; `caddy` traci sekcję `ports` oraz zmienne ACME
- `Caddyfile` — adresy z przedrostkiem `http://`, bez TLS i bez ACME
- `ClientApp/src/app/services/api.service.ts` — `uploadPhoto` dla pojedynczego pliku
- `ClientApp/src/app/image-picker/image-picker.component.ts` i `.html` — wysyłka po jednym pliku,
  trzy równolegle, licznik postępu, obsługa plików nieudanych
- `ClientApp/src/app/upload-progress/upload-progress.component.ts` i `.html` — rzeczywisty postęp
- `docs/DEPLOYMENT.md` — rozdział o tunelu zastępujący rozdział o przekierowaniu portów i ACME

## Konfiguracja poza repozytorium

1. Konto Cloudflare, dodanie domeny `kasiaikrzys.pl` w planie darmowym
2. Podmiana serwerów nazw w az.pl z `ns6/ns7/ns8.az.pl` na adresy wskazane przez Cloudflare.
   Rejestracja domeny zostaje w az.pl; zmienia się wyłącznie obsługa strefy DNS
3. Utworzenie tunelu w panelu Zero Trust i skopiowanie tokenu do `.env`
4. Publiczne nazwy hosta tunelu: `kasiaikrzys.pl` oraz `www.kasiaikrzys.pl`, obie na `http://caddy:80`
5. Usunięcie reguł przekierowania portów w routerze — nie są już potrzebne

## Ryzyka

**Tunel jest pojedynczym punktem awarii.** Awaria `cloudflared` odcina stronę, mimo że reszta
działa. Łagodzi to `restart: unless-stopped`; procedura ręcznego restartu trafia do runbooka.

**Token tunelu jest sekretem** o sile poświadczenia — kto go zdobędzie, może podszyć się pod tunel.
Trafia wyłącznie do `.env`, który jest w `.gitignore` i w `.dockerignore`.

**Cloudflare cache'uje pliki spod `/photos/*`.** Działa to na korzyść — galeria ładuje się szybciej,
a nazwy plików zawierają GUID, więc nie istnieje ryzyko pokazania nieaktualnej treści.

**Ruch przechodzi przez podmiot trzeci.** Cloudflare odszyfrowuje ruch, więc widzi przesyłane
zdjęcia. Dla galerii weselnej to akceptowalne, ale należy to zapisać jawnie, a nie przemilczeć.

## Testowanie

- `ng build --configuration production` i `dotnet build` przed commitem
- `docker compose config` i `docker compose build`
- Ręcznie po wdrożeniu: wysyłka pojedynczego zdjęcia, wysyłka dwudziestu naraz z licznikiem
  postępu, zachowanie przy zerwanym połączeniu w trakcie wysyłki, deep-link `/<slug>` po
  odświeżeniu, trwałość zdjęć po `docker compose restart api`, przekierowanie z `www`,
  brak nasłuchu na portach 80 i 443 hosta

## Poza zakresem

Niezmiennie otwarte i wymagające osobnej decyzji: `GET /api/photos/event/{id}/download` oddaje
ZIP całej galerii bez uwierzytelnienia, a po tej zmianie będzie dostępny publicznie. Naprawa
wymaga zmiany sposobu pobierania w panelu administratora, który woła ten adres przez
`window.location.href` i nie może dodać nagłówka autoryzacji.

Niezmienione pozostają również: brak generowania miniatur, brak limitowania prób logowania
administratora, brak testów automatycznych.
