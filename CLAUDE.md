# DELTA Wi-Fi Analyzer (macOS-App)

Name der App: **DELTA Wi-Fi Analyzer** (Difference Evaluation of Latency, Throughput & Attenuation), Repository `delta-wifi`. Xcode-Projekt, Target, Scheme und Swift-Modul heißen `DeltaWiFi`. Der frühere Name NetMap lebt nur in den Bundle-IDs (`de.mhs131.NetMap…`) und in der Typkennung `de.mhs131.netmap.wlanprojekt` weiter; beide bleiben bewusst unverändert, damit vorhandene Projektdateien und Freigaben gültig bleiben.

Native macOS-App (Swift/SwiftUI), mit der Alex das WLAN im Haus seiner Eltern **vor und nach einem Netzumbau** ausmisst und den Unterschied als Heatmap auf Grundrissen zeigt. Alle Messungen entstehen in der App selbst. Die früher geplanten Bash-Skripte (`wlan-messung.sh`, `wlan-logger.sh`) und die Web-Messkarte wurden nie eingesetzt; einen Import ihrer Dateien gibt es deshalb nicht (er war gebaut und wurde wieder entfernt).

Sprache der Oberfläche: **Deutsch**. Code, Bezeichner und Kommentare dürfen englisch sein.

## Hintergrund

- **Haus:** 3 Etagen à ca. 75 m², Holz-Kassettenbau, Stahlbetonkeller. Router im offenen Treppenhaus im EG.
- **Vorher:** FRITZ!Box 7590 mit Repeatern 2× 1160 und 1× 1750E. Nur 2,4 und 5 GHz.
- **Nachher:** FRITZ!Box 5690 Pro mit 3× FRITZ!Repeater 1700. Die Box funkt zusätzlich auf 6 GHz.
- **Messgerät:** MacBook Neo (Wi-Fi 6E, also auch 6 GHz).
- **Zweck:** Belegen, dass Abdeckung und Stabilität besser geworden sind. Die Beschwerden waren Abbrüche und hakende Streams, deshalb sind **Paketverlust und Latenzspitzen** genauso wichtig wie die Signalstärke.

## Funktionen

### 1. Projekt
- Ein Projekt pro Haus, als Dokument gespeichert (siehe „Datenformat“).
- **Etagen bzw. Bereiche verwaltet der Nutzer selbst** (anlegen, umbenennen, sortieren, löschen). Ein neues Projekt hat keine; ist einmal eine angelegt, bleibt mindestens eine. Nur Dateien ohne Etagenliste (alter Stand) bekommen beim Öffnen Keller, Erdgeschoss, Obergeschoss, Außen (IDs `keller`, `eg`, `og`, `aussen`).
- Pro Etage ein Grundriss als Bild (JPG/PNG/HEIC per Drag & Drop oder Öffnen-Dialog). Ohne Bild ein schematischer Platzhalter.
- **Phasen verwaltet der Nutzer ebenfalls selbst.** Standard: Vorher (`VORHER`) und Nachher (`NACHHER`). Mindestens eine Phase bleibt.
- Ansichten: eine einzelne Phase oder der **Vergleich zweier frei gewählter Phasen** (von → nach).
- Punkte, Grundrisse, Messungen und Läufe verweisen über IDs auf Etage und Phase; Namen sind frei. Beim Löschen einer Etage bzw. Phase verschwindet ihr Inhalt mit (Rückfrage, widerrufbar).

### 2. Feste Messpunkte
Es gibt keine fest eingebaute Punktliste. Ein neues Projekt startet ohne Etagen und ohne Punkte und **fragt beim Öffnen nach den Etagen bzw. Bereichen und den Messpunkten darin** (Einrichtungsdialog; „Später“ ist möglich, dann zeigt das Fenster einen Hinweis mit Knopf zur Einrichtung).

- Punkte lassen sich ergänzen. Ergänzte Punkte gelten für alle Phasen.
- Position auf dem Grundriss per Klick setzen bzw. per Ziehen verschieben (relativ 0…1).
- **Geführter Ablauf:** nächster offener Punkt der aktuellen Phase, Punkt gezielt wiederholen, optionale Notiz je Messung. Es zählt die jeweils letzte Messung pro Punkt und Phase, frühere bleiben gespeichert.
- **Eine Messung (ca. 1,5–2 min):**
  - **Signal:** 10 Stichproben im 1-s-Takt, Median auf ganze Zahl gerundet. Erfasst werden RSSI, Rauschen, SNR (RSSI − Rauschen), Link-Rate, Band, Kanal, Kanalbreite, PHY-Modus, BSSID, SSID. Rauschwerte außerhalb von −110…−75 dBm gelten als nicht gemessen (macOS meldet zeitweise Unsinn wie −17 dBm): Sie fallen aus dem Median, und die Auswertung übergeht den SNR bereits gespeicherter Messungen und Lauf-Samples mit solchem Rauschen.
  - **Ping, parallel zur Signalmessung:** 60 s auf das Gateway und 60 s auf `1.1.1.1`, jeweils über das WLAN-Interface. Auswerten: Verlust in %, Durchschnitt und Maximum in ms.
  - **Internet-Speedtest:** `/usr/bin/networkQuality -I <if> -s -c` liefert JSON mit `dl_throughput` / `ul_throughput` in bit/s. Abschaltbar.
  - **Intern (optional):** `iperf3 -c <server> -B <wlan-ip> -t 10 -P 4 [-R] -J`, Wert aus `end.sum_received.bits_per_second` (Summe der vier parallelen Ströme; ein Einzelstrom schwankte über WLAN zu stark). Server-IP einstellbar, in der Praxis ein Raspberry Pi am LAN-Kabel. Nur wenn `iperf3` gefunden wird (Homebrew-Pfade `/opt/homebrew/bin`, `/usr/local/bin`).

### 3. Lauf-Modus (Heatmap-Begehung)
- Lauf starten: Etage und Phase wählen.
- Ab dem Start sampelt die App jede Sekunde die Funkwerte (RSSI, Rauschen, SNR, Link-Rate, Band, BSSID) mit Zeitstempel.
- Alex läuft mit dem MacBook und klickt alle paar Meter seine aktuelle Position auf dem Grundriss an. Jeder Klick wird als Tap mit Zeitstempel gespeichert. „Letzte Position löschen“ nimmt den letzten Tap zurück.
- **Zuordnung:** Jedes Sample zwischen erstem und letztem Tap bekommt seine Position durch lineare Interpolation zwischen den beiden umgebenden Taps (nach Zeit). Samples außerhalb fallen weg. Das kann live geschehen, muss aber spätestens beim Beenden passieren.
- Weg und Taps auf dem Plan zeichnen. Abgeschlossene Läufe gestrichelt, den aktiven durchgezogen.
- Läufe sind auflistbar und löschbar.
- Läufe liefern nur Funkwerte (Signal, SNR, Link-Rate), keinen Ping und keinen Durchsatz.

### 4. Heatmap und Auswertung
Gewählte Messgröße, Grenzwerte für die Farbskala (gut / okay / kritisch):

| Messgröße | Feld | besser | gut | okay |
|---|---|---|---|---|
| Signalstärke | rssi_dbm | höher | ≥ −60 dBm | ≥ −70 dBm |
| Signal-Rausch-Abstand | snr_db | höher | ≥ 30 dB | ≥ 20 dB |
| Link-Rate | txrate_mbit | höher | ≥ 400 Mbit/s | ≥ 150 Mbit/s |
| Paketverlust zur Box | ping_gw_verlust_pct | niedriger | 0 % | ≤ 1 % |
| Max. Ping zur Box | ping_gw_max_ms | niedriger | ≤ 30 ms | ≤ 100 ms |
| Internet Download | inet_down_mbit | höher | ≥ 120 Mbit/s bzw. ≥ 80 % des Anschlusses | ≥ 60 Mbit/s bzw. ≥ 50 % des Anschlusses |
| Intern Download (iperf3) | lan_down_mbit | höher | ≥ 300 Mbit/s | ≥ 100 Mbit/s |

- **Anschlussrate (optional):** Der Nutzer kann die synchronisierte Down- und Upload-Rate des Internetanschlusses im Projekt eintragen (`line_down_mbit`, `line_up_mbit`). Ist der Download gesetzt, richten sich die Grenzen für „Internet Download“ danach (gut ab 80 %, okay ab 50 %), und Internet-Werte erscheinen zusätzlich als Anteil, z. B. „212 Mbit/s (85 %)“. Ohne Eingabe gelten die festen Grenzen.
- **Interpolation:** inverse Distanzgewichtung über alle Werte der Etage und Phase, also feste Punkte plus Lauf-Samples. Gewicht `w = 1 / (d² + 0.0004)^1.25` (entspricht Potenz 2,5, an den Punkten geglättet). Distanzen seitenverhältnis-korrigiert: x wird mit Breite/Höhe des Plans multipliziert, y bleibt 0…1.
- **Farbe:** stetiger Verlauf Rot → Gelb → Grün entlang der Grenzwerte, Deckkraft ca. 0,58.
- **Unsicherheit sichtbar machen:** Je weiter ein Pixel vom nächsten Messwert entfernt ist, desto blasser (Mindestdeckkraft 0,18). Ab etwa 0,32 Planhöhe deutlich blass, mit Lauf-Daten schon ab 0,18.
- **Vergleich:** zwei getrennt interpolierte Felder der beiden gewählten Phasen pixelweise subtrahieren. Grau = unverändert, Grün = besser, Rot = schlechter. Volle Sättigung bei 1,5 × |gut − okay|. Wo eine Phase fehlt, nichts zeichnen.
- **Marker der festen Punkte:** farbiger Punkt plus Wert. Im Vergleich „vorher → nachher“.
- **Übersicht je Phase:** Median, Anzahl gut/okay/kritisch, Anzahl besser/schlechter.
- **Tabelle aller Punkte:** Vorher, Nachher, Veränderung.
- **Detailansicht je Punkt:** alle Werte beider Phasen nebeneinander, dazu die Notizen.

## Technik

- **SwiftUI**, Mindestziel die installierte macOS-Version. Keine Fremdabhängigkeiten.
- **Funkwerte über CoreWLAN:** `CWWiFiClient.shared().interface()` mit `rssiValue()`, `noiseMeasurement()`, `transmitRate()`, `wlanChannel()` (Nummer, Band, Breite), `activePHYMode()`, `bssid()`, `ssid()`, `interfaceName`.
- **BSSID/SSID** liefert macOS nur mit **Ortungsfreigabe**:
  - `CLLocationManager` beim Start anfragen.
  - Info.plist braucht `NSLocationUsageDescription` (und `NSLocationWhenInUseUsageDescription`) mit deutschem Text, etwa: „Die App benötigt die Ortungsfreigabe, damit macOS anzeigt, mit welchem WLAN-Knoten der Mac verbunden ist.“
  - Fehlt die Freigabe, weiter messen und in der UI deutlich auf die fehlende BSSID hinweisen.
- **App Sandbox aus:** Entitlements ohne `com.apple.security.app-sandbox`, damit `ping`, `networkQuality`, `route` und `iperf3` per `Process` laufen. Hardened Runtime darf aktiv bleiben.
- **Gateway ermitteln:** `route -n get default` und die Zeile `gateway:` auswerten. Fallback `192.168.178.1`.
- **WLAN-IP:** IP-Adresse des CoreWLAN-Interface (z. B. `getifaddrs`). Ping mit `/sbin/ping -b <if> -c 60 -i 1 <ziel>`, ausgewertet werden die Zeilen `packet loss` und `round-trip min/avg/max/stddev`.
- **Nebenläufigkeit:** Messungen als `async` Tasks, abbrechbar. UI bleibt bedienbar, Fortschritt sichtbar.
- **Kein sudo, kein `wdutil`** mehr nötig.

## Datenformat

- Dokumentbasiert (`DocumentGroup` mit `FileDocument`), eigene Endung **`.wlanprojekt`**, Inhalt JSON (UTType selbst deklarieren).
- Das Format (historisch an `projekt.json` der Web-Messkarte angelehnt):

```jsonc
{
  "version": 1,
  "floors":  [ { "id": "eg", "name": "Erdgeschoss" } ],                                // Etagen in Anzeigereihenfolge; fehlt die Liste, gelten die vier Standardetagen
  "phases":  [ { "id": "VORHER", "name": "Vorher" }, { "id": "NACHHER", "name": "Nachher" } ],  // Phasen in Anzeigereihenfolge; fehlt die Liste, gelten Vorher/Nachher
  "plans":   { "eg": { "src": "data:image/jpeg;base64,…", "w": 1600, "h": 1120 } },   // Grundriss je Etage, auf max. 1600 px skaliert
  "points":  [ { "name": "Büro Keller", "floor": "keller", "x": 0.28, "y": 0.38 } ],   // x/y relativ 0…1, null = nicht gesetzt
  "measurements": {                                                                   // je Phase: Punktname → letzte Messung
    "VORHER":  { "Büro Keller": { "rssi_dbm": -79, "noise_dbm": -92, "snr_db": 13, "txrate_mbit": 6, "band_ghz": "2.4",
                 "ping_gw_verlust_pct": 1.7, "ping_gw_avg_ms": 18, "ping_gw_max_ms": 310,
                 "ping_inet_verlust_pct": 3.3, "ping_inet_max_ms": 290, "inet_down_mbit": 22, "inet_up_mbit": 8,
                 "lan_down_mbit": 5, "bssid": "", "notiz": "", "wiederholung": 1 } },
    "NACHHER": { }
  },
  "walks":  [ { "id": "w…", "phase": "VORHER", "floor": "eg", "started": 1791745000000,
                "taps": [ { "t": 1791745003000, "x": 0.5, "y": 0.56 } ] } ],           // Zeiten in ms seit Epoch
  "survey": { "w…": [ [0.5, 0.56, -52, 40, 744, "5"] ] }                              // [x, y, rssi, snr, rate, band], optional als 7. Element die BSSID (Erweiterung der App)
}
```

- Zusätzlich darf die App die Messhistorie (alle Wiederholungen) speichern. Die Felder oben müssen beim Lesen und Schreiben kompatibel bleiben.

## Windows-Pendant (`windows/`)

Dieselbe App für Windows 10/11: **C# mit WPF auf .NET 10**, keine Fremdabhängigkeiten in der App (nur xUnit im Test-Projekt). Funktionen, Grenzwerte, Heatmap und Dateiformat sind identisch; beide Apps öffnen dieselben `.wlanprojekt`-Dateien. Änderungen an Logik oder Format bitte immer in beiden Apps nachziehen.

- **Aufbau wie in der Mac-App:** `windows/DeltaWiFi/` mit `Model` (Projekt, JSON), `Logic` (reine, getestete Funktionen), `Services` (WLAN, Ping, Speedtest, iperf3, Messablauf, Lauf-Rekorder), `Views` (WPF). Tests in `windows/DeltaWiFi.Tests`.
- **Oberfläche:** Ein Fenster je Projekt. `MainWindow.Refresh()` bringt nach jeder Änderung die ganze Anzeige auf den Stand des Projekts; Änderungen laufen über `MainWindow.Edit(...)` und sind damit widerrufbar (Strg+Z). Hell-/Dunkelmodus über das Fluent-Thema von WPF (`ThemeMode="System"`), eigene Farben ohne feste Hell-/Dunkelwerte (siehe `Views/Ui.cs`).
- **Funkwerte über `wlanapi.dll`** (`Services/WiFi.cs`): `WlanQueryInterface` mit `current_connection` (SSID, BSSID, PHY, Link-Rate), `realtime_connection_quality` (ab Windows 11 24H2: RSSI, Frequenz) und als Rückfall `rssi` bzw. die BSS-Liste.
  - **Kein Rauschpegel, also kein SNR:** Windows liefert ihn nicht. `noise_dbm` und `snr_db` bleiben bei Windows-Messungen leer.
  - **Kanalbreite** melden die meisten Treiber nicht; sie wird aus den Information Elements des Beacons gelesen (`RadioChannel.Width`: HT-, VHT-, im 6-GHz-Band HE-Operation).
  - **Ortungsfreigabe:** Ohne „Ortungsdienste“ für Desktop-Apps verweigert Windows SSID und BSSID (`ERROR_ACCESS_DENIED`). Dann weiter messen und im Messpanel darauf hinweisen (Knopf öffnet `ms-settings:privacy-location`).
- **Ping:** `IcmpSendEcho2Ex` aus `iphlpapi.dll` mit der IP-Adresse des WLAN-Adapters als Absender, 60 Anfragen im Sekundentakt, Wartezeit je Anfrage 4 s. Kein Aufruf von `ping.exe`, weil dessen Ausgabe von der Systemsprache abhängt.
- **Gateway und WLAN-IP:** aus `NetworkInterface` des Adapters (über seine GUID), Fallback `192.168.178.1`.
- **Internet-Speedtest:** eingebaut statt `networkQuality`: vier parallele HTTP-Ströme gegen `speed.cloudflare.com` (`__down` / `__up`), 8 s je Richtung, die ersten 2 s zählen nicht. Die Werte sind mit denen vom Mac nur grob vergleichbar.
- **iperf3:** wie am Mac (`-c <server> -B <wlan-ip> -t 10 -P 4 [-R] -J`); gesucht wird `iperf3.exe` neben der App, im `PATH` und in den WinGet-Verknüpfungen.
- **Grundrisse:** WPF-Imaging (WIC). JPG und PNG immer, HEIC nur mit installierter HEIF-Bilderweiterung.
- **Einstellungen** (Speedtest an/aus, iperf3-Server, Laufwege zeigen): `%APPDATA%\DeltaWiFi\settings.json`.
- **Bauen und testen** (im Ordner `windows/`, .NET 10 SDK nötig, kein Visual Studio):
  ```
  dotnet build
  dotnet test
  dotnet run --project DeltaWiFi
  dotnet publish DeltaWiFi -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
  ```
- **Hardware-abhängig und nur am Gerät prüfbar:** `Services/WiFi.cs`, `Pinger.cs`, `SpeedTest.cs`, iperf3 und die Ortungsfreigabe.

## Bauen und Arbeiten

- Nach jeder Änderung bauen und Fehler sofort beheben:
  ```
  xcodebuild -project <Name>.xcodeproj -scheme <Name> -configuration Debug -destination 'platform=macOS' build
  ```
  `<Name>` bitte aus dem Projektordner ermitteln. Falls `xcodebuild` über ein fehlendes SDK klagt, hat Alex `sudo xcode-select -s /Applications/Xcode.app` noch nicht ausgeführt: darauf hinweisen, nicht selbst sudo ausführen.
- **Reine Logik als testbare Funktionen** (Unit-Tests, falls ein Test-Target existiert): Zeitinterpolation, IDW, Farbskala, Ping-/networkQuality-Auswertung.
- **Hardware-abhängige Teile** (CoreWLAN, echte Pings, Ortungsfreigabe) kann nur Alex am Gerät prüfen. Sag konkret, was er testen soll.
- **In kleinen, lauffähigen Schritten bauen:**
  1. Dokument + Datenmodell
  2. Grundriss-Ansicht mit Punkten
  3. Heatmap
  4. feste Messung
  5. Lauf-Modus
  6. Übersicht/Tabelle
- **Gestaltung:** sachlich, gut lesbar, Hell- und Dunkelmodus. Signalfarben (grün/gelb/rot) nur für Status, nicht als Deko.
