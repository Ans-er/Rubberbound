# Rubberbound

Teammitglieder: Andreas Rothaler (217345)

Unity Version: 6000.0.71f1

## Verwendete Assets

- Obi Rope - Virtual Method https://assetstore.unity.com/packages/tools/physics/obi-rope-55579
- stylised-character-controller - Joe Binns (Open Source, Basis für den Character Controller) https://github.com/joebinns/stylised-character-controller
- Character Movement Fundamentals - Jan Ott (erster Character Controller, später ersetzt) https://github.com/Jan-Ott/CharacterMovementFundamentals
- FishNet: Networking Evolved - FirstGearGames (für den Prediction-Prototyp) https://assetstore.unity.com/packages/tools/network/fishnet-networking-evolved-207815
- POLYGON Starter Pack / POLYGON Generic - Synty Studios (Level-Umgebung) https://assetstore.unity.com/publishers/5217
- AllSky Free - rpgwhitelock (Skybox) https://assetstore.unity.com/packages/2d/textures-materials/sky/allsky-free-10-sky-skybox-set-146014
- Low Poly Kangaroo: Modell und Animationen wurden von einem Freund eigens für dieses Projekt erstellt
- Unity Packages: Netcode for GameObjects, Multiplayer Services (Lobby/Relay), Input System, TextMesh Pro

## Eigens erstellte Assets

- Level-Design und Szenenaufbau (Lobby- und Spielszene, Checkpoints, Ziellinie)
- Alle Gameplay- und Netzwerk-Skripte in `Assets/Scripts`, soweit oben und in den Quellen nicht anders angegeben (Seilform-Streaming, Lobby-System, Rundenlogik, Orbit-Kamera)

## Startup

Zum Spielen wird eine Internetverbindung benötigt, da die Verbindung der beiden Spieler über Unity Relay läuft.

Das Projekt nutzt Unity Gaming Services (UGS). Damit es nicht über den Account des Autors läuft, kann das Projekt einmalig mit einem eigenen UGS-Projekt verbunden werden:

1. Projekt in Unity öffnen (Version über den Unity Hub installieren).
2. Unter **Project Settings / Services** mit dem eigenen Account anmelden und ein Cloud-Projekt verknüpfen.
3. Im UGS Dashboard (Web) für das Projekt **Authentication** (anonym reicht), **Relay** und **Lobby** aktivieren.

Danach:

1. Die Startszene `Assets/Scenes/LobbyScene.unity` öffnen und starten.
2. Ein Spieler klickt **Create Game**, der zweite klickt **Join Game** und wird automatisch der offenen Lobby zugeteilt. Ein Join-Code muss nirgends eingegeben werden (warum, steht in den Lessons Learned).
3. Der Host startet die Runde aus der Lobby heraus.
4. Zum lokalen Testen zwei Instanzen starten (z.B. Editor + Build). Beide Instanzen melden sich automatisch als unterschiedliche Spieler an.

## Steuerung

| Taste | Funktion |
| :---: | :--- |
| **W / A / S / D** | Bewegen |
| **Maus** | Kamera drehen (Orbit-Kamera) |
| **Leertaste (halten)** | Sprung aufladen. Während des Aufladens ist die Position eingefroren |
| **Leertaste (loslassen)** | Abspringen. Je länger aufgeladen, desto höher und weiter |
| **Esc** | Pausenmenü |

## Beschreibung des Projektes

Rubberbound ist ein kooperatives Online-Rage-Game für zwei Spieler. Vorbild ist *Chained Together*, mit einem entscheidenden Unterschied: Die beiden Spieler sind nicht durch eine starre Kette verbunden, sondern durch ein echtes, physikalisch simuliertes Gummiband. Das Band dehnt sich, speichert Energie und schleudert beide Spieler zurück, wenn sie sich unkoordiniert bewegen. Wer das Level schaffen will, muss jeden Sprung mit dem Partner absprechen.

Drei Dinge tragen das Spielgefühl:

- **Die elastische Kopplung.** Das Gummiband ist eine echte Obi-Rope-Simulation. Es wickelt sich um Hindernisse, snappt zurück und wirkt mit realen Kräften auf beide Spieler.
- **Der aufgeladene Sprung.** Leertaste halten lädt den Sprung auf, loslassen schießt ab (Jump-King-Prinzip). Das macht jeden Sprung zu einer bewussten Entscheidung.
- **Der kräftebasierte Controller.** Ein Floating-Capsule-Controller nach dem Vorbild von *Very Very Valet*: Der Körper schwebt über einen Raycast-Feder-Dämpfer über dem Boden, die gesamte Bewegung läuft über Kräfte.

Gespielt wird als animiertes Low-Poly-Känguru. Das Spiel enthält ein komplettes Rundengerüst: Spawn-Punkte, Checkpoints, Respawn beim Herunterfallen, einen Level-Timer und eine Ziellinie.

## Verwendete Technologien

- **Unity (URP)** als Engine.
- **Obi Rope**, ein partikelbasierter XPBD-Solver (Extended Position Based Dynamics) mit Burst-Backend. Das Band zieht über Dynamic Attachments an beiden Spieler-Rigidbodies.
- **Unity Netcode for GameObjects (NGO)** mit **Unity Lobby und Relay** für Matchmaking und Verbindungsaufbau ohne Port-Forwarding.
- Ein **server-autoritatives Architekturmodell**: Der Host simuliert als einziger die komplette Physik (beide Spielerkörper und das Seil), der Client sendet nur Input und bekommt Transforms und Seilform zurückgestreamt.
- **FishNet** für einen begonnenen Prototyp mit Client-Side Prediction.
- **Unity Input System** für die Eingaben.

## Besondere Herausforderungen / Lessons Learned

Der Weg zum fertigen Gummiband war eine Kette von Erkenntnissen: vom Multiplayer-Fundament über das selbst geschriebene Seil und den falschen Character Controller bis zum Tunneling-Problem und der Netzwerk-Physik. Die Reihenfolge hier ist die tatsächliche Chronologie des Projekts. Die mit Abstand meiste Zeit haben die letzten beiden Kapitel gekostet: das Tunneling durch dünne Hindernisse und die Synchronisation der nicht-deterministischen Seilphysik über das Netzwerk.

### Erst die Testschleife: Lobby und Relay ohne Join-Code

Noch bevor die erste Zeile Seil-Code entstand, habe ich das Multiplayer-Fundament gebaut. Im Projekt aus dem letzten Semester lief das Verbinden über einen Join-Code: Der Host sah den Code unten rechts im UI, der Client musste ihn abtippen. Für gelegentliches Spielen ist das in Ordnung. Mir war aber von Anfang an klar, wie oft ich in diesem Projekt Multiplayer-Situationen testen würde, und dann ist das ständige Kopieren und Eintippen des Codes wirklich lästig.

Als Grundlage habe ich den [Lobby/Relay-Kurs von Code Monkey](https://www.youtube.com/watch?v=7glCsF9fv3s) durchgearbeitet und das System anschließend gezielt auf schnelles Testen umgebaut: Es gibt keinen sichtbaren Join-Code mehr. Der Host klickt **Create Game**, dabei wird eine Lobby erstellt, eine Relay-Allocation angelegt und der Relay-Join-Code versteckt in den Lobby-Daten abgelegt (nur für Lobby-Mitglieder sichtbar). Der zweite Spieler klickt **Join Game** und wird automatisch der neuesten offenen Lobby mit freiem Platz zugeteilt (`JoinNewestLobby` in `RBGameLobby.cs`); den Relay-Code liest sein Client selbst aus den Lobby-Daten. Jede Test-Iteration besteht damit aus genau zwei Klicks.

Ein paar Details, die sich dabei bewährt haben:

- Die anonyme UGS-Anmeldung bekommt bei jedem Start ein zufälliges Profil. Dadurch zählen zwei Instanzen auf demselben Rechner (Editor + Build) als verschiedene Spieler, sonst würde sich die zweite Instanz mit derselben Identität anmelden.
- Der Host hält die Lobby mit einem Heartbeat alle 15 Sekunden am Leben, sonst räumt der Lobby-Service sie nach kurzer Inaktivität weg.
- Ein Connection-Approval-Callback auf dem Host lehnt Verbindungen ab, wenn das Spiel schon läuft oder voll ist (maximal 2 Spieler), inklusive Begründung für den abgelehnten Client.
- Beim Spielstart wird die Lobby gesperrt (`IsLocked`), damit niemand mitten in eine laufende Runde joint.
- Beim Beenden löscht der Host seine Lobby bzw. trägt sich der Client selbst aus. So bleiben keine verwaisten Lobbys übrig, in die ein späteres **Join Game** sonst hineinlaufen würde.

**Fazit:** Die Zeit, die früh in die Testschleife geflossen ist, hat sich über das ganze Projekt verzinst. Gerade die späteren Netzwerk-Kapitel hätten mit Code-Abtippen bei jedem Testlauf ein Vielfaches an Zeit gekostet.

### Vom selbst geschriebenen Verlet-Seil zu Obi Rope

Eingestiegen bin ich mit einem [Tutorial-Video über 2D-Verlet-Integration für Seilsimulation](https://www.youtube.com/watch?v=bxG3XP4MVzk). Den Code daraus habe ich auf 3D umgeschrieben und daraus das Skript `RopeVerlet.cs` gebaut (heute unter `Assets/Scripts/Rubber Band/old/`), gerendert über einen LineRenderer. Das lief und sah auch nach einem Seil aus, aber mir wurde schnell klar, dass das für ein Gameplay-Gummiband nicht reicht: keine stabile Kollision mit der Umgebung, keine saubere Kraftkopplung an Rigidbodies, und jede Verbesserung hätte bedeutet, einen eigenen Physik-Solver zu schreiben.

Aus der Recherche in dieser Phase stammen zwei Funde: der erste Character Controller des Projekts (dazu gleich mehr) und [Obi Rope](https://assetstore.unity.com/packages/tools/physics/obi-rope-55579) mit dem dahinterliegenden XPBD-Ansatz, auf den ich umgestellt habe. Die Beschäftigung mit Verlet-Integration und Constraints war trotzdem kein verlorener Aufwand: Sie hat mir später sehr geholfen zu verstehen, was Obi intern eigentlich tut.

### Der falsche Character Controller

Als Character Controller hatte ich zunächst [Character Movement Fundamentals](https://github.com/Jan-Ott/CharacterMovementFundamentals) im Einsatz. Ich hielt ihn für einen Rigidbody-Controller, was er formal auch ist, er hat einen Rigidbody. Der Haken: Er bewegt diesen kinematisch, also ohne echte Physik. Externe Kräfte wirken auf einen kinematischen Körper schlicht nicht, und damit konnte das Obi-Seil die Spieler nicht ziehen.

Das habe ich anfangs nicht durchschaut und stattdessen lange an meinen Seil-Skripten herumgedoktert, bis klar wurde, dass die Ursache der kinematische Rigidbody selbst war. Die Konsequenz war der Wechsel auf den [stylised-character-controller](https://github.com/joebinns/stylised-character-controller), einen Open-Source-Nachbau des Controllers aus *Very Very Valet* ([Video der Entwickler](https://www.youtube.com/watch?v=qdskE8PJy6Q)): eine schwebende Kapsel über einem Raycast-Feder-Dämpfer, komplett kräftebasiert.

**Fazit:** Rigidbody ist nicht gleich Physik. Und der erzwungene Wechsel war ein Glücksfall, denn genau diese Kräftebasis wurde später der Schlüssel zur Tunneling-Lösung.

### Das Seil tunnelt durch dünne Pfähle

Das hartnäckigste Problem: Wird das Gummiband um einen dünnen Pfahl gespannt und beide Spieler ziehen kräftig, rutscht das Seil durch den Pfahl hindurch, statt außen herumzulaufen. Das ist kein gewöhnlicher Bug, sondern ein struktureller Konflikt im XPBD-Solver. Obi löst pro Schritt zwei konkurrierende Bedingungen: Die Dehnungs-Constraints ziehen das Seil auf seine Ruhelänge zusammen, die Kollisions-Constraints halten es am Pfahl auf. Unter starkem Zug überstimmt die Dehnung die Kollision, und das Seil rutscht durch.

Bei der Suche nach Lösungsansätzen bin ich auf [Incremental Potential Contact (IPC)](https://ipc-sim.github.io/) gestoßen, ein Verfahren, das Durchdringungen prinzipiell unmöglich macht, indem die Annäherung an ein Hindernis für den Solver beliebig teuer wird. Mit KI-Unterstützung habe ich versucht, diese Logik als analytischen Max-Dehnungs-Anschlag umzusetzen (`RubberBandForce.cs`): Ab einer Dehnungsschwelle wird die nach außen gerichtete Geschwindigkeit der Spieler abgebaut, sodass die Spannung den Pfahl nie durchbrechen kann. Das hat funktioniert, das Tunneling war weg. Aber der Eingriff hat das normale Movement überall träge und gebremst gemacht, auch fernab jedes Pfahls. Ich bin deshalb wieder zurückgegangen, das Skript ist heute standardmäßig deaktiviert (`useEndStopBackstop = false`) und nur noch ein Notnagel.

Die eigentliche Lösung lag in Obi selbst, aber nicht im Inspector:

1. **Der Resolution-Cap im Obi-Code.** Obi deckelt die Blueprint-Resolution eines Seils im Quellcode auf 1, damit hat das Seil zu wenige Partikel, um sich um einen dünnen Pfahl zu legen. Ich habe die Stelle angepasst, sodass Resolution 2 möglich wurde. Das war der Durchbruch: Erst damit kann sich das Band stabil wickeln.
2. **Substeps auf 12.** Mit genügend Constraint-Iterationen pro Frame gewinnt die Kollision den Konflikt gegen die Dehnung.
3. **Das Kraft-Budget des Controllers.** Der kräftebasierte Controller begrenzt, wie stark ein Spieler ziehen kann. Niemand kann das Seil mit roher Gewalt überdehnen.

**Fazit:** Das Tunneling war ein Engine-Limit, kein Slider-Problem. Und die Sackgasse hat gelehrt: Eine vorhandene Physik-Engine nicht daneben nachbauen, sondern richtig konfigurieren.

### Ein Seil für beide Spieler: Netzwerk-Physik

Obi ist über verschiedene Rechner hinweg nicht deterministisch. Würde jeder Spieler sein eigenes Seil simulieren, sähen beide ein unterschiedliches Gummiband mit verschiedenen Wicklungen, und genau dieses Auseinanderdriften hatte ich in einer frühen Version auch.

Die Lösung ist eine einzige autoritative Simulation: Der Host simuliert beide Spielerkörper und das Seil in einer echten Obi-Welt. Der Client sendet nur seinen Input hoch und bekommt die Körper-Transforms und die Seilform zurück. Auf dem Client ist Obi reine Anzeige, ein nicht-simulierendes Puppet-Seil rendert die gestreamte Form. Als Merksatz: Auf dem Host läuft das komplette Single-Player-Spiel, der Client ist eine Fernbedienung mit Bildschirm.

Das Streaming übernehmen `RopeStreamSender.cs`, `RopeStreamReceiver.cs` und `RopeStreamProtocol.cs`: Partikelpositionen als Half-Floats, rund 20 Snapshots pro Sekunde, gesendet als `UnreliableSequenced` (veraltete Pakete werden verworfen statt nachgesendet). Die Bandbreite bleibt im einstelligen KB/s-Bereich. `RopeVisualPin.cs` heftet die gezeichneten Seilenden kosmetisch an die Spieler, damit das Band bei schnellen Sprüngen optisch nicht abreißt.

Eine Erkenntnis aus der Umsetzung: Der Versuch, den Seilzustand in einen zweiten, simulierenden Solver auf dem Client zu injizieren, scheitert, weil Obis Burst-Pipeline externe Eingriffe überschreibt. Ein Puppet, dem man nur Positionen setzt, ist der einzige Weg, der nicht gegen die Engine kämpft.

### Client-Side Prediction und die Grenze von NGO

Das server-autoritative Modell ist korrekt, aber nicht responsiv: Die eigene Bewegung des Clients wartet eine volle Rundreise zum Host. Der etablierte Fix ist Client-Side Prediction, wobei nur die eigene Bewegung vorhergesagt werden soll. Der Seilzug bleibt host-autoritativ, bei einem weichen Gummiband fällt eine Rundreise Verspätung nicht auf.

Beim Umsetzungsversuch bin ich an NGO gescheitert: Es bringt kein Rollback- oder Reconcile-System mit, und mit `NetworkRigidbody` und `UseRigidBodyForMotion` überschreibt NGO jeden Tick die Pose über einen `protected internal` Pfad, es gibt also keine saubere Stelle, um das auf dem vorhersagenden Client zu unterdrücken. Kurzfristig habe ich stattdessen die Latenz selbst reduziert (Tickrate von 30 auf 60, kleinere Interpolationspuffer), damit ist das Spiel in normalen Netzen gut spielbar. Als Ausblick ist die Migration auf FishNet begonnen, das Prediction als First-Class-Feature mitbringt: Der Character Controller ist als Proof-of-Concept bereits auf `Replicate`/`Reconcile` portiert, der Verbindungs-Layer und das Rope-Streaming sind noch offen.

## Besondere Leistung

- **Eingriff in den Obi-Quellcode.** Das Tunneling war mit den vorgesehenen Einstellungen des Assets nicht lösbar. Erst die Analyse des Solver-Verhaltens und das Anheben des hart codierten Resolution-Caps im Obi-Code haben das stabile Wickeln um dünne Hindernisse möglich gemacht.
- **Eigene Netzwerk-Physik-Architektur für einen nicht-deterministischen Solver.** Server-autoritatives Modell mit selbst entwickeltem Seilform-Streaming (Half-Float-Wire-Format, `UnreliableSequenced`, Interpolation auf dem Client). Beide Spieler sehen garantiert das identische Seil, bei einstelliger KB/s-Bandbreite.

## Projektstruktur (wichtige Dateien)

- Lobby/Verbindung: `Assets/Scripts/Networking/RBGameLobby.cs` (Lobby, Relay, Auto-Join), `Assets/Scripts/Networking/RubberBandMultiplayer.cs` (Host/Client-Start, Connection Approval)
- Rundenlogik: `Assets/Scripts/RBGameManager.cs` (Spawning, Checkpoints, Respawn, Timer, Ziellinie), `Assets/Scripts/Checkpoint.cs`, `Assets/Scripts/PlayerRespawn.cs`
- Seil: `Assets/Scripts/Rubber Band/RubberBandSpawner.cs` (Host = echte Simulation, Client = Anzeige-Puppet), `RubberBandForce.cs` (deaktivierter IPC-Notnagel), `RopeStreamSender.cs` / `RopeStreamReceiver.cs` / `RopeStreamProtocol.cs` (Seilform-Streaming), `RopeVisualPin.cs` (kosmetisches Anheften der Seilenden)
- Character Controller: `Assets/stylised-character-controller-main/.../PhysicsBasedCharacterController.cs` (inklusive aufgeladenem Sprung)
- Kamera: `Assets/Scripts/Orbitcamera.cs`
- Erster Seil-Prototyp: `Assets/Scripts/Rubber Band/old/RopeVerlet.cs`

## Quellen

- Unity Lobby/Relay Tutorial-Kurs von Code Monkey https://www.youtube.com/watch?v=7glCsF9fv3s
- Tutorial: 2D-Verlet-Integration für Seilsimulation in Unity https://www.youtube.com/watch?v=bxG3XP4MVzk
- Video: Der Physik-Character-Controller von *Very Very Valet* https://www.youtube.com/watch?v=qdskE8PJy6Q
- stylised-character-controller (Joe Binns) https://github.com/joebinns/stylised-character-controller
- Character Movement Fundamentals (Jan Ott) https://github.com/Jan-Ott/CharacterMovementFundamentals
- Obi Rope https://assetstore.unity.com/packages/tools/physics/obi-rope-55579
- Incremental Potential Contact (IPC) https://ipc-sim.github.io/
- XPBD: Position-Based Simulation of Compliant Constrained Dynamics (Macklin et al. 2016) http://mmacklin.com/xpbd.pdf

## Video

liegt im Repo Ordner "Videos"
https://github.com/Ans-er/Rubberbound/tree/main/Videos

## Projekt-Link

https://github.com/Ans-er/Rubberbound
