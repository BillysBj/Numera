# Auftragsverarbeitungsvertrag (AVV) — ENTWURF

> ⚠️ **ENTWURF, nicht rechtlich geprüft.** Vorlage für den Fachanwalt. Platzhalter `[…]` ersetzen.
> Vertrag nach **Art. 28 DSGVO** zwischen dem Kunden (Verantwortlicher) und dem Anbieter
> (Auftragsverarbeiter) über die Verarbeitung personenbezogener Daten im Rahmen von Numera.

## Parteien

- **Verantwortlicher** („Kunde"): der registrierende Mandant (Unternehmer i. S. d. § 14 BGB).
- **Auftragsverarbeiter** („Anbieter"): NOEMA Essentials UG (haftungsbeschränkt),
  Schäftlarnstraße 72, 81371 München, vertreten durch den Geschäftsführer Vasilios Petropoulos
  (Amtsgericht München, HRB 312487).

Dieser AVV wird mit Vertragsschluss (Registrierung) Bestandteil des Nutzungsvertrags.

## 1. Gegenstand und Dauer

Gegenstand ist die Verarbeitung personenbezogener Daten durch den Anbieter ausschließlich zur
Erbringung der SaaS-Leistung „Numera". Die Dauer entspricht der Laufzeit des Nutzungsvertrags.

## 2. Art, Zweck, Datenarten, Betroffene

- **Art/Zweck:** Speicherung und Verarbeitung der vom Kunden eingegebenen Geschäfts- und
  Buchhaltungsdaten zur Rechnungs-/Belegverwaltung, E-Rechnung, offenen Posten, Mahnwesen, Banking.
- **Datenarten:** Stamm- und Kontaktdaten von Geschäftspartnern des Kunden (Name, Anschrift,
  Steuer-/USt-Id, Bankverbindung, E-Mail, Telefon), Rechnungs- und Belegdaten, ggf. Transaktions-/
  Kontodaten (Banking-Funktion).
- **Betroffene:** Geschäftspartner, Kunden, Lieferanten und Beschäftigte des Kunden.

## 3. Weisungsbindung

Der Anbieter verarbeitet die Daten ausschließlich auf dokumentierte Weisung des Kunden (dieser
AVV, die Nutzung der Funktionen, Support-Anfragen). Hält der Anbieter eine Weisung für
rechtswidrig, informiert er den Kunden.

## 4. Vertraulichkeit

Der Anbieter verpflichtet die zur Verarbeitung befugten Personen auf Vertraulichkeit bzw. stellt
sicher, dass sie einer gesetzlichen Verschwiegenheit unterliegen.

## 5. Technische und organisatorische Maßnahmen (Art. 32)

Der Anbieter trifft die in [`toms.md`](toms.md) beschriebenen Maßnahmen und hält sie dem Stand der
Technik entsprechend aktuell. Die TOMs sind Anlage zu diesem Vertrag.

## 6. Unterauftragsverarbeiter (Art. 28 Abs. 2, 4)

Der Kunde stimmt dem Einsatz folgender Unterauftragsverarbeiter zu:

| Subunternehmer | Zweck | Ort/Region |
|---|---|---|
| Oracle Cloud Infrastructure | Hosting/Infrastruktur | `[Region bestätigen — EU?]` |
| Stripe Payments Europe, Ltd. | Zahlungs-/Abo-Abwicklung | EU/Irland |
| finAPI GmbH | Kontoanbindung (Banking) | Deutschland |
| `[SMTP-/E-Mail-Anbieter]` | Versand System-/Rechnungs-E-Mails | `[…]` |

Der Anbieter informiert über beabsichtigte Änderungen und räumt ein Widerspruchsrecht ein. Mit
jedem Unterauftragsverarbeiter bestehen Verträge mit Pflichten, die diesem AVV entsprechen.

## 7. Unterstützung des Verantwortlichen

Der Anbieter unterstützt den Kunden angemessen bei Betroffenenrechten (Art. 15–22), bei
Datenschutz-Folgenabschätzungen (Art. 35) und bei Meldepflichten (Art. 33/34). Numera stellt
hierfür insbesondere einen vollständigen **Datenexport** (Art. 20) und eine **Konto-Löschung**
bereit; für aufbewahrungspflichtige Geschäftspartner erfolgt eine **Anonymisierung** (Art. 17
i. V. m. §§ 147 AO/257 HGB).

## 8. Löschung und Rückgabe (Art. 28 Abs. 3 lit. g)

Nach Beendigung löscht der Anbieter die Daten oder gibt sie zurück, soweit keine gesetzliche
Aufbewahrungspflicht entgegensteht. Numera realisiert dies über Export + Löschung mit einer
30-tägigen Wiederherstellungsfrist, danach unwiderrufliche Löschung inkl. Blobs und externer
Identitäten (Keycloak); ein Lösch-Nachweis wird in einem personendaten-freien Lösch-Log geführt.

## 9. Nachweise und Kontrollen (Art. 28 Abs. 3 lit. h)

Der Anbieter stellt die zum Nachweis erforderlichen Informationen bereit und ermöglicht
angemessene Überprüfungen. `[Prüfmodalitäten konkretisieren.]`

## 10. Meldung von Verletzungen

Der Anbieter meldet dem Kunden Verletzungen des Schutzes personenbezogener Daten unverzüglich nach
Bekanntwerden mit den verfügbaren Informationen.

## 11. Schlussbestimmungen

Es gilt deutsches Recht. Bei Widersprüchen zwischen AVV und Nutzungsvertrag geht dieser AVV in
datenschutzrechtlichen Fragen vor. `[Anwaltlich prüfen.]`
