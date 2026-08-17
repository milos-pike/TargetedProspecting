# Targeted Prospecting

Mod pro Vintage Story, který přidává možnost cíleného průzkumu rud pomocí pátracího krumpáče a vzorku hledaného materiálu.

Mod původně vznikl pro osobní použití a následně byl upraven pro veřejné vydání.

## Čeština

### Co mod dělá

Při držení podporovaného vzorku rudy v levé ruce lze pomocí pátracího krumpáče spustit cílený průzkum okolí.

Pokud je hledaný materiál nalezen, mod vytvoří waypoint k nejbližšímu odpovídajícímu místu.

Výsledek průzkumu závisí také na materiálu použitého pátracího krumpáče.

Mod dále přidává možnost rozebrat podporované pátrací krumpáče a získat zpět část kovu podle jejich zbývající trvanlivosti.

### Požadavky

- Vintage Story 1.22.0 nebo novější
- žádné další mody nejsou vyžadovány

### Jazyky

- čeština
- angličtina

### Instalace

Stáhněte soubor:

`targetedprospecting_1.0.0.zip`

a vložte jej do složky `Mods` ve Vintage Story.

ZIP není potřeba rozbalovat.

### Diagnostika

Mod obsahuje volitelný diagnostický logger určený především pro řešení problémů a bug reportů.

### Poděkování

Velké poděkování patří **Mathi_bubble** za pečlivé testování modu a cennou zpětnou vazbu během vývoje.

### Vývoj

Implementace probíhala za asistence nástrojů LLM, a to v oblastech překladů, refaktoringu a částečného generování funkcí.

Zdrojový kód je zveřejněn otevřeně.

---

## Podrobný popis mechaniky

Targeted Prospecting rozšiřuje běžný pátrací krumpáč o možnost cíleného hledání konkrétní rudy podle vzorku drženého v levé ruce.

### Zahájení průzkumu

Pro spuštění cíleného průzkumu je potřeba:

- držet podporovaný pátrací krumpáč,
- mít v levé ruce podporovaný vzorek hledaného materiálu,
- mít pátrací krumpáč alespoň na 80 % jeho maximální trvanlivosti,
- nemít aktivní cooldown z předchozího cíleného průzkumu, který trvá 1 herní den.

Průzkum lze zahájit při běžném použití pátracího krumpáče rozbitím kamenného bloku.

### Oblast průzkumu

Mod prohledává oblast 3 × 3 chunky kolem místa, kde byl průzkum spuštěn.

Vyhledává skutečné bloky odpovídající vzorku drženému v levé ruce.

Pokud je nalezen vhodný výskyt, mod vybere nejbližší odpovídající lokaci a vytvoří k ní waypoint.

### Počet waypointů podle materiálu krumpáče

Materiál pátracího krumpáče ovlivňuje, kolik výsledků může cílený průzkum označit:

- měď – 1 waypoint
- bronzové varianty – 2 waypointy
- železo – 3 waypointy
- meteorické železo – 3 waypointy
- ocel – 4 waypointy

Waypointy jsou vybírány podle vzdálenosti. Pokud již má hráč odpovídající naleziště označené předchozím průzkumem, duplicitní waypoint se znovu nevytvoří.

### Ocelový bonus

Ocelový pátrací krumpáč může při průzkumu vytvořit 1 dodatečný waypoint k nalezišti podporovaného drahokamu.

Podporovány jsou:

- diamant,
- smaragd,
- peridot.

Pokud je nalezen dosud neoznačený diamant, má přednost.

Pokud diamant nalezen není a v oblasti se nachází současně smaragd i peridot, mod mezi nimi náhodně vybere. Pokud je dostupný pouze jeden z nich, bude označen ten.

Pokud se v oblasti nachází více vhodných nalezišť vybraného drahokamu, jedno z nich je vybráno náhodně.

Již označená naleziště se znovu neoznačují.

### Trvanlivost nástroje

Cílený průzkum má vlastní cenu opotřebení pátracího krumpáče.

Při úspěšném průzkumu se odečte:

- 75 bodů trvanlivosti

Při neúspěšném průzkumu se použije:

- běžné herní opotřebení pátracího krumpáče

Hráči v režimu Creative nebo Spectator trvanlivost neztrácejí.

### Cooldown

Cílený průzkum lze použít pouze jednou za herní den.

Cooldown se řídí serverovým herním časem.

Pro testování je možné cooldown resetovat připraveným příkazem. Tato možnost je dostupná pouze hráčům s odpovídajícím oprávněním v režimu Creative.

### Sytost

Použití cíleného průzkumu má také cenu v podobě sytosti hráče.

Mechanika je omezena tak, aby spotřeba nepřesáhla 50 % maximální sytosti.

Hráči v režimu Creative nebo Spectator tuto cenu neplatí.

### Podporované vzorky

Jako vzorek lze použít odpovídající předměty reprezentující hledanou surovinu, například:

- valouny,
- kusy rudy,
- krystalizované kusy rudy,
- podporované minerální vzorky.

Mod vždy hledá materiál odpovídající vzorku drženému v levé ruce.

Pokud hráč drží předmět v levé ruce, může pomocí příkazu `/prospectingtargeted` ověřit, zda jej mod rozpozná jako podporovaný vzorek a zda podle něj může vyhledávat.

### Rozebírání pátracích krumpáčů

Hráč může částečně opotřebované pátrací krumpáče, které již nesplňují požadavek 80 % trvanlivosti pro cílený průzkum, rozebrat a získat zpět část kovu.

Podporované pátrací krumpáče lze rozebrat v crafting gridu pomocí:

- pily,
- kladiva,
- dláta.

V režimu Survival se pila, kladivo a dláto při každém rozebrání opotřebí o 1 bod trvanlivosti.

V režimu Creative se tyto nástroje neopotřebovávají.

Rozebrat lze pátrací krumpáče z těchto materiálů:

- měď,
- cínový bronz,
- bismutový bronz,
- černý bronz,
- železo,
- meteorické železo,
- ocel.

Množství získaných kovových valounů závisí na zbývající trvanlivosti krumpáče.

Výsledek je omezen na 1 až 20 valounů.

Například:

- 100 % trvanlivosti → 20 valounů
- 75 % → 15 valounů
- 50 % → 10 valounů
- 25 % → 5 valounů
- téměř zničený krumpáč → minimálně 1 valoun

Násada je při rozebrání zničena.

Zlaté a stříbrné pátrací krumpáče tato mechanika nepodporuje.

### Diagnostický logger

Mod obsahuje vlastní diagnostický logger, který zaznamenává průběh cíleného průzkumu a důležité rozhodovací kroky.

Logger je standardně vypnutý a pro použití musí být nejprve zapnut. Na serveru jej může zapnout hráč s odpovídajícím oprávněním.

Logger je určen především pro diagnostiku problémů a bug reporty. Umožňuje zpětně zjistit, co se během průzkumu stalo a jak mod při jednotlivých krocích rozhodoval.

---

## English

### About

Targeted Prospecting is a mod for Vintage Story that adds targeted ore prospecting using a prospecting pick and a sample of the material held in the off-hand.

If a matching deposit is found, the mod creates a waypoint to its location.

The number of marked deposits depends on the material of the prospecting pick. Steel prospecting picks can also mark one supported gemstone deposit.

The mod also allows supported prospecting picks to be disassembled and partially recycled based on their remaining durability.

### How it works

Targeted prospecting requires a supported ore sample in the off-hand and a prospecting pick with at least 80% durability.

The scan covers a 3 × 3 chunk area and can create:

- Copper – 1 waypoint
- Bronze variants – 2 waypoints
- Iron / Meteoric Iron – 3 waypoints
- Steel – 4 waypoints

Steel prospecting picks can also create 1 additional gemstone waypoint for diamond, emerald or peridot.

A successful targeted prospecting scan costs 75 durability. The ability can normally be used once per in-game day.

Supported prospecting picks can also be disassembled using a saw, hammer and chisel. Depending on the remaining durability, the player receives between 1 and 20 metal bits.

### Requirements

- Vintage Story 1.22.0 or newer
- no additional mods required

### Languages

- Czech
- English

### Installation

Download:

`targetedprospecting_1.0.0.zip`

and place it into the Vintage Story `Mods` folder.

Do not extract the ZIP file.

### Diagnostics

The mod includes an optional diagnostic logger intended mainly for troubleshooting and bug reports.

### Acknowledgements

Special thanks to **Mathi_bubble** for thorough testing and valuable feedback during development.

### Development

The implementation was developed with the assistance of LLM tools, mainly for translations, refactoring and partial function generation.

The source code is published openly.
