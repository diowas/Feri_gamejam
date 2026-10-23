# FERI UM – PS1 mapa za Unity (Halloween game jam)

Sprehodljiva 3D mapa stavbe G2 na FERI (Maribor) v PS1 stilu: avla, tribuna in stopnice,
zgornji hodnik z Alfo in Beto, učilnice P01–P06, delujoče dvigalo, parkirišči −1/−2 in jedilnica.
Mapa se zgradi z enim klikom iz `Data/feri_layout.json`.

## Hitri začetek

1. Uvozi `FERI_Map.unitypackage` (ali skopiraj mapo `FERI_Map` v `Assets/`).
   Deluje v Unity 2022.3+ in Unity 6, z Built-in RP ali URP. Oba vhodna sistema (stari Input Manager in novi Input System) sta podprta.
2. V meniju izberi **FERI → Zgradi mapo v novo sceno**.
   Ustvari in shrani se scena `Assets/FERI_Map/FERI_Map.unity` z vso geometrijo, colliderji, lučmi, dvigalom in igralcem. Peka osvetlitve traja nekaj sekund.
3. Pritisni **Play**.

| Tipka | Akcija |
|---|---|
| WASD + miška | hoja / pogled |
| Shift | tek |
| Ctrl | počep |
| E ali levi klik | interakcija (gumbi dvigala) |
| F | svetilka |
| Esc | sprosti miško (klik jo spet zaklene) |

## Kaj je v mapi

Mere so ocenjene iz tvojega natančnega tlorisa (≈ 0,07 m na piksel) in 360° posnetkov.
Koordinate: **X+** proti vhodu na Koroški, **Z+** proti P01–P03, hala je na `y = 0`.

- **Avla (hala)** – teraco tla, svetli robni pasovi, stebri in lesena fasada s severne strani, perforirana pločevina z juga, steklena streha, dva mostova čez atrij. Vhodna steklena stena na vzhodu z zaprtima izhodoma (trak), prazno varnostnikovo mesto, dve pokvarjeni dvigali (»NE DELA«), zaprte stopnice (JV), stopnice gor (S, levi krak prečrtan), prazen hodnik do Referata (zaklenjen).
- **Tribuna + stopnice** – lesena tribuna (previsoka za hojo, z vrvjo na vrhu), granitne stopnice z ograjo na severni strani in ozke lesene stopnice ob P06.
- **Zgornji nivo (`y = 2`)** – greš po stopnicah gor, desno so **stopnice dol** (slepe, do zaklenjene kleti), naprej na desni **vrata V2** in nato **V1** v Alfo. Na levi Beta (V4 odprta, V3 zaklenjena). Na koncu hodnika steklena stena, jeklene stopnice na galerijo in **most ven v temo**, ki je na koncu zaprt. Spodaj ni nič, v daljavi bela hiša z enim prižganim oknom.
- **Alfa (G2-P1)** – vrste se spuščajo proti tabli (10 vrst), lesene stene, okenski trak, črn akustični pas, kovinski strop s svetlobnimi trakovi, table + platno.
- **Beta (G2-P2)** – vrste se dvigajo proti stekleni zadnji steni, barvni sedeži, črna akustična stena.
- **P01–P06** – ravna tla, mize in stoli po tvoji skici, tabla na označeni steni. P01–P03 velika okna, P04–P06 majhna okna visoko pod stropom. P04 in P06 sta brez luči, v P02 ena utripa.
- **Prehod do dvigala** – ob Beti, z zaklenjenimi stranskimi vrati (vhod 2) in zaklenjenimi stopnicami.
- **Dvigalo** – plošča kot v resničnem dvigalu (3 4 / 1 2 / −1 0 / −3 −2):
  - **−2, −1** parkirišči (avtomobili, stebri, rampa med nivojema, zaprta garažna vrata),
  - **0** zgornji nivo avle (kjer dvigalo stoji),
  - **4** jedilnica na galeriji nad atrijem (pult, hladilnika, mize in stoli),
  - **1, 2, 3, −3** se ne odzovejo (pisk + rdeč gumb).
  - Pred vrati v vsakem delujočem nadstropju je gumb za klic in prikazovalnik nadstropja.

Tlorisi vseh nivojev in posnetki iz predogleda so v mapi `Preview/` poleg paketa.

## PS1 osvetlitev

Vsi materiali uporabljajo shader **FERI/PS1** (tresenje vozlišč, afino teksturiranje, točkovno filtriranje, megla, dithering). Unityjeve luči nanj ne vplivajo, namesto njih:

- **PS1Light** – statične luči so zapečene v barve vozlišč (s sencami), utripajoče (`flicker > 0`) se računajo sproti. Po premikanju luči: **FERI → Ponovno zapeci osvetlitev**.
- **PS1Flashlight** – svetilka (na kameri igralca). Če imate model svetilke (npr. norenwind paket), dajte komponento nanj, sveti v smeri `forward`.
- **PS1Lighting** (objekt *PS1 Lighting*) – ambient, megla, svetlost, tresenje, dithering.
- **PS1Camera** – izris v nizki resoluciji (240 vrstic). Izklopi `lowRes`, če želiš polno ločljivost.

## Uporaba asset packov

- **Teksture**: v `FERI_Map/Materials` zamenjaj teksturo v materialu (npr. `park_floor`, `concrete` s teksturami iz *PSX Parking Garage* ali *PSX Office*). Builder obstoječih materialov ne prepiše, zato zamenjave ostanejo tudi po ponovni gradnji.
- **Propi (buče, svetilka, pisarniški predmeti)**: izberi prefab ali material in klikni **FERI → Pretvori izbrane materiale v PS1**. Tako dobijo isti videz in jih osvetlijo PS1 luči (dinamično, brez peke).
- Na parkirišču so preprosti avtomobili iz kock – lahko jih zamenjate s tistimi iz *PSX Parking Garage*.

## Pogosta vprašanja

- **Mapa je črno-bela / manjkajo teksture**: zaženi **FERI → Posodobi materiale**, nato znova **FERI → Zgradi mapo v novo sceno**.
- **Spremenjene skripte se ne poznajo**: desni klik na `Assets/FERI_Map` → **Reimport**.
- **Material je vijoličen**: shader se ni prevedel – preveri Console.

## Spreminjanje mape

Postavitev je v `~Generator/layout.py` (Unity mape z `~` ignorira). Potrebuješ Python 3, numpy in Pillow:

```
cd Assets/FERI_Map
python ~Generator/tex.py Textures
python ~Generator/layout.py Data/feri_layout.json Textures/atlas.json
python ~Generator/render.py Data/feri_layout.json Textures ~Preview plan   # tlorisi za preverjanje
```

Nato v Unityju spet **FERI → Zgradi mapo …**. Funkcije `box`, `wall_x/wall_z` (z luknjami za vrata in okna), `door`, `stairs`, `rail_x/rail_z`, `lamp` in `classroom` pokrijejo vse, kar je v mapi.

## Predpostavke

- Mere so ocene (natančnih načrtov nismo imeli); tloris se ujema z razporeditvijo prostorov.
- Nadstropja 1–3 niso modelirana (fasade atrija so samo kulisa).
- V3 v Beto je zaklenjena, ker je na višini zadnjih vrst in je s hodnika ni mogoče doseči brez stopnic.
- Dvigalo »0« je na zgornjem nivoju avle (ob Beti, tako kot na skici), ne na nivoju vhoda.
